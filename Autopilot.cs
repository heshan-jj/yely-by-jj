using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CoolModeTray
{
    public enum TriggerReason
    {
        Manual,
        ProcessRule,
        BatteryRule,
        TemperatureThreshold
    }

    class AutopilotEngine
    {
        private readonly Settings _settings;
        private string _matchedExe;
        private bool _matchedDisable;
        private TriggerReason _activeTrigger = TriggerReason.Manual;
        private string _activeDescription = "Manual";

        public TriggerReason ActiveTrigger { get { return _activeTrigger; } }
        public string ActiveDescription { get { return _activeDescription; } }

        public AutopilotEngine(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Evaluates rules against running processes, battery status, and temperatures.
        /// Returns null if no automated rule applies (maintain manual state),
        /// or true/false to request CoolMode ON (true) or OFF (false).
        /// </summary>
        public bool? Evaluate(Snapshot snap, bool currentCoolMode)
        {
            if (!_settings.Autopilot)
            {
                _activeTrigger = TriggerReason.Manual;
                _activeDescription = "Manual";
                return null;
            }

            // 1. Process-specific rules (highest priority)
            if (_settings.Rules.Count > 0)
            {
                HashSet<string> runningExes = GetRunningProcessNames();
                foreach (Rule r in _settings.Rules)
                {
                    if (string.IsNullOrEmpty(r.Exe)) continue;
                    string target = r.Exe.Trim();
                    if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        target = target.Substring(0, target.Length - 4);

                    if (runningExes.Contains(target.ToLowerInvariant()))
                    {
                        _matchedExe = r.Exe;
                        _matchedDisable = r.Disable;
                        _activeTrigger = TriggerReason.ProcessRule;
                        _activeDescription = r.Disable
                            ? "App Running: " + r.Exe + " (Boost Requested)"
                            : "App Running: " + r.Exe + " (CoolMode Enforced)";

                        // If rule says Disable == true, CoolMode should be OFF (false)
                        // If rule says Disable == false, CoolMode should be ON (true)
                        return !r.Disable;
                    }
                }
            }

            // 2. Battery Rule
            if (_settings.BatteryRule && !snap.OnAC && snap.Battery > 0)
            {
                _activeTrigger = TriggerReason.BatteryRule;
                _activeDescription = "On Battery (" + snap.Battery + "%)";
                return true; // Enforce CoolMode on battery to save power
            }

            // 3. Thermal Hysteresis Rule
            if (_settings.TempRule && snap.CpuTemp > 0)
            {
                if (snap.CpuTemp >= _settings.TempOn)
                {
                    _activeTrigger = TriggerReason.TemperatureThreshold;
                    _activeDescription = "High Temp: " + snap.CpuTemp + "°C (>= " + _settings.TempOn + "°C)";
                    return true;
                }
                if (currentCoolMode && snap.CpuTemp <= _settings.TempOff)
                {
                    _activeTrigger = TriggerReason.TemperatureThreshold;
                    _activeDescription = "Cooled Down: " + snap.CpuTemp + "°C (<= " + _settings.TempOff + "°C)";
                    return false;
                }
            }

            _activeTrigger = TriggerReason.Manual;
            _activeDescription = "Manual / Idle";
            return null;
        }

        private static HashSet<string> GetRunningProcessNames()
        {
            var set = new HashSet<string>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        set.Add(p.ProcessName.ToLowerInvariant());
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return set;
        }
    }
}
