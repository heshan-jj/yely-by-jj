using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Yelly
{
    /// <summary>Fixed-size ring of the latest samples, used for sparklines.</summary>
    class Hist
    {
        public const int N = 60;
        public readonly float[] V = new float[N];
        public int Count;

        public void Add(float v)
        {
            if (Count < N) V[Count++] = v;
            else { Array.Copy(V, 1, V, 0, N - 1); V[N - 1] = v; }
        }
    }

    class Snapshot
    {
        public int CpuTemp = -1, GpuTemp = -1, GpuFan = -1, Battery = -1;
        public float CpuLoad = -1, CpuMHz = -1, RamPct = -1, RamUsed = -1, RamTotal = -1;
        public float GpuLoad = -1, GpuPower = -1, GpuClock = -1, GpuMemUsed = -1, GpuMemTotal = -1;
        public string CpuName = "", GpuName = "";
        public bool OnAC = true;
        public int PeakCpu = -1, PeakGpu = -1, NCpu;
        public double SumCpu;
        public readonly Hist HCpuTemp = new Hist(), HGpuTemp = new Hist(), HCpuLoad = new Hist(), HCpuMHz = new Hist();
        public readonly Hist HGpuLoad = new Hist(), HGpuPower = new Hist(), HRam = new Hist();
    }

    class Telemetry : IDisposable
    {
        public readonly Snapshot S = new Snapshot();
        PerformanceCounter tz, load, freq;
        Process smi;

        [StructLayout(LayoutKind.Sequential)]
        class MemStatus
        {
            public uint Length = 64;
            public uint Load;
            public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt;
        }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MemStatus s);

        public Telemetry(bool demo)
        {
            if (demo) { FillDemo(); return; }
            S.CpuName = CpuNameFromRegistry();
            try
            {
                string[] inst = new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames();
                if (inst.Length > 0) tz = Counter("Thermal Zone Information", "Temperature", inst[0]);
            }
            catch { /* firmware exposes no thermal zone: CPU temp stays unavailable */ }
            load = Counter("Processor Information", "% Processor Utility", "_Total");
            freq = Counter("Processor Information", "Processor Frequency", "_Total");
            StartSmi();
            AppDomain.CurrentDomain.ProcessExit += delegate { Dispose(); };
        }

        static PerformanceCounter Counter(string cat, string name, string inst)
        {
            try { var c = new PerformanceCounter(cat, name, inst, true); c.NextValue(); return c; }
            catch { return null; }
        }

        static string CpuNameFromRegistry()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    string n = Convert.ToString(k.GetValue("ProcessorNameString"));
                    n = Regex.Replace(n, @"\((R|TM|tm)\)|\s+CPU.*$|\s+with .*$", "");
                    return Regex.Replace(n, @"\s+", " ").Trim();
                }
            }
            catch { return ""; }
        }

        void StartSmi()
        {
            try
            {
                var psi = new ProcessStartInfo("nvidia-smi",
                    "--query-gpu=name,temperature.gpu,utilization.gpu,power.draw,memory.used,memory.total,clocks.sm,fan.speed --format=csv,noheader,nounits -lms 1500");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                smi = Process.Start(psi);
                smi.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { OnSmiLine(e.Data); };
                smi.BeginOutputReadLine();
            }
            catch { smi = null; /* no NVIDIA driver: GPU cards show "--" */ }
        }

        void OnSmiLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            string[] p = line.Split(',');
            if (p.Length < 8) return;
            S.GpuName = p[0].Trim().Replace("NVIDIA ", "");
            S.GpuTemp = (int)Num(p[1]);
            S.GpuLoad = Num(p[2]);
            S.GpuPower = Num(p[3]);
            S.GpuMemUsed = Num(p[4]);
            S.GpuMemTotal = Num(p[5]);
            S.GpuClock = Num(p[6]);
            S.GpuFan = (int)Num(p[7]);
        }

        static float Num(string s)
        {
            float v;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        public void Poll()
        {
            try { if (tz != null) { float k = tz.NextValue(); if (k > 200 && k < 420) S.CpuTemp = (int)Math.Round(k - 273.15f); } } catch { }
            try { if (load != null) S.CpuLoad = Math.Max(0, Math.Min(100, load.NextValue())); } catch { }
            try { if (freq != null) S.CpuMHz = freq.NextValue(); } catch { }

            var m = new MemStatus();
            if (GlobalMemoryStatusEx(m))
            {
                S.RamTotal = m.TotalPhys / 1073741824f;
                S.RamUsed = (m.TotalPhys - m.AvailPhys) / 1073741824f;
                S.RamPct = m.Load;
            }

            PowerStatus ps = SystemInformation.PowerStatus;
            S.OnAC = ps.PowerLineStatus != PowerLineStatus.Offline;
            S.Battery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 || ps.BatteryLifePercent > 1f
                ? -1 : (int)Math.Round(ps.BatteryLifePercent * 100);

            Push(S.HCpuTemp, S.CpuTemp); Push(S.HGpuTemp, S.GpuTemp); Push(S.HCpuLoad, S.CpuLoad);
            Push(S.HCpuMHz, S.CpuMHz); Push(S.HGpuLoad, S.GpuLoad); Push(S.HGpuPower, S.GpuPower); Push(S.HRam, S.RamPct);

            if (S.CpuTemp > 0) { S.PeakCpu = Math.Max(S.PeakCpu, S.CpuTemp); S.SumCpu += S.CpuTemp; S.NCpu++; }
            if (S.GpuTemp > 0) S.PeakGpu = Math.Max(S.PeakGpu, S.GpuTemp);
        }

        static void Push(Hist h, float v)
        {
            if (v >= 0) h.Add(v);
        }

        void FillDemo()
        {
            S.CpuName = "13th Gen Intel Core i5-13420H";
            S.GpuName = "GeForce RTX 2050";
            S.CpuTemp = 71; S.GpuTemp = 58; S.GpuFan = -1; S.Battery = 80; S.OnAC = false;
            S.CpuLoad = 23; S.CpuMHz = 2053; S.RamPct = 71; S.RamUsed = 11.2f; S.RamTotal = 15.7f;
            S.GpuLoad = 12; S.GpuPower = 28; S.GpuClock = 1245; S.GpuMemUsed = 412; S.GpuMemTotal = 4096;
            S.PeakCpu = 92; S.PeakGpu = 66; S.SumCpu = 68 * 400; S.NCpu = 400;
            for (int i = 0; i < Hist.N; i++)
            {
                double w = Math.Sin(i / 6.0);
                S.HCpuTemp.Add((float)(70 + 8 * w)); S.HGpuTemp.Add((float)(58 + 3 * Math.Cos(i / 5.0)));
                S.HCpuLoad.Add((float)(25 + 18 * Math.Sin(i / 3.0))); S.HCpuMHz.Add((float)(2400 + 600 * w));
                S.HGpuLoad.Add((float)(12 + 8 * Math.Sin(i / 4.0))); S.HGpuPower.Add((float)(26 + 6 * Math.Cos(i / 7.0)));
                S.HRam.Add((float)(70 + 2 * w));
            }
        }

        public void Dispose()
        {
            try { if (smi != null && !smi.HasExited) smi.Kill(); } catch { }
            smi = null;
        }
    }
}
