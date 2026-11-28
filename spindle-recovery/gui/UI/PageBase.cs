using System.Drawing;
using System.Windows.Forms;

namespace SpindleRecovery.UI
{
    /// <summary>A page with a title and subtitle header and a content area below it.</summary>
    public class PageBase : UserControl
    {
        protected readonly Panel Header = new Panel();
        protected readonly Panel Body = new Panel();
        protected readonly Label TitleLabel = new Label();
        protected readonly Label SubtitleLabel = new Label();

        public PageBase(string title, string subtitle)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Background;
            Font = Theme.Body;
            Padding = new Padding(Theme.S(32), Theme.S(24), Theme.S(32), Theme.S(24));

            Header.Dock = DockStyle.Top;
            Header.Height = Theme.S(78);
            Header.BackColor = Theme.Background;

            TitleLabel.Text = title;
            TitleLabel.Font = Theme.Title;
            TitleLabel.ForeColor = Theme.Text;
            TitleLabel.AutoSize = true;
            TitleLabel.Location = new Point(0, 0);

            SubtitleLabel.Text = subtitle;
            SubtitleLabel.Font = Theme.Subtitle;
            SubtitleLabel.ForeColor = Theme.TextMuted;
            SubtitleLabel.AutoSize = true;
            SubtitleLabel.Location = new Point(Theme.S(2), Theme.S(40));

            Header.Controls.Add(TitleLabel);
            Header.Controls.Add(SubtitleLabel);

            Body.Dock = DockStyle.Fill;
            Body.BackColor = Theme.Background;

            Controls.Add(Body);
            Controls.Add(Header);
        }

        /// <summary>Called by the main window each time this page is brought to the front.</summary>
        public virtual void OnPageShown() { }

        protected static Label MakeLabel(string text, Font font, Color color, bool autoSize = true)
        {
            return new Label { Text = text, Font = font, ForeColor = color, AutoSize = autoSize, BackColor = Color.Transparent };
        }
    }
}
