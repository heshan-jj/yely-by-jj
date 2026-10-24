using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject.ToString()); } catch { }
            };
            Application.ThreadException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception.ToString()); } catch { }
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

    internal class CoolModeContext : ApplicationContext
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

        public CoolModeContext()
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
            _tempItem = new ToolStripMenuItem("CPU: --", null, (s, e) => ShowDashboard());
            _tempItem.Enabled = false;
            _tempItem.Font = new Font(_tempItem.Font, FontStyle.Bold);

            _dashboardItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => ShowDashboard());
            _toggleItem = new ToolStripMenuItem("Toggle CoolMode", null, (s, e) => ToggleCoolMode());

            _autostartItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutostart());
            _autostartItem.Checked = Startup.IsOn();

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

            string tooltip = (_isCoolMode ? "CoolMode: ACTIVE\n" : "CoolMode: OFF\n") + tempSummary;
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
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                Color bg;
                if (temp >= Settings.Alert)
                    bg = T.Red;
                else if (cool)
                    bg = T.Accent;
                else
                    bg = T.Orange;

                using (var brush = new SolidBrush(bg))
                {
                    g.FillRectangle(brush, 0, 0, 16, 16);
                }

                string label = Settings.TrayTemp && temp > 0 && temp < 100
                    ? temp.ToString()
                    : (cool ? "C" : "N");
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
