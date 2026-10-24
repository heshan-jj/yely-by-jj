using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            this.Size = new Size(680, 500);
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

            _nav0Rect = new Rectangle(12, 60, 156, 32);
            _nav1Rect = new Rectangle(12, 96, 156, 32);
            _nav2Rect = new Rectangle(12, 132, 156, 32);

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

                if (ch != _closeHover || mh != _minHover)
                {
                    _closeHover = ch;
                    _minHover = mh;
                    this.Invalidate();
                }

                bool isHand = ch || mh || _nav0Rect.Contains(e.Location) || _nav1Rect.Contains(e.Location) || _nav2Rect.Contains(e.Location);
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

                // Nav switching
                if (_nav0Rect.Contains(e.Location)) { _selectedNav = 0; this.Invalidate(); return; }
                if (_nav1Rect.Contains(e.Location)) { _selectedNav = 1; this.Invalidate(); return; }
                if (_nav2Rect.Contains(e.Location)) { _selectedNav = 2; this.Invalidate(); return; }

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
                prompt.Width = 360;
                prompt.Height = 190;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "Add Autopilot Rule";
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.MaximizeBox = false;
                prompt.MinimizeBox = false;
                prompt.BackColor = T.Bg;

                var lbl = new Label() { Left = 20, Top = 16, Width = 300, Text = "Process Name (e.g. game.exe, blender, chrome):", Font = T.F(9f, false) };
                var txt = new TextBox() { Left = 20, Top = 40, Width = 300, Font = T.F(9.5f, false) };

                var combo = new ComboBox() { Left = 20, Top = 72, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, Font = T.F(9f, false) };
                combo.Items.Add("Disable CoolMode (Allow Full Boost while running)");
                combo.Items.Add("Enable CoolMode (Enforce Cooling while running)");
                combo.SelectedIndex = 0;

                var btnOk = new Button() { Text = "Add Rule", Left = 135, Width = 90, Top = 110, DialogResult = DialogResult.OK, Font = T.F(9f, true), BackColor = T.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                var btnCancel = new Button() { Text = "Cancel", Left = 230, Width = 90, Top = 110, DialogResult = DialogResult.Cancel, Font = T.F(9f, false), FlatStyle = FlatStyle.Flat };

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
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

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
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Ui.Logo, new Rectangle(16, 17, 22, 22));
                Ui.Text(g, "Yelly", T.F(11.5f, true), T.Text, 44, 18);
            }
            else
            {
                Ui.Text(g, "Yelly", T.F(11.5f, true), T.Text, 16, 18);
            }

            // Nav Items
            DrawNavItem(g, _nav0Rect, "📊  Dashboard", _selectedNav == 0);
            DrawNavItem(g, _nav1Rect, "🤖  Autopilot", _selectedNav == 1);
            DrawNavItem(g, _nav2Rect, "⚙️  Settings", _selectedNav == 2);

            // Sidebar footer device info
            string devInfo = !string.IsNullOrEmpty(_context.Telemetry.S.CpuName)
                ? _context.Telemetry.S.CpuName
                : "Windows PC";
            if (devInfo.Length > 24) devInfo = devInfo.Substring(0, 24) + "...";
            Ui.Text(g, devInfo, T.F(7.5f, false), T.Text3, 14, this.Height - 32);

            // 2. Window Controls (Minimize & Close)
            DrawWindowControls(g);

            // 3. Main Page Content
            int pageX = sideW + 28;
            int pageW = this.Width - pageX - 28;

            if (_selectedNav == 0)
            {
                DrawDashboardPage(g, pageX, pageW);
            }
            else if (_selectedNav == 1)
            {
                DrawAutopilotPage(g, pageX, pageW);
            }
            else
            {
                DrawSettingsPage(g, pageX, pageW);
            }

            // Outer border
            Ui.Stroke(g, T.BorderStrong, new Rectangle(0, 0, this.Width, this.Height), 0);
        }

        private void DrawNavItem(Graphics g, Rectangle r, string text, bool selected)
        {
            bool hover = r.Contains(_mousePos);
            if (selected)
            {
                Ui.Fill(g, T.Selected, r, 5);
            }
            else if (hover)
            {
                Ui.Fill(g, T.Hover, r, 5);
            }
            Ui.Text(g, text, T.F(9f, selected), selected ? T.Text : T.Text2, r.X + 10, r.Y + 7);
        }

        private void DrawWindowControls(Graphics g)
        {
            if (_minHover) Ui.Fill(g, T.Hover, _minBtnRect, 4);
            Ui.Line(g, T.Text3, _minBtnRect.X + 8, _minBtnRect.Y + 14, _minBtnRect.X + 18, _minBtnRect.Y + 14);

            if (_closeHover) Ui.Fill(g, Color.FromArgb(253, 235, 236), _closeBtnRect, 4);
            Color closeColor = _closeHover ? T.Red : T.Text3;
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
            int propY = 92;
            Ui.Text(g, "Status:", T.F(8.5f, false), T.Text3, x, propY + 2);
            int tagW = Ui.Tag(g, _context.IsCoolMode ? "CoolMode Active" : "Turbo Boost Active",
                _context.IsCoolMode ? T.TagBlue : T.TagOrange, x + 48, propY);

            Ui.Text(g, "Trigger:", T.F(8.5f, false), T.Text3, x + 56 + tagW, propY + 2);
            Ui.Tag(g, _context.Autopilot.ActiveDescription, T.TagGray, x + 106 + tagW, propY);

            // Notion Callout Box
            Rectangle calloutRect = new Rectangle(x, 122, w, 48);
            Color calloutBg = _context.IsCoolMode ? T.CalloutBlue : T.CalloutYellow;
            Ui.Fill(g, calloutBg, calloutRect, 6);

            string calloutText = _context.IsCoolMode
                ? "💡 CoolMode is active. Turbo Boost is locked off (99% cap) to keep cores cool and fans quiet."
                : "⚡ Full Performance is active. Maximum turbo boost clocks and single-core burst speeds enabled.";
            Ui.Text(g, calloutText, T.F(8.5f, false), T.Text, calloutRect.X + 12, calloutRect.Y + 14);

            // Hardware Telemetry Cards (Side by side)
            int cardW = (w - 14) / 2;
            int cardY = 182;
            int cardH = 150;

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
            int barY = 344;
            string ramStr = _context.Telemetry.S.RamUsed > 0
                ? string.Format("RAM: {0:F1} / {1:F1} GB ({2:F0}%)", _context.Telemetry.S.RamUsed, _context.Telemetry.S.RamTotal, _context.Telemetry.S.RamPct)
                : "RAM: --";
            string pwrStr = _context.Telemetry.S.Battery >= 0
                ? string.Format("Power: {0} ({1}%)", _context.Telemetry.S.OnAC ? "Plugged in" : "On Battery", _context.Telemetry.S.Battery)
                : (_context.Telemetry.S.OnAC ? "Power: AC Connected" : "Power: Battery");

            Ui.Text(g, ramStr + "   •   " + pwrStr, T.F(8f, false), T.Text3, x + 4, barY);

            // Notion Action Button
            _heroBtnRect = new Rectangle(x, 376, w, 44);
            bool hover = _heroBtnRect.Contains(_mousePos);
            Color btnBg = _context.IsCoolMode
                ? (hover ? T.Selected : T.Hover)
                : (hover ? T.AccentHover : T.Accent);
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
            Ui.Fill(g, Color.White, r, 6);
            Ui.Stroke(g, T.Border, r, 6);

            // Title
            Ui.Text(g, title, T.F(8f, true), T.Text3, r.X + 14, r.Y + 12);

            // Big Temp Number
            string tempStr = temp > 0 ? Ui.Temp(_context.Settings, temp) : "--";
            Color numColor = temp >= _context.Settings.Alert ? T.Red : T.Text;
            Ui.Text(g, tempStr, T.F(24f, true), numColor, r.X + 12, r.Y + 28);

            // Subtitle & details
            if (subtitle.Length > 24) subtitle = subtitle.Substring(0, 24) + "..";
            Ui.Text(g, subtitle, T.F(8f, true), T.Text2, r.X + 14, r.Y + 76);
            Ui.Text(g, details, T.F(7.5f, false), T.Text3, r.X + 14, r.Y + 94);

            // Sparkline
            DrawSparkline(g, new Rectangle(r.X + 14, r.Y + 114, r.Width - 28, 22), hist, numColor);
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
            Ui.Text(g, "Automatically adjust power states based on running games, battery, or thermals.", T.F(8.5f, false), T.Text3, x, 84);

            // Master Switch
            _toggleAutopilotRect = new Rectangle(x, 114, w, 28);
            DrawCheckbox(g, _toggleAutopilotRect, "Enable Autopilot Automation", _context.Settings.Autopilot);

            // Triggers
            _toggleBatteryRect = new Rectangle(x + 20, 148, w - 20, 24);
            DrawCheckbox(g, _toggleBatteryRect, "Force CoolMode on Battery power", _context.Settings.BatteryRule);

            _toggleTempRect = new Rectangle(x + 20, 178, w - 20, 24);
            DrawCheckbox(g, _toggleTempRect, "Auto CoolMode when CPU exceeds " + _context.Settings.TempOn + "°C", _context.Settings.TempRule);

            // Rules Table Header
            int tableY = 220;
            Ui.Text(g, "APPLICATION RULES", T.F(8f, true), T.Text3, x, tableY);

            _addRuleBtnRect = new Rectangle(x + w - 100, tableY - 4, 100, 24);
            bool addHover = _addRuleBtnRect.Contains(_mousePos);
            Ui.Fill(g, addHover ? T.Hover : T.Bg, _addRuleBtnRect, 4);
            Ui.Stroke(g, T.Border, _addRuleBtnRect, 4);
            Ui.TextIn(g, "+ Add Rule", T.F(8f, true), T.Text, _addRuleBtnRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Table Box
            Rectangle tableRect = new Rectangle(x, tableY + 24, w, 180);
            Ui.Fill(g, Color.White, tableRect, 6);
            Ui.Stroke(g, T.Border, tableRect, 6);

            _ruleDeleteRects.Clear();
            if (_context.Settings.Rules.Count == 0)
            {
                Ui.TextIn(g, "No process rules configured. Click '+ Add Rule' to add a game or app.", T.F(8.5f, false), T.Text3, tableRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int rowY = tableRect.Y + 6;
                for (int i = 0; i < Math.Min(5, _context.Settings.Rules.Count); i++)
                {
                    Rule r = _context.Settings.Rules[i];
                    Ui.Text(g, r.Exe, T.F(9f, true), T.Text, tableRect.X + 14, rowY + 6);

                    string actionText = r.Disable ? "Disable CoolMode (Turbo On)" : "Force CoolMode (Quiet)";
                    TagColor tc = r.Disable ? T.TagOrange : T.TagBlue;
                    Ui.Tag(g, actionText, tc, tableRect.X + 180, rowY + 4);

                    // Delete button
                    Rectangle delRect = new Rectangle(tableRect.Right - 36, rowY + 4, 24, 20);
                    _ruleDeleteRects.Add(delRect);
                    bool dHover = delRect.Contains(_mousePos);
                    if (dHover) Ui.Fill(g, T.Hover, delRect, 3);
                    Ui.TextIn(g, "✕", T.F(8f, false), T.Text3, delRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    rowY += 32;
                    if (i < _context.Settings.Rules.Count - 1)
                        Ui.Line(g, T.Border, tableRect.X + 8, rowY, tableRect.Right - 8, rowY);
                }
            }
        }

        private void DrawSettingsPage(Graphics g, int x, int w)
        {
            Ui.Text(g, "⚙️", T.F(20f, false), T.Text, x, 22);
            Ui.Text(g, "Preferences & Configuration", T.F(16f, true), T.Text, x, 56);
            Ui.Text(g, "Fine-tune power profiles and system tray behavior.", T.F(8.5f, false), T.Text3, x, 84);

            int optY = 120;
            _toggleStartRect = new Rectangle(x, optY, w, 28);
            DrawCheckbox(g, _toggleStartRect, "Start with Windows at login", Startup.IsOn());

            optY += 36;
            _toggleTrayTempRect = new Rectangle(x, optY, w, 28);
            DrawCheckbox(g, _toggleTrayTempRect, "Show live 2-digit temperature in System Tray icon", _context.Settings.TrayTemp);

            optY += 36;
            _toggleUnitRect = new Rectangle(x, optY, w, 28);
            DrawCheckbox(g, _toggleUnitRect, "Use Fahrenheit (°F) instead of Celsius (°C)", _context.Settings.Fahrenheit);

            // Technical details card
            optY += 50;
            Rectangle infoRect = new Rectangle(x, optY, w, 140);
            Ui.Fill(g, T.Side, infoRect, 6);
            Ui.Stroke(g, T.Border, infoRect, 6);

            Ui.Text(g, "POWER SPECIFICATION", T.F(8f, true), T.Text3, infoRect.X + 14, infoRect.Y + 12);
            Ui.Text(g, "• Target: Processor Performance Boost Mode (PERFBOOSTMODE: be337238-...)", T.F(8f, false), T.Text2, infoRect.X + 14, infoRect.Y + 34);
            Ui.Text(g, "• Throttle Cap: Maximum Processor State = 99% (AC & DC)", T.F(8f, false), T.Text2, infoRect.X + 14, infoRect.Y + 54);
            Ui.Text(g, "• Normal State: Restores Original Boost (Aggressive) & 100% Cap", T.F(8f, false), T.Text2, infoRect.X + 14, infoRect.Y + 74);
            Ui.Text(g, "• Telemetry: ACPI Thermal Zone Information + NVIDIA Management", T.F(8f, false), T.Text2, infoRect.X + 14, infoRect.Y + 94);
            Ui.Text(g, "• Zero Drivers: Pure Win32 API calls via powrprof.dll (no Ring-0 driver)", T.F(8f, false), T.Text2, infoRect.X + 14, infoRect.Y + 114);
        }

        private void DrawCheckbox(Graphics g, Rectangle r, string text, bool check)
        {
            Rectangle box = new Rectangle(r.X, r.Y + 4, 16, 16);
            Ui.Fill(g, check ? T.Accent : Color.White, box, 4);
            Ui.Stroke(g, check ? T.Accent : T.BorderStrong, box, 4);

            if (check)
            {
                using (var pen = new Pen(Color.White, 1.8f))
                {
                    g.DrawLine(pen, box.X + 3, box.Y + 8, box.X + 7, box.Y + 12);
                    g.DrawLine(pen, box.X + 7, box.Y + 12, box.X + 13, box.Y + 4);
                }
            }

            Ui.Text(g, text, T.F(9f, false), T.Text, r.X + 26, r.Y + 3);
        }
    }
}
