# Riot Auto Login

A tiny Windows tray app that watches for the Riot Client. If the client is open and **logged in**, it does nothing. If it's open and **logged out**, it pops up a small picker with your saved accounts, then fills in the username and password, ticks **Stay signed in**, and signs in.

- Idle cost is tiny: roughly 15-25 MB of RAM and near-zero CPU. It polls every few seconds and no window exists until it's needed.
- Passwords are encrypted with Windows DPAPI and stored in `%APPDATA%\RiotAutoLogin\accounts.dat`. They can only be decrypted by your Windows user on your PC.
- Written in C# (.NET 8, WinForms) with no third-party packages.

> **Disclaimer:** This is an unofficial tool and is not affiliated with or endorsed by Riot Games. It simulates keyboard input in the Riot Client login window, which Riot does not officially support. Use it at your own risk.

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (to run a pre-built copy)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only if you want to build it yourself)

## Get it

**Option A: pre-built** (if a release is available)
Download the zip from the Releases page, extract the whole folder somewhere permanent (for example `C:\Tools\RiotAutoLogin\`), and run `RiotAutoLogin.exe`.

**Option B: build from source**
1. Clone the repo.
2. Double-click `publish.bat`. This creates a `publish` folder.
3. Copy the whole `publish` folder somewhere permanent and run `RiotAutoLogin.exe`.

To test without publishing, run `dotnet run` in the project folder.

## Using it

1. Run `RiotAutoLogin.exe`. A tray icon appears (check the `^` arrow near the clock).
2. Right-click it and choose **Manage accounts…** to add your accounts (label, username, password).
3. Tick **Start with Windows** so it runs automatically. Windows starts the exe from wherever it is when you tick this, so don't move the folder afterwards.
4. Open Riot Client. If you're logged out, the picker appears after a few seconds. Choose an account and it signs in.

| Tray menu item | What it does |
|---|---|
| Manage accounts… | Add, edit or delete saved accounts |
| Check Riot status | Shows what the tool currently detects (useful for troubleshooting) |
| Start with Windows | Turns autostart on or off |
| Exit | Closes the tool |

## How it works

- **Is Riot running?** It looks for `RiotClientServices.exe`.
- **Logged in or not?** It reads Riot's `lockfile` and asks the client's local API (`/rso-auth/v1/authorization`) whether a session exists. This is an assumption about Riot's behavior and may change in future updates.
- **Filling the form:** It focuses the Riot Client window, types the credentials as Unicode keystrokes, tabs to the **Stay signed in** checkbox, ticks it, and submits. It checks that Riot Client is the focused window before typing, so credentials are never typed into another app.
- **2FA:** If an account uses 2FA, you still enter the code yourself.

## Troubleshooting

**It types into the wrong field or ticks the wrong box.**
The login form is filled by tabbing, so it depends on Riot's focus order. At the top of the `Automation` class in `Core.cs` there are constants you can change:

```csharp
const int InitialTabs = 0;               // Tabs before the username (0 = username already focused)
const int TabsPasswordToCheckbox = 6;    // Password -> 5 social buttons -> checkbox
const bool TickStaySignedIn = true;
```

Rebuild after changing them. If Riot redesigns the login screen, these numbers may need updating.

**The picker never appears.**
Open **Check Riot status** from the tray menu while on the login screen. It should show `LoggedOut`. If it shows `Unknown`, open an issue and include the text from that box.

**Build fails with `NU1100` (can't resolve a package).**
NuGet can't reach nuget.org. This project uses no packages, so a plain `publish.bat` or `dotnet run` should work. Avoid adding single-file or trimming options, which pull extra packages.

## License

Add your preferred license here (for example MIT).
