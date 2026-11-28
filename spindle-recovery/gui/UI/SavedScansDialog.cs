using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>Lists saved scans so their results can be reopened without scanning again.</summary>
    public class SavedScansDialog : Form
    {
        readonly ListView list = new ListView();
        readonly FlatButton openBtn = FlatButton.Create("Open", ButtonStyle.Primary, 120);
        readonly FlatButton deleteBtn = FlatButton.Create("Delete", ButtonStyle.Danger, 110);
        readonly FlatButton closeBtn = FlatButton.Create("Close", ButtonStyle.Secondary, 110);
        public string SelectedId { get; private set; }

        public SavedScansDialog()
        {
            Text = "Saved scans";
            Font = Theme.Body;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(760), Theme.S(440));
            MinimumSize = new Size(Theme.S(600), Theme.S(320));

            var hint = new Label
            {
                Text = "Reopen the results of an earlier scan. Files from the file system need the same drive (or image) to be connected; reconstructed files are read from their recovery vault.",
                Font = Theme.Small, ForeColor = Theme.TextMuted, Dock = DockStyle.Top, Height = Theme.S(44), Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), 0),
            };
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.BorderStyle = BorderStyle.None;
            list.Dock = DockStyle.Fill;
            list.Columns.Add("Scanned", Theme.S(150));
            list.Columns.Add("Device", Theme.S(300));
            list.Columns.Add("Files", Theme.S(80), HorizontalAlignment.Right);
            list.Columns.Add("Deleted", Theme.S(80), HorizontalAlignment.Right);
            list.Columns.Add("Size", Theme.S(90), HorizontalAlignment.Right);
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(16), Theme.S(4), Theme.S(16), Theme.S(4)), BackColor = Color.White };
            listHost.Controls.Add(list);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(64), BackColor = Color.White };
            bar.Controls.AddRange(new Control[] { openBtn, deleteBtn, closeBtn });
            bar.Resize += (s, e) =>
            {
                closeBtn.Location = new Point(bar.Width - closeBtn.Width - Theme.S(16), Theme.S(12));
                openBtn.Location = new Point(closeBtn.Left - openBtn.Width - Theme.S(10), Theme.S(12));
                deleteBtn.Location = new Point(Theme.S(16), Theme.S(12));
            };
            Controls.Add(listHost);
            Controls.Add(bar);
            Controls.Add(hint);

            openBtn.Click += (s, e) => Open();
            list.DoubleClick += (s, e) => Open();
            closeBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            deleteBtn.Click += (s, e) =>
            {
                if (list.SelectedItems.Count == 0) return;
                var m = (ScanStore.Meta)list.SelectedItems[0].Tag;
                if (MessageBox.Show(this, "Delete the saved results of this scan?\n\nRecovered files and the recovery vault are not touched.", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { ScanStore.Delete(m.Id); } catch (Exception ex) { MainForm.Error(this, "Could not delete the saved scan.", ex); }
                Reload();
            };
            list.SelectedIndexChanged += (s, e) => openBtn.Enabled = deleteBtn.Enabled = list.SelectedItems.Count > 0;
            Reload();
        }

        void Reload()
        {
            list.Items.Clear();
            foreach (var m in ScanStore.List())
            {
                var it = new ListViewItem(new[]
                {
                    m.Created.ToString("g"), m.Title + (m.Stopped ? "  (stopped)" : ""),
                    m.Files.ToString("N0"), m.Deleted.ToString("N0"), FileTypes.FormatSize(m.Bytes),
                }) { Tag = m };
                list.Items.Add(it);
            }
            if (list.Items.Count > 0) list.Items[0].Selected = true;
            openBtn.Enabled = deleteBtn.Enabled = list.Items.Count > 0;
        }

        void Open()
        {
            if (list.SelectedItems.Count == 0) return;
            SelectedId = ((ScanStore.Meta)list.SelectedItems[0].Tag).Id;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
