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
        FileSelection selection;
        readonly WrapLabel typesSummary = new WrapLabel();
        readonly CheckBox freeOnly = new CheckBox();
        readonly CheckBox keepCorrupted = new CheckBox();
        readonly TextBox vault = new TextBox();
        public DeepScanOptions Options { get; private set; }

        public static string DefaultVaultRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Spindle Recovery Vault");

        public ScanOptionsDialog(DiskInfo disk, PartitionInfo part, FileSelection preselected = null)
        {
            this.disk = disk; this.part = part;
            selection = preselected == null || preselected.IsEmpty ? FileSelection.All() : preselected;
            Text = "Deep Scan options";
            Font = Theme.Body;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(600), Theme.S(470));

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

            // the formats chosen on the home screen, with a way to change them
            var lookFor = new Label { Text = "Looking for:", Font = Theme.BodyBold, ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, y + Theme.S(2)) };
            Controls.Add(lookFor);
            var change = FlatButton.Create("Change…", ButtonStyle.Secondary, 100);
            change.Height = Theme.S(30);
            change.Location = new Point(ClientSize.Width - x - change.Width, y - Theme.S(4));
            change.Click += (s, e) =>
            {
                using (var dlg = new FormatPickerDialog(selection))
                    if (dlg.ShowDialog(this) == DialogResult.OK) { selection = dlg.Selection; ShowSelection(); }
            };
            Controls.Add(change);
            typesSummary.Font = Theme.Body;
            typesSummary.ForeColor = Theme.Text;
            typesSummary.Location = new Point(x + lookFor.PreferredWidth + Theme.S(8), y + Theme.S(2));
            typesSummary.Width = change.Left - typesSummary.Left - Theme.S(10);
            Controls.Add(typesSummary);
            ShowSelection();
            y += Theme.S(50);

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

        void ShowSelection()
        {
            var text = selection.Describe();
            typesSummary.Text = char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        void Accept()
        {
            if (selection.IsEmpty)
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
                Selection = selection,
                Categories = selection.Categories,
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
