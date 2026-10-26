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

    /// <summary>Semantic colors for the warm light and charcoal dark themes.</summary>
    static class T
    {
        public static bool Dark;
        public static Color Bg { get { return Dark ? Color.FromArgb(28, 29, 32) : Color.FromArgb(232, 228, 220); } }
        public static Color Side { get { return Dark ? Color.FromArgb(23, 24, 27) : Color.FromArgb(220, 216, 208); } }
        public static Color Surface { get { return Dark ? Color.FromArgb(37, 39, 43) : Color.FromArgb(247, 245, 240); } }
        public static Color Border { get { return Dark ? Color.FromArgb(64, 67, 73) : Color.FromArgb(204, 199, 190); } }
        public static Color BorderStrong { get { return Dark ? Color.FromArgb(94, 98, 106) : Color.FromArgb(176, 170, 160); } }
        public static Color Text { get { return Dark ? Color.FromArgb(242, 241, 237) : Color.FromArgb(28, 27, 24); } }
        public static Color Text2 { get { return Dark ? Color.FromArgb(216, 214, 208) : Color.FromArgb(68, 64, 58); } }
        public static Color Text3 { get { return Dark ? Color.FromArgb(177, 178, 183) : Color.FromArgb(96, 91, 84); } }
        public static Color Hover { get { return Dark ? Color.FromArgb(49, 52, 58) : Color.FromArgb(206, 201, 192); } }
        public static Color Selected { get { return Dark ? Color.FromArgb(64, 68, 76) : Color.FromArgb(192, 186, 176); } }
        public static Color Accent { get { return Dark ? Color.FromArgb(35, 131, 226) : Color.FromArgb(35, 131, 226); } }
        public static Color AccentHover { get { return Dark ? Color.FromArgb(0, 119, 212) : Color.FromArgb(0, 119, 212); } }
        public static Color Red { get { return Dark ? Color.FromArgb(255, 128, 128) : Color.FromArgb(224, 62, 62); } }
        public static Color Orange { get { return Dark ? Color.FromArgb(255, 183, 105) : Color.FromArgb(217, 115, 13); } }
        public static Color Green { get { return Dark ? Color.FromArgb(110, 214, 184) : Color.FromArgb(15, 123, 108); } }
        public static Color CalloutBlue { get { return Dark ? Color.FromArgb(30, 54, 73) : Color.FromArgb(206, 226, 238); } }
        public static Color CalloutYellow { get { return Dark ? Color.FromArgb(65, 51, 30) : Color.FromArgb(236, 220, 176); } }
        public static Color CalloutRed { get { return Dark ? Color.FromArgb(74, 36, 40) : Color.FromArgb(240, 214, 214); } }

        public static TagColor TagGray { get { return Dark ? new TagColor(Color.FromArgb(56, 59, 65), Color.FromArgb(225, 226, 230)) : new TagColor(Color.FromArgb(227, 226, 224), Color.FromArgb(50, 48, 44)); } }
        public static TagColor TagBlue { get { return Dark ? new TagColor(Color.FromArgb(35, 66, 89), Color.FromArgb(172, 218, 255)) : new TagColor(Color.FromArgb(211, 229, 239), Color.FromArgb(24, 51, 71)); } }
        public static TagColor TagOrange { get { return Dark ? new TagColor(Color.FromArgb(76, 51, 30), Color.FromArgb(255, 206, 158)) : new TagColor(Color.FromArgb(250, 222, 201), Color.FromArgb(73, 41, 14)); } }
        public static TagColor TagGreen { get { return Dark ? new TagColor(Color.FromArgb(30, 64, 53), Color.FromArgb(162, 227, 194)) : new TagColor(Color.FromArgb(219, 237, 219), Color.FromArgb(28, 56, 41)); } }

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
                    f = new Font("Google Sans", pt * 96f / 72f, style, GraphicsUnit.Pixel);
                }
                catch (ArgumentException)
                {
                    f = new Font("Segoe UI", pt * 96f / 72f, style, GraphicsUnit.Pixel);
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
                f = new Font(iconFamily, pt * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }
    }

    static class Ui
    {


        /// <summary>Antialiased shapes and text drawn in the same scalable coordinate space.</summary>
        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static void Text(Graphics g, string s, Font f, Color c, int x, int y)
        {
            Text(g, s, f, c, x, y, T.Bg);
        }

        /// <param name="back">Surface already painted under the glyphs. Used when composited ClearType is unavailable.</param>
        public static void Text(Graphics g, string s, Font f, Color c, int x, int y, Color back)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (var brush = new SolidBrush(c))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
                g.DrawString(s, f, brush, new PointF(x, y), format);
        }

        public static void TextIn(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra)
        {
            TextIn(g, s, f, c, r, extra, Color.Empty);
        }

        public static void TextIn(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra, Color back)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0 || r.Height <= 0) return;
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            using (var brush = new SolidBrush(c))
            {
                format.Alignment = (extra & TextFormatFlags.HorizontalCenter) != 0 ? StringAlignment.Center
                    : (extra & TextFormatFlags.Right) != 0 ? StringAlignment.Far : StringAlignment.Near;
                format.LineAlignment = (extra & TextFormatFlags.VerticalCenter) != 0 ? StringAlignment.Center
                    : (extra & TextFormatFlags.Bottom) != 0 ? StringAlignment.Far : StringAlignment.Near;
                bool wrap = (extra & TextFormatFlags.WordBreak) != 0;
                format.Trimming = wrap ? StringTrimming.EllipsisWord : StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.LineLimit | (wrap ? 0 : StringFormatFlags.NoWrap);
                g.DrawString(s, f, brush, r, format);
            }
        }

        public static int W(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
                return (int)Math.Ceiling(g.MeasureString(s, f, int.MaxValue, format).Width);
        }

        public static void DrawLogo(Graphics g, Rectangle r)
        {
            if (Logo == null) return;
            var saved = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (!T.Dark) g.DrawImage(Logo, r);
            else
            {
                using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                {
                    var matrix = new System.Drawing.Imaging.ColorMatrix();
                    matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 0;
                    matrix.Matrix40 = T.Text.R / 255f;
                    matrix.Matrix41 = T.Text.G / 255f;
                    matrix.Matrix42 = T.Text.B / 255f;
                    attrs.SetColorMatrix(matrix);
                    g.DrawImage(Logo, r, 0, 0, Logo.Width, Logo.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            g.Restore(saved);
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
            return Tag(g, s, c, x, y, int.MaxValue);
        }

        public static int Tag(Graphics g, string s, TagColor c, int x, int y, int maxWidth)
        {
            Font f = T.F(9f, false);
            int w = Math.Max(0, Math.Min(W(s, f) + 16, maxWidth));
            Fill(g, c.Bg, new Rectangle(x, y, w, 22), 3);
            TextIn(g, s, f, c.Fg, new Rectangle(x + 8, y, Math.Max(0, w - 16), 22), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter, c.Bg);
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

    }
}
