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
        private int _selectedNav = 0; // 0 = Dashboard, 1 = Autopilot, 2 = Settings

        private Rectangle _closeBtnRect;
        private Rectangle _minBtnRect;
        private bool _closeHover;
        private bool _minHover;

        // Interactive regions for click handling
        private Rectangle _heroBtnRect;
        private Rectangle _nav0Rect;
        private Rectangle _nav1Rect;
        private Rectangle _nav2Rect;
        private Rectangle _addRuleBtnRect;
        private Rectangle _toggleAutopilotRect;
        private Rectangle _toggleBatteryRect;
        private Rectangle _toggleTempRect;
        private Rectangle _toggleStartRect;
        private Rectangle _toggleTrayTempRect;
        private Rectangle _toggleUnitRect;
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

            _closeBtnRect = new Rectangle(this.Width - 36, 10, 26, 26);
            _minBtnRect = new Rectangle(this.Width - 66, 10, 26, 26);

            _nav0Rect = new Rectangle(12, 64, 156, 36);
            _nav1Rect = new Rectangle(12, 104, 156, 36);
            _nav2Rect = new Rectangle(12, 144, 156, 36);
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
                if (e.Button == MouseButtons.Left && e.Y <= 40 && !_closeBtnRect.Contains(e.Location) && !_minBtnRect.Contains(e.Location))
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, 0xA1, 0x2, 0);
                }
            };

            this.MouseMove += (s, e) =>
            {
                _mousePos = e.Location;
                bool ch = _closeBtnRect.Contains(e.Location);
                bool mh = _minBtnRect.Contains(e.Location);
                int nav = _nav0Rect.Contains(e.Location) ? 0 : _nav1Rect.Contains(e.Location) ? 1 : _nav2Rect.Contains(e.Location) ? 2 : -1;
                bool hero = _heroBtnRect.Contains(e.Location);
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
                if (_selectedNav == 0 && _heroBtnRect.Contains(e.Location)) isHand = true;
                if (_selectedNav == 1)
                {
                    if (_addRuleBtnRect.Contains(e.Location) || _toggleAutopilotRect.Contains(e.Location) || _toggleBatteryRect.Contains(e.Location) || _toggleTempRect.Contains(e.Location))
                        isHand = true;
                    foreach (var r in _ruleDeleteRects) if (r.Contains(e.Location)) isHand = true;
                }
                if (_selectedNav == 2)
                {
                    if (_toggleStartRect.Contains(e.Location) || _toggleTrayTempRect.Contains(e.Location) || _toggleUnitRect.Contains(e.Location))
                        isHand = true;
                }

                this.Cursor = isHand ? Cursors.Hand : Cursors.Default;
            };

            this.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;

                if (_closeBtnRect.Contains(e.Location))
                {
                    this.Hide();
                    return;
                }
                if (_minBtnRect.Contains(e.Location))
                {
                    this.WindowState = FormWindowState.Minimized;
                    return;
                }

                if (_nav0Rect.Contains(e.Location)) { SelectNav(0); return; }
                if (_nav1Rect.Contains(e.Location)) { SelectNav(1); return; }
                if (_nav2Rect.Contains(e.Location)) { SelectNav(2); return; }

                // Dashboard click
                if (_selectedNav == 0 && _heroBtnRect.Contains(e.Location))
                {
                    _context.ToggleCoolMode();
                    return;
                }

                // Autopilot click
                if (_selectedNav == 1)
                {
                    if (_toggleAutopilotRect.Contains(e.Location))
                    {
                        _context.Settings.Autopilot = !_context.Settings.Autopilot;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleBatteryRect.Contains(e.Location))
                    {
                        _context.Settings.BatteryRule = !_context.Settings.BatteryRule;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleTempRect.Contains(e.Location))
                    {
                        _context.Settings.TempRule = !_context.Settings.TempRule;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_addRuleBtnRect.Contains(e.Location))
                    {
                        PromptAddRule();
                        return;
                    }
                    for (int i = 0; i < _ruleDeleteRects.Count; i++)
                    {
                        if (_ruleDeleteRects[i].Contains(e.Location) && i < _context.Settings.Rules.Count)
                        {
                            _context.Settings.Rules.RemoveAt(i);
                            _context.Settings.Save();
                            this.Invalidate();
                            return;
                        }
                    }
                }

                // Settings click
                if (_selectedNav == 2)
                {
                    if (_toggleStartRect.Contains(e.Location))
                    {
                        _context.ToggleAutostart();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleTrayTempRect.Contains(e.Location))
                    {
                        _context.Settings.TrayTemp = !_context.Settings.TrayTemp;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                    if (_toggleUnitRect.Contains(e.Location))
                    {
                        _context.Settings.Fahrenheit = !_context.Settings.Fahrenheit;
                        _context.Settings.Save();
                        this.Invalidate();
                        return;
                    }
                }
            };
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

        private Rectangle NavRect(int index)
        {
            if (index == 1) return _nav1Rect;
            if (index == 2) return _nav2Rect;
            return _nav0Rect;
        }

        private void SelectNav(int index)
        {
            if (index == _selectedNav && _pageT >= 0.999f) return;
            _slideDir = index >= _pageTo ? 1 : -1;
            _pageFrom = _pageT >= 0.999f ? _selectedNav : _pageTo;
            _pageTo = index;
            _selectedNav = index;
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
            Ui.Prepare(g);

            // 1. Draw Notion Sidebar
            int sideW = 180;
            using (var b = new SolidBrush(T.Side))
            {
                g.FillRectangle(b, 0, 0, sideW, this.Height);
            }
            Ui.Line(g, T.Border, sideW, 0, sideW, this.Height);

            // Workspace Header
            if (Ui.Logo != null)
            {
                g.DrawImage(Ui.Logo, new Rectangle(16, 16, 24, 24));
                Ui.Text(g, "Yelly", T.F(12f, true), T.Text, 46, 18, T.Side);
            }
            else
            {
                Ui.Text(g, "Yelly", T.F(12f, true), T.Text, 16, 18, T.Side);
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

            DrawNavItem(g, _nav0Rect, "📊  Dashboard", 0);
            DrawNavItem(g, _nav1Rect, "🤖  Autopilot", 1);
            DrawNavItem(g, _nav2Rect, "⚙️  Settings", 2);

            // Sidebar footer device info
            string devInfo = !string.IsNullOrEmpty(_context.Telemetry.S.CpuName)
                ? _context.Telemetry.S.CpuName
                : "Windows PC";
            Ui.TextIn(g, devInfo, T.F(9f, false), T.Text3, new Rectangle(14, this.Height - 40, sideW - 28, 22), 0, T.Side);

            // 2. Main page, sliding and fading between sections
            int pageX = sideW + 28;
            int pageW = this.Width - pageX - 28;
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
                var content = new Rectangle(sideW, 0, this.Width - sideW, this.Height);
                g.SetClip(content);
                using (var wash = new SolidBrush(T.Bg))
                    g.FillRectangle(wash, content);
                BlitPage(g, _pageA, away, 1f);
                BlitPage(g, _pageB, into, 1f);
                g.ResetClip();
            }

            DrawWindowControls(g);

            // Outer border
            Ui.Stroke(g, T.BorderStrong, new Rectangle(0, 0, this.Width, this.Height), 0);
        }

        private Bitmap EnsurePageBmp(Bitmap bmp)
        {
            if (bmp != null && bmp.Width == this.Width && bmp.Height == this.Height) return bmp;
            if (bmp != null) bmp.Dispose();
            return new Bitmap(this.Width, this.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        private void RenderPage(Bitmap bmp, int nav, int pageX, int pageW)
        {
            using (var pg = Graphics.FromImage(bmp))
            {
                pg.Clear(T.Bg);
                Ui.Prepare(pg);
                DrawPage(pg, nav, pageX, pageW);
            }
        }

        private void DrawPage(Graphics g, int nav, int pageX, int pageW)
        {
            if (nav == 0) DrawDashboardPage(g, pageX, pageW);
            else if (nav == 1) DrawAutopilotPage(g, pageX, pageW);
            else DrawSettingsPage(g, pageX, pageW);
        }

        private void BlitPage(Graphics g, Bitmap bmp, int dx, float alpha)
        {
            if (bmp == null || alpha <= 0.01f) return;
            int side = 180;
            var dest = new Rectangle(side + dx, 0, bmp.Width - side, bmp.Height);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            if (alpha >= 0.999f)
            {
                g.DrawImage(bmp, dest, side, 0, bmp.Width - side, bmp.Height, GraphicsUnit.Pixel);
                return;
            }
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm[3, 3] = alpha;
                ia.SetColorMatrix(cm);
                g.DrawImage(bmp, dest, side, 0, bmp.Width - side, bmp.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private void DrawNavItem(Graphics g, Rectangle r, string text, int index)
        {
            bool selected = index == _selectedNav && Math.Abs(_pillY - r.Y) < 2f;
            Color back = selected ? T.Selected : T.Side;
            Ui.TextIn(g, text, T.F(10f, selected), selected ? T.Text : T.Text2, new Rectangle(r.X + 10, r.Y, r.Width - 16, r.Height), TextFormatFlags.VerticalCenter, back);
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
            // Notion Page Title
            if (Ui.Logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Ui.Logo, new Rectangle(x, 20, 30, 30));
                Ui.Text(g, "System Performance & Cooling", T.F(16f, true), T.Text, x, 56);
            }
            else
            {
                Ui.Text(g, "❄️", T.F(20f, false), T.Text, x, 22);
                Ui.Text(g, "System Performance & Cooling", T.F(16f, true), T.Text, x, 56);
            }

            // Status Properties Header
            int propY = 102;
            Ui.TextIn(g, "Status:", T.F(9f, false), T.Text3, new Rectangle(x, propY, 58, 22), TextFormatFlags.VerticalCenter);
            int tagW = Ui.Tag(g, _context.IsCoolMode ? "CoolMode Active" : "Turbo Boost Active",
                _context.IsCoolMode ? T.TagBlue : T.TagOrange, x + 58, propY);

            int triggerX = x + 74 + tagW;
            Ui.TextIn(g, "Trigger:", T.F(9f, false), T.Text3, new Rectangle(triggerX, propY, 64, 22), TextFormatFlags.VerticalCenter);
            Ui.Tag(g, _context.Autopilot.ActiveDescription, T.TagGray, triggerX + 64, propY);

            // Callout
            Rectangle calloutRect = new Rectangle(x, 138, w, 68);
            Color calloutBg = _context.IsCoolMode ? T.CalloutBlue : T.CalloutYellow;
            Ui.Fill(g, calloutBg, calloutRect, 6);

            string calloutText = _context.IsCoolMode
                ? "💡 CoolMode is active. Turbo Boost is locked off (99% cap) to keep cores cool and fans quiet."
                : "⚡ Full Performance is active. Maximum turbo boost clocks and single-core burst speeds enabled.";
            Ui.TextIn(g, calloutText, T.F(10f, false), T.Text, new Rectangle(calloutRect.X + 14, calloutRect.Y + 8, calloutRect.Width - 28, calloutRect.Height - 16), TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);

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

            Ui.Text(g, ramStr + "   •   " + pwrStr, T.F(9f, false), T.Text3, x + 4, barY);

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

        private void DrawAutopilotPage(Graphics g, int x, int w)
        {
            Ui.Text(g, "🤖", T.F(20f, false), T.Text, x, 22);
            Ui.Text(g, "Autopilot Engine", T.F(16f, true), T.Text, x, 56);
            Ui.TextIn(g, "Automatically adjust power states based on running games, battery, or thermals.", T.F(10f, false), T.Text3, new Rectangle(x, 84, w, 28), TextFormatFlags.WordBreak);

            _toggleAutopilotRect = new Rectangle(x, 118, w, 32);
            DrawCheckbox(g, _toggleAutopilotRect, "Enable Autopilot Automation", _context.Settings.Autopilot);

            _toggleBatteryRect = new Rectangle(x + 20, 156, w - 20, 28);
            DrawCheckbox(g, _toggleBatteryRect, "Force CoolMode on Battery power", _context.Settings.BatteryRule);

            _toggleTempRect = new Rectangle(x + 20, 188, w - 20, 28);
            DrawCheckbox(g, _toggleTempRect, "Auto CoolMode when CPU exceeds " + _context.Settings.TempOn + "°C", _context.Settings.TempRule);

            int tableY = 232;
            Ui.Text(g, "APPLICATION RULES", T.F(9f, true), T.Text3, x, tableY);

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
                    Ui.Text(g, r.Exe, T.F(10f, true), T.Text, tableRect.X + 14, rowY + 6, T.Surface);

                    string actionText = r.Disable ? "Disable CoolMode (Turbo On)" : "Force CoolMode (Quiet)";
                    TagColor tc = r.Disable ? T.TagOrange : T.TagBlue;
                    Ui.Tag(g, actionText, tc, tableRect.X + 200, rowY + 4);

                    Rectangle delRect = new Rectangle(tableRect.Right - 40, rowY + 4, 26, 22);
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
            Ui.Text(g, "⚙️", T.F(20f, false), T.Text, x, 22);
            Ui.Text(g, "Preferences & Configuration", T.F(16f, true), T.Text, x, 56);
            Ui.Text(g, "Fine-tune power profiles and system tray behavior.", T.F(10f, false), T.Text3, x, 86);

            int optY = 124;
            _toggleStartRect = new Rectangle(x, optY, w, 32);
            DrawCheckbox(g, _toggleStartRect, "Start with Windows at login", Startup.IsOn());

            optY += 40;
            _toggleTrayTempRect = new Rectangle(x, optY, w, 32);
            DrawCheckbox(g, _toggleTrayTempRect, "Show live 2-digit temperature in System Tray icon", _context.Settings.TrayTemp);

            optY += 40;
            _toggleUnitRect = new Rectangle(x, optY, w, 32);
            DrawCheckbox(g, _toggleUnitRect, "Use Fahrenheit (°F) instead of Celsius (°C)", _context.Settings.Fahrenheit);

            optY += 52;
            Rectangle infoRect = new Rectangle(x, optY, w, 156);
            Ui.Fill(g, T.Side, infoRect, 6);
            Ui.Stroke(g, T.Border, infoRect, 6);

            int specW = infoRect.Width - 28;
            Ui.TextIn(g, "POWER SPECIFICATION", T.F(9f, true), T.Text3, new Rectangle(infoRect.X + 14, infoRect.Y + 14, specW, 20), 0);
            Ui.TextIn(g, "• Target: Processor Performance Boost Mode (PERFBOOSTMODE: be337238-...)", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 40, specW, 20), 0);
            Ui.TextIn(g, "• Throttle Cap: Maximum Processor State = 99% (AC & DC)", T.F(9f, false), T.Text2, new Rectangle(infoRect.X + 14, infoRect.Y + 62, specW, 20), 0);
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
    }
}
