using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Yelly
{
    struct TagColor
    {
        public Color Bg, Fg;
        public TagColor(Color bg, Color fg) { Bg = bg; Fg = fg; }
    }

    /// <summary>Warm stone light theme. Page is darker than pure white; cards sit above it.</summary>
    static class T
    {
        public static readonly Color Bg = Color.FromArgb(232, 228, 220);
        public static readonly Color Side = Color.FromArgb(220, 216, 208);
        public static readonly Color Surface = Color.FromArgb(247, 245, 240);
        public static readonly Color Border = Color.FromArgb(204, 199, 190);
        public static readonly Color BorderStrong = Color.FromArgb(176, 170, 160);
        public static readonly Color Text = Color.FromArgb(28, 27, 24);
        public static readonly Color Text2 = Color.FromArgb(68, 64, 58);
        public static readonly Color Text3 = Color.FromArgb(96, 91, 84);
        public static readonly Color Hover = Color.FromArgb(206, 201, 192);
        public static readonly Color Selected = Color.FromArgb(192, 186, 176);
        public static readonly Color Accent = Color.FromArgb(35, 131, 226);
        public static readonly Color AccentHover = Color.FromArgb(0, 119, 212);
        public static readonly Color Red = Color.FromArgb(224, 62, 62);
        public static readonly Color Orange = Color.FromArgb(217, 115, 13);
        public static readonly Color Green = Color.FromArgb(15, 123, 108);
        public static readonly Color CalloutBlue = Color.FromArgb(206, 226, 238);
        public static readonly Color CalloutYellow = Color.FromArgb(236, 220, 176);
        public static readonly Color CalloutRed = Color.FromArgb(240, 214, 214);

        public static readonly TagColor TagGray = new TagColor(Color.FromArgb(227, 226, 224), Color.FromArgb(50, 48, 44));
        public static readonly TagColor TagBlue = new TagColor(Color.FromArgb(211, 229, 239), Color.FromArgb(24, 51, 71));
        public static readonly TagColor TagOrange = new TagColor(Color.FromArgb(250, 222, 201), Color.FromArgb(73, 41, 14));
        public static readonly TagColor TagGreen = new TagColor(Color.FromArgb(219, 237, 219), Color.FromArgb(28, 56, 41));

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static string iconFamily;

        public static Font F(float pt, bool bold)
        {
            string key = pt + (bold ? "b" : "r");
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;
                try
                {
                    f = new Font("Google Sans", pt, style, GraphicsUnit.Point);
                }
                catch (ArgumentException)
                {
                    f = new Font("Segoe UI", pt, style, GraphicsUnit.Point);
                }
                fonts[key] = f;
            }
            return f;
        }

        /// <summary>Glyph font for sidebar icons; null when no icon font is installed.</summary>
        public static Font Icon(float pt)
        {
            if (iconFamily == null)
            {
                iconFamily = "";
                using (var col = new InstalledFontCollection())
                    foreach (FontFamily ff in col.Families)
                        if (ff.Name == "Segoe Fluent Icons") { iconFamily = ff.Name; break; }
                        else if (ff.Name == "Segoe MDL2 Assets") iconFamily = ff.Name;
            }
            if (iconFamily.Length == 0) return null;
            string key = "icon" + pt;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(iconFamily, pt, FontStyle.Regular, GraphicsUnit.Point);
                fonts[key] = f;
            }
            return f;
        }
    }

    static class Ui
    {
        const TextFormatFlags Base = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding;

        /// <summary>Antialiased shapes. Text is composited ClearType so it stays sharp on the back buffer.</summary>
        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void Text(Graphics g, string s, Font f, Color c, int x, int y)
        {
            Text(g, s, f, c, x, y, T.Bg);
        }

        /// <param name="back">Surface already painted under the glyphs. Used when composited ClearType is unavailable.</param>
        public static void Text(Graphics g, string s, Font f, Color c, int x, int y, Color back)
        {
            if (string.IsNullOrEmpty(s)) return;
            Size sz = TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), Base);
            var r = new Rectangle(x, y, Math.Max(1, sz.Width + 2), Math.Max(1, sz.Height + 2));
            if (!DrawSharp(g, s, f, c, r, (int)(Base | TextFormatFlags.NoClipping)))
                TextRenderer.DrawText(g, s, f, new Point(x, y), c, back, Base);
        }

        public static void TextIn(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra)
        {
            TextIn(g, s, f, c, r, extra, Color.Empty);
        }

        public static void TextIn(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra, Color back)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0 || r.Height <= 0) return;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.GlyphOverhangPadding | extra;
            if ((extra & TextFormatFlags.WordBreak) == 0)
                flags |= TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            else
                flags |= TextFormatFlags.TextBoxControl;
            if (!DrawSharp(g, s, f, c, r, (int)flags))
            {
                if (back.A == 0)
                    TextRenderer.DrawText(g, s, f, r, c, flags);
                else
                    TextRenderer.DrawText(g, s, f, r, c, back, flags);
            }
        }

        public static int W(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), Base).Width;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            if (rad <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            float maxRad = Math.Min(r.Width, r.Height) / 2f;
            if (rad > maxRad) rad = maxRad;
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, Color c, Rectangle r, int rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (rad <= 0)
            {
                using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
                return;
            }
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, Color c, Rectangle r, int rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (rad <= 0)
            {
                using (var pen = new Pen(c, 1f))
                    g.DrawRectangle(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
                return;
            }
            using (var p = Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad))
            using (var pen = new Pen(c, 1f))
                g.DrawPath(pen, p);
        }

        public static void Line(Graphics g, Color c, int x1, int y1, int x2, int y2)
        {
            using (var pen = new Pen(c, 1f))
                g.DrawLine(pen, x1 + 0.5f, y1 + 0.5f, x2 + 0.5f, y2 + 0.5f);
        }

        /// <summary>Property tag. Returns the pill width.</summary>
        public static int Tag(Graphics g, string s, TagColor c, int x, int y)
        {
            Font f = T.F(9f, false);
            int w = W(s, f) + 16;
            Fill(g, c.Bg, new Rectangle(x, y, w, 22), 3);
            TextIn(g, s, f, c.Fg, new Rectangle(x, y, w, 22), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return w;
        }

        public static string Temp(Settings cfg, int celsius)
        {
            return (cfg.Fahrenheit ? (int)Math.Round(celsius * 9.0 / 5.0 + 32) : celsius) + (cfg.Fahrenheit ? "\u00B0F" : "\u00B0C");
        }

        private static Image _logo;
        public static Image Logo
        {
            get
            {
                if (_logo == null)
                {
                    try
                    {
                        var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Yelly.png");
                        if (stream != null)
                        {
                            _logo = Image.FromStream(stream);
                        }
                        else if (File.Exists("Yelly.png"))
                        {
                            _logo = Image.FromFile("Yelly.png");
                        }
                    }
                    catch { }
                }
                return _logo;
            }
        }

        public static Icon AppIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Fill(g, T.Accent, new Rectangle(1, 1, 30, 30), 7);
                using (var f = new Font("Google Sans", 16f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("C", f, b, new RectangleF(0, 0, 32, 33), sf);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        const int DttComposited = 0x2000;
        const int DttTextColor = 1;
        static IntPtr _theme;
        static bool _themeFailed;
        static IntPtr _memDc;
        static IntPtr _dib;
        static IntPtr _bits;
        static int _dibW, _dibH, _stride;

        static bool DrawSharp(Graphics g, string s, Font f, Color c, Rectangle r, int dtFlags)
        {
            if (_themeFailed || r.Width > 1600 || r.Height > 400) return false;
            try
            {
                if (_theme == IntPtr.Zero)
                {
                    _theme = OpenThemeData(IntPtr.Zero, "globals");
                    if (_theme == IntPtr.Zero) { _themeFailed = true; return false; }
                }
                EnsureDib(r.Width, r.Height);
                int rowBytes = r.Width * 4;
                for (int row = 0; row < r.Height; row++)
                    ZeroMemory(new IntPtr(_bits.ToInt64() + row * _stride), (IntPtr)rowBytes);

                IntPtr hfont = f.ToHfont();
                IntPtr oldFont = SelectObject(_memDc, hfont);
                var rc = new RECT { L = 0, T = 0, R = r.Width, B = r.Height };
                var opts = new DTTOPTS();
                opts.dwSize = (uint)Marshal.SizeOf(typeof(DTTOPTS));
                opts.dwFlags = DttComposited | DttTextColor;
                opts.crText = (uint)(c.R | (c.G << 8) | (c.B << 16));
                int hr = DrawThemeTextEx(_theme, _memDc, 0, 0, s, -1, dtFlags, ref rc, ref opts);
                SelectObject(_memDc, oldFont);
                DeleteObject(hfont);
                if (hr != 0) return false;

                IntPtr dest = g.GetHdc();
                try
                {
                    var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                    return AlphaBlend(dest, r.X, r.Y, r.Width, r.Height, _memDc, 0, 0, r.Width, r.Height, blend);
                }
                finally
                {
                    g.ReleaseHdc(dest);
                }
            }
            catch
            {
                _themeFailed = true;
                return false;
            }
        }

        static void EnsureDib(int w, int h)
        {
            if (_dib != IntPtr.Zero && w <= _dibW && h <= _dibH) return;
            if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
            if (_memDc != IntPtr.Zero) { DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            _dibW = Math.Max(w, 64);
            _dibH = Math.Max(h, 32);
            _stride = _dibW * 4;
            IntPtr screen = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screen);
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            info.bmiHeader.biWidth = _dibW;
            info.bmiHeader.biHeight = -_dibH;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = 0;
            _dib = CreateDIBSection(screen, ref info, 0, out _bits, IntPtr.Zero, 0);
            ReleaseDC(IntPtr.Zero, screen);
            SelectObject(_memDc, _dib);
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr OpenThemeData(IntPtr hwnd, string classList);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int DrawThemeTextEx(IntPtr theme, IntPtr hdc, int part, int state, string text, int count, int flags, ref RECT rect, ref DTTOPTS opts);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("msimg32.dll")]
        static extern bool AlphaBlend(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int xs, int ys, int ws, int hs, BLENDFUNCTION fn);
        [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
        static extern void ZeroMemory(IntPtr dest, IntPtr length);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DTTOPTS
        {
            public uint dwSize, dwFlags, crText, crBorder, crShadow;
            public int iTextShadowType, ptShadowOffsetX, ptShadowOffsetY, iBorderSize, iFontPropId, iColorPropId, iStateId, fApplyOverlay, iGlowSize;
            public IntPtr pfnDrawTextCallback, lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }
    }
}
