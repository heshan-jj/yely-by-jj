using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace Yelly
{
    // Exercises the real form without starting telemetry, Autopilot, or power writes.
    internal static class LayoutChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string output = args.Length == 0 ? null : args[0];
            if (output != null) Directory.CreateDirectory(output);
            int count = 0;
            foreach (bool dark in new[] { false, true })
            {
                T.Dark = dark;
                var settings = new Settings();
                settings.Rules.Add(new Rule { Exe = "An application with a very long process name.exe", Disable = true });
                var context = (YellyContext)FormatterServices.GetUninitializedObject(typeof(YellyContext));
                typeof(YellyContext).GetField("Settings").SetValue(context, settings);
                typeof(YellyContext).GetField("Telemetry").SetValue(context, new Telemetry(true));
                typeof(YellyContext).GetField("Autopilot").SetValue(context, new AutopilotEngine(settings));
                typeof(YellyContext).GetField("_isCoolMode", Private).SetValue(context, true);
                using (var form = new MainForm(context))
                {
                    // Create the handle before injecting synthetic DPI values.
                    IntPtr handle = form.Handle;
                    foreach (int dpi in new[] { 96, 120, 144, 192 })
                    {
                        float scale = dpi / 96f;
                        typeof(MainForm).GetMethod("ApplyDisplayScale", Private).Invoke(form, new object[] { (float)dpi, new Rectangle(0, 0, 4000, 3000) });
                        Require(form.ClientSize == new Size((int)(800 * scale), (int)(540 * scale)), "Window size at " + dpi);
                        for (int page = 0; page < 4; page++)
                        {
                            // Click the physical location of each navigation item.
                            var physical = new Point((int)(90 * scale), (int)((82 + page * 40) * scale));
                            typeof(Control).GetMethod("OnMouseClick", Private).Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Left, 1, physical.X, physical.Y, 0) });
                            Require((int)typeof(MainForm).GetField("_selectedNav", Private).GetValue(form) == page, "Hit target at " + dpi);
                            if (page > 0)
                            {
                                typeof(MainForm).GetField("_pageT", Private).SetValue(form, 0.5f);
                                using (var transition = new Bitmap(form.Width, form.Height))
                                using (var graphics = Graphics.FromImage(transition))
                                    typeof(MainForm).GetMethod("OnPaint", Private).Invoke(form, new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, transition.Size)) });
                                var pageImage = (Bitmap)typeof(MainForm).GetField("_pageB", Private).GetValue(form);
                                Require(pageImage.Size == form.ClientSize, "Transition resolution at " + dpi);
                            }
                            typeof(MainForm).GetField("_pageT", Private).SetValue(form, 1f);
                            using (var image = new Bitmap(form.Width, form.Height))
                            {
                                using (var graphics = Graphics.FromImage(image))
                                {
                                    graphics.Clear(T.Bg);
                                    typeof(MainForm).GetMethod("OnPaint", Private).Invoke(form, new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, image.Size)) });
                                }
                                Require(image.GetPixel((int)(190 * scale), (int)(510 * scale)).ToArgb() == T.Bg.ToArgb(), "Page background at " + dpi);
                                if (output != null && (dpi == 96 || dpi == 192))
                                    image.Save(Path.Combine(output, (dark ? "dark" : "light") + "-" + dpi + "-" + page + ".png"));
                            }
                            if (page == 3)
                            {
                                var toggle = (Rectangle)typeof(MainForm).GetField("_shortcutCaptureRect", Private).GetValue(form);
                                var logical = (Point)typeof(MainForm).GetMethod("ToLayout", Private).Invoke(form, new object[] { new Point((int)((toggle.X + 8) * scale), (int)((toggle.Y + 8) * scale)) });
                                Require(toggle.Contains(logical), "Theme hit target at " + dpi);
                            }
                            count++;
                        }
                    }
                    typeof(MainForm).GetMethod("ApplyDisplayScale", Private).Invoke(form, new object[] { 192f, new Rectangle(0, 0, 1024, 600) });
                    Require(form.Width <= 1008 && form.Height <= 584, "Small display fit");
                }
            }
            Console.WriteLine("PASS: " + count + " theme/page/scale combinations, navigation and shortcut hit targets, transition resolution, and small display fit.");
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new Exception("FAIL: " + description);
        }
    }
}
