using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Yelly
{
    internal class MainForm : Form
    {
        private readonly YellyContext _context;
        private const int LayoutWidth = 800, LayoutHeight = 540;
        private float _displayScale = 1f;
        private int _selectedNav = 0; // 0 = Dashboard, 1 = Autopilot, 2 = Hardware, 3 = Settings
        private int _hardwareScroll;
        private int _hardwareContentBottom;

        private Rectangle _closeBtnRect;
        private Rectangle _minBtnRect;
        private bool _closeHover;
        private bool _minHover;

        // Interactive regions for click handling
        private Rectangle _heroBtnRect;
        private Rectangle _nav0Rect;
        private Rectangle _nav1Rect;
        private Rectangle _nav2Rect;
        private Rectangle _nav3Rect;
        private Rectangle _addRuleBtnRect;
        private Rectangle _toggleAutopilotRect;
        private Rectangle _toggleBatteryRect;
        private Rectangle _toggleTempRect;
        private Rectangle _toggleStartRect;
        private Rectangle _toggleTrayTempRect;
        private Rectangle _toggleUnitRect;
        private Rectangle _toggleShortcutRect;
        private Rectangle _shortcutCaptureRect;
        private Rectangle _capMinusRect;
        private Rectangle _capPlusRect;
        private List<Rectangle> _ruleDeleteRects = new List<Rectangle>();

        private Point _mousePos = Point.Empty;
        private readonly System.Windows.Forms.Timer _anim;
        private int _animClock;
        private int _hoverNav = -1;
        private float _pillY;
        private float _pillTarget;
        private float _hoverY;
        private float _hoverA;
        private float _closeA;
        private float _minA;
        private float _heroA;
        private bool _heroInside;
        private float _pageT = 1f;
        private int _pageFrom;
        private int _pageTo;
        private int _slideDir = 1;
        private bool _pageSnap;
        private Bitmap _pageA;
        private Bitmap _pageB;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        private Point ToLayout(Point point)
        {
            return new Point((int)(point.X / _displayScale), (int)(point.Y / _displayScale));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            float dpi;
            try { dpi = GetDpiForWindow(Handle); }
            catch (EntryPointNotFoundException) { using (var g = CreateGraphics()) dpi = g.DpiX; }
            ApplyDisplayScale(dpi, Screen.FromHandle(Handle).WorkingArea);
        }

        private void ApplyDisplayScale(float dpi, Rectangle workingArea)
        {
            // Fit smaller work areas without allowing the fixed dashboard to clip.
            float fit = Math.Min((workingArea.Width - 16f) / LayoutWidth, (workingArea.Height - 16f) / LayoutHeight);
            _displayScale = Math.Max(0.25f, Math.Min(dpi / 96f, fit));
            ClientSize = new Size((int)Math.Round(LayoutWidth * _displayScale), (int)Math.Round(LayoutHeight * _displayScale));
            ResetPageImages();
            Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x02E0) // WM_DPICHANGED, including moves between monitors.
            {
                var suggested = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                Rectangle bounds = Rectangle.FromLTRB(suggested.Left, suggested.Top, suggested.Right, suggested.Bottom);
                Rectangle work = Screen.FromRectangle(bounds).WorkingArea;
                ApplyDisplayScale(message.WParam.ToInt32() & 0xffff, work);
                Location = new Point(Math.Max(work.Left, Math.Min(bounds.Left, work.Right - Width)),
                    Math.Max(work.Top, Math.Min(bounds.Top, work.Bottom - Height)));
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        private void ResetPageImages()
        {
            if (_pageA != null) { _pageA.Dispose(); _pageA = null; }
            if (_pageB != null) { _pageB.Dispose(); _pageB = null; }
            _pageT = 1f;
            _pageSnap = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_anim != null) _anim.Dispose();
                ResetPageImages();
            }
            base.Dispose(disposing);
        }

        public MainForm(YellyContext context)
        {
            _context = context;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.OptimizedDoubleBuffer, true);

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Size = new Size(800, 540);
            this.BackColor = T.Bg;
            this.ShowInTaskbar = true;
            this.Text = "Yelly";
            try
            {
                if (File.Exists("Yelly.ico"))
                    this.Icon = new Icon("Yelly.ico");
                else
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            _closeBtnRect = new Rectangle(LayoutWidth - 36, 10, 26, 26);
            _minBtnRect = new Rectangle(LayoutWidth - 66, 10, 26, 26);

            _nav0Rect = new Rectangle(12, 64, 156, 36);
            _nav1Rect = new Rectangle(12, 104, 156, 36);
            _nav2Rect = new Rectangle(12, 144, 156, 36);
            _nav3Rect = new Rectangle(12, 184, 156, 36);
            _pillY = _pillTarget = _nav0Rect.Y;
            _hoverY = _nav0Rect.Y;

            _anim = new System.Windows.Forms.Timer();
            _anim.Interval = 15;
            _anim.Tick += (s, e) => StepAnim();
            _animClock = Environment.TickCount;

            this.MouseLeave += (s, e) =>
            {
                _mousePos = new Point(-1, -1);
                if (_hoverNav != -1 || _closeHover || _minHover)
                {
                    _hoverNav = -1;
                    _closeHover = false;
                    _minHover = false;
                    Kick();
                }
                this.Cursor = Cursors.Default;
            };

            this.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && ToLayout(e.Location).Y <= 40 && !_closeBtnRect.Contains(ToLayout(e.Location)) && !_minBtnRect.Contains(ToLayout(e.Location)))
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, 0xA1, 0x2, 0);
                }
            };

            this.MouseMove += (s, e) =>
            {
                _mousePos = ToLayout(e.Location);
                bool ch = _closeBtnRect.Contains(ToLayout(e.Location));
                bool mh = _minBtnRect.Contains(ToLayout(e.Location));
                int nav = HitNav(ToLayout(e.Location));
                bool hero = _heroBtnRect.Contains(ToLayout(e.Location));
                if (ch != _closeHover || mh != _minHover || nav != _hoverNav || hero != _heroInside)
                {
                    _closeHover = ch;
                    _minHover = mh;
                    if (nav >= 0 && _hoverA < 0.08f) _hoverY = NavRect(nav).Y;
                    _hoverNav = nav;
                    _heroInside = hero;
                    Kick();
                }

                bool isHand = ch || mh || nav >= 0;
                if (_selectedNav == 0 && _heroBtnRect.Contains(ToLayout(e.Location))) isHand = true;
                if (_selectedNav == 1)
                {
                    if (_addRuleBtnRect.Contains(ToLayout(e.Location)) || _toggleAutopilotRect.Contains(ToLayout(e.Location)) || _toggleBatteryRect.Contains(ToLayout(e.Location)) || _toggleTempRect.Contains(ToLayout(e.Location)))
                        isHand = true;
                    foreach (var r in _ruleDeleteRects) if (r.Contains(ToLayout(e.Location))) isHand = true;
                }
                if (_selectedNav == 3)
                {
                    if (_toggleStartRect.Contains(ToLayout(e.Location)) || _toggleTrayTempRect.Contains(ToLayout(e.Location)) || _toggleUnitRect.Contains(ToLayout(e.Location)) || _toggleShortcutRect.Contains(ToLayout(e.Location)) || _shortcutCaptureRect.Contains(ToLayout(e.Location)) || _capMinusRect.Contains(ToLayout(e.Location)) || _capPlusRect.Contains(ToLayout(e.Location)))
                        isHand = true;
                }

                this.Cursor = isHand ? Cursors.Hand : Cursors.Default;
            };

            this.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;

                if (_closeBtnRect.Contains(ToLayout(e.Location)))
                {
                    this.Hide();
                    return;
                }
                if (_minBtnRect.Contains(ToLayout(e.Location)))
                {
                    this.WindowState = FormWindowState.Minimized;
                    return;
                }

                int clickedNav = HitNav(ToLayout(e.Location));
                if (clickedNav >= 0) { SelectNav(clickedNav); return; }

                // Dashboard click
                if (_selectedNav == 0 && _heroBtnRect.Contains(ToLayout(e.Location)))
                {
                    _context.ToggleCoolMode();
                    return;
                }

                // Autopilot click
                if (_selectedNav == 1)
                {
                    if (_toggleAutopilotRect.Contains(ToLayout(e.Location)))
                    {
                        _context.Settings.Autopilot = !_context.Settings.Autopilot;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleBatteryRect.Contains(ToLayout(e.Location)))
                    {
                        _context.Settings.BatteryRule = !_context.Settings.BatteryRule;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleTempRect.Contains(ToLayout(e.Location)))
                    {
                        _context.Settings.TempRule = !_context.Settings.TempRule;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_addRuleBtnRect.Contains(ToLayout(e.Location)))
                    {
                        PromptAddRule();
                        return;
                    }
                    for (int i = 0; i < _ruleDeleteRects.Count; i++)
                    {
                        if (_ruleDeleteRects[i].Contains(ToLayout(e.Location)) && i < _context.Settings.Rules.Count)
                        {
                            _context.Settings.Rules.RemoveAt(i);
                            _context.Settings.Save();
                            this.Invalidate();
                            return;
                        }
                    }
                }

                // Settings click
                if (_selectedNav == 3)
                {
                    if (_capMinusRect.Contains(ToLayout(e.Location))) { ChangeCoolModeCap(-1); return; }
                    if (_capPlusRect.Contains(ToLayout(e.Location))) { ChangeCoolModeCap(1); return; }
                    if (_toggleShortcutRect.Contains(ToLayout(e.Location)))
                    {
                        _context.ConfigureShortcut(!_context.Settings.ShortcutEnabled, _context.Settings.ShortcutModifiers, _context.Settings.ShortcutKey);
                        Invalidate();
                        return;
                    }
                    if (_shortcutCaptureRect.Contains(ToLayout(e.Location)))
                    {
                        PromptShortcut();
                        return;
                    }
                    if (_toggleStartRect.Contains(ToLayout(e.Location)))
                    {
                        _context.ToggleAutostart();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleTrayTempRect.Contains(ToLayout(e.Location)))
                    {
                        _context.Settings.TrayTemp = !_context.Settings.TrayTemp;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleUnitRect.Contains(ToLayout(e.Location)))
                    {
                        _context.Settings.Fahrenheit = !_context.Settings.Fahrenheit;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                }
            };

            this.MouseWheel += (s, e) =>
            {
                if (_selectedNav != 2) return;
                _hardwareScroll = Math.Max(0, Math.Min(MaxHardwareScroll(), _hardwareScroll - e.Delta / 120 * 42));
                Invalidate();
            };
        }

        private int HitNav(Point point)
        {
            if (_nav0Rect.Contains(point)) return 0;
            if (_nav1Rect.Contains(point)) return 1;
            if (_nav2Rect.Contains(point)) return 2;
            if (_nav3Rect.Contains(point)) return 3;
            return -1;
        }

        private void PromptAddRule()
        {
            using (var prompt = new Form())
            {
                prompt.Width = 460;
                prompt.Height = 210;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "Add Autopilot Rule";
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.BackColor = T.Bg;
                prompt.ForeColor = T.Text;
                prompt.AutoScaleMode = AutoScaleMode.None;
                prompt.ClientSize = new Size(440, 168);

                var lbl = new Label() { Left = 20, Top = 16, Width = 400, Text = "Process Name (e.g. game.exe, blender, chrome):", Font = T.F(10f, false), ForeColor = T.Text, BackColor = T.Bg };
                var txt = new TextBox() { Left = 20, Top = 44, Width = 400, Font = T.F(10f, false), BackColor = T.Surface, ForeColor = T.Text };

                var combo = new ComboBox() { Left = 20, Top = 80, Width = 400, DropDownStyle = ComboBoxStyle.DropDownList, Font = T.F(10f, false), BackColor = T.Surface, ForeColor = T.Text };
                combo.Items.Add("Disable CoolMode (Allow Full Boost while running)");
                combo.Items.Add("Enable CoolMode (Enforce Cooling while running)");
                combo.SelectedIndex = 0;

                var btnOk = new Button() { Text = "Add Rule", Left = 220, Width = 96, Top = 124, DialogResult = DialogResult.OK, Font = T.F(10f, true), BackColor = T.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var btnCancel = new Button() { Text = "Cancel", Left = 324, Width = 96, Top = 124, DialogResult = DialogResult.Cancel, Font = T.F(10f, false), BackColor = T.Surface, ForeColor = T.Text, FlatStyle = FlatStyle.Flat };

                prompt.Controls.Add(lbl);
                prompt.Controls.Add(txt);
                prompt.Controls.Add(combo);
                prompt.Controls.Add(btnOk);
                prompt.Controls.Add(btnCancel);
                prompt.AcceptButton = btnOk;
                prompt.CancelButton = btnCancel;
                prompt.Scale(new SizeF(_displayScale, _displayScale));

                if (prompt.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(txt.Text.Trim()))
                {
                    string exeName = txt.Text.Trim();
                    if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        exeName += ".exe";

                    _context.Settings.Rules.Add(new Rule
                    {
                        Exe = exeName,
                        Disable = (combo.SelectedIndex == 0)
                    });
                    _context.Settings.Save();
                    this.Invalidate();
                }
            }
        }

        private void PromptShortcut()
        {
            using (var prompt = new Form())
            {
                prompt.Text = "Choose Keyboard Shortcut";
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.ShowInTaskbar = false;
                prompt.AutoScaleMode = AutoScaleMode.None;
                prompt.ClientSize = new Size(420, 148);
                prompt.BackColor = T.Bg;
                prompt.ForeColor = T.Text;
                prompt.KeyPreview = true;

                var instruction = new Label { Left = 20, Top = 18, Width = 380, Height = 34,
                    Text = "Press a key combination using Ctrl, Alt, or Shift.", Font = T.F(10f, false), ForeColor = T.Text, BackColor = T.Bg };
                string display = YellyContext.ShortcutDisplay(_context.Settings.ShortcutModifiers, _context.Settings.ShortcutKey);
                var preview = new Label { Left = 20, Top = 58, Width = 380, Height = 28,
                    Text = "Current: " + display, TextAlign = ContentAlignment.MiddleCenter,
                    Font = T.F(11f, true), ForeColor = T.Text, BackColor = T.Surface, BorderStyle = BorderStyle.FixedSingle };
                var cancel = new Button { Text = "Cancel", Left = 304, Top = 102, Width = 96, Height = 30,
                    DialogResult = DialogResult.Cancel, Font = T.F(10f, false), BackColor = T.Surface, ForeColor = T.Text, FlatStyle = FlatStyle.Flat };
                prompt.Controls.Add(instruction);
                prompt.Controls.Add(preview);
                prompt.Controls.Add(cancel);
                prompt.CancelButton = cancel;

                int newModifiers = _context.Settings.ShortcutModifiers;
                int newKey = _context.Settings.ShortcutKey;
                bool captured = false;
                prompt.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) { prompt.DialogResult = DialogResult.Cancel; prompt.Close(); return; }
                    int modifiers = 0;
                    if (e.Control) modifiers |= 2;
                    if (e.Alt) modifiers |= 1;
                    if (e.Shift) modifiers |= 4;
                    if (modifiers == 0 || e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey) return;
                    newModifiers = modifiers;
                    newKey = (int)e.KeyCode;
                    captured = true;
                    prompt.DialogResult = DialogResult.OK;
                    prompt.Close();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                };

                prompt.Scale(new SizeF(_displayScale, _displayScale));
                if (prompt.ShowDialog(this) == DialogResult.OK && captured)
                    _context.ConfigureShortcut(_context.Settings.ShortcutEnabled, newModifiers, newKey);
                Invalidate();
            }
        }

        private void ChangeCoolModeCap(int change)
        {
            int next = Math.Max(50, Math.Min(100, _context.Settings.Cap + change));
            if (next == _context.Settings.Cap) return;
            _context.Settings.Cap = next;
            _context.Settings.Save();
            if (_context.IsCoolMode) _context.ApplyCoolModeCap();
            Invalidate();
        }

        private Rectangle NavRect(int index)
        {
            if (index == 1) return _nav1Rect;
            if (index == 2) return _nav2Rect;
            if (index == 3) return _nav3Rect;
            return _nav0Rect;
        }

        private void SelectNav(int index)
        {
            if (index == _selectedNav && _pageT >= 0.999f) return;
            _slideDir = index >= _pageTo ? 1 : -1;
            _pageFrom = _pageT >= 0.999f ? _selectedNav : _pageTo;
            _pageTo = index;
            _selectedNav = index;
            if (index == 2) _hardwareScroll = Math.Min(_hardwareScroll, MaxHardwareScroll());
            _pageT = 0f;
            _pageSnap = true;
            _pillTarget = NavRect(index).Y;
            Kick();
        }

        private void Kick()
        {
            _animClock = Environment.TickCount;
            if (!_anim.Enabled) _anim.Start();
            this.Invalidate();
        }

        private static float Approach(float current, float target, float dt, float speed)
        {
            float next = current + (target - current) * (1f - (float)Math.Exp(-speed * dt));
            if (Math.Abs(target - next) < 0.004f) return target;
            return next;
        }

        private static float EaseOut(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                a.R + (int)((b.R - a.R) * t),
                a.G + (int)((b.G - a.G) * t),
                a.B + (int)((b.B - a.B) * t));
        }

        private void StepAnim()
        {
            int now = Environment.TickCount;
            int elapsed = now - _animClock;
            _animClock = now;
            if (elapsed < 1 || elapsed > 80) elapsed = 16;
            float dt = elapsed / 1000f;

            float pill = Approach(_pillY, _pillTarget, dt, 18f);
            float hoverA = Approach(_hoverA, _hoverNav >= 0 && _hoverNav != _selectedNav ? 1f : 0f, dt, 22f);
            float hoverY = _hoverY;
            if (_hoverNav >= 0) hoverY = Approach(_hoverY, NavRect(_hoverNav).Y, dt, 22f);
            float closeA = Approach(_closeA, _closeHover ? 1f : 0f, dt, 24f);
            float minA = Approach(_minA, _minHover ? 1f : 0f, dt, 24f);
            float heroA = Approach(_heroA, _heroInside && _selectedNav == 0 ? 1f : 0f, dt, 20f);
            float page = _pageT >= 1f ? 1f : Math.Min(1f, _pageT + dt / 0.28f);

            bool busy = pill != _pillY || hoverA != _hoverA || hoverY != _hoverY || closeA != _closeA || minA != _minA || heroA != _heroA || page != _pageT;
            _pillY = pill;
            _hoverA = hoverA;
            _hoverY = hoverY;
            _closeA = closeA;
            _minA = minA;
            _heroA = heroA;
            _pageT = page;
            if (!busy) _anim.Stop();
            else this.Invalidate();
        }

        public void RefreshUi()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(RefreshUi));
                return;
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.ScaleTransform(_displayScale, _displayScale);
            Ui.Prepare(g);

            // 1. Draw Notion Sidebar
            int sideW = 180;
            using (var b = new SolidBrush(T.Side))
            {
                g.FillRectangle(b, 0, 0, sideW, LayoutHeight);
            }
            Ui.Line(g, T.Border, sideW, 0, sideW, LayoutHeight);

            // Workspace Header
            Rectangle brandRow = new Rectangle(16, 16, sideW - 32, 24);
            if (Ui.Logo != null)
            {
                Ui.DrawLogo(g, new Rectangle(brandRow.Location, new Size(24, 24)));
                // Google Sans' visible capitals sit above its line-box center.
                Ui.TextIn(g, "Yelly", T.F(12f, true), T.Text, new Rectangle(brandRow.X + 30, brandRow.Y + 3, brandRow.Width - 30, brandRow.Height), TextFormatFlags.VerticalCenter, T.Side);
            }
            else
            {
                Ui.TextIn(g, "Yelly", T.F(12f, true), T.Text, brandRow, TextFormatFlags.VerticalCenter, T.Side);
            }

            if (_hoverA > 0.01f)
            {
                var hover = _nav0Rect;
                hover.Y = (int)Math.Round(_hoverY);
                Ui.Fill(g, Color.FromArgb((int)(255 * _hoverA), T.Hover), hover, 6);
            }
            var pill = _nav0Rect;
            pill.Y = (int)Math.Round(_pillY);
            Ui.Fill(g, T.Selected, pill, 6);

            DrawNavItem(g, _nav0Rect, "Dashboard", 0);
            DrawNavItem(g, _nav1Rect, "Autopilot", 1);
            DrawNavItem(g, _nav2Rect, "Hardware", 2);
            DrawNavItem(g, _nav3Rect, "Settings", 3);

            // Sidebar footer device info
            string devInfo = !string.IsNullOrEmpty(_context.Telemetry.S.CpuName)
                ? _context.Telemetry.S.CpuName
                : "Windows PC";
            Ui.TextIn(g, devInfo, T.F(9f, false), T.Text3, new Rectangle(14, LayoutHeight - 40, sideW - 28, 22), 0, T.Side);

            // 2. Main page, sliding and fading between sections
            int pageX = sideW + 28;
            int pageW = LayoutWidth - pageX - 28;
            if (_pageT >= 0.999f)
            {
                DrawPage(g, _selectedNav, pageX, pageW);
            }
            else
            {
                float eased = EaseOut(_pageT);
                _pageA = EnsurePageBmp(_pageA);
                _pageB = EnsurePageBmp(_pageB);
                if (_pageSnap)
                {
                    RenderPage(_pageA, _pageFrom, pageX, pageW);
                    RenderPage(_pageB, _pageTo, pageX, pageW);
                    _pageSnap = false;
                }
                int away = (int)Math.Round(-48 * eased * _slideDir);
                int into = (int)Math.Round(48 * (1f - eased) * _slideDir);
                var content = new Rectangle(sideW, 0, LayoutWidth - sideW, LayoutHeight);
                g.SetClip(content);
                using (var wash = new SolidBrush(T.Bg))
                    g.FillRectangle(wash, content);
                BlitPage(g, _pageA, away, 1f);
                BlitPage(g, _pageB, into, 1f);
                g.ResetClip();
            }

            DrawWindowControls(g);

            // Outer border
            Ui.Stroke(g, T.BorderStrong, new Rectangle(0, 0, LayoutWidth, LayoutHeight), 0);
        }

        private Bitmap EnsurePageBmp(Bitmap bmp)
        {
            int width = (int)Math.Round(LayoutWidth * _displayScale);
            int height = (int)Math.Round(LayoutHeight * _displayScale);
            if (bmp != null && bmp.Width == width && bmp.Height == height) return bmp;
            if (bmp != null) bmp.Dispose();
            return new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        private void RenderPage(Bitmap bmp, int nav, int pageX, int pageW)
        {
            using (var pg = Graphics.FromImage(bmp))
            {
                pg.Clear(T.Bg);
                pg.ScaleTransform(_displayScale, _displayScale);
                Ui.Prepare(pg);
                DrawPage(pg, nav, pageX, pageW);
            }
        }

        private void DrawPage(Graphics g, int nav, int pageX, int pageW)
        {
            if (nav == 0) DrawDashboardPage(g, pageX, pageW);
            else if (nav == 1) DrawAutopilotPage(g, pageX, pageW);
            else if (nav == 2) DrawHardwarePage(g, pageX, pageW);
            else DrawSettingsPage(g, pageX, pageW);
        }

        private void BlitPage(Graphics g, Bitmap bmp, int dx, float alpha)
        {
            if (bmp == null || alpha <= 0.01f) return;
            int side = 180;
            var dest = new Rectangle(side + dx, 0, LayoutWidth - side, LayoutHeight);
            int sourceX = (int)Math.Round(side * _displayScale);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (alpha >= 0.999f)
            {
                g.DrawImage(bmp, dest, sourceX, 0, bmp.Width - sourceX, bmp.Height, GraphicsUnit.Pixel);
                return;
            }
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm[3, 3] = alpha;
                ia.SetColorMatrix(cm);
                g.DrawImage(bmp, dest, sourceX, 0, bmp.Width - sourceX, bmp.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private void DrawNavItem(Graphics g, Rectangle r, string text, int index)
        {
            bool selected = index == _selectedNav && Math.Abs(_pillY - r.Y) < 2f;
            Color back = selected ? T.Selected : T.Side;
            DrawSectionIcon(g, index == 3 ? 4 : index, new Rectangle(r.X + 10, r.Y + 8, 20, 20), selected ? T.Text : T.Text2, back);
            Ui.TextIn(g, text, T.F(10f, selected), selected ? T.Text : T.Text2, new Rectangle(r.X + 38, r.Y, r.Width - 46, r.Height), TextFormatFlags.VerticalCenter, back);
        }

        private void DrawSectionIcon(Graphics g, int index, Rectangle r, Color color, Color back)
        {
            Font font = T.Icon(14f);
            if (font != null)
            {
                string glyph = index == 0 || index == 2 ? "\uE9D9" : index == 1 ? "\uE945" : "\uE713";
                Ui.TextIn(g, glyph, font, color, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, back);
            }
            else
            {
                using (var pen = new Pen(color, 1.5f))
                {
                    g.DrawRectangle(pen, r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
                    g.DrawLine(pen, r.X + 6, r.Y + r.Height / 2, r.Right - 6, r.Y + r.Height / 2);
                }
            }
        }

        private void DrawPageHeader(Graphics g, int x, int w, int index, string title)
        {
            Rectangle iconRect = new Rectangle(x, 20, 30, 30);
            if (index == 0 && Ui.Logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                Ui.DrawLogo(g, iconRect);
            }
            else DrawSectionIcon(g, index, iconRect, T.Text, T.Bg);
            Ui.TextIn(g, title, T.F(16f, true), T.Text, new Rectangle(x, 56, w, 30), TextFormatFlags.VerticalCenter);
        }

        private void DrawWindowControls(Graphics g)
        {
            if (_minA > 0.01f) Ui.Fill(g, Color.FromArgb((int)(255 * _minA), T.Hover), _minBtnRect, 4);
            Ui.Line(g, T.Text3, _minBtnRect.X + 8, _minBtnRect.Y + 14, _minBtnRect.X + 18, _minBtnRect.Y + 14);

            if (_closeA > 0.01f) Ui.Fill(g, Color.FromArgb((int)(255 * _closeA), T.CalloutRed), _closeBtnRect, 4);
            Color closeColor = _closeA > 0.5f ? T.Red : T.Text3;
            Ui.Line(g, closeColor, _closeBtnRect.X + 8, _closeBtnRect.Y + 8, _closeBtnRect.X + 18, _closeBtnRect.Y + 18);
            Ui.Line(g, closeColor, _closeBtnRect.X + 18, _closeBtnRect.Y + 8, _closeBtnRect.X + 8, _closeBtnRect.Y + 18);
        }

        private void DrawDashboardPage(Graphics g, int x, int w)
        {
            DrawPageHeader(g, x, w, 0, "System Performance & Cooling");

            // Status Properties Header
            int propY = 102;
            Ui.TextIn(g, "Status:", T.F(9f, false), T.Text3, new Rectangle(x, propY, 58, 22), TextFormatFlags.VerticalCenter);
            int tagW = Ui.Tag(g, _context.IsCoolMode ? "CoolMode Active" : "Turbo Boost Active",
                _context.IsCoolMode ? T.TagBlue : T.TagOrange, x + 58, propY);

            int triggerX = x + 74 + tagW;
            Ui.TextIn(g, "Trigger:", T.F(9f, false), T.Text3, new Rectangle(triggerX, propY, 64, 22), TextFormatFlags.VerticalCenter);
            Ui.Tag(g, _context.Autopilot.ActiveDescription, T.TagGray, triggerX + 64, propY, x + w - triggerX - 64);

            // Callout
            Rectangle calloutRect = new Rectangle(x, 138, w, 68);
            Color calloutBg = _context.IsCoolMode ? T.CalloutBlue : T.CalloutYellow;
            Ui.Fill(g, calloutBg, calloutRect, 6);

            string calloutText = _context.IsCoolMode
                ? "CoolMode is active. Turbo Boost is locked off (" + _context.Settings.Cap + "% cap) to keep cores cool and fans quiet."
                : "Full Performance is active. Maximum turbo boost clocks and single-core burst speeds enabled.";
            DrawSectionIcon(g, 1, new Rectangle(calloutRect.X + 14, calloutRect.Y + (calloutRect.Height - 20) / 2, 20, 20), T.Text, calloutBg);
            Ui.TextIn(g, calloutText, T.F(10f, false), T.Text, new Rectangle(calloutRect.X + 42, calloutRect.Y + 8, calloutRect.Width - 56, calloutRect.Height - 16), TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);

            // Hardware Telemetry Cards (Side by side)
            int cardW = (w - 14) / 2;
            int cardY = 222;
            int cardH = 168;

            DrawMetricCard(g, new Rectangle(x, cardY, cardW, cardH),
                "CPU Thermal",
                _context.Telemetry.S.CpuTemp,
                _context.Telemetry.S.CpuName,
                "Load: " + (_context.Telemetry.S.CpuLoad >= 0 ? _context.Telemetry.S.CpuLoad.ToString("F0") + "%" : "--") + "  •  Clock: " + (_context.Telemetry.S.CpuMHz > 0 ? (_context.Telemetry.S.CpuMHz / 1000f).ToString("F1") + " GHz" : "--"),
                _context.Telemetry.S.HCpuTemp);

            DrawMetricCard(g, new Rectangle(x + cardW + 14, cardY, cardW, cardH),
                "NVIDIA GPU",
                _context.Telemetry.S.GpuTemp,
                !string.IsNullOrEmpty(_context.Telemetry.S.GpuName) ? _context.Telemetry.S.GpuName : "Discrete GPU",
                "Load: " + (_context.Telemetry.S.GpuLoad >= 0 ? _context.Telemetry.S.GpuLoad.ToString("F0") + "%" : "--") + "  •  Power: " + (_context.Telemetry.S.GpuPower >= 0 ? _context.Telemetry.S.GpuPower.ToString("F0") + " W" : "--"),
                _context.Telemetry.S.HGpuTemp);

            // Secondary Stats Bar
            int barY = 406;
            string ramStr = _context.Telemetry.S.RamUsed > 0
                ? string.Format("RAM: {0:F1} / {1:F1} GB ({2:F0}%)", _context.Telemetry.S.RamUsed, _context.Telemetry.S.RamTotal, _context.Telemetry.S.RamPct)
                : "RAM: --";
            string pwrStr = _context.Telemetry.S.Battery >= 0
                ? string.Format("Power: {0} ({1}%)", _context.Telemetry.S.OnAC ? "Plugged in" : "On Battery", _context.Telemetry.S.Battery)
                : (_context.Telemetry.S.OnAC ? "Power: AC Connected" : "Power: Battery");

            Ui.TextIn(g, ramStr + "   •   " + pwrStr, T.F(9f, false), T.Text3, new Rectangle(x, barY, w, 22), TextFormatFlags.VerticalCenter);

            // Action button
            _heroBtnRect = new Rectangle(x, 434, w, 46);
            Color btnBg = _context.IsCoolMode
                ? Mix(T.Hover, T.Selected, _heroA)
                : Mix(T.Accent, T.AccentHover, _heroA);
            Color btnFg = _context.IsCoolMode ? T.Text : Color.White;

            Ui.Fill(g, btnBg, _heroBtnRect, 6);
            if (_context.IsCoolMode) Ui.Stroke(g, T.BorderStrong, _heroBtnRect, 6);

            string btnLabel = _context.IsCoolMode
                ? "Turn CoolMode OFF (Restore Full Boost)"
                : "❄  Enable CoolMode (Quiet & Cool)";
            Ui.TextIn(g, btnLabel, T.F(10f, true), btnFg, _heroBtnRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawMetricCard(Graphics g, Rectangle r, string title, int temp, string subtitle, string details, Hist hist)
        {
            Ui.Fill(g, T.Surface, r, 6);
            Ui.Stroke(g, T.Border, r, 6);

            Ui.Text(g, title, T.F(9f, true), T.Text3, r.X + 14, r.Y + 12, T.Surface);

            string tempStr = temp > 0 ? Ui.Temp(_context.Settings, temp) : "--";
            Color numColor = temp >= _context.Settings.Alert ? T.Red : T.Text;
            Ui.Text(g, tempStr, T.F(24f, true), numColor, r.X + 14, r.Y + 32, T.Surface);

            Ui.TextIn(g, subtitle, T.F(9f, true), T.Text2, new Rectangle(r.X + 14, r.Y + 82, r.Width - 28, 20), 0, T.Surface);
            Ui.TextIn(g, details, T.F(9f, false), T.Text3, new Rectangle(r.X + 14, r.Y + 104, r.Width - 28, 20), 0, T.Surface);

            DrawSparkline(g, new Rectangle(r.X + 14, r.Y + 128, r.Width - 28, 26), hist, numColor);
        }

        private void DrawSparkline(Graphics g, Rectangle r, Hist h, Color lineCol)
        {
            if (h.Count < 2) return;
            float max = 100f, min = 40f;
            for (int i = 0; i < h.Count; i++)
            {
                if (h.V[i] > max) max = h.V[i];
                if (h.V[i] < min) min = h.V[i];
            }
            float range = Math.Max(10f, max - min);

            PointF[] pts = new PointF[h.Count];
            for (int i = 0; i < h.Count; i++)
            {
                float px = r.X + (i / (float)(Hist.N - 1)) * r.Width;
                float py = r.Bottom - ((h.V[i] - min) / range) * r.Height;
                pts[i] = new PointF(px, py);
            }

            using (var pen = new Pen(Color.FromArgb(160, lineCol), 1.5f))
            {
                g.DrawLines(pen, pts);
            }
        }

        private int MaxHardwareScroll()
        {
            return Math.Max(0, _hardwareContentBottom - 488);
        }

        private void DrawHardwarePage(Graphics g, int x, int w)
        {
            DrawPageHeader(g, x, w, 2, "Hardware Monitor");
            Ui.TextIn(g, "Live readings reported by Windows, firmware, and the NVIDIA driver.", T.F(10f, false), T.Text3, new Rectangle(x, 88, w, 24), TextFormatFlags.VerticalCenter);

            Rectangle viewport = new Rectangle(x, 128, w, 360);
            GraphicsState saved = g.Save();
            g.SetClip(viewport);
            int y = viewport.Y - _hardwareScroll;
            Snapshot snapshot = _context.Telemetry.S;

            MonitorSection(g, viewport, ref y, "PROCESSOR  ·  " + (string.IsNullOrEmpty(snapshot.CpuName) ? "Name unavailable" : snapshot.CpuName));
            MonitorRow(g, viewport, ref y, "Thermal-zone temperature", snapshot.CpuTemp > 0 ? Ui.Temp(_context.Settings, snapshot.CpuTemp) : "Unavailable");
            MonitorRow(g, viewport, ref y, "Total utilization", snapshot.CpuLoad >= 0 ? snapshot.CpuLoad.ToString("F0") + "%" : "Unavailable");
            MonitorRow(g, viewport, ref y, "Average clock", snapshot.CpuMHz > 0 ? (snapshot.CpuMHz / 1000f).ToString("F2") + " GHz" : "Unavailable");
            MonitorSection(g, viewport, ref y, "LOGICAL PROCESSORS  ·  " + snapshot.Processors.Count);
            if (snapshot.Processors.Count == 0)
                MonitorRow(g, viewport, ref y, "Per-processor readings", "Unavailable on this system");
            else
            {
                foreach (ProcessorReading processor in snapshot.Processors)
                {
                    string name = processor.Name.Replace(',', ':');
                    string load = processor.Load >= 0 ? processor.Load.ToString("F0") + "%" : "--";
                    string clock = processor.MHz > 0 ? (processor.MHz / 1000f).ToString("F2") + " GHz" : "--";
                    MonitorRow(g, viewport, ref y, "CPU " + name, load + "  ·  " + clock);
                }
            }

            MonitorSection(g, viewport, ref y, "GRAPHICS  ·  " + (string.IsNullOrEmpty(snapshot.GpuName) ? "NVIDIA sensor unavailable" : snapshot.GpuName));
            MonitorRow(g, viewport, ref y, "GPU temperature", snapshot.GpuTemp > 0 ? Ui.Temp(_context.Settings, snapshot.GpuTemp) : "Unavailable");
            MonitorRow(g, viewport, ref y, "GPU utilization", snapshot.GpuLoad >= 0 ? snapshot.GpuLoad.ToString("F0") + "%" : "Unavailable");
            MonitorRow(g, viewport, ref y, "GPU fan", snapshot.GpuFan >= 0 ? snapshot.GpuFan + "%" : "Unavailable");
            MonitorRow(g, viewport, ref y, "GPU power", snapshot.GpuPower >= 0 ? snapshot.GpuPower.ToString("F0") + " W" : "Unavailable");
            MonitorRow(g, viewport, ref y, "GPU core clock", snapshot.GpuClock > 0 ? snapshot.GpuClock.ToString("F0") + " MHz" : "Unavailable");
            MonitorRow(g, viewport, ref y, "GPU memory", snapshot.GpuMemUsed >= 0 && snapshot.GpuMemTotal > 0
                ? snapshot.GpuMemUsed.ToString("F0") + " / " + snapshot.GpuMemTotal.ToString("F0") + " MiB" : "Unavailable");

            MonitorSection(g, viewport, ref y, "STORAGE  ·  " + (snapshot.StorageReady ? snapshot.Drives.Count + " physical drives" : "loading drive details"));
            if (!snapshot.StorageReady)
                MonitorRow(g, viewport, ref y, "Drive details", "Loading...");
            else if (snapshot.Drives.Count == 0)
            {
                MonitorRow(g, viewport, ref y, "Physical drives", "Unavailable");
                MonitorRow(g, viewport, ref y, "Drive temperature", "Windows did not report sensor data");
            }
            else
            {
                foreach (StorageReading drive in snapshot.Drives)
                {
                    string driveName = string.IsNullOrEmpty(drive.Name) ? "Physical drive" : drive.Name;
                    MonitorSection(g, viewport, ref y, drive.Type.ToUpperInvariant() + "  ·  " + driveName);
                    MonitorRow(g, viewport, ref y, "Capacity", drive.SizeBytes > 0 ? DriveSize(drive.SizeBytes) : "Unavailable");
                    MonitorRow(g, viewport, ref y, "Temperature", drive.Temperature > 0 ? Ui.Temp(_context.Settings, drive.Temperature) : "Unavailable");
                    MonitorRow(g, viewport, ref y, "Maximum operating temperature", drive.TemperatureMax > 0 ? Ui.Temp(_context.Settings, drive.TemperatureMax) : "Unavailable");
                    MonitorRow(g, viewport, ref y, "Wear indicator", drive.Wear >= 0 ? drive.Wear + "%" : "Unavailable");
                    MonitorRow(g, viewport, ref y, "Power-on hours", drive.PowerOnHours >= 0 ? drive.PowerOnHours.ToString("N0") + " h" : "Unavailable");
                }
            }

            MonitorSection(g, viewport, ref y, "MEMORY");
            MonitorRow(g, viewport, ref y, "Physical RAM", snapshot.RamUsed >= 0 && snapshot.RamTotal > 0
                ? snapshot.RamUsed.ToString("F1") + " / " + snapshot.RamTotal.ToString("F1") + " GB  ·  " + snapshot.RamPct.ToString("F0") + "%"
                : "Unavailable");

            MonitorSection(g, viewport, ref y, "POWER");
            MonitorRow(g, viewport, ref y, "Power source", snapshot.OnAC ? "AC power" : "Battery");
            MonitorRow(g, viewport, ref y, "Battery charge", snapshot.Battery >= 0 ? snapshot.Battery + "%" : "No battery detected");
            MonitorRow(g, viewport, ref y, "Battery time remaining", !snapshot.OnAC && snapshot.BatteryRemainingSeconds > 0
                ? TimeSpan.FromSeconds(snapshot.BatteryRemainingSeconds).ToString(@"h\:mm") : "Unavailable");

            _hardwareContentBottom = y + _hardwareScroll;
            g.Restore(saved);
            int maxScroll = MaxHardwareScroll();
            if (_hardwareScroll > maxScroll) _hardwareScroll = maxScroll;
            string scrollHint = maxScroll > 0
                ? "Scroll for more readings  ·  " + (_hardwareScroll + viewport.Height).ToString() + " / " + _hardwareContentBottom
                : "Unavailable readings are not reported by this device or its drivers.";
            Ui.TextIn(g, scrollHint, T.F(8f, false), T.Text3, new Rectangle(x, 498, w, 22), TextFormatFlags.VerticalCenter);
        }

        private void MonitorSection(Graphics g, Rectangle viewport, ref int y, string title)
        {
            Ui.TextIn(g, title, T.F(9f, true), T.Text3, new Rectangle(viewport.X, y + 4, viewport.Width, 22), TextFormatFlags.VerticalCenter);
            y += 27;
            Ui.Line(g, T.Border, viewport.X, y, viewport.Right, y);
            y += 4;
        }

        private void MonitorRow(Graphics g, Rectangle viewport, ref int y, string label, string value)
        {
            Ui.TextIn(g, label, T.F(9f, false), T.Text2, new Rectangle(viewport.X + 8, y, viewport.Width * 2 / 3, 24), TextFormatFlags.VerticalCenter);
            Ui.TextIn(g, value, T.F(9f, true), T.Text, new Rectangle(viewport.X + viewport.Width * 2 / 3, y, viewport.Width / 3, 24), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            y += 25;
        }

        private static string DriveSize(long bytes)
        {
            const double gib = 1024d * 1024d * 1024d;
            double size = bytes / gib;
            return size >= 1024d ? (size / 1024d).ToString("F2") + " TB" : size.ToString("F0") + " GB";
        }

        private void DrawAutopilotPage(Graphics g, int x, int w)
        {
            DrawPageHeader(g, x, w, 1, "Autopilot Engine");
            Ui.TextIn(g, "Automatically adjust power states based on running games, battery, or thermals.", T.F(10f, false), T.Text3, new Rectangle(x, 88, w, 28), TextFormatFlags.WordBreak);

            _toggleAutopilotRect = new Rectangle(x, 118, w, 32);
            DrawCheckbox(g, _toggleAutopilotRect, "Enable Autopilot Automation", _context.Settings.Autopilot);

            _toggleBatteryRect = new Rectangle(x + 20, 156, w - 20, 28);
            DrawCheckbox(g, _toggleBatteryRect, "Force CoolMode on Battery power", _context.Settings.BatteryRule);

            _toggleTempRect = new Rectangle(x + 20, 188, w - 20, 28);
            DrawCheckbox(g, _toggleTempRect, "Auto CoolMode when CPU exceeds " + _context.Settings.TempOn + "°C", _context.Settings.TempRule);

            int tableY = 232;
            Ui.TextIn(g, "APPLICATION RULES", T.F(9f, true), T.Text3, new Rectangle(x, tableY - 4, w - 124, 26), TextFormatFlags.VerticalCenter);

            _addRuleBtnRect = new Rectangle(x + w - 112, tableY - 4, 112, 26);
            bool addHover = _addRuleBtnRect.Contains(_mousePos);
            Ui.Fill(g, addHover ? T.Hover : T.Bg, _addRuleBtnRect, 4);
            Ui.Stroke(g, T.Border, _addRuleBtnRect, 4);
            Ui.TextIn(g, "+ Add Rule", T.F(9f, true), T.Text, _addRuleBtnRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle tableRect = new Rectangle(x, tableY + 28, w, 200);
            Ui.Fill(g, T.Surface, tableRect, 6);
            Ui.Stroke(g, T.Border, tableRect, 6);

            _ruleDeleteRects.Clear();
            if (_context.Settings.Rules.Count == 0)
            {
                Ui.TextIn(g, "No process rules configured. Click '+ Add Rule' to add a game or app.", T.F(10f, false), T.Text3, tableRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            else
            {
                int rowY = tableRect.Y + 6;
                for (int i = 0; i < Math.Min(5, _context.Settings.Rules.Count); i++)
                {
                    Rule r = _context.Settings.Rules[i];
                    int actionX = tableRect.X + Math.Max(150, (int)(tableRect.Width * 0.40f));
                    Ui.TextIn(g, r.Exe, T.F(10f, true), T.Text, new Rectangle(tableRect.X + 14, rowY + 4, actionX - tableRect.X - 26, 26), TextFormatFlags.VerticalCenter, T.Surface);

                    string actionText = r.Disable ? "Disable CoolMode (Turbo On)" : "Force CoolMode (Quiet)";
                    TagColor tc = r.Disable ? T.TagOrange : T.TagBlue;
                    Ui.Tag(g, actionText, tc, actionX, rowY + 6, tableRect.Right - 52 - actionX);

                    Rectangle delRect = new Rectangle(tableRect.Right - 40, rowY + 4, 26, 26);
                    _ruleDeleteRects.Add(delRect);
                    bool dHover = delRect.Contains(_mousePos);
                    if (dHover) Ui.Fill(g, T.Hover, delRect, 3);
                    Ui.TextIn(g, "✕", T.F(9f, false), T.Text3, delRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    rowY += 36;
                    if (i < _context.Settings.Rules.Count - 1)
                        Ui.Line(g, T.Border, tableRect.X + 8, rowY, tableRect.Right - 8, rowY);
                }
            }
        }

        private void DrawSettingsPage(Graphics g, int x, int w)
        {
            DrawPageHeader(g, x, w, 3, "Preferences & Configuration");
            Ui.TextIn(g, "Fine-tune power profiles and system tray behavior.", T.F(10f, false), T.Text3, new Rectangle(x, 88, w, 24), TextFormatFlags.VerticalCenter);

            int optY = 124;
            _toggleStartRect = new Rectangle(x, optY, w, 32);
            DrawCheckbox(g, _toggleStartRect, "Start with Windows at login", Startup.IsOn());

            optY += 40;
            _toggleTrayTempRect = new Rectangle(x, optY, w, 32);
            DrawCheckbox(g, _toggleTrayTempRect, "Show live 2-digit temperature in System Tray icon", _context.Settings.TrayTemp);

            optY += 40;
            _toggleUnitRect = new Rectangle(x, optY, w, 32);
            DrawCheckbox(g, _toggleUnitRect, "Use Fahrenheit (°F) instead of Celsius (°C)", _context.Settings.Fahrenheit);

            optY += 40;
            Ui.TextIn(g, "COOLMODE PROCESSOR LIMIT", T.F(9f, true), T.Text3, new Rectangle(x, optY, w, 20), TextFormatFlags.VerticalCenter);
            optY += 24;
            int capControlW = 150;
            int capControlX = x + w - capControlW;
            Ui.TextIn(g, "Maximum processor state", T.F(10f, false), T.Text, new Rectangle(x, optY, w - capControlW - 8, 32), TextFormatFlags.VerticalCenter);
            _capMinusRect = new Rectangle(capControlX, optY, 36, 32);
            _capPlusRect = new Rectangle(capControlX + capControlW - 36, optY, 36, 32);
            DrawStepButton(g, _capMinusRect, "−");
            DrawStepButton(g, _capPlusRect, "+");
            Ui.TextIn(g, _context.Settings.Cap + "%", T.F(10f, true), T.Text, new Rectangle(capControlX + 38, optY, capControlW - 76, 32), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            optY += 40;
            Ui.TextIn(g, "KEYBOARD SHORTCUT", T.F(9f, true), T.Text3, new Rectangle(x, optY, w, 20), TextFormatFlags.VerticalCenter);
            optY += 24;
            _toggleShortcutRect = new Rectangle(x, optY, w - 178, 32);
            DrawCheckbox(g, _toggleShortcutRect, "Enable CoolMode shortcut", _context.Settings.ShortcutEnabled);
            _shortcutCaptureRect = new Rectangle(x + w - 168, optY, 168, 32);
            Ui.Fill(g, _shortcutCaptureRect.Contains(_mousePos) ? T.Hover : T.Surface, _shortcutCaptureRect, 5);
            Ui.Stroke(g, T.Border, _shortcutCaptureRect, 5);
            Ui.TextIn(g, YellyContext.ShortcutDisplay(_context.Settings.ShortcutModifiers, _context.Settings.ShortcutKey), T.F(9f, true), T.Text, _shortcutCaptureRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            optY += 44;
            Rectangle infoRect = new Rectangle(x, optY, w, 156);
            Ui.Fill(g, T.Side, infoRect, 6);
            Ui.Stroke(g, T.Border, infoRect, 6);

            int specW = infoRect.Width - 28;
            Ui.TextIn(g, "POWER SPECIFICATION", T.F(9f, true), T.Text3, new Rectangle(infoRect.X + 14, infoRect.Y + 14, specW, 20), 0);
            Ui.TextIn(g, "• Target: Processor Performance Boost Mode (PERFBOOSTMODE: be337238-...)", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 40, specW, 20), 0);
            Ui.TextIn(g, "• Throttle Cap: Maximum Processor State = " + _context.Settings.Cap + "% (AC & DC)", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 62, specW, 20), 0);
            Ui.TextIn(g, "• Normal State: Restores Original Boost (Aggressive) & 100% Cap", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 84, specW, 20), 0);
            Ui.TextIn(g, "• Telemetry: ACPI Thermal Zone Information + NVIDIA Management", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 106, specW, 20), 0);
            Ui.TextIn(g, "• Zero Drivers: Pure Win32 API calls via powrprof.dll (no Ring-0 driver)", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 128, specW, 20), 0);
        }

        private void DrawCheckbox(Graphics g, Rectangle r, string text, bool check)
        {
            Rectangle box = new Rectangle(r.X, r.Y + (r.Height - 16) / 2, 16, 16);
            Ui.Fill(g, check ? T.Accent : T.Surface, box, 4);
            Ui.Stroke(g, check ? T.Accent : T.BorderStrong, box, 4);

            if (check)
            {
                using (var pen = new Pen(Color.White, 1.8f))
                {
                    g.DrawLine(pen, box.X + 3.5f, box.Y + 8f, box.X + 7f, box.Y + 12f);
                    g.DrawLine(pen, box.X + 7f, box.Y + 12f, box.X + 13f, box.Y + 4f);
                }
            }

            Ui.TextIn(g, text, T.F(10f, false), T.Text, new Rectangle(r.X + 26, r.Y, r.Width - 26, r.Height), TextFormatFlags.VerticalCenter);
        }

        private void DrawStepButton(Graphics g, Rectangle r, string text)
        {
            Ui.Fill(g, r.Contains(_mousePos) ? T.Hover : T.Surface, r, 5);
            Ui.Stroke(g, T.Border, r, 5);
            Ui.TextIn(g, text, T.F(12f, true), T.Text, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
