# CoolModeTray

A zero-dependency, lightweight (~12 KB) Windows system tray utility that brings the core functionality of **Outbyte Camomile**'s "CoolMode" and live temperature monitoring without bloat, kernel drivers, background services, analytics, or subscriptions.

## Features
- **Live Temperatures**:
  - **CPU Temp**: Queried via standard Windows ACPI thermal performance counters (`Thermal Zone Information`).
  - **GPU Temp**: Queried via background async calls to `nvidia-smi` (NVIDIA RTX/GTX GPUs).
  - **Tray Icon Display**: Renders real-time 2-digit temperature directly inside the system tray icon (Cyan = CoolMode, Amber = Normal, Red = Thermal Alert >= 85°C).
  - **Tooltip & Menu**: Displays full `CPU: XX°C | GPU: YY°C` on hover and in the context menu.
- **CoolMode Toggle**:
  - Disables Intel/AMD CPU Turbo Boost (`PERFBOOSTMODE` = 0) and caps maximum CPU state to 99% (`PROCTHROTTLEMAX`).
  - Restores full boost on demand with one click.
- **Start with Windows**: Toggle automatic launch at startup from the right-click menu.
- **Zero Dependencies**: Single-file executable built with standard Windows components.

## Building
Run `build.bat` or compile with the Windows built-in C# compiler:
```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:CoolModeTray.exe CoolModeTray.cs
```
