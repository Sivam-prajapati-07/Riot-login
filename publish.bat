@echo off
echo Building RiotAutoLogin...
dotnet publish -c Release -o "%~dp0publish"
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo.
echo Done. Copy the whole "publish" folder somewhere permanent and run RiotAutoLogin.exe from it:
echo %~dp0publish\RiotAutoLogin.exe
pause
