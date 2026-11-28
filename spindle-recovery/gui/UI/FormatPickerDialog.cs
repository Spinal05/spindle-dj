using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>Lets you tick individual formats (JPEG, HEIC, MP4, DOCX…) inside each file-type category.</summary>
    public class FormatPickerDialog : Form
    {
        readonly TreeView tree = new TreeView();
        readonly Label summary = new Label();
        readonly Dictionary<FileCategory, TreeNode> catNodes = new Dictionary<FileCategory, TreeNode>();
        bool syncing;

        public FileSelection Selection { get; private set; }

        public FormatPickerDialog(FileSelection current)
        {
            Text = "Choose file formats";
            Font = Theme.Body;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(640), Theme.S(600));
            MinimumSize = new Size(Theme.S(520), Theme.S(420));

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(70), BackColor = Color.White, Padding = new Padding(Theme.S(20), Theme.S(14), Theme.S(20), 0) };
            var title = new Label { Text = "Which formats should be recovered?", Font = Theme.Section, ForeColor = Theme.Text, AutoSize = true, Location = new Point(Theme.S(20), Theme.S(14)) };
            var hint = new Label
            {
                Text = "Tick a category for all of its formats, or open it to pick individual formats. Fewer formats make Deep Scan faster.",
                Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = false, Location = new Point(Theme.S(20), Theme.S(40)),
                Size = new Size(Theme.S(600), Theme.S(20)),
            };
            head.Controls.Add(title);
            head.Controls.Add(hint);
            head.Resize += (s, e) => hint.Width = head.Width - Theme.S(40);

            tree.CheckBoxes = true;
            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.Font = Theme.Body;
            tree.ItemHeight = Theme.S(26);
            tree.ShowLines = false;
            tree.FullRowSelect = true;
            tree.HideSelection = true;
            foreach (var c in FileTypes.DisplayOrder)
            {
                var cn = new TreeNode(FileTypes.DisplayName(c)) { Tag = c, NodeFont = Theme.BodyBold };
                foreach (var f in FileTypes.Formats.Where(f => f.Category == c))
                    cn.Nodes.Add(new TreeNode(f.Label + "    " + f.ExtensionList) { Tag = f, Checked = current == null || current.FormatIds.Contains(f.Id) });
                catNodes[c] = cn;
                tree.Nodes.Add(cn);
            }
            foreach (var cn in catNodes.Values) UpdateParent(cn);
            tree.ExpandAll();
            tree.AfterCheck += OnAfterCheck;
            Shown += (s, e) => { if (tree.Nodes.Count > 0) { tree.SelectedNode = null; tree.Nodes[0].EnsureVisible(); } };
            var treeHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(16), Theme.S(4), Theme.S(16), Theme.S(4)), BackColor = Color.White };
            treeHost.Controls.Add(tree);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(104), BackColor = Color.White };
            summary.Font = Theme.Small; summary.ForeColor = Theme.TextMuted; summary.AutoSize = false;
            summary.Location = new Point(Theme.S(20), Theme.S(8)); summary.Height = Theme.S(34);
            var all = FlatButton.Create("Select all", ButtonStyle.Ghost, 110);
            var none = FlatButton.Create("Clear", ButtonStyle.Ghost, 90);
            var popular = FlatButton.Create("Photos & videos", ButtonStyle.Ghost, 150);
            var ok = FlatButton.Create("OK", ButtonStyle.Primary, 110);
            var cancel = FlatButton.Create("Cancel", ButtonStyle.Secondary, 110);
            foreach (var b in new[] { all, none, popular }) b.Height = Theme.S(34);
            bar.Controls.AddRange(new Control[] { summary, all, none, popular, ok, cancel });
            bar.Resize += (s, e) =>
            {
                summary.Width = bar.Width - Theme.S(40);
                all.Location = new Point(Theme.S(12), Theme.S(52));
                none.Location = new Point(all.Right + Theme.S(4), Theme.S(52));
                popular.Location = new Point(none.Right + Theme.S(4), Theme.S(52));
                cancel.Location = new Point(bar.Width - cancel.Width - Theme.S(20), Theme.S(50));
                ok.Location = new Point(cancel.Left - ok.Width - Theme.S(10), Theme.S(50));
            };
            all.Click += (s, e) => SetAll(_ => true);
            none.Click += (s, e) => SetAll(_ => false);
            popular.Click += (s, e) => SetAll(f => f.Category == FileCategory.Pictures || f.Category == FileCategory.Video);
            ok.Click += (s, e) =>
            {
                var sel = Current();
                if (sel.IsEmpty)
                {
                    MessageBox.Show(this, "Choose at least one format.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Selection = sel;
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(treeHost);
            Controls.Add(bar);
            Controls.Add(head);
            UpdateSummary();
        }

        FileSelection Current() => new FileSelection(catNodes.Values.SelectMany(cn => cn.Nodes.Cast<TreeNode>())
            .Where(n => n.Checked).Select(n => ((FormatOption)n.Tag).Id));

        void SetAll(Func<FormatOption, bool> pick)
        {
            syncing = true;
            foreach (var cn in catNodes.Values)
            {
                foreach (TreeNode n in cn.Nodes) n.Checked = pick((FormatOption)n.Tag);
                UpdateParent(cn);
            }
            syncing = false;
            UpdateSummary();
        }

        void OnAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (syncing) return;
            syncing = true;
            if (e.Node.Tag is FileCategory)
            {
                foreach (TreeNode n in e.Node.Nodes) n.Checked = e.Node.Checked;
                UpdateParent(e.Node);
            }
            else if (e.Node.Parent != null) UpdateParent(e.Node.Parent);
            syncing = false;
            UpdateSummary();
        }

        /// <summary>Category box is ticked when any of its formats is; its label shows how many.</summary>
        void UpdateParent(TreeNode cn)
        {
            var c = (FileCategory)cn.Tag;
            int total = cn.Nodes.Count, on = cn.Nodes.Cast<TreeNode>().Count(n => n.Checked);
            bool was = syncing;
            syncing = true;
            cn.Checked = on > 0;
            syncing = was;
            cn.Text = FileTypes.DisplayName(c) + (on == total ? "" : on == 0 ? "   (none)" : $"   ({on} of {total})");
        }

        void UpdateSummary()
        {
            var sel = Current();
            summary.ForeColor = sel.IsEmpty ? Theme.Danger : Theme.TextMuted;
            summary.Text = sel.IsEmpty ? "Nothing is chosen yet." : "Looking for: " + sel.Describe();
        }
    }
}
