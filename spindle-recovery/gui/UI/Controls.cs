using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    public enum ButtonStyle { Primary, Secondary, Danger, Ghost }

    /// <summary>A flat, rounded button.</summary>
    public class FlatButton : Control
    {
        public ButtonStyle Style = ButtonStyle.Primary;
        public Icons.Kind? Icon;
        bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = Theme.BodyBold;
            Height = Theme.S(40);
            Width = Theme.S(160);
            BackColor = Color.Transparent;
        }

        public static FlatButton Create(string text, ButtonStyle style, int width = 160, Icons.Kind? icon = null)
        {
            return new FlatButton { Text = text, Style = style, Width = Theme.S(width), Icon = icon };
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color bg, fg, border;
            switch (Style)
            {
                case ButtonStyle.Primary:
                    bg = hover ? Theme.AccentHover : Theme.Accent; fg = Color.White; border = bg; break;
                case ButtonStyle.Danger:
                    bg = hover ? Color.FromArgb(254, 226, 226) : Color.White; fg = Theme.Danger; border = Color.FromArgb(252, 165, 165); break;
                case ButtonStyle.Ghost:
                    bg = hover ? Theme.AccentSoft : Color.Transparent; fg = Theme.Accent; border = Color.Transparent; break;
                default:
                    bg = hover ? Color.FromArgb(243, 246, 252) : Color.White; fg = Theme.Text; border = Theme.Border; break;
            }
            if (!Enabled)
            {
                bg = Style == ButtonStyle.Primary ? Color.FromArgb(180, 196, 230) : Color.FromArgb(246, 247, 250);
                fg = Style == ButtonStyle.Primary ? Color.White : Theme.TextFaint;
            }
            if (down) r.Inflate(-0.5f, -0.5f);
            using (var path = Theme.RoundRect(r, Theme.S(8f)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                if (border != Color.Transparent) using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            var textSize = g.MeasureString(Text, Font);
            float iconSize = Icon.HasValue ? Theme.S(18f) : 0;
            float gap = Icon.HasValue ? Theme.S(8f) : 0;
            float total = textSize.Width + iconSize + gap;
            float x = (Width - total) / 2;
            if (Icon.HasValue)
                Icons.Draw(g, Icon.Value, new RectangleF(x, (Height - iconSize) / 2, iconSize, iconSize), fg);
            using (var b = new SolidBrush(fg))
                g.DrawString(Text, Font, b, x + iconSize + gap, (Height - textSize.Height) / 2 + 1);
        }
    }

    /// <summary>A sidebar navigation entry.</summary>
    public class NavButton : Control
    {
        public Icons.Kind Icon;
        bool active, hover;
        public bool Active { get => active; set { active = value; Invalidate(); } }

        public NavButton(string text, Icons.Kind icon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text; Icon = icon;
            Height = Theme.S(44);
            Cursor = Cursors.Hand;
            Font = Theme.Font(10f);
            BackColor = Theme.Sidebar;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            g.Clear(Theme.Sidebar);
            var r = new RectangleF(Theme.S(10), Theme.S(3), Width - Theme.S(20), Height - Theme.S(6));
            if (active || hover)
                using (var path = Theme.RoundRect(r, Theme.S(8f)))
                using (var b = new SolidBrush(active ? Theme.SidebarActive : Theme.SidebarHover)) g.FillPath(b, path);
            if (active)
                using (var b = new SolidBrush(Color.FromArgb(96, 165, 250)))
                    g.FillRectangle(b, r.X, r.Y + Theme.S(10), Theme.S(3), r.Height - Theme.S(20));
            var color = active ? Theme.SidebarTextActive : Theme.SidebarText;
            float icon = Theme.S(18f);
            Icons.Draw(g, Icon, new RectangleF(r.X + Theme.S(14), (Height - icon) / 2, icon, icon), active ? Color.FromArgb(147, 197, 253) : color);
            using (var b = new SolidBrush(color))
            {
                var sz = g.MeasureString(Text, Font);
                g.DrawString(Text, Font, b, r.X + Theme.S(44), (Height - sz.Height) / 2);
            }
        }
    }

    /// <summary>A white rounded card.</summary>
    public class Card : Panel
    {
        public int Radius = 12;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Padding = new Padding(Theme.S(20));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            g.Clear(Parent?.BackColor ?? Theme.Background);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S((float)Radius)))
            {
                g.FillPath(Brushes.White, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>A slim, rounded progress bar with an optional indeterminate animation.</summary>
    public class ModernProgress : Control
    {
        double value;
        bool indeterminate;
        float phase;
        readonly Timer timer = new Timer { Interval = 30 };
        public Color BarColor = Theme.Accent;

        public ModernProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(10);
            timer.Tick += (s, e) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
        }

        public double Value { get => value; set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public bool Indeterminate
        {
            get => indeterminate;
            set { indeterminate = value; timer.Enabled = value; Invalidate(); }
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            g.Clear(Parent?.BackColor ?? Color.White);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(r, Height / 2f))
            using (var b = new SolidBrush(Color.FromArgb(229, 233, 242))) g.FillPath(b, path);
            RectangleF fill;
            if (indeterminate)
            {
                float w = Width * 0.3f;
                float x = (phase - 0.3f) * Width;
                fill = RectangleF.Intersect(new RectangleF(x, 0, w, Height - 1), r);
            }
            else fill = new RectangleF(0, 0, (float)(r.Width * value), r.Height);
            if (fill.Width > 1)
                using (var path = Theme.RoundRect(fill, Height / 2f))
                using (var b = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(2, Width), Height), Color.FromArgb(59, 130, 246), Color.FromArgb(124, 58, 237), 0f))
                    g.FillPath(b, path);
        }
    }

    /// <summary>Tile showing how many files of a category were found.</summary>
    public class CategoryTile : Control
    {
        public FileCategory Category;
        public int Count;
        public long Bytes;
        bool hover;
        public bool Selected;

        public CategoryTile(FileCategory c)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Category = c;
            Size = new Size(Theme.S(150), Theme.S(84));
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            g.Clear(Parent?.BackColor ?? Theme.Background);
            var color = Theme.CategoryColors[(int)Category];
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(12f)))
            {
                g.FillPath(Brushes.White, path);
                using (var p = new Pen(hover || Selected ? color : Theme.Border, hover || Selected ? 1.6f : 1f)) g.DrawPath(p, path);
            }
            bool compact = Width < Theme.S(150);
            float ic = Theme.S(compact ? 28f : 40f);
            var ir = new RectangleF(Theme.S(compact ? 10 : 14), (Height - ic) / 2, ic, ic);
            using (var path = Theme.RoundRect(ir, Theme.S(10f)))
            using (var b = new SolidBrush(Color.FromArgb(30, color))) g.FillPath(b, path);
            ir.Inflate(-Theme.S(compact ? 6f : 9f), -Theme.S(compact ? 6f : 9f));
            Icons.Draw(g, Icons.ForCategory(Category), ir, color);
            float tx = Theme.S(compact ? 46 : 66);
            float maxW = Width - tx - Theme.S(8);
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(DeviceList.Ellipsis(g, FileTypes.DisplayName(Category), Theme.BodyBold, maxW), Theme.BodyBold, b, tx, Height / 2f - Theme.S(20));
            using (var b = new SolidBrush(Theme.TextMuted))
            {
                g.DrawString(DeviceList.Ellipsis(g, $"{Count:N0} files", Theme.Small, maxW), Theme.Small, b, tx, Height / 2f + Theme.S(1));
                if (Height > Theme.S(70))
                    g.DrawString(DeviceList.Ellipsis(g, FileTypes.FormatSize(Bytes), Theme.Small, maxW), Theme.Small, b, tx, Height / 2f + Theme.S(17));
            }
        }
    }

    /// <summary>A rounded, toggleable pill used to pick file types.</summary>
    public class ChipToggle : Control
    {
        bool selected, hover;
        public Color Accent = Theme.Accent;
        public Icons.Kind? Icon;
        public event EventHandler SelectedChanged;

        public bool Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
        }

        public ChipToggle(string text, Icons.Kind? icon, Color accent)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = text; Icon = icon; Accent = accent;
            Font = Theme.Font(9f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            Height = Theme.S(32);
            using (var g = CreateGraphics())
                Width = (int)Math.Ceiling(g.MeasureString(text, Font).Width) + Theme.S(icon.HasValue ? 44 : 28);
            TabStop = true;
        }

        /// <summary>Changes the text and resizes the chip to fit it.</summary>
        public void SetLabel(string text)
        {
            if (Text == text) return;
            Text = text;
            using (var g = CreateGraphics())
                Width = (int)Math.Ceiling(g.MeasureString(text, Font).Width) + Theme.S(Icon.HasValue ? 44 : 28);
            Invalidate();
        }

        protected override void OnClick(EventArgs e) { Focus(); Selected = !Selected; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Selected = !Selected; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.HighQuality(g);
            g.Clear(Parent?.BackColor ?? Color.White);
            var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
            using (var path = Theme.RoundRect(r, Height / 2f))
            {
                using (var b = new SolidBrush(selected ? Color.FromArgb(28, Accent) : hover ? Color.FromArgb(246, 248, 252) : Color.White)) g.FillPath(b, path);
                using (var p = new Pen(selected ? Accent : Focused ? Theme.Accent : Theme.Border, selected ? 1.6f : 1f)) g.DrawPath(p, path);
            }
            float x = Theme.S(10f);
            var fg = selected ? Accent : Theme.TextMuted;
            if (selected)
            {
                Icons.Draw(g, Icons.Kind.Check, new RectangleF(x, (Height - Theme.S(13f)) / 2, Theme.S(13f), Theme.S(13f)), Accent);
                x += Theme.S(16f);
            }
            else if (Icon.HasValue)
            {
                Icons.Draw(g, Icon.Value, new RectangleF(x, (Height - Theme.S(15f)) / 2, Theme.S(15f), Theme.S(15f)), Theme.TextFaint);
                x += Theme.S(18f);
            }
            using (var b = new SolidBrush(selected ? Theme.Text : fg))
            {
                var sz = g.MeasureString(Text, Font);
                g.DrawString(Text, Font, b, x, (Height - sz.Height) / 2 + 0.5f);
            }
        }
    }

    /// <summary>A label that wraps to its width.</summary>
    public class WrapLabel : Label
    {
        public WrapLabel() { AutoSize = false; }
        protected override void OnResize(EventArgs e) { base.OnResize(e); FitHeight(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitHeight(); }
        void FitHeight()
        {
            if (Width <= 0) return;
            var sz = TextRenderer.MeasureText(Text, Font, new Size(Width, int.MaxValue), TextFormatFlags.WordBreak);
            Height = sz.Height + Theme.S(2);
        }
    }
}
