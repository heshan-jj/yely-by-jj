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
        private static Mutex _appMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static void Main()
        {
            bool createdNew;
            _appMutex = new Mutex(true, "Global\\CoolModeTray_SingleInstance_Mutex", out createdNew);

            if (!createdNew)
            {
                // Already running: activate existing window
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                        break;
                    }
                }
                return;
            }

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
        private readonly ToolStripMenuItem _dashboardItem;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly System.Windows.Forms.Timer _timer;

        private MainForm _form;
        private bool _isCoolMode;
        private int _cpuTemp = -1;
        private int _gpuTemp = -1;
        private PerformanceCounter _cpuCounter;
        private bool _gpuPolling;
        private IntPtr _lastHIcon = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

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
            _tempItem = new ToolStripMenuItem("CPU: --°C", null, (s, e) => ShowDashboard());
            _tempItem.Enabled = false;
            _tempItem.Font = new Font(_tempItem.Font, FontStyle.Bold);

            _dashboardItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => ShowDashboard());
            _toggleItem = new ToolStripMenuItem("CoolMode (Boost Off)", null, (s, e) => ToggleCoolMode());

            _autostartItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutostart());
            _autostartItem.Checked = IsAutostartEnabled();

            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => Exit());

            _menu.Items.Add(_tempItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_dashboardItem);
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
                    ShowDashboard();
            };
            _trayIcon.DoubleClick += (s, e) => ToggleCoolMode();

            InitCpuCounter();
            _isCoolMode = ReadCurrentIsCoolMode();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1500;
            _timer.Tick += (s, e) => RefreshTelemetry();
            _timer.Start();

            RefreshTelemetry();
            UpdateUi();

            // Show dashboard on first launch so user sees the app running
            ShowDashboard();
        }

        public void ShowDashboard()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
            }
            _form.Show();
            _form.WindowState = FormWindowState.Normal;
            _form.Activate();
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

        public bool ReadCurrentIsCoolMode()
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

        public void SetCoolMode(bool enable)
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
                    1200,
                    enable ? "CoolMode Enabled" : "Normal Mode Enabled",
                    enable ? "CPU Boost Off (99% cap). Running cool & quiet."
                           : "CPU Boost On (100%). Full performance active.",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating power setting: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ToggleCoolMode()
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
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _trayIcon.Text = tooltip;

            UpdateTrayIcon(_isCoolMode, _cpuTemp);

            if (_form != null && !_form.IsDisposed)
            {
                _form.UpdateMetrics(_isCoolMode, _cpuTemp, _gpuTemp);
            }
        }

        private void UpdateTrayIcon(bool cool, int temp)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                Color bg;
                if (temp >= 85)
                    bg = Color.FromArgb(220, 50, 40);
                else if (cool)
                    bg = Color.FromArgb(0, 150, 220);
                else
                    bg = Color.FromArgb(235, 130, 20);

                using (var brush = new SolidBrush(bg))
                {
                    g.FillRectangle(brush, 0, 0, 16, 16);
                }

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
                Icon newIcon = Icon.FromHandle(hIcon);

                _trayIcon.Icon = newIcon;

                if (_lastHIcon != IntPtr.Zero)
                {
                    DestroyIcon(_lastHIcon);
                }
                _lastHIcon = hIcon;
            }
        }

        public static bool IsAutostartEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                return key != null && key.GetValue("CoolModeTray") != null;
            }
        }

        public void ToggleAutostart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null) return;
                bool enabled = IsAutostartEnabled();
                if (enabled)
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

        public void Exit()
        {
            _timer.Stop();
            _timer.Dispose();
            if (_cpuCounter != null) _cpuCounter.Dispose();
            if (_lastHIcon != IntPtr.Zero) DestroyIcon(_lastHIcon);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }

    public class MainForm : Form
    {
        private readonly CoolModeContext _context;
        private Label _lblStatus;
        private Label _lblCpu;
        private Label _lblGpu;
        private Button _btnToggle;
        private CheckBox _chkAutostart;

        public MainForm(CoolModeContext context)
        {
            _context = context;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "CoolMode Dashboard";
            this.Size = new Size(360, 260);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(245, 247, 250);

            _lblStatus = new Label
            {
                Location = new Point(20, 20),
                Size = new Size(300, 32),
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var pnlMetrics = new Panel
            {
                Location = new Point(20, 65),
                Size = new Size(305, 65),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _lblCpu = new Label
            {
                Location = new Point(10, 15),
                Size = new Size(140, 35),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Text = "CPU: --°C",
                TextAlign = ContentAlignment.MiddleCenter
            };

            _lblGpu = new Label
            {
                Location = new Point(155, 15),
                Size = new Size(140, 35),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Text = "GPU: --°C",
                TextAlign = ContentAlignment.MiddleCenter
            };

            pnlMetrics.Controls.Add(_lblCpu);
            pnlMetrics.Controls.Add(_lblGpu);

            _btnToggle = new Button
            {
                Location = new Point(20, 145),
                Size = new Size(305, 42),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnToggle.Click += (s, e) => _context.ToggleCoolMode();

            _chkAutostart = new CheckBox
            {
                Text = "Start with Windows",
                Location = new Point(25, 195),
                Size = new Size(180, 22),
                Font = new Font("Segoe UI", 9f),
                Checked = CoolModeContext.IsAutostartEnabled()
            };
            _chkAutostart.CheckedChanged += (s, e) =>
            {
                if (_chkAutostart.Checked != CoolModeContext.IsAutostartEnabled())
                    _context.ToggleAutostart();
            };

            this.Controls.Add(_lblStatus);
            this.Controls.Add(pnlMetrics);
            this.Controls.Add(_btnToggle);
            this.Controls.Add(_chkAutostart);

            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };
        }

        public void UpdateMetrics(bool isCool, int cpuTemp, int gpuTemp)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool, int, int>(UpdateMetrics), isCool, cpuTemp, gpuTemp);
                return;
            }

            if (isCool)
            {
                _lblStatus.Text = "❄️ CoolMode: ACTIVE";
                _lblStatus.ForeColor = Color.FromArgb(0, 120, 200);
                _btnToggle.Text = "Turn CoolMode OFF (Full Boost)";
                _btnToggle.BackColor = Color.FromArgb(230, 240, 250);
                _btnToggle.ForeColor = Color.FromArgb(0, 100, 180);
            }
            else
            {
                _lblStatus.Text = "⚡ Normal Mode (Boost ON)";
                _lblStatus.ForeColor = Color.FromArgb(210, 100, 10);
                _btnToggle.Text = "Turn CoolMode ON (Quiet & Cool)";
                _btnToggle.BackColor = Color.FromArgb(254, 240, 230);
                _btnToggle.ForeColor = Color.FromArgb(190, 80, 0);
            }

            _lblCpu.Text = cpuTemp > 0 ? "CPU: " + cpuTemp + "°C" : "CPU: --";
            _lblCpu.ForeColor = cpuTemp >= 85 ? Color.Red : Color.FromArgb(40, 40, 40);

            _lblGpu.Text = gpuTemp > 0 ? "GPU: " + gpuTemp + "°C" : "GPU: --";
            _lblGpu.ForeColor = gpuTemp >= 85 ? Color.Red : Color.FromArgb(40, 40, 40);
        }
    }
}
