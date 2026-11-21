RiotAutoLogin
 
BUILD THE FINAL APP
  Double-click publish.bat  (needs the .NET 8 SDK installed)
  The app appears in the "publish" folder next to it (RiotAutoLogin.exe plus a few support files).
 
MAKE IT AUTOMATIC
  1. Copy the WHOLE publish folder to a permanent place, e.g. C:\Tools\RiotAutoLogin\
     (Windows starts the exe from wherever it is when you tick the box, so don't move it afterwards.)
  2. Run RiotAutoLogin.exe from there. A tray icon appears (check the ^ arrow near the clock).
  3. Right-click the tray icon -> tick "Start with Windows".
 
TRAY MENU
  Manage accounts...   add / edit / delete IDs
  Check Riot status    shows what the tool detects (for troubleshooting)
  Start with Windows   autostart on/off
  Exit
 
If Riot changes its login screen and the wrong thing gets clicked, adjust the constants at the top of
the Automation class in Core.cs and rebuild.
 