using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace RiotAutoLogin;

// ───────────────────────── Accounts (encrypted with Windows DPAPI) ─────────────────────────

public class Account
{
    public string Label { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public static class Store
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RiotAutoLogin.v1");

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RiotAutoLogin");

    static string FilePath => Path.Combine(Dir, "accounts.dat");

    public static List<Account> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<Account>();
            byte[] enc = File.ReadAllBytes(FilePath);
            byte[] json = Dpapi.Unprotect(enc, Entropy);
            return JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
        }
        catch
        {
            return new List<Account>();
        }
    }

    public static void Save(List<Account> accounts)
    {
        Directory.CreateDirectory(Dir);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(accounts);
        byte[] enc = Dpapi.Protect(json, Entropy);
        File.WriteAllBytes(FilePath, enc);
    }
}

// Direct call to Windows DPAPI (same thing ProtectedData does, but needs no NuGet package).
static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptProtectData(ref DATA_BLOB pDataIn, IntPtr szDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr hMem);

    const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    public static byte[] Protect(byte[] data, byte[] entropy) => Run(data, entropy, true);
    public static byte[] Unprotect(byte[] data, byte[] entropy) => Run(data, entropy, false);

    static byte[] Run(byte[] data, byte[] entropy, bool protect)
    {
        GCHandle h1 = GCHandle.Alloc(data, GCHandleType.Pinned);
        GCHandle h2 = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        try
        {
            var input = new DATA_BLOB { cbData = data.Length, pbData = h1.AddrOfPinnedObject() };
            var ent = new DATA_BLOB { cbData = entropy.Length, pbData = h2.AddrOfPinnedObject() };
            DATA_BLOB output;

            bool ok = protect
                ? CryptProtectData(ref input, IntPtr.Zero, ref ent, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref ent, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out output);

            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally { LocalFree(output.pbData); }
        }
        finally
        {
            h1.Free();
            h2.Free();
        }
    }
}

// ───────────────────────── Start with Windows ─────────────────────────

public static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "RiotAutoLogin";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;
        if (enabled) key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue(ValueName, false);
    }
}

// ───────────────────────── Riot Client local API: logged in or not? ─────────────────────────

public enum LoginState { Unknown, LoggedOut, LoggedIn }

public static class RiotApi
{
    static HttpClient? _http;

    // Only ever talks to 127.0.0.1; Riot's local API uses a self-signed certificate.
    static HttpClient Client => _http ??= new HttpClient(new HttpClientHandler
    {
        UseProxy = false,
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
    })
    { Timeout = TimeSpan.FromSeconds(3) };

    static string LockfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Riot Games", "Riot Client", "Config", "lockfile");

    public static async Task<(LoginState State, string Detail)> CheckAsync()
    {
        string path = LockfilePath;
        if (!File.Exists(path))
            return (LoginState.Unknown, "No lockfile at:\n" + path);

        try
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs))
                text = await sr.ReadToEndAsync();

            // lockfile format: name:pid:port:password:protocol
            string[] p = text.Trim().Split(':');
            if (p.Length < 5)
                return (LoginState.Unknown, "Lockfile has an unexpected format.");

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://127.0.0.1:{p[2]}/rso-auth/v1/authorization");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + p[3])));

            using var resp = await Client.SendAsync(req);
            int code = (int)resp.StatusCode;
            string body = await resp.Content.ReadAsStringAsync();
            if (body.Length > 300) body = body.Substring(0, 300) + "…";
            string detail = $"HTTP {code}\n{body}";

            if (code == 200) return (LoginState.LoggedIn, detail);
            if (code == 404) return (LoginState.LoggedOut, detail);
            return (LoginState.Unknown, detail);
        }
        catch (Exception ex)
        {
            return (LoginState.Unknown, "Error talking to Riot Client: " + ex.Message);
        }
    }
}

// ───────────────────────── Filling in the login form ─────────────────────────

public static class Automation
{
    // ── Tweak these if the Riot login screen's focus order differs on your machine ──
    const int InitialTabs = 0;               // Tabs pressed before typing the username (0 = username box already focused)
    // Tabs from the password box to the "Stay signed in" checkbox.
    // Password -> Facebook -> Google -> Apple -> Xbox -> PlayStation -> checkbox = 6
    const int TabsPasswordToCheckbox = 6;
    const bool TickStaySignedIn = true;      // Press Space on the checkbox (set false if it is already ticked by default)

