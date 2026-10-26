using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Yelly
{
    class Rule
    {
        public string Exe;
        /// <summary>true = force CoolMode off while running, false = force it on.</summary>
        public bool Disable;
    }

    class Settings
    {
        public bool Autopilot = true;
        public bool BatteryRule;
        public bool TempRule;
        public int TempOn = 85, TempOff = 70;
        public int Cap = 99;
        public bool DisableBoost = true;
        public bool StartMinimized;
        public bool CloseToTray = true;
        public bool Notify = true;
        public bool TrayTemp = true;
        public bool Fahrenheit;
        public bool ShortcutEnabled;
        public int ShortcutModifiers = 3; // Ctrl + Alt
        public int ShortcutKey = (int)Keys.K;
        public int Alert = 85;
        public int Refresh = 1;
        /// <summary>Power values captured before CoolMode was first enabled, restored when it is turned off.</summary>
        public int OrigBoost = 2, OrigMax = 100;
        public List<Rule> Rules = new List<Rule>();

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yelly");
                string file = Path.Combine(dir, "settings.ini");
                if (!File.Exists(file))
                {
                    string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoolModeTray", "settings.ini");
                    if (File.Exists(old))
                    {
                        try { Directory.CreateDirectory(dir); File.Copy(old, file, true); } catch { }
                    }
                }
                return file;
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "Autopilot": s.Autopilot = v == "1"; break;
                        case "BatteryRule": s.BatteryRule = v == "1"; break;
                        case "TempRule": s.TempRule = v == "1"; break;
                        case "TempOn": s.TempOn = I(v, s.TempOn); break;
                        case "TempOff": s.TempOff = I(v, s.TempOff); break;
                        case "Cap": s.Cap = I(v, s.Cap); break;
                        case "DisableBoost": s.DisableBoost = v == "1"; break;
                        case "StartMinimized": s.StartMinimized = v == "1"; break;
                        case "CloseToTray": s.CloseToTray = v == "1"; break;
                        case "Notify": s.Notify = v == "1"; break;
                        case "TrayTemp": s.TrayTemp = v == "1"; break;
                        case "Fahrenheit": s.Fahrenheit = v == "1"; break;
                        case "ShortcutEnabled": s.ShortcutEnabled = v == "1"; break;
                        case "ShortcutModifiers": s.ShortcutModifiers = I(v, s.ShortcutModifiers) & 7; break;
                        case "ShortcutKey": s.ShortcutKey = I(v, s.ShortcutKey); break;
                        case "Alert": s.Alert = I(v, s.Alert); break;
                        case "Refresh": s.Refresh = I(v, s.Refresh); break;
                        case "OrigBoost": s.OrigBoost = I(v, s.OrigBoost); break;
                        case "OrigMax": s.OrigMax = I(v, s.OrigMax); break;
                        case "Rule":
                            string[] p = v.Split('|');
                            if (p[0].Length > 0) s.Rules.Add(new Rule { Exe = p[0], Disable = p.Length < 2 || p[1] == "1" });
                            break;
                    }
                }
            }
            catch { /* corrupt file: fall back to defaults */ }
            s.Clamp();
            return s;
        }

        public void Clamp()
        {
            Cap = Math.Max(50, Math.Min(100, Cap));
            Alert = Math.Max(60, Math.Min(100, Alert));
            Refresh = Math.Max(1, Math.Min(5, Refresh));
            TempOn = Math.Max(50, Math.Min(100, TempOn));
            TempOff = Math.Max(40, Math.Min(TempOn - 5, TempOff));
            OrigBoost = Math.Max(0, Math.Min(4, OrigBoost));
            OrigMax = Math.Max(1, Math.Min(100, OrigMax));
            if (ShortcutKey < 1 || ShortcutKey > 255) ShortcutKey = (int)Keys.K;
            if ((ShortcutModifiers & 7) == 0) ShortcutModifiers = 3;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var l = new List<string>();
                l.Add("Autopilot=" + B(Autopilot));
                l.Add("BatteryRule=" + B(BatteryRule));
                l.Add("TempRule=" + B(TempRule));
                l.Add("TempOn=" + TempOn);
                l.Add("TempOff=" + TempOff);
                l.Add("Cap=" + Cap);
                l.Add("DisableBoost=" + B(DisableBoost));
                l.Add("StartMinimized=" + B(StartMinimized));
                l.Add("CloseToTray=" + B(CloseToTray));
                l.Add("Notify=" + B(Notify));
                l.Add("TrayTemp=" + B(TrayTemp));
                l.Add("Fahrenheit=" + B(Fahrenheit));
                l.Add("ShortcutEnabled=" + B(ShortcutEnabled));
                l.Add("ShortcutModifiers=" + ShortcutModifiers);
                l.Add("ShortcutKey=" + ShortcutKey);
                l.Add("Alert=" + Alert);
                l.Add("Refresh=" + Refresh);
                l.Add("OrigBoost=" + OrigBoost);
                l.Add("OrigMax=" + OrigMax);
                foreach (Rule r in Rules) l.Add("Rule=" + r.Exe + "|" + B(r.Disable));
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { /* read-only profile: settings just won't persist */ }
        }

        static string B(bool b) { return b ? "1" : "0"; }

        static int I(string v, int d)
        {
            int r;
            return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : d;
        }
    }

    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsOn()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key, false))
                return k != null && (k.GetValue("Yelly") != null || k.GetValue("CoolModeTray") != null);
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (k == null) return;
                k.DeleteValue("CoolModeTray", false);
                if (on) k.SetValue("Yelly", "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                else k.DeleteValue("Yelly", false);
            }
        }
    }

    /// <summary>Windows power-plan processor settings (boost mode + max processor state).</summary>
    static class Power
    {
        static readonly Guid Sub = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static readonly Guid Boost = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        static readonly Guid Max = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");

        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint v);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint v);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint v);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

        static Guid Active()
        {
            IntPtr p;
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) throw new InvalidOperationException("No active power scheme");
            try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
            finally { LocalFree(p); }
        }

        public static void Read(out uint boost, out uint max)
        {
            Guid s = Active(), sub = Sub, b = Boost, m = Max;
            boost = 2; max = 100;
            PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref b, out boost);
            PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref m, out max);
        }

        public static bool IsCool(uint boost, uint max)
        {
            return boost == 0 || max < 100;
        }

        public static void Write(uint boost, uint max)
        {
            Guid s = Active(), sub = Sub, b = Boost, m = Max;
            PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sub, ref b, boost);
            PowerWriteDCValueIndex(IntPtr.Zero, ref s, ref sub, ref b, boost);
            PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sub, ref m, max);
            PowerWriteDCValueIndex(IntPtr.Zero, ref s, ref sub, ref m, max);
            PowerSetActiveScheme(IntPtr.Zero, ref s);
        }
    }
}
