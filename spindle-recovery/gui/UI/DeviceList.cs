using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>Owner-drawn list of disks with their partitions nested underneath.</summary>
    public class DeviceList : ScrollableControl
    {
        class Row
        {
            public string Header;
            public DiskInfo Disk;
            public PartitionInfo Part;
            public Rectangle Bounds;
        }

        readonly List<Row> rows = new List<Row>();
        List<DiskInfo> disks = new List<DiskInfo>();
        Row hover, selected;

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;

        public DiskInfo SelectedDisk => selected?.Disk ?? selected?.Part?.Disk;
        public PartitionInfo SelectedPartition => selected?.Part;
        public string EmptyText = "Looking for storage devices…";

        public DeviceList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.White;
            AutoScroll = true;
        }

        public void SetDisks(List<DiskInfo> list)
        {
            var prevPath = SelectedDisk?.Path;
            var prevPart = SelectedPartition?.Number;
            disks = list ?? new List<DiskInfo>();
            Layout2();
            selected = rows.FirstOrDefault(r => r.Header == null && (r.Disk ?? r.Part.Disk).Path == prevPath && (prevPart == null ? r.Part == null : r.Part?.Number == prevPart))
                       ?? rows.FirstOrDefault(r => r.Header == null);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Select(DiskInfo disk)
        {
            selected = rows.FirstOrDefault(r => r.Disk == disk) ?? selected;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Layout2(); }

        void Layout2()
        {
            rows.Clear();
            int y = 0;
            int w = ClientSize.Width;
            void AddHeader(string text) { rows.Add(new Row { Header = text, Bounds = new Rectangle(0, y, w, Theme.S(40)) }); y += Theme.S(40); }
            var devices = disks.Where(d => d.Kind != DeviceKind.Image).ToList();
            var images = disks.Where(d => d.Kind == DeviceKind.Image).ToList();
            if (devices.Count > 0) AddHeader("STORAGE DEVICES");
            foreach (var d in devices) AddDisk(d, ref y, w);
            if (images.Count > 0) AddHeader("DISK IMAGES");
            foreach (var d in images) AddDisk(d, ref y, w);
            AutoScrollMinSize = new Size(0, y + Theme.S(8));
        }

        void AddDisk(DiskInfo d, ref int y, int w)
        {
            rows.Add(new Row { Disk = d, Bounds = new Rectangle(0, y, w, Theme.S(64)) });
            y += Theme.S(64);
            foreach (var p in d.Partitions.Where(p => p.Status != 'E'))
            {
                rows.Add(new Row { Part = p, Bounds = new Rectangle(0, y, w, Theme.S(50)) });
                y += Theme.S(50);
            }
        }

        Row HitTest(Point p)
        {
            var pt = new Point(p.X, p.Y - AutoScrollPosition.Y);
            return rows.FirstOrDefault(r => r.Header == null && r.Bounds.Contains(pt));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = HitTest(e.Location);
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = null; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var h = HitTest(e.Location);
            if (h != null && h != selected) { selected = h; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (HitTest(e.Location) != null) ItemActivated?.Invoke(this, EventArgs.Empty);
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            var selectable = rows.Where(r => r.Header == null).ToList();
            int i = selectable.IndexOf(selected);
            if (e.KeyCode == Keys.Down && i < selectable.Count - 1) { selected = selectable[i + 1]; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
            if (e.KeyCode == Keys.Up && i > 0) { selected = selectable[i - 1]; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
            if (e.KeyCode == Keys.Enter && selected != null) ItemActivated?.Invoke(this, EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            g.Clear(Color.White);
            if (rows.Count == 0)
            {
                using (var b = new SolidBrush(Theme.TextMuted))
                {
                    var sz = g.MeasureString(EmptyText, Theme.Body);
                    g.DrawString(EmptyText, Theme.Body, b, (Width - sz.Width) / 2, Theme.S(60));
                }
                return;
            }
            g.TranslateTransform(0, AutoScrollPosition.Y);
            int sizeColX = Width - Theme.S(150);
            int fsColX = Width - Theme.S(300);
            foreach (var r in rows)
            {
                var b = r.Bounds;
                if (b.Bottom + AutoScrollPosition.Y < 0 || b.Top + AutoScrollPosition.Y > Height) continue;
                if (r.Header != null)
                {
                    using (var br = new SolidBrush(Theme.TextFaint))
                        g.DrawString(r.Header, Theme.Font(8f, FontStyle.Bold), br, Theme.S(24), b.Y + Theme.S(16));
                    continue;
                }
                if (r == selected || r == hover)
                {
                    var hr = new RectangleF(Theme.S(8), b.Y + 2, b.Width - Theme.S(16), b.Height - 4);
                    using (var path = Theme.RoundRect(hr, Theme.S(8f)))
                    using (var br = new SolidBrush(r == selected ? Theme.RowSelected : Theme.RowHover)) g.FillPath(br, path);
                }
                if (r.Disk != null)
                {
                    var d = r.Disk;
                    var tile = new RectangleF(Theme.S(22), b.Y + Theme.S(12), Theme.S(40), Theme.S(40));
                    using (var path = Theme.RoundRect(tile, Theme.S(10f)))
                    using (var br = new SolidBrush(Theme.AccentSoft)) g.FillPath(br, path);
                    tile.Inflate(-Theme.S(8f), -Theme.S(8f));
                    Icons.Draw(g, Icons.ForDisk(d), tile, Theme.Accent);
                    using (var br = new SolidBrush(Theme.Text))
                        g.DrawString(Ellipsis(g, d.Title, Theme.BodyBold, fsColX - Theme.S(90)), Theme.BodyBold, br, Theme.S(76), b.Y + Theme.S(13));
                    var sub = new List<string>();
                    if (d.Kind == DeviceKind.Image) sub.Add(d.Path);
                    else
                    {
                        sub.Add(d.Path);
                        if (!string.IsNullOrEmpty(d.Interface)) sub.Add(d.Interface);
                        if (d.Kind == DeviceKind.Removable) sub.Add("Removable");
                    }
                    if (d.ReadOnly) sub.Add("read-only");
                    using (var br = new SolidBrush(Theme.TextMuted))
                    {
                        g.DrawString(Ellipsis(g, string.Join("  ·  ", sub), Theme.Small, fsColX - Theme.S(90)), Theme.Small, br, Theme.S(76), b.Y + Theme.S(35));
                        var pt = d.Partitions.Count == 0 ? "No partitions" : $"{d.Partitions.Count(p => p.Status != 'E')} partition{(d.Partitions.Count(p => p.Status != 'E') == 1 ? "" : "s")}";
                        g.DrawString(pt, Theme.Small, br, fsColX, b.Y + Theme.S(24));
                    }
                    using (var br = new SolidBrush(Theme.Text))
                        g.DrawString(FileTypes.FormatSize(d.SizeBytes), Theme.BodyBold, br, sizeColX, b.Y + Theme.S(22));
                }
                else
                {
                    var p = r.Part;
                    using (var pen = new Pen(Theme.Border, 1.2f))
                    {
                        g.DrawLine(pen, Theme.S(42), b.Y - Theme.S(4), Theme.S(42), b.Y + b.Height / 2);
                        g.DrawLine(pen, Theme.S(42), b.Y + b.Height / 2, Theme.S(56), b.Y + b.Height / 2);
                    }
                    var ic = new RectangleF(Theme.S(64), b.Y + Theme.S(14), Theme.S(22), Theme.S(22));
                    Icons.Draw(g, Icons.Kind.Volume, ic, p.Status == 'D' ? Theme.Warning : Theme.TextMuted);
                    using (var br = new SolidBrush(Theme.Text))
                        g.DrawString(Ellipsis(g, p.DisplayName, Theme.Body, fsColX - Theme.S(110)), Theme.Body, br, Theme.S(98), b.Y + Theme.S(8));
                    using (var br = new SolidBrush(Theme.TextMuted))
                    {
                        var info = (p.Number > 0 ? "#" + p.Number + "  ·  " : "") + StatusText(p.Status);
                        g.DrawString(info, Theme.Small, br, Theme.S(98), b.Y + Theme.S(28));
                        g.DrawString(Ellipsis(g, p.FileSystem, Theme.Small, Theme.S(140)), Theme.Small, br, fsColX, b.Y + Theme.S(17));
                        g.DrawString(FileTypes.FormatSize(p.SizeBytes), Theme.Small, br, sizeColX, b.Y + Theme.S(17));
                    }
                }
            }
        }

        static string StatusText(char c)
        {
            switch (c)
            {
                case '*': return "Primary, bootable";
                case 'P': return "Primary";
                case 'L': return "Logical";
                case 'E': return "Extended";
                case 'D': return "Deleted";
                default: return "Volume";
            }
        }

        public static string Ellipsis(Graphics g, string text, Font f, float max)
        {
            if (string.IsNullOrEmpty(text) || g.MeasureString(text, f).Width <= max) return text ?? "";
            for (int n = text.Length - 1; n > 1; n--)
            {
                var t = text.Substring(0, n) + "…";
                if (g.MeasureString(t, f).Width <= max) return t;
            }
            return "…";
        }
    }
}
