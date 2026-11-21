using System.Diagnostics;

namespace RiotAutoLogin;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Only one copy at a time.
        using var mutex = new Mutex(true, @"Local\RiotAutoLogin_SingleInstance", out bool isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}

/// <summary>Tray icon + a slow timer that watches Riot Client. No window exists until it is needed.</summary>
sealed class TrayContext : ApplicationContext
{
    const int IdleIntervalMs = 5000;     // Riot not running
    const int WatchIntervalMs = 3000;    // Riot running
    const int TicksBeforePrompt = 3;     // ~9 s of "not logged in" before popping up (lets "stay signed in" finish auto-login)

    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;

    bool _busy;
    bool _handled;          // already prompted (or skipped) during this logged-out period
    int _trackedPid;
    int _loggedOutTicks;

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Manage accounts…", null, (_, _) => OpenManager());
        menu.Items.Add("Check Riot status", null, async (_, _) => await ShowStatusAsync());

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = Startup.IsEnabled(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => Startup.Set(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Riot Auto Login",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenManager();

        _timer = new System.Windows.Forms.Timer { Interval = IdleIntervalMs };
        _timer.Tick += OnTick;
        _timer.Start();

        Native.TrimMemory();
    }

    static Icon LoadIcon()
    {
        try
        {
            using var s = typeof(TrayContext).Assembly.GetManifestResourceStream("app.ico");
            if (s != null) return new Icon(s, SystemInformation.SmallIconSize);
        }
        catch { }
        return SystemIcons.Application;
    }

    async void OnTick(object? sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try { await CheckAsync(); }
        catch { /* never crash the background watcher */ }
        finally { _busy = false; }
    }

    async Task CheckAsync()
    {
        int pid = GetRiotPid();

        if (pid == 0)
        {
            _timer.Interval = IdleIntervalMs;
            _trackedPid = 0; _handled = false; _loggedOutTicks = 0;
            return;
        }

        _timer.Interval = WatchIntervalMs;

        if (pid != _trackedPid)
        {
            _trackedPid = pid; _handled = false; _loggedOutTicks = 0;
        }

        var (state, _) = await RiotApi.CheckAsync();

        if (state == LoginState.LoggedIn)
        {
            // Logged in: do nothing. Reset so a later logout can prompt again.
            _handled = false; _loggedOutTicks = 0;
            return;
        }

        if (state == LoginState.Unknown) { _loggedOutTicks = 0; return; }

        // LoggedOut
        if (_handled) return;
        if (++_loggedOutTicks < TicksBeforePrompt) return;

        // Only prompt once the login window is actually on screen.
        if (Automation.FindRiotWindow() == IntPtr.Zero)
        {
            _loggedOutTicks = TicksBeforePrompt - 1;
            return;
        }

        _handled = true;
        await PromptAndLoginAsync();
    }

    async Task PromptAndLoginAsync()
    {
        var accounts = Store.Load();
        if (accounts.Count == 0)
        {
            _tray.ShowBalloonTip(5000, "Riot Auto Login",
                "No accounts saved yet. Right-click the tray icon → Manage accounts.", ToolTipIcon.Info);
            return;
        }

        Account? chosen;
        using (var picker = new PickerForm(accounts))
        {
            chosen = picker.ShowDialog() == DialogResult.OK ? picker.Selected : null;
        }

        if (chosen != null)
        {
            string? error = await Automation.LoginAsync(chosen);
            if (error != null)
                _tray.ShowBalloonTip(5000, "Riot Auto Login", error, ToolTipIcon.Warning);
        }

        Native.TrimMemory();
    }

    static int GetRiotPid()
    {
        var procs = Process.GetProcessesByName("RiotClientServices");
        try { return procs.Length > 0 ? procs[0].Id : 0; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    bool _managerOpen;
    void OpenManager()
    {
        if (_managerOpen) return;
        _managerOpen = true;
        try { using var f = new ManagerForm(); f.ShowDialog(); }
        finally { _managerOpen = false; Native.TrimMemory(); }
    }

    async Task ShowStatusAsync()
    {
        int pid = GetRiotPid();
        var win = Automation.FindRiotWindow();
        var (state, detail) = await RiotApi.CheckAsync();
        MessageBox.Show(
            $"RiotClientServices running: {(pid != 0 ? "yes (PID " + pid + ")" : "no")}\n" +
            $"Riot window found: {(win != IntPtr.Zero ? "yes" : "no")}\n" +
            $"Detected state: {state}\n\n{detail}",
            "Riot Auto Login – status");
    }

    void ExitApp()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
