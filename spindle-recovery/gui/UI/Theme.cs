using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpindleRecovery.UI
{
    /// <summary>Colours, fonts and DPI helpers shared by every screen.</summary>
    public static class Theme
    {
        public static readonly Color Sidebar = Color.FromArgb(24, 28, 41);
        public static readonly Color SidebarHover = Color.FromArgb(36, 41, 58);
        public static readonly Color SidebarActive = Color.FromArgb(44, 51, 74);
        public static readonly Color SidebarText = Color.FromArgb(168, 176, 196);
        public static readonly Color SidebarTextActive = Color.White;

        public static readonly Color Background = Color.FromArgb(244, 246, 250);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 230, 238);
        public static readonly Color RowHover = Color.FromArgb(241, 245, 255);
        public static readonly Color RowSelected = Color.FromArgb(226, 235, 255);

        public static readonly Color Text = Color.FromArgb(28, 33, 46);
        public static readonly Color TextMuted = Color.FromArgb(110, 118, 135);
        public static readonly Color TextFaint = Color.FromArgb(160, 167, 182);

        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentSoft = Color.FromArgb(219, 234, 254);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);

        public static readonly Color[] CategoryColors =
        {
            Color.FromArgb(236, 72, 153),  // Pictures
            Color.FromArgb(124, 58, 237),  // Video
            Color.FromArgb(245, 158, 11),  // Audio
            Color.FromArgb(37, 99, 235),   // Documents
            Color.FromArgb(16, 185, 129),  // Archives
            Color.FromArgb(100, 116, 139), // Other
        };

        public static float Scale = 1f;

        public static void Init()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { Scale = 1f; }
        }

        public static int S(int px) => (int)Math.Round(px * Scale);
        public static float S(float px) => px * Scale;

        static string _family;
        public static string FontFamilyName
        {
            get
            {
                if (_family != null) return _family;
                foreach (var name in new[] { "Segoe UI", "Segoe UI Variable Text", "Noto Sans", "DejaVu Sans", "Arial" })
                {
                    try
                    {
                        using (var f = new FontFamily(name)) { _family = name; return _family; }
                    }
                    catch { }
                }
                return _family = FontFamily.GenericSansSerif.Name;
            }
        }

        // Font sizes are in points; WinForms scales them with DPI itself.
        public static Font Font(float size, FontStyle style = FontStyle.Regular) => new Font(FontFamilyName, size, style, GraphicsUnit.Point);
        public static readonly Font Body = Font(9.75f);
        public static readonly Font BodyBold = Font(9.75f, FontStyle.Bold);
        public static readonly Font Small = Font(8.5f);
        public static readonly Font Title = Font(17f, FontStyle.Bold);
        public static readonly Font Subtitle = Font(10.5f);
        public static readonly Font Section = Font(11f, FontStyle.Bold);
        public static readonly Font Big = Font(22f, FontStyle.Bold);

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }
    }
}
