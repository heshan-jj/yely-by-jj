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

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            // Set awareness before creating any WinForms windows.
            try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDpiAwareness(2); }
            catch (EntryPointNotFoundException) { EnableLegacyDpi(); }
            catch (DllNotFoundException) { EnableLegacyDpi(); }
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

        private static void EnableLegacyDpi()
        {
            try { SetProcessDpiAwareness(2); }
            catch (DllNotFoundException) { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        }
    }

    internal class TrayColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return T.Surface; } }
        public override Color ImageMarginGradientBegin { get { return T.Side; } }
        public override Color ImageMarginGradientMiddle { get { return T.Side; } }
        public override Color ImageMarginGradientEnd { get { return T.Side; } }
        public override Color MenuItemSelected { get { return T.Hover; } }
        public override Color MenuItemBorder { get { return T.BorderStrong; } }
        public override Color MenuBorder { get { return T.Border; } }
        public override Color SeparatorDark { get { return T.Border; } }
        public override Color SeparatorLight { get { return T.Surface; } }
        public override Color CheckBackground { get { return T.Selected; } }
        public override Color CheckSelectedBackground { get { return T.Selected; } }
    }

    internal sealed class GlobalShortcutWindow : NativeWindow, IDisposable
    {
        private const int ShortcutId = 0x5945;
        private const uint HotkeyMessage = 0x0312;
        private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
        private readonly Action _onShortcut;
        private bool _registered;
        private int _modifiers, _key;
        private bool _disposed;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        public GlobalShortcutWindow(Action onShortcut)
        {
            _onShortcut = onShortcut;
            var parameters = new CreateParams { Caption = "YellyShortcutSink", Parent = new IntPtr(-3) };
            CreateHandle(parameters);
        }

        public bool TrySet(bool enabled, int modifiers, int key, out int error)
        {
            error = 0;
            if (!enabled)
            {
                if (_registered) UnregisterHotKey(Handle, ShortcutId);
                _registered = false;
                return true;
            }
            if (_registered && _modifiers == modifiers && _key == key) return true;

            bool restore = _registered;
            int oldModifiers = _modifiers, oldKey = _key;
            if (restore)
            {
                UnregisterHotKey(Handle, ShortcutId);
                _registered = false;
            }
            uint nativeModifiers = (uint)(modifiers & 7) | ModNoRepeat;
            if (RegisterHotKey(Handle, ShortcutId, nativeModifiers, (uint)key))
            {
                _registered = true;
                _modifiers = modifiers;
                _key = key;
                return true;
            }

            error = Marshal.GetLastWin32Error();
            if (restore && RegisterHotKey(Handle, ShortcutId, (uint)(oldModifiers & 7) | ModNoRepeat, (uint)oldKey))
            {
                _registered = true;
                _modifiers = oldModifiers;
                _key = oldKey;
            }
            return false;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == HotkeyMessage && message.WParam.ToInt32() == ShortcutId)
            {
                _onShortcut();
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_registered) UnregisterHotKey(Handle, ShortcutId);
            _registered = false;
            DestroyHandle();
            _disposed = true;
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
        private readonly GlobalShortcutWindow _shortcutWindow;

        private MainForm _form;
        private bool _isCoolMode;
        private IntPtr _lastHIcon = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public bool IsCoolMode { get { return _isCoolMode; } }

        public YellyContext()
        {
            Settings = Settings.Load();
            T.Dark = true;
            Telemetry = new Telemetry(false);
            Autopilot = new AutopilotEngine(Settings);

            // Read initial power state
            uint boost, max;
            Power.Read(out boost, out max);
            _isCoolMode = Power.IsCool(boost, max);

            // Tray Menu
            _menu = new ContextMenuStrip();
            _menu.Renderer = new ToolStripProfessionalRenderer(new TrayColors());
            _menu.Opening += (s, e) =>
            {
                _menu.BackColor = T.Surface;
                _menu.ForeColor = T.Text;
                foreach (ToolStripItem item in _menu.Items) item.ForeColor = T.Text;
            };
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

            _shortcutWindow = new GlobalShortcutWindow(ToggleCoolMode);
            if (Settings.ShortcutEnabled)
            {
                int error;
                if (!_shortcutWindow.TrySet(true, Settings.ShortcutModifiers, Settings.ShortcutKey, out error))
                    _trayIcon.ShowBalloonTip(3500, "Keyboard shortcut unavailable", ShortcutDisplay(Settings.ShortcutModifiers, Settings.ShortcutKey) + " is already in use or could not be registered.", ToolTipIcon.Warning);
            }

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

        public void ApplyCoolModeCap()
        {
            if (!_isCoolMode) return;
            try { Power.Write(0, (uint)Settings.Cap); }
            catch (Exception ex)
            {
                MessageBox.Show("Error applying the CoolMode processor limit: " + ex.Message, "CoolMode Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ToggleCoolMode()
        {
            SetCoolMode(!_isCoolMode, true);
        }

        public bool ConfigureShortcut(bool enabled, int modifiers, int key)
        {
            int error;
            if (!_shortcutWindow.TrySet(enabled, modifiers, key, out error))
            {
                string reason = error == 1409 ? "That shortcut is already in use by another app." : "Windows could not register that shortcut (error " + error + ").";
                MessageBox.Show(reason + " Choose another key combination and try again.", "Keyboard Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            Settings.ShortcutEnabled = enabled;
            Settings.ShortcutModifiers = modifiers;
            Settings.ShortcutKey = key;
            Settings.Save();
            if (_form != null && !_form.IsDisposed) _form.RefreshUi();
            return true;
        }

        public static string ShortcutDisplay(int modifiers, int key)
        {
            string name = Enum.IsDefined(typeof(Keys), key) ? new KeysConverter().ConvertToString((Keys)key) : "Key " + key;
            return ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "") +
                ((modifiers & 4) != 0 ? "Shift + " : "") + name;
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
            _shortcutWindow.Dispose();
            if (_lastHIcon != IntPtr.Zero) DestroyIcon(_lastHIcon);
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }
    }
}
