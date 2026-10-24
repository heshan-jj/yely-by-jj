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
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject.ToString());
            };
            Application.ThreadException += (s, e) =>
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception.ToString());
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            bool createdNew;
            _appMutex = new Mutex(true, "Local\\CoolModeTray_SingleInstance_Mutex", out createdNew);

            if (!createdNew)
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9);
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
            _timer.Interval = 1200;
            _timer.Tick += (s, e) => RefreshTelemetry();
            _timer.Start();

            RefreshTelemetry();
            UpdateUi();

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
        private bool _isCoolMode;
        private int _cpuTemp = -1;
        private int _gpuTemp = -1;

        private Rectangle _closeBtnRect;
        private Rectangle _minBtnRect;
        private Rectangle _toggleBtnRect;
        private Rectangle _autostartRect;
        private bool _closeHover;
        private bool _minHover;
        private bool _toggleHover;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public MainForm(CoolModeContext context)
        {
            _context = context;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.OptimizedDoubleBuffer, true);

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(380, 420);
            this.BackColor = Color.FromArgb(18, 21, 28); // Deep obsidian slate
            this.ShowInTaskbar = true;
            this.Text = "CoolMode Dashboard";

            _closeBtnRect = new Rectangle(340, 12, 28, 26);
            _minBtnRect = new Rectangle(308, 12, 28, 26);
            _toggleBtnRect = new Rectangle(24, 290, 332, 54);
            _autostartRect = new Rectangle(24, 362, 170, 24);

            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.Y <= 45 && !_closeBtnRect.Contains(e.Location) && !_minBtnRect.Contains(e.Location))
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, 0xA1, 0x2, 0);
                }
            };

            this.MouseMove += (s, e) =>
            {
                bool ch = _closeBtnRect.Contains(e.Location);
                bool mh = _minBtnRect.Contains(e.Location);
                bool th = _toggleBtnRect.Contains(e.Location);

                if (ch != _closeHover || mh != _minHover || th != _toggleHover)
                {
                    _closeHover = ch;
                    _minHover = mh;
                    _toggleHover = th;
                    this.Invalidate();
                }

                if (th || ch || mh || _autostartRect.Contains(e.Location))
                    this.Cursor = Cursors.Hand;
                else
                    this.Cursor = Cursors.Default;
            };

            this.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (_closeBtnRect.Contains(e.Location))
                    {
                        this.Hide();
                    }
                    else if (_minBtnRect.Contains(e.Location))
                    {
                        this.WindowState = FormWindowState.Minimized;
                    }
                    else if (_toggleBtnRect.Contains(e.Location))
                    {
                        _context.ToggleCoolMode();
                    }
                    else if (_autostartRect.Contains(e.Location))
                    {
                        _context.ToggleAutostart();
                        this.Invalidate(_autostartRect);
                    }
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

            _isCoolMode = isCool;
            _cpuTemp = cpuTemp;
            _gpuTemp = gpuTemp;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 1. Outer subtle border
            using (var borderPen = new Pen(Color.FromArgb(40, 48, 62), 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }

            // 2. Custom Title Bar
            using (var titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(240, 244, 248)))
            {
                g.DrawString("❄  CoolMode", titleFont, titleBrush, 22, 16);
            }

            // Minimize Button
            if (_minHover)
            {
                using (var hb = new SolidBrush(Color.FromArgb(35, 42, 54)))
                {
                    g.FillRectangle(hb, _minBtnRect);
                }
            }
            using (var minPen = new Pen(Color.FromArgb(180, 190, 205), 1.5f))
            {
                g.DrawLine(minPen, _minBtnRect.X + 8, _minBtnRect.Y + 14, _minBtnRect.X + 20, _minBtnRect.Y + 14);
            }

            // Close Button
            if (_closeHover)
            {
                using (var hb = new SolidBrush(Color.FromArgb(220, 50, 50)))
                {
                    g.FillRectangle(hb, _closeBtnRect);
                }
            }
            using (var closePen = new Pen(_closeHover ? Color.White : Color.FromArgb(180, 190, 205), 1.5f))
            {
                g.DrawLine(closePen, _closeBtnRect.X + 9, _closeBtnRect.Y + 8, _closeBtnRect.X + 19, _closeBtnRect.Y + 18);
                g.DrawLine(closePen, _closeBtnRect.X + 19, _closeBtnRect.Y + 8, _closeBtnRect.X + 9, _closeBtnRect.Y + 18);
            }

            // 3. Status Hero Banner
            Rectangle bannerRect = new Rectangle(24, 55, 332, 68);
            Color bannerBg = _isCoolMode ? Color.FromArgb(12, 38, 64) : Color.FromArgb(38, 25, 16);
            Color bannerBorder = _isCoolMode ? Color.FromArgb(2, 132, 199) : Color.FromArgb(217, 119, 6);

            using (var path = GetRoundedPath(bannerRect, 10))
            {
                using (var bb = new SolidBrush(bannerBg))
                {
                    g.FillPath(bb, path);
                }
                using (var bp = new Pen(bannerBorder, 1.2f))
                {
                    g.DrawPath(bp, path);
                }
            }

            // Status Text
            string statusTitle = _isCoolMode ? "❄  COOLMODE ACTIVE" : "⚡  TURBO BOOST ON";
            Color statusColor = _isCoolMode ? Color.FromArgb(56, 189, 248) : Color.FromArgb(251, 146, 60);
            using (var font = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(statusColor))
            {
                g.DrawString(statusTitle, font, brush, bannerRect.X + 16, bannerRect.Y + 14);
            }

            string statusSub = _isCoolMode
                ? "CPU Boost Disabled (99% Cap) • Low Temps & Silent Fans"
                : "Full Performance Active • Maximum Single-Core Clocks";
            using (var font = new Font("Segoe UI", 8.5f))
            using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184)))
            {
                g.DrawString(statusSub, font, brush, bannerRect.X + 16, bannerRect.Y + 40);
            }

            // 4. Two Hardware Sensor Cards
            DrawCard(g, new Rectangle(24, 138, 160, 134), "CPU THERMAL", _cpuTemp, "ACPI Thermal Zone");
            DrawCard(g, new Rectangle(196, 138, 160, 134), "NVIDIA GPU", _gpuTemp, "GeForce RTX 2050");

            // 5. Hero Action Button
            using (var path = GetRoundedPath(_toggleBtnRect, 12))
            {
                Color btnStart, btnEnd;
                if (_isCoolMode)
                {
                    btnStart = _toggleHover ? Color.FromArgb(30, 41, 59) : Color.FromArgb(24, 32, 47);
                    btnEnd = _toggleHover ? Color.FromArgb(40, 52, 72) : Color.FromArgb(30, 41, 59);
                }
                else
                {
                    btnStart = _toggleHover ? Color.FromArgb(14, 165, 233) : Color.FromArgb(2, 132, 199);
                    btnEnd = _toggleHover ? Color.FromArgb(2, 132, 199) : Color.FromArgb(3, 105, 161);
                }

                using (var brush = new LinearGradientBrush(_toggleBtnRect, btnStart, btnEnd, 90f))
                {
                    g.FillPath(brush, path);
                }

                Color outlineColor = _isCoolMode
                    ? (_toggleHover ? Color.FromArgb(71, 85, 105) : Color.FromArgb(51, 65, 85))
                    : (_toggleHover ? Color.FromArgb(125, 211, 252) : Color.FromArgb(56, 189, 248));
                using (var pen = new Pen(outlineColor, 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }

            string btnText = _isCoolMode ? "Turn CoolMode OFF (Full Boost)" : "❄  Activate CoolMode";
            Color btnTextColor = _isCoolMode ? Color.FromArgb(226, 232, 240) : Color.White;
            using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var brush = new SolidBrush(btnTextColor))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(btnText, font, brush, _toggleBtnRect, sf);
            }

            // 6. Footer: Start with Windows & Tray hint
            bool autostart = CoolModeContext.IsAutostartEnabled();
            Rectangle boxRect = new Rectangle(_autostartRect.X, _autostartRect.Y + 4, 14, 14);
            using (var pen = new Pen(Color.FromArgb(100, 116, 139), 1.2f))
            {
                g.DrawRectangle(pen, boxRect);
            }
            if (autostart)
            {
                using (var cb = new SolidBrush(Color.FromArgb(56, 189, 248)))
                {
                    g.FillRectangle(cb, boxRect.X + 2, boxRect.Y + 2, 10, 10);
                }
            }
            using (var font = new Font("Segoe UI", 8.5f))
            using (var brush = new SolidBrush(Color.FromArgb(160, 174, 192)))
            {
                g.DrawString("Start with Windows", font, brush, _autostartRect.X + 22, _autostartRect.Y + 2);
            }

            using (var font = new Font("Segoe UI", 8f))
            using (var brush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString("Minimizes to tray on close", font, brush, 356, _autostartRect.Y + 3, sf);
            }
        }

        private void DrawCard(Graphics g, Rectangle rect, string title, int temp, string subtitle)
        {
            using (var path = GetRoundedPath(rect, 10))
            {
                using (var brush = new SolidBrush(Color.FromArgb(25, 30, 40)))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(Color.FromArgb(38, 46, 60), 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Card Title
            using (var font = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184)))
            {
                g.DrawString(title, font, brush, rect.X + 14, rect.Y + 14);
            }

            // Temp Value & Color
            string tempStr = temp > 0 ? temp.ToString() : "--";
            Color tempColor;
            if (temp >= 85)
                tempColor = Color.FromArgb(244, 63, 94);   // Hot Red
            else if (temp >= 70)
                tempColor = Color.FromArgb(251, 146, 60);  // Warm Orange
            else if (temp > 0)
                tempColor = Color.FromArgb(56, 189, 248);  // Cool Blue
            else
                tempColor = Color.FromArgb(100, 116, 139);

            using (var font = new Font("Segoe UI", 26f, FontStyle.Bold))
            using (var brush = new SolidBrush(tempColor))
            {
                g.DrawString(tempStr, font, brush, rect.X + 10, rect.Y + 30);
            }

            // °C unit symbol next to number
            int offset = tempStr.Length * 20 + 16;
            using (var font = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(148, 163, 184)))
            {
                g.DrawString("°C", font, brush, rect.X + offset, rect.Y + 38);
            }

            // Subtitle
            using (var font = new Font("Segoe UI", 7.5f))
            using (var brush = new SolidBrush(Color.FromArgb(100, 116, 139)))
            {
                g.DrawString(subtitle, font, brush, rect.X + 14, rect.Y + 84);
            }

            // Subtle thermal progress bar (0 - 100°C)
            Rectangle barBg = new Rectangle(rect.X + 14, rect.Y + 106, rect.Width - 28, 6);
            using (var barPath = GetRoundedPath(barBg, 3))
            using (var bBrush = new SolidBrush(Color.FromArgb(38, 46, 60)))
            {
                g.FillPath(bBrush, barPath);
            }

            if (temp > 0)
            {
                int fillWidth = Math.Max(6, Math.Min(barBg.Width, (int)((temp / 100f) * barBg.Width)));
                Rectangle fillRect = new Rectangle(barBg.X, barBg.Y, fillWidth, barBg.Height);
                using (var fillPath = GetRoundedPath(fillRect, 3))
                using (var fBrush = new SolidBrush(tempColor))
                {
                    g.FillPath(fBrush, fillPath);
                }
            }
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
