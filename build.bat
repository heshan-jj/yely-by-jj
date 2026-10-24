@echo off
echo Stopping existing instances...
taskkill /F /IM Yelly.exe >nul 2>&1
taskkill /F /IM CoolModeTray.exe >nul 2>&1

echo Compiling Yelly...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /win32icon:Yelly.ico /resource:Yelly.png /out:Yelly.exe Ui.cs Settings.cs Telemetry.cs Autopilot.cs MainForm.cs Yelly.cs
if %ERRORLEVEL% EQU 0 (
    echo [OK] Compiled successfully to Yelly.exe
    echo Starting Yelly in system tray...
    start "" "%~dp0Yelly.exe"
) else (
    echo [ERROR] Build failed.
    exit /b 1
)
