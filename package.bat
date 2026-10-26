@echo off
setlocal
echo Stopping running instances of Yelly...
taskkill /F /IM Yelly.exe >nul 2>&1
taskkill /F /IM CoolModeTray.exe >nul 2>&1

echo Compiling Yelly.exe...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /r:System.Management.dll /win32icon:Yelly.ico /resource:Yelly.png /out:Yelly.exe Ui.cs Settings.cs Telemetry.cs Autopilot.cs MainForm.cs Yelly.cs
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] C# compilation failed.
    exit /b %ERRORLEVEL%
)

set ISCC="C:\Users\HeshanJJ\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"

echo Compiling installer with Inno Setup...
%ISCC% installer.iss
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Inno Setup packaging failed.
    exit /b %ERRORLEVEL%
)

echo [OK] Installer built successfully at dist\YellySetup.exe