    public static IntPtr FindRiotWindow()
    {
        foreach (string name in new[] { "RiotClientUx", "Riot Client" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    IntPtr h = proc.MainWindowHandle;
                    if (h != IntPtr.Zero && Native.IsWindowVisible(h)) return h;
                }
                catch { }
                finally { proc.Dispose(); }
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Returns null on success, or an error message.</summary>
    public static async Task<string?> LoginAsync(Account acc)
    {
        IntPtr hwnd = FindRiotWindow();
        if (hwnd == IntPtr.Zero) return "Riot Client window not found.";

        if (!await FocusAsync(hwnd)) return "Couldn't bring Riot Client to the front.";
        await Task.Delay(400);

        for (int i = 0; i < InitialTabs; i++) await Native.PressAsync(Native.VK_TAB);

        // Safety: never type credentials unless Riot Client really is the focused window.
        if (Native.GetForegroundWindow() != hwnd) return "Riot Client lost focus, aborted.";
        await Native.SelectAllAsync();
        await Native.TypeAsync(acc.Username);

        await Native.PressAsync(Native.VK_TAB);

        if (Native.GetForegroundWindow() != hwnd) return "Riot Client lost focus, aborted.";
        await Native.SelectAllAsync();
        await Native.TypeAsync(acc.Password);

        // Walk past the social-login buttons to the "Stay signed in" checkbox and tick it.
        for (int i = 0; i < TabsPasswordToCheckbox; i++) await Native.PressAsync(Native.VK_TAB);
        if (Native.GetForegroundWindow() != hwnd) return "Riot Client lost focus, aborted.";
        if (TickStaySignedIn) await Native.PressAsync(Native.VK_SPACE);

        // Go back to the password box and press Enter there, which submits the form.
        for (int i = 0; i < TabsPasswordToCheckbox; i++) await Native.PressShiftTabAsync();

        if (Native.GetForegroundWindow() != hwnd) return "Riot Client lost focus, aborted.";
        await Native.PressAsync(Native.VK_RETURN);
        return null;
    }

    static async Task<bool> FocusAsync(IntPtr h)
    {
        if (Native.IsIconic(h)) Native.ShowWindow(h, 9 /* SW_RESTORE */);
        for (int i = 0; i < 6; i++)
        {
            Native.SetForegroundWindow(h);
            await Task.Delay(150);
            if (Native.GetForegroundWindow() == h) return true;
        }
        return false;
    }
}

// ───────────────────────── Win32 helpers ─────────────────────────

static class Native
{
    public const ushort VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_SPACE = 0x20, VK_A = 0x41;

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr hProcess);

    static INPUT Key(ushort vk, ushort scan, uint flags) => new INPUT
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    static void Send(params INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    public static async Task PressAsync(ushort vk)
    {
        Send(Key(vk, 0, 0), Key(vk, 0, KEYEVENTF_KEYUP));
        await Task.Delay(120);
    }

    public static async Task PressShiftTabAsync()
    {
        Send(Key(VK_SHIFT, 0, 0), Key(VK_TAB, 0, 0), Key(VK_TAB, 0, KEYEVENTF_KEYUP), Key(VK_SHIFT, 0, KEYEVENTF_KEYUP));
        await Task.Delay(120);
    }

    public static async Task SelectAllAsync()
    {
        Send(Key(VK_CONTROL, 0, 0), Key(VK_A, 0, 0), Key(VK_A, 0, KEYEVENTF_KEYUP), Key(VK_CONTROL, 0, KEYEVENTF_KEYUP));
        await Task.Delay(120);
    }

    /// <summary>Types text as Unicode keystrokes, so special characters in passwords are safe.</summary>
    public static async Task TypeAsync(string text)
    {
        foreach (char c in text)
        {
            Send(Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            await Task.Delay(25);
        }
        await Task.Delay(100);
    }

    public static void TrimMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        EmptyWorkingSet(Process.GetCurrentProcess().Handle);
    }
}
