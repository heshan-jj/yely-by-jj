using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Yelly
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
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject.ToString()); } catch { }
            };
            Application.ThreadException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception.ToString()); } catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            bool createdNew;
            _appMutex = new Mutex(true, "Local\\Yelly_SingleInstance_Mutex", out createdNew);

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
            Application.Run(new YellyContext());
        }
    }

    internal class YellyContext : ApplicationContext
    {
        public readonly Settings Settings;
        public readonly Telemetry Telemetry;
        public readonly AutopilotEngine Autopilot;

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _tempItem;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _dashboardItem;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly System.Windows.Forms.Timer _timer;

        private MainForm _form;
        private bool _isCoolMode;
        private IntPtr _lastHIcon = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public bool IsCoolMode { get { return _isCoolMode; } }

        public YellyContext()
        {
            Settings = Settings.Load();
            Telemetry = new Telemetry(false);
            Autopilot = new AutopilotEngine(Settings);

            // Read initial power state
            uint boost, max;
            Power.Read(out boost, out max);
            _isCoolMode = Power.IsCool(boost, max);

            // Tray Menu
            _menu = new ContextMenuStrip();
            _menu.Font = T.F(9f, false);
            _tempItem = new ToolStripMenuItem("CPU: --", null, (s, e) => ShowDashboard());
            _tempItem.Enabled = false;
            _tempItem.Font = T.F(9f, true);

            _dashboardItem = new ToolStripMenuItem("Open Yelly Dashboard", null, (s, e) => ShowDashboard());
            _toggleItem = new ToolStripMenuItem("Toggle CoolMode", null, (s, e) => ToggleCoolMode());

            _autostartItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutostart());
            _autostartItem.Checked = Startup.IsOn();

            var exitItem = new ToolStripMenuItem("Exit Yelly", null, (s, e) => Exit());

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

            // Refresh Timer
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = Settings.Refresh * 1000;
            _timer.Tick += (s, e) => OnTick();
            _timer.Start();

            OnTick();

            if (!Settings.StartMinimized)
            {
                ShowDashboard();
            }
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

        private void OnTick()
        {
            Telemetry.Poll();

            // Evaluate Autopilot rules
            bool? req = Autopilot.Evaluate(Telemetry.S, _isCoolMode);
            if (req.HasValue && req.Value != _isCoolMode)
            {
                SetCoolMode(req.Value, false);
            }

            UpdateUi();
        }

        public void SetCoolMode(bool enable, bool userExplicit)
        {
            try
            {
                uint boostTarget = enable ? 0u : (uint)Settings.OrigBoost;
                uint maxTarget = enable ? (uint)Settings.Cap : (uint)Settings.OrigMax;

                Power.Write(boostTarget, maxTarget);
                _isCoolMode = enable;
                UpdateUi();

                if (Settings.Notify)
                {
                    string title = enable ? "CoolMode Enabled" : "Normal Mode Restored";
                    string desc = enable
                        ? string.Format("CPU Turbo Boost capped at {0}%. Cooling down.", Settings.Cap)
                        : "CPU Turbo Boost unlocked. Maximum burst performance restored.";
                    _trayIcon.ShowBalloonTip(1200, title, desc, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error updating power configuration: " + ex.Message, "CoolMode Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ToggleCoolMode()
        {
            SetCoolMode(!_isCoolMode, true);
        }

        public void ToggleAutostart()
        {
            bool current = Startup.IsOn();
            Startup.Set(!current);
            _autostartItem.Checked = !current;
        }

        private void UpdateUi()
        {
            string tempSummary = (Telemetry.S.CpuTemp > 0 ? "CPU: " + Ui.Temp(Settings, Telemetry.S.CpuTemp) : "CPU: --") +
                                 (Telemetry.S.GpuTemp > 0 ? " | GPU: " + Ui.Temp(Settings, Telemetry.S.GpuTemp) : "");

            _tempItem.Text = tempSummary;
            _toggleItem.Text = _isCoolMode ? "[✓] CoolMode (Active)" : "Enable CoolMode";

            string tooltip = "Yelly - " + (_isCoolMode ? "CoolMode" : "Turbo Boost") + "\n" + tempSummary;
            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _trayIcon.Text = tooltip;

            UpdateTrayIcon(_isCoolMode, Telemetry.S.CpuTemp);

            if (_form != null && !_form.IsDisposed)
            {
                _form.RefreshUi();
            }
        }

        private void UpdateTrayIcon(bool cool, int temp)
        {
            Color bg;
            if (temp >= Settings.Alert)
                bg = T.Red;
            else if (cool)
                bg = T.Accent;
            else
                bg = T.Orange;

            string label = null;
            bool logo = false;
            if (Settings.TrayTemp && temp > 0 && temp < 100)
                label = temp.ToString();
            else if (Ui.Logo != null)
                logo = true;
            else
                label = "Y";

            using (var bmp = RenderTrayBitmap(32, bg, label, logo))
            {
                IntPtr hIcon = bmp.GetHicon();
                _trayIcon.Icon = Icon.FromHandle(hIcon);
                if (_lastHIcon != IntPtr.Zero)
                    DestroyIcon(_lastHIcon);
                _lastHIcon = hIcon;
            }
        }

        /// <summary>Draws the tray glyph at 4x and scales it down so the digits stay smooth.</summary>
        private static Bitmap RenderTrayBitmap(int size, Color bg, string label, bool logo)
        {
            int big = size * 4;
            using (var hi = new Bitmap(big, big, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(hi))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(bg);

                if (logo && Ui.Logo != null)
                {
                    int margin = (int)(big * 0.14f);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Ui.Logo, new Rectangle(margin, margin, big - margin * 2, big - margin * 2));
                }
                else if (!string.IsNullOrEmpty(label))
                {
                    float px = (label.Length >= 2 ? 0.62f : 0.72f) * big;
                    Font font;
                    try { font = new Font("Google Sans", px, FontStyle.Bold, GraphicsUnit.Pixel); }
                    catch (ArgumentException) { font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel); }
                    using (font)
                    using (var brush = new SolidBrush(Color.White))
                    using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(label, font, brush, new RectangleF(0, 0, big, big), format);
                }

                var small = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var down = Graphics.FromImage(small))
                {
                    down.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    down.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    down.CompositingQuality = CompositingQuality.HighQuality;
                    down.DrawImage(hi, new Rectangle(0, 0, size, size));
                }
                return small;
            }
        }

        public void Exit()
        {
            _timer.Stop();
            _timer.Dispose();
            Telemetry.Dispose();
            if (_lastHIcon != IntPtr.Zero) DestroyIcon(_lastHIcon);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }
}
