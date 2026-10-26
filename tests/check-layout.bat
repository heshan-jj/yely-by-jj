@echo off
setlocal
cd /d "%~dp0.."
set "LAYOUT_CHECK_EXE=%TEMP%\YellyLayoutChecks.exe"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:Yelly.LayoutChecks /reference:System.Management.dll /resource:Yelly.png /out:"%LAYOUT_CHECK_EXE%" Ui.cs Settings.cs Telemetry.cs Autopilot.cs MainForm.cs Yelly.cs tests\LayoutChecks.cs
if errorlevel 1 exit /b 1
"%LAYOUT_CHECK_EXE%" %*
exit /b %ERRORLEVEL%
