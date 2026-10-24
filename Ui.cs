using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CoolModeTray
{
    struct TagColor
    {
        public Color Bg, Fg;
        public TagColor(Color bg, Color fg) { Bg = bg; Fg = fg; }
    }

    /// <summary>Notion light theme tokens.</summary>
    static class T
    {
        public static readonly Color Bg = Color.White;
        public static readonly Color Side = Color.FromArgb(247, 247, 245);
        public static readonly Color Border = Color.FromArgb(233, 233, 231);
        public static readonly Color BorderStrong = Color.FromArgb(217, 216, 213);
        public static readonly Color Text = Color.FromArgb(55, 53, 47);
        public static readonly Color Text2 = Color.FromArgb(120, 119, 116);
        public static readonly Color Text3 = Color.FromArgb(155, 154, 151);
        public static readonly Color Hover = Color.FromArgb(239, 239, 237);
        public static readonly Color Selected = Color.FromArgb(230, 230, 227);
        public static readonly Color Accent = Color.FromArgb(35, 131, 226);
        public static readonly Color AccentHover = Color.FromArgb(0, 119, 212);
        public static readonly Color Red = Color.FromArgb(224, 62, 62);
        public static readonly Color Orange = Color.FromArgb(217, 115, 13);
        public static readonly Color Green = Color.FromArgb(15, 123, 108);
        public static readonly Color CalloutBlue = Color.FromArgb(231, 243, 248);
        public static readonly Color CalloutYellow = Color.FromArgb(251, 243, 219);
        public static readonly Color CalloutRed = Color.FromArgb(253, 235, 236);

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
                f = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", pt, FontStyle.Regular, GraphicsUnit.Point);
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
        const TextFormatFlags Base = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public static void Text(Graphics g, string s, Font f, Color c, int x, int y)
        {
            TextRenderer.DrawText(g, s, f, new Point(x, y), c, Base);
        }

        public static void TextIn(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, s, f, r, c, Base | extra);
        }

        public static int W(string s, Font f)
        {
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
                using (var pen = new Pen(c, 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                return;
            }
            using (var p = Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), rad))
            using (var pen = new Pen(c, 1f))
                g.DrawPath(pen, p);
        }

        public static void Line(Graphics g, Color c, int x1, int y1, int x2, int y2)
        {
            using (var pen = new Pen(c, 1f))
                g.DrawLine(pen, x1, y1, x2, y2);
        }

        /// <summary>Notion property tag. Returns the pill width.</summary>
        public static int Tag(Graphics g, string s, TagColor c, int x, int y)
        {
            Font f = T.F(8.25f, false);
            int w = W(s, f) + 14;
            Fill(g, c.Bg, new Rectangle(x, y, w, 20), 3);
            TextIn(g, s, f, c.Fg, new Rectangle(x, y, w, 20), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return w;
        }

        public static string Temp(Settings cfg, int celsius)
        {
            return (cfg.Fahrenheit ? (int)Math.Round(celsius * 9.0 / 5.0 + 32) : celsius) + (cfg.Fahrenheit ? "\u00B0F" : "\u00B0C");
        }

        public static Icon AppIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Fill(g, T.Accent, new Rectangle(1, 1, 30, 30), 7);
                using (var f = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel))
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
