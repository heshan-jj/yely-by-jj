@echo off
echo Building CoolModeTray...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:CoolModeTray.exe CoolModeTray.cs
if %ERRORLEVEL% EQU 0 (
    echo [OK] Compiled successfully to CoolModeTray.exe
) else (
    echo [ERROR] Build failed.
)
pause
