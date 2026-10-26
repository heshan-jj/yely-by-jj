# Yelly

<p align="center">
  <img src="Yelly.png" width="128" alt="Yelly Logo" />
</p>

A zero-dependency, lightweight Windows desktop & system tray utility that replicates the core functionality of **Outbyte Camomile**'s "CoolMode" and live temperature monitoring without bloat, kernel drivers, telemetry, or background services.

Includes a desktop dashboard, live hardware telemetry with sparklines, and an Autopilot engine that can switch power states for you.

## At a glance

<p align="center">
  <img src="docs/how-it-works.svg" alt="How Yelly works: watch telemetry, decide with Autopilot, apply a power-plan change" width="920" />
</p>

<p align="center">
  <img src="docs/modes.svg" alt="CoolMode caps the processor at 99 percent with boost off. Full performance restores boost and a 100 percent cap." width="920" />
</p>

<p align="center">
  <img src="docs/autopilot.svg" alt="Autopilot checks a running app, then battery, then CPU heat. The first match wins." width="920" />
</p>

<p align="center">
  <img src="docs/tray.svg" alt="Tray icon colors: blue is CoolMode, amber is full boost, red is a thermal alert." width="920" />
</p>

## Features

- **Branded & Notion-Themed UI**:
  - Distinctive Yelly branding, custom window controls, and a warm stone light theme.
  - Interactive navigation: **Dashboard**, **Autopilot**, **Hardware**, and **Settings**.
  - One-click hero action card to instantly toggle CoolMode.
  - Dark theme by default, including themed logos, tags, dialogs, and tray menus.
  - Adjustable CoolMode processor cap from 50% to 100%; changes apply immediately while CoolMode is active.
  - Optional system-wide CoolMode shortcut: enable it in **Settings**, then click the key combination to record your preferred Ctrl, Alt, or Shift shortcut. If another app owns that combination, Yelly keeps the previous shortcut and explains the conflict.
  - Per-monitor display scaling keeps text, controls, and click targets aligned as the window moves between screens. The dashboard fits within smaller working areas.

- **Real-Time Hardware Telemetry**:
  - A scrollable **Hardware Monitor** page with CPU model, thermal-zone temperature, total and per-logical-processor load and clock, GPU sensor readings, physical drive model/type/capacity, drive temperature and reliability data, RAM, and battery status.
  - **CPU**: Thermal zone temperature (°C / °F), utility load percentage, clock frequency (GHz), and live sparkline.
  - **GPU**: NVIDIA RTX/GTX temperature, fan speed, core utilization, power draw (W), clock, and VRAM usage via non-blocking background streaming.
  - **Storage**: Physical disk model, SSD/HDD type, capacity, device-reported temperature, wear indicator, and power-on hours through the Windows Storage provider. Temperature and reliability fields appear when the drive and its firmware expose them.
  - **Memory & Power**: Physical RAM committed/total, battery charging status, battery percentage, and AC line detection.

- **Autopilot Automation Engine**:
  - Process-specific rules: Automatically disables CoolMode when high-performance apps (e.g. `cyberpunk2077.exe`, `blender.exe`) are launched, and re-engages CoolMode when closed.
  - Battery awareness: Automatically forces CoolMode when running on battery power to maximize endurance.
  - Thermal trigger: Optional automatic CoolMode engagement when temperatures exceed configurable threshold (with hysteresis).

- **System Tray Integration**:
  - Live 2-digit temperature rendering directly inside the system tray icon (blue = CoolMode, amber = full performance, red = thermal alert).
  - Quick-action context menu with live temps, toggle shortcut, dashboard launcher, and startup toggle.

- **Zero External Dependencies**:
  - Written in pure C# using Windows .NET 4.x WinForms and GDI+. Embedded Win32 `.ico` and in-app logo resource. Compiles directly with Windows' built-in `csc.exe` in under 2 seconds.

## Building & Packaging

- **Check themes and display scaling**: run `tests\check-layout.bat`. This renders all four pages in both themes at 100%, 125%, 150%, and 200%, checks navigation and theme hit targets, and checks small-screen fit without changing power settings. Pass an output folder to save render previews.

- **Compile standalone executable**:
  Run [build.bat](file:///d:/Code/Projects/yely%20by%20jj/build.bat) or compile with Windows' built-in C# compiler:
  ```cmd
  C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /reference:System.Management.dll /win32icon:Yelly.ico /resource:Yelly.png /out:Yelly.exe Ui.cs Settings.cs Telemetry.cs Autopilot.cs MainForm.cs Yelly.cs
  ```

- **Package into Windows installer**:
  Run [package.bat](file:///d:/Code/Projects/yely%20by%20jj/package.bat) to build `dist\YellySetup.exe` with Inno Setup.

