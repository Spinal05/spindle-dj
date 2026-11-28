using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>Simple vector icons drawn with GDI+ (no bitmap assets needed).</summary>
    public static class Icons
    {
        public enum Kind { Recover, Partition, Backup, Vault, About, HardDisk, Usb, Volume, Image, Partition2, Pictures, Video, Audio, Documents, Archives, Other, Email, Search, Folder, Check, Shield }

        public static void Draw(Graphics g, Kind kind, RectangleF r, Color color)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;
            float w = Math.Max(1.2f, s / 13f);
            using (var pen = new Pen(color, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var brush = new SolidBrush(color))
            {
                RectangleF R(float a, float b, float c, float d) => new RectangleF(x + a * s, y + b * s, c * s, d * s);
                PointF P(float a, float b) => new PointF(x + a * s, y + b * s);
                switch (kind)
                {
                    case Kind.Recover: // circular arrow around a document
                        g.DrawArc(pen, R(0.12f, 0.12f, 0.76f, 0.76f), 200, 280);
                        g.FillPolygon(brush, new[] { P(0.06f, 0.30f), P(0.30f, 0.32f), P(0.14f, 0.50f) });
                        g.DrawRectangle(pen, x + 0.38f * s, y + 0.36f * s, 0.24f * s, 0.30f * s);
                        break;
                    case Kind.Partition:
                    case Kind.Partition2:
                        g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                        g.DrawLine(pen, P(0.5f, 0.5f), P(0.5f, 0.12f));
                        g.DrawLine(pen, P(0.5f, 0.5f), P(0.83f, 0.66f));
                        using (var b2 = new SolidBrush(Color.FromArgb(90, color)))
                            g.FillPie(b2, x + 0.12f * s, y + 0.12f * s, 0.76f * s, 0.76f * s, -90, 116);
                        break;
                    case Kind.Backup:
                        g.DrawRectangle(pen, x + 0.14f * s, y + 0.56f * s, 0.72f * s, 0.26f * s);
                        g.FillEllipse(brush, R(0.70f, 0.66f, 0.07f, 0.07f));
                        g.DrawLine(pen, P(0.5f, 0.12f), P(0.5f, 0.44f));
                        g.DrawLines(pen, new[] { P(0.36f, 0.31f), P(0.5f, 0.45f), P(0.64f, 0.31f) });
                        break;
                    case Kind.Vault:
                    case Kind.Folder:
                        using (var path = new GraphicsPath())
                        {
                            path.AddLines(new[] { P(0.10f, 0.26f), P(0.38f, 0.26f), P(0.46f, 0.34f), P(0.90f, 0.34f), P(0.90f, 0.80f), P(0.10f, 0.80f) });
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        break;
                    case Kind.About:
                        g.DrawEllipse(pen, R(0.12f, 0.12f, 0.76f, 0.76f));
                        g.DrawLine(pen, P(0.5f, 0.46f), P(0.5f, 0.70f));
                        g.FillEllipse(brush, R(0.455f, 0.28f, 0.09f, 0.09f));
                        break;
                    case Kind.HardDisk:
                        using (var path = Theme.RoundRect(R(0.08f, 0.22f, 0.84f, 0.56f), s * 0.08f)) g.DrawPath(pen, path);
                        g.DrawLine(pen, P(0.08f, 0.56f), P(0.92f, 0.56f));
                        g.FillEllipse(brush, R(0.72f, 0.63f, 0.08f, 0.08f));
                        g.FillEllipse(brush, R(0.58f, 0.63f, 0.08f, 0.08f));
                        break;
                    case Kind.Usb:
                        using (var path = Theme.RoundRect(R(0.26f, 0.30f, 0.48f, 0.60f), s * 0.07f)) g.DrawPath(pen, path);
                        g.DrawRectangle(pen, x + 0.34f * s, y + 0.08f * s, 0.32f * s, 0.22f * s);
                        g.FillRectangle(brush, R(0.40f, 0.15f, 0.06f, 0.07f));
                        g.FillRectangle(brush, R(0.54f, 0.15f, 0.06f, 0.07f));
                        break;
                    case Kind.Volume:
                        g.DrawRectangle(pen, x + 0.12f * s, y + 0.30f * s, 0.76f * s, 0.40f * s);
                        g.FillRectangle(brush, R(0.20f, 0.46f, 0.30f, 0.08f));
                        break;
                    case Kind.Image:
                        g.DrawEllipse(pen, R(0.10f, 0.10f, 0.80f, 0.80f));
                        g.DrawEllipse(pen, R(0.40f, 0.40f, 0.20f, 0.20f));
                        g.DrawArc(pen, R(0.22f, 0.22f, 0.56f, 0.56f), 200, 70);
                        break;
                    case Kind.Pictures:
                        using (var path = Theme.RoundRect(R(0.10f, 0.18f, 0.80f, 0.64f), s * 0.08f)) g.DrawPath(pen, path);
                        g.DrawLines(pen, new[] { P(0.16f, 0.74f), P(0.40f, 0.48f), P(0.58f, 0.66f), P(0.68f, 0.56f), P(0.86f, 0.74f) });
                        g.FillEllipse(brush, R(0.62f, 0.28f, 0.12f, 0.12f));
                        break;
                    case Kind.Video:
                        using (var path = Theme.RoundRect(R(0.08f, 0.24f, 0.60f, 0.52f), s * 0.07f)) g.DrawPath(pen, path);
                        g.DrawPolygon(pen, new[] { P(0.70f, 0.42f), P(0.92f, 0.28f), P(0.92f, 0.72f), P(0.70f, 0.58f) });
                        break;
                    case Kind.Audio:
                        g.DrawLine(pen, P(0.40f, 0.70f), P(0.40f, 0.18f));
                        g.DrawLine(pen, P(0.40f, 0.18f), P(0.80f, 0.12f));
                        g.DrawLine(pen, P(0.80f, 0.12f), P(0.80f, 0.62f));
                        g.FillEllipse(brush, R(0.20f, 0.62f, 0.22f, 0.18f));
                        g.FillEllipse(brush, R(0.60f, 0.55f, 0.22f, 0.18f));
                        break;
                    case Kind.Documents:
                        using (var path = new GraphicsPath())
                        {
                            path.AddLines(new[] { P(0.22f, 0.08f), P(0.60f, 0.08f), P(0.80f, 0.28f), P(0.80f, 0.92f), P(0.22f, 0.92f) });
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        g.DrawLine(pen, P(0.34f, 0.48f), P(0.68f, 0.48f));
                        g.DrawLine(pen, P(0.34f, 0.62f), P(0.68f, 0.62f));
                        g.DrawLine(pen, P(0.34f, 0.76f), P(0.56f, 0.76f));
                        break;
                    case Kind.Archives:
                        g.DrawRectangle(pen, x + 0.14f * s, y + 0.30f * s, 0.72f * s, 0.56f * s);
                        g.DrawRectangle(pen, x + 0.10f * s, y + 0.16f * s, 0.80f * s, 0.14f * s);
                        g.DrawLine(pen, P(0.40f, 0.46f), P(0.60f, 0.46f));
                        break;
                    case Kind.Other:
                        using (var path = new GraphicsPath())
                        {
                            path.AddLines(new[] { P(0.22f, 0.08f), P(0.60f, 0.08f), P(0.80f, 0.28f), P(0.80f, 0.92f), P(0.22f, 0.92f) });
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        g.DrawLines(pen, new[] { P(0.60f, 0.08f), P(0.60f, 0.28f), P(0.80f, 0.28f) });
                        break;
                    case Kind.Email:
                        using (var path = Theme.RoundRect(R(0.10f, 0.22f, 0.80f, 0.56f), s * 0.06f)) g.DrawPath(pen, path);
                        g.DrawLines(pen, new[] { P(0.12f, 0.26f), P(0.5f, 0.56f), P(0.88f, 0.26f) });
                        break;
                    case Kind.Search:
                        g.DrawEllipse(pen, R(0.12f, 0.12f, 0.52f, 0.52f));
                        g.DrawLine(pen, P(0.58f, 0.58f), P(0.88f, 0.88f));
                        break;
                    case Kind.Check:
                        g.DrawLines(pen, new[] { P(0.18f, 0.52f), P(0.40f, 0.74f), P(0.82f, 0.28f) });
                        break;
                    case Kind.Shield:
                        using (var path = new GraphicsPath())
                        {
                            path.AddLines(new[] { P(0.5f, 0.08f), P(0.86f, 0.22f), P(0.82f, 0.58f), P(0.5f, 0.92f), P(0.18f, 0.58f), P(0.14f, 0.22f) });
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        g.DrawLines(pen, new[] { P(0.34f, 0.50f), P(0.46f, 0.62f), P(0.68f, 0.38f) });
                        break;
                }
            }
            g.Restore(state);
        }

        public static Kind ForCategory(FileCategory c)
        {
            switch (c)
            {
                case FileCategory.Pictures: return Kind.Pictures;
                case FileCategory.Video: return Kind.Video;
                case FileCategory.Audio: return Kind.Audio;
                case FileCategory.Documents: return Kind.Documents;
                case FileCategory.Archives: return Kind.Archives;
                case FileCategory.Email: return Kind.Email;
                default: return Kind.Other;
            }
        }

        public static Kind ForDisk(DiskInfo d)
        {
            switch (d.Kind)
            {
                case DeviceKind.Removable: return Kind.Usb;
                case DeviceKind.Volume: return Kind.Volume;
                case DeviceKind.Image: return Kind.Image;
                default: return Kind.HardDisk;
            }
        }

        /// <summary>The application logo: a stylised disk platter with a recovery arrow.</summary>
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var lg = new LinearGradientBrush(r, Color.FromArgb(59, 130, 246), Color.FromArgb(124, 58, 237), 45f))
                g.FillEllipse(lg, r);
            float s = r.Width;
            using (var pen = new Pen(Color.FromArgb(230, Color.White), s * 0.07f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, r.X + s * 0.22f, r.Y + s * 0.22f, s * 0.56f, s * 0.56f, 210, 270);
                g.FillPolygon(Brushes.White, new[] {
                    new PointF(r.X + s * 0.13f, r.Y + s * 0.36f), new PointF(r.X + s * 0.33f, r.Y + s * 0.30f), new PointF(r.X + s * 0.30f, r.Y + s * 0.50f) });
            }
            g.FillEllipse(Brushes.White, r.X + s * 0.43f, r.Y + s * 0.43f, s * 0.14f, s * 0.14f);
            g.Restore(state);
        }
    }
}
