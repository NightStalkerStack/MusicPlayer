@echo off
setlocal

set "APP_NAME=MusicPlayer"
set "SCRIPT_DIR=%~dp0"
set "EXE_PATH=%SCRIPT_DIR%MusicPlayer.exe"

if not exist "%EXE_PATH%" (
    set "EXE_PATH=%SCRIPT_DIR%MusicPlayer_win-x64\MusicPlayer.exe"
)

if not exist "%EXE_PATH%" (
    echo MusicPlayer.exe was not found.
    echo Put this .bat file in the same folder as MusicPlayer.exe,
    echo or put it beside the MusicPlayer_win-x64 folder.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$exe = [System.IO.Path]::GetFullPath('%EXE_PATH%');" ^
  "$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\%APP_NAME%.lnk';" ^
  "$shell = New-Object -ComObject WScript.Shell;" ^
  "$link = $shell.CreateShortcut($shortcut);" ^
  "$link.TargetPath = $exe;" ^
  "$link.WorkingDirectory = Split-Path $exe;" ^
  "$link.IconLocation = $exe + ',0';" ^
  "$link.Save();"

if errorlevel 1 (
    echo Failed to create the start menu shortcut.
    pause
    exit /b 1
)

echo Start menu shortcut created: %APP_NAME%
echo You can now search for "%APP_NAME%" in Windows Search or launcher tools.
pause
