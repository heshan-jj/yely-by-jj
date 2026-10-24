# CoolModeTray

A zero-dependency, lightweight Windows desktop & system tray utility that replicates the core functionality of **Outbyte Camomile**'s "CoolMode" and live temperature monitoring without bloat, kernel drivers, telemetry, or background services.

Includes a Notion-themed desktop dashboard, comprehensive hardware telemetry with live sparklines, and an intelligent Autopilot engine.

## Features

- **Notion-Themed Dashboard**:
  - Clean, slate & ivory Notion light aesthetic with responsive layout, callout cards, breadcrumb property pills, and custom window chrome.
  - Interactive navigation: **Dashboard**, **Autopilot**, and **Settings**.
  - One-click hero action card to instantly toggle CoolMode.

- **Real-Time Hardware Telemetry**:
  - **CPU**: Thermal zone temperature (°C / °F), utility load percentage, clock frequency (GHz), and live sparkline.
  - **GPU**: NVIDIA RTX/GTX temperature, GPU core utilization, power draw (W), and VRAM usage via non-blocking background streaming.
  - **Memory & Power**: Physical RAM committed/total, battery charging status, battery percentage, and AC line detection.

- **Autopilot Automation Engine**:
  - Process-specific rules: Automatically disables CoolMode when high-performance apps (e.g. `cyberpunk2077.exe`, `blender.exe`) are launched, and re-engages CoolMode when closed.
  - Battery awareness: Automatically forces CoolMode when running on battery power to maximize endurance.
  - Thermal trigger: Optional automatic CoolMode engagement when temperatures exceed configurable threshold (with hysteresis).

- **System Tray Integration**:
  - Live 2-digit temperature rendering directly inside the system tray icon (Cyan = CoolMode, Amber = Normal, Red = Thermal Alert).
  - Quick-action context menu with live temps, toggle shortcut, dashboard launcher, and startup toggle.

- **Zero External Dependencies**:
  - Written in pure C# using Windows .NET 4.x WinForms and GDI+. Compiles directly with Windows' built-in `csc.exe` in under 2 seconds.

## Building & Running

Run [build.bat](file:///d:/Code/Projects/yely%20by%20jj/build.bat) or compile with Windows' built-in C# compiler:
```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /out:CoolModeTray.exe Ui.cs Settings.cs Telemetry.cs Autopilot.cs MainForm.cs CoolModeTray.cs
```
