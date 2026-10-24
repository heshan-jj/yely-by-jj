@echo off
echo Stopping existing CoolModeTray instances...
taskkill /F /IM CoolModeTray.exe >nul 2>&1

echo Compiling CoolModeTray...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:CoolModeTray.exe Ui.cs Settings.cs Telemetry.cs Autopilot.cs MainForm.cs CoolModeTray.cs
if %ERRORLEVEL% EQU 0 (
    echo [OK] Compiled successfully to CoolModeTray.exe
    echo Starting CoolModeTray in system tray...
    start "" "%~dp0CoolModeTray.exe"
) else (
    echo [ERROR] Build failed.
    exit /b 1
)
