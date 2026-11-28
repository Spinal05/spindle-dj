using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>Deep Scan options: file types, free-space-only, and where reconstructed files are kept.</summary>
    public class ScanOptionsDialog : Form
    {
        readonly DiskInfo disk;
        readonly PartitionInfo part;
        readonly Dictionary<FileCategory, CheckBox> cats = new Dictionary<FileCategory, CheckBox>();
        readonly CheckBox freeOnly = new CheckBox();
        readonly CheckBox keepCorrupted = new CheckBox();
        readonly TextBox vault = new TextBox();
        public DeepScanOptions Options { get; private set; }

        public static string DefaultVaultRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Spindle Recovery Vault");

        public ScanOptionsDialog(DiskInfo disk, PartitionInfo part, ICollection<FileCategory> preselected = null)
        {
            this.disk = disk; this.part = part;
            Text = "Deep Scan options";
            Font = Theme.Body;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(600), Theme.S(500));

            int y = Theme.S(22), x = Theme.S(28);
            var title = new Label { Text = "What should Deep Scan look for?", Font = Theme.Section, ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, y) };
            Controls.Add(title);
            y += Theme.S(30);
            var hint = new WrapLabel
            {
                Text = "Deep Scan reads every sector and rebuilds files from their content signatures. It works on any file system, " +
                       "including formatted and damaged drives, but original file names and folders cannot be restored.",
                Font = Theme.Small, ForeColor = Theme.TextMuted, Location = new Point(x, y), Width = ClientSize.Width - 2 * x,
            };
            Controls.Add(hint);
            y += hint.Height + Theme.S(12);

            int col = 0;
            var tips = new ToolTip();
            foreach (var c in FileTypes.DisplayOrder)
            {
                var cb = new CheckBox
                {
                    Text = FileTypes.DisplayName(c), AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text,
                    Checked = preselected == null || preselected.Count == 0 || preselected.Contains(c),
                    Location = new Point(x + col * Theme.S(180), y),
                };
                tips.SetToolTip(cb, FileTypes.Examples(c));
                cats[c] = cb;
                Controls.Add(cb);
                if (++col == 3) { col = 0; y += Theme.S(30); }
            }
            if (col != 0) y += Theme.S(30);
            y += Theme.S(6);

            freeOnly.Text = "Search unallocated space only (faster — finds deleted files only)";
            freeOnly.AutoSize = true; freeOnly.Location = new Point(x, y); freeOnly.ForeColor = Theme.Text;
            freeOnly.Enabled = part != null;
            Controls.Add(freeOnly);
            y += Theme.S(28);
            keepCorrupted.Text = "Keep partially recovered (possibly corrupted) files";
            keepCorrupted.AutoSize = true; keepCorrupted.Location = new Point(x, y); keepCorrupted.ForeColor = Theme.Text;
            Controls.Add(keepCorrupted);
            y += Theme.S(44);

            Controls.Add(new Label { Text = "Recovery vault", Font = Theme.Section, ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, y) });
            y += Theme.S(28);
            var vhint = new WrapLabel
            {
                Text = "Reconstructed files are written here while scanning. Choose a different drive from the one you are recovering, " +
                       "otherwise new data may overwrite the files you are trying to get back.",
                Font = Theme.Small, ForeColor = Theme.TextMuted, Location = new Point(x, y), Width = ClientSize.Width - 2 * x,
            };
            Controls.Add(vhint);
            y += vhint.Height + Theme.S(8);

            var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmm");
            var safeName = Regex.Replace((part?.DisplayName ?? disk.Title) ?? "Disk", @"[^\w\- ()]", "_").Trim();
            vault.Text = Path.Combine(DefaultVaultRoot, safeName + " " + stamp);
            vault.Location = new Point(x, y);
            vault.Width = ClientSize.Width - 2 * x - Theme.S(110);
            vault.Font = Theme.Body;
            Controls.Add(vault);
            var browse = FlatButton.Create("Browse…", ButtonStyle.Secondary, 100);
            browse.Height = Theme.S(30);
            browse.Location = new Point(vault.Right + Theme.S(10), y - Theme.S(3));
            browse.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog { Description = "Choose where reconstructed files are stored", ShowNewFolderButton = true })
                    if (fb.ShowDialog(this) == DialogResult.OK) vault.Text = Path.Combine(fb.SelectedPath, "Spindle Recovery " + stamp);
            };
            Controls.Add(browse);

            var ok = FlatButton.Create("Start scan", ButtonStyle.Primary, 140, Icons.Kind.Search);
            var cancel = FlatButton.Create("Cancel", ButtonStyle.Secondary, 110);
            ok.Location = new Point(ClientSize.Width - x - ok.Width, ClientSize.Height - Theme.S(64));
            cancel.Location = new Point(ok.Left - cancel.Width - Theme.S(10), ok.Top);
            ok.Click += (s, e) => Accept();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = null;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        void Accept()
        {
            var chosen = new HashSet<FileCategory>(cats.Where(kv => kv.Value.Checked).Select(kv => kv.Key));
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "Choose at least one kind of file.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var path = vault.Text.Trim();
            if (path.Length == 0 || !Path.IsPathRooted(path))
            {
                MessageBox.Show(this, "Choose a full folder path for the recovery vault.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (IsOnSourceDrive(path, disk, part))
            {
                var r = MessageBox.Show(this,
                    "The recovery vault is on the same drive you are scanning.\n\n" +
                    "Writing to it can overwrite the deleted files you want to recover. Choose another drive (for example an external disk) if you can.\n\n" +
                    "Continue anyway?", "Same drive", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            try { Directory.CreateDirectory(path); }
            catch (Exception ex) { MainForm.Error(this, "The recovery vault folder could not be created.", ex); return; }
            Options = new DeepScanOptions
            {
                Disk = disk,
                Partition = part,
                Categories = chosen,
                FreeSpaceOnly = freeOnly.Checked && part != null,
                KeepCorrupted = keepCorrupted.Checked,
                VaultDir = path,
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>True when <paramref name="path"/> lives on the device (or partition) being recovered.</summary>
        public static bool IsOnSourceDrive(string path, DiskInfo disk, PartitionInfo part)
        {
            string root;
            try { root = Path.GetPathRoot(Path.GetFullPath(path))?.TrimEnd('\\', '/'); } catch { return false; }
            if (string.IsNullOrEmpty(root)) return false;
            var letters = new List<string>();
            if (part != null) { if (part.DriveLetter != null) letters.Add(part.DriveLetter); }
            else letters.AddRange(disk.Partitions.Where(p => p.DriveLetter != null).Select(p => p.DriveLetter));
            if (disk.Kind == DeviceKind.Volume && disk.Path.Length >= 6) letters.Add(disk.Path.Substring(4, 2));
            return letters.Any(l => string.Equals(l, root, StringComparison.OrdinalIgnoreCase));
        }
    }
}
