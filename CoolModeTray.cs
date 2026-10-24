using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CoolModeTray
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CoolModeContext());
        }
    }

    public class CoolModeContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _tempItem;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly System.Windows.Forms.Timer _timer;

        private bool _isCoolMode;
        private int _cpuTemp = -1;
        private int _gpuTemp = -1;
        private PerformanceCounter _cpuCounter;
        private bool _gpuPolling;

        // Windows Power Settings GUIDs
        private static readonly Guid SubGroupCpu = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid SettingPerfBoost = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        private static readonly Guid SettingMaxThrottle = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr pActivePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid pSchemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, uint acValueIndex);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, uint dcValueIndex);

        public CoolModeContext()
        {
            _menu = new ContextMenuStrip();
            _tempItem = new ToolStripMenuItem("CPU: --°C", null, (s, e) => ToggleCoolMode());
            _tempItem.Enabled = false;
            _tempItem.Font = new Font(_tempItem.Font, FontStyle.Bold);

            _toggleItem = new ToolStripMenuItem("CoolMode (Boost Off)", null, (s, e) => ToggleCoolMode());

            _autostartItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutostart());
            _autostartItem.Checked = IsAutostartEnabled();

            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Exit());

            _menu.Items.Add(_tempItem);
            _menu.Items.Add(_toggleItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_autostartItem);
            _menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                ContextMenuStrip = _menu,
                Visible = true
            };
            _trayIcon.Click += (s, e) =>
            {
                MouseEventArgs me = e as MouseEventArgs;
                if (me != null && me.Button == MouseButtons.Left)
                    ToggleCoolMode();
            };

            InitCpuCounter();
            _isCoolMode = ReadCurrentIsCoolMode();

            // Setup 1.5s refresh timer
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1500;
            _timer.Tick += (s, e) => RefreshTelemetry();
            _timer.Start();

            RefreshTelemetry();
            UpdateUi();
        }

        private void InitCpuCounter()
        {
            try
            {
                var cat = new PerformanceCounterCategory("Thermal Zone Information");
                string[] instances = cat.GetInstanceNames();
                if (instances.Length > 0)
                {
                    _cpuCounter = new PerformanceCounter("Thermal Zone Information", "Temperature", instances[0]);
                }
            }
            catch
            {
                _cpuCounter = null;
            }
        }

        private void RefreshTelemetry()
        {
            // Read CPU thermal zone
            if (_cpuCounter != null)
            {
                try
                {
                    float kelvin = _cpuCounter.NextValue();
                    if (kelvin > 100f && kelvin < 500f)
                        _cpuTemp = (int)Math.Round(kelvin - 273.15f);
                }
                catch
                {
                    InitCpuCounter();
                }
            }

            // Async background poll for GPU temp via nvidia-smi
            if (!_gpuPolling)
            {
                _gpuPolling = true;
                ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "nvidia-smi",
                            Arguments = "--query-gpu=temperature.gpu --format=csv,noheader,nounits",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true
                        };
                        using (var p = Process.Start(psi))
                        {
                            if (p != null)
                            {
                                string output = p.StandardOutput.ReadToEnd();
                                p.WaitForExit(1000);
                                int val;
                                if (int.TryParse(output.Trim(), out val))
                                {
                                    _gpuTemp = val;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // nvidia-smi not available or no nvidia gpu
                    }
                    finally
                    {
                        _gpuPolling = false;
                    }
                });
            }

            UpdateUi();
        }

        private Guid GetActiveSchemeGuid()
        {
            IntPtr ptr;
            PowerGetActiveScheme(IntPtr.Zero, out ptr);
            return (Guid)Marshal.PtrToStructure(ptr, typeof(Guid));
        }

        private bool ReadCurrentIsCoolMode()
        {
            try
            {
                Guid scheme = GetActiveSchemeGuid();
                Guid sub = SubGroupCpu;
                Guid boost = SettingPerfBoost;
                Guid maxThrot = SettingMaxThrottle;

                uint boostVal = 2;
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref boost, out boostVal);

                uint maxVal = 100;
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref maxThrot, out maxVal);

                return boostVal == 0 || maxVal < 100;
            }
            catch
            {
                return false;
            }
        }

        private void SetCoolMode(bool enable)
        {
            try
            {
                Guid scheme = GetActiveSchemeGuid();
                Guid sub = SubGroupCpu;
                Guid boost = SettingPerfBoost;
                Guid maxThrot = SettingMaxThrottle;

                uint boostTarget = enable ? 0u : 2u;
                uint maxThrottle = enable ? 99u : 100u;

                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref boost, boostTarget);
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref boost, boostTarget);
                PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref maxThrot, maxThrottle);
                PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref maxThrot, maxThrottle);
                PowerSetActiveScheme(IntPtr.Zero, ref scheme);

                _isCoolMode = enable;
                UpdateUi();

                _trayIcon.ShowBalloonTip(
                    1500,
                    enable ? "CoolMode Enabled" : "Normal Mode Enabled",
                    enable ? "CPU Turbo Boost disabled (99% cap). Temps & fans will cool down."
                           : "CPU Turbo Boost enabled (100%). Full performance restored.",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error toggling power setting: " + ex.Message, "CoolMode Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ToggleCoolMode()
        {
            SetCoolMode(!_isCoolMode);
        }

        private void UpdateUi()
        {
            string tempSummary = (_cpuTemp > 0 ? "CPU: " + _cpuTemp + "°C" : "CPU: --") +
                                 (_gpuTemp > 0 ? " | GPU: " + _gpuTemp + "°C" : "");

            _tempItem.Text = tempSummary;
            _toggleItem.Text = _isCoolMode ? "[*] CoolMode (ACTIVE)" : "Enable CoolMode";

            string tooltip = (_isCoolMode ? "CoolMode: ACTIVE\n" : "CoolMode: OFF\n") + tempSummary;
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63); // NotifyIcon.Text max 63 chars on older Win32
            _trayIcon.Text = tooltip;

            if (_trayIcon.Icon != null) _trayIcon.Icon.Dispose();
            _trayIcon.Icon = GenerateTrayIcon(_isCoolMode, _cpuTemp);
        }

        private static Icon GenerateTrayIcon(bool cool, int temp)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                Color bg;
                if (temp >= 85)
                    bg = Color.FromArgb(220, 50, 40);      // High temp alert: Red
                else if (cool)
                    bg = Color.FromArgb(0, 150, 220);      // CoolMode active: Cyan/Blue
                else
                    bg = Color.FromArgb(235, 130, 20);     // Normal mode: Amber

                using (var brush = new SolidBrush(bg))
                {
                    g.FillRectangle(brush, 0, 0, 16, 16);
                }

                // If temp available, render 2-digit temperature; else render 'C' or 'N'
                string label = temp > 0 && temp < 100 ? temp.ToString() : (cool ? "C" : "N");
                float fontSize = label.Length >= 2 ? 7.0f : 8.5f;

                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Tahoma", fontSize, FontStyle.Bold))
                {
                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(label, font, textBrush, new RectangleF(-1, 0, 18, 16), format);
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private static bool IsAutostartEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                return key != null && key.GetValue("CoolModeTray") != null;
            }
        }

        private void ToggleAutostart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null) return;
                if (_autostartItem.Checked)
                {
                    key.DeleteValue("CoolModeTray", false);
                    _autostartItem.Checked = false;
                }
                else
                {
                    key.SetValue("CoolModeTray", "\"" + Application.ExecutablePath + "\"");
                    _autostartItem.Checked = true;
                }
            }
        }

        private void Exit()
        {
            _timer.Stop();
            _timer.Dispose();
            if (_cpuCounter != null) _cpuCounter.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }
}
