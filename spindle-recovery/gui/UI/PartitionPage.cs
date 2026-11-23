using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>TestDisk partition table analysis and repair.</summary>
    public class PartitionPage : PageBase
    {
        readonly ComboBox diskBox = new ComboBox();
        readonly FlatButton refreshBtn = FlatButton.Create("Refresh", ButtonStyle.Secondary, 100);
        readonly FlatButton analyzeBtn = FlatButton.Create("Quick analysis", ButtonStyle.Primary, 160, Icons.Kind.Search);
        readonly FlatButton deeperBtn = FlatButton.Create("Deeper search", ButtonStyle.Secondary, 150);
        readonly FlatButton writeBtn = FlatButton.Create("Write partition table", ButtonStyle.Danger, 200);
        readonly FlatButton backupBtn = FlatButton.Create("Back up table", ButtonStyle.Ghost, 140);
        readonly FlatButton logBtn = FlatButton.Create("View log", ButtonStyle.Ghost, 110);
        readonly ModernProgress busy = new ModernProgress();
        readonly Label status = new Label();
        readonly DataGridView currentGrid = MakeGrid();
        readonly DataGridView foundGrid = MakeGrid();
        readonly Label currentTitle = new Label();
        readonly Label foundTitle = new Label();
        List<DiskInfo> disks = new List<DiskInfo>();
        PartitionTableResult last;
        bool lastDeeper;
        DiskInfo lastDisk;

        public PartitionPage() : base("Partition Recovery",
            "Find lost or deleted partitions and rebuild the partition table with TestDisk.")
        {
            var top = new Card { Dock = DockStyle.Top, Height = Theme.S(150) };
            var lbl = MakeLabel("Disk", Theme.BodyBold, Theme.Text);
            lbl.BackColor = Color.White;
            lbl.Location = new Point(Theme.S(22), Theme.S(22));
            diskBox.DropDownStyle = ComboBoxStyle.DropDownList;
            diskBox.Font = Theme.Body;
            diskBox.Location = new Point(Theme.S(70), Theme.S(18));
            diskBox.Width = Theme.S(460);
            refreshBtn.Height = Theme.S(30);
            refreshBtn.Location = new Point(diskBox.Right + Theme.S(10), Theme.S(15));
            analyzeBtn.Location = new Point(Theme.S(22), Theme.S(62));
            deeperBtn.Location = new Point(analyzeBtn.Right + Theme.S(10), Theme.S(62));
            writeBtn.Location = new Point(deeperBtn.Right + Theme.S(10), Theme.S(62));
            backupBtn.Location = new Point(writeBtn.Right + Theme.S(10), Theme.S(62));
            logBtn.Location = new Point(backupBtn.Right + Theme.S(4), Theme.S(62));
            busy.Location = new Point(Theme.S(22), Theme.S(114));
            busy.BackColor = Color.White;
            busy.Visible = false;
            status.Font = Theme.Small; status.ForeColor = Theme.TextMuted; status.AutoSize = false; status.BackColor = Color.White;
            status.Location = new Point(Theme.S(22), Theme.S(110)); status.Size = new Size(Theme.S(900), Theme.S(22));
            top.Controls.AddRange(new Control[] { lbl, diskBox, refreshBtn, analyzeBtn, deeperBtn, writeBtn, backupBtn, logBtn, busy, status });
            top.Resize += (s, e) => { status.Width = top.Width - Theme.S(44); busy.Width = Theme.S(200); status.Left = busy.Visible ? busy.Right + Theme.S(12) : Theme.S(22); };

            var info = new Card { Dock = DockStyle.Bottom, Height = Theme.S(84) };
            var infoText = new WrapLabel
            {
                Text = "How it works: Quick analysis compares the partition table with the file systems TestDisk finds on the disk. " +
                       "Deeper search scans every cylinder for lost partitions. Nothing is changed until you choose Write partition table, " +
                       "which replaces the table with the partitions found (back it up first). Restart Windows after writing.",
                Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Color.White, Location = new Point(Theme.S(22), Theme.S(16)),
            };
            info.Controls.Add(infoText);
            info.Resize += (s, e) => infoText.Width = info.Width - Theme.S(44);

            var tables = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Background, Padding = new Padding(0, Theme.S(14), 0, Theme.S(14)) };
            tables.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(30)));
            tables.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            tables.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(40)));
            tables.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            currentTitle.Text = "Current partition table"; currentTitle.Font = Theme.Section; currentTitle.ForeColor = Theme.Text; currentTitle.Dock = DockStyle.Fill;
            foundTitle.Text = "Partitions found by TestDisk"; foundTitle.Font = Theme.Section; foundTitle.ForeColor = Theme.Text; foundTitle.Dock = DockStyle.Fill;
            foundTitle.TextAlign = ContentAlignment.BottomLeft;
            tables.Controls.Add(currentTitle, 0, 0);
            tables.Controls.Add(Wrap(currentGrid), 0, 1);
            tables.Controls.Add(foundTitle, 0, 2);
            tables.Controls.Add(Wrap(foundGrid), 0, 3);

            Body.Controls.Add(tables);
            Body.Controls.Add(info);
            Body.Controls.Add(top);

            refreshBtn.Click += async (s, e) => await LoadDisksAsync();
            analyzeBtn.Click += async (s, e) => await AnalyzeAsync(false);
            deeperBtn.Click += async (s, e) => await AnalyzeAsync(true);
            writeBtn.Click += async (s, e) => await WriteAsync();
            backupBtn.Click += async (s, e) => await BackupAsync();
            logBtn.Click += (s, e) => ShowLog();

            diskBox.SelectedIndexChanged += (s, e) => { last = null; Fill(currentGrid, null); Fill(foundGrid, null); UpdateButtons(); status.Text = "Choose Quick analysis to inspect this disk."; };
            UpdateButtons();
        }

        bool loadedOnce;
        public override async void OnPageShown()
        {
            // reload when first shown, or when new disk images were attached meanwhile
            var images = MainForm.Instance.Devices.AttachedImages;
            if (!loadedOnce || images.Any(i => !disks.Any(d => d.Path == i.Path)))
            {
                loadedOnce = true;
                await LoadDisksAsync();
            }
        }

        static Control Wrap(Control c)
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(1)) };
            c.Dock = DockStyle.Fill;
            card.Controls.Add(c);
            return card;
        }

        static DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(238, 241, 246),
                EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = Theme.S(34),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Theme.TextMuted, Font = Theme.Font(8.5f, FontStyle.Bold), SelectionBackColor = Color.White, SelectionForeColor = Theme.TextMuted };
            g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Theme.Text, Font = Theme.Body, SelectionBackColor = Theme.RowSelected, SelectionForeColor = Theme.Text };
            g.RowTemplate.Height = Theme.S(32);
            foreach (var h in new[] { "#", "STATUS", "TYPE", "FILE SYSTEM", "START SECTOR", "SIZE", "LABEL" })
                g.Columns.Add(h, h);
            g.Columns[0].FillWeight = 30;
            g.Columns[1].FillWeight = 80;
            return g;
        }

        async Task LoadDisksAsync()
        {
            refreshBtn.Enabled = false;
            status.Text = "Detecting disks…";
            try
            {
                disks = (await RecoveryService.ListDevicesAsync(CancellationToken.None)).Where(d => d.Kind != DeviceKind.Volume).ToList();
                disks.AddRange(MainForm.Instance.Devices.AttachedImages.Where(i => !disks.Any(d => d.Path == i.Path)));
                diskBox.Items.Clear();
                foreach (var d in disks) diskBox.Items.Add($"{d.Title}  —  {FileTypes.FormatSize(d.SizeBytes)}  ({d.Path})");
                diskBox.Items.Add("Open a disk image…");
                if (disks.Count > 0) diskBox.SelectedIndex = 0;
                status.Text = disks.Count == 0 ? "No disks detected. Administrator rights are required, or open a disk image." : "Choose Quick analysis to inspect the selected disk.";
            }
            catch (Exception ex) { status.Text = "Disk detection failed: " + ex.Message; }
            finally { refreshBtn.Enabled = true; UpdateButtons(); }
        }

        async Task<DiskInfo> SelectedDiskAsync()
        {
            int i = diskBox.SelectedIndex;
            if (i < 0) return null;
            if (i < disks.Count) return disks[i];
            using (var dlg = new OpenFileDialog { Title = "Open a disk image", Filter = "Disk images (*.img;*.dd;*.raw;*.bin;*.vhd)|*.img;*.dd;*.raw;*.bin;*.vhd|All files (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                var d = await RecoveryService.OpenImageAsync(dlg.FileName, CancellationToken.None);
                disks.Add(d);
                diskBox.Items.Insert(disks.Count - 1, $"{d.Title}  —  {FileTypes.FormatSize(d.SizeBytes)}  (image)");
                diskBox.SelectedIndex = disks.Count - 1;
                return d;
            }
        }

        void SetBusy(bool on, string text)
        {
            busy.Visible = on;
            busy.Indeterminate = on;
            status.Left = on ? Theme.S(22) + Theme.S(212) : Theme.S(22);
            status.Text = text;
            foreach (var b in new Control[] { analyzeBtn, deeperBtn, writeBtn, backupBtn, refreshBtn, diskBox }) b.Enabled = !on;
            if (!on) UpdateButtons();
        }

        void UpdateButtons()
        {
            bool has = diskBox.SelectedIndex >= 0;
            analyzeBtn.Enabled = deeperBtn.Enabled = backupBtn.Enabled = has;
            // Writing only makes sense when TestDisk found partitions the current table lacks.
            writeBtn.Enabled = last != null && last.Missing.Count > 0 && lastDisk != null && !lastDisk.ReadOnly;
            logBtn.Enabled = last != null;
        }

        async Task AnalyzeAsync(bool deeper)
        {
            DiskInfo d;
            try { d = await SelectedDiskAsync(); }
            catch (Exception ex) { MainForm.Error(this, "The image could not be opened.", ex); return; }
            if (d == null) return;
            SetBusy(true, deeper ? "Deeper search in progress — every cylinder is checked, this can take a long time on large disks…" : "Analysing the partition structure…");
            try
            {
                last = await RecoveryService.AnalyzePartitionsAsync(d, deeper, false, null, CancellationToken.None);
                lastDeeper = deeper;
                lastDisk = d;
                Fill(currentGrid, last.Current);
                Fill(foundGrid, last.Found);
                currentTitle.Text = "Current partition table" + (string.IsNullOrEmpty(last.PartitionTableType) ? "" : "  ·  " + last.PartitionTableType);
                var missing = last.Missing.Count;
                status.Text = last.Found.Count == 0
                    ? "No partitions found." + (deeper ? "" : " Try Deeper search.")
                    : missing > 0
                        ? $"{missing} partition{(missing == 1 ? "" : "s")} found that the current table does not contain. Review them, then Write partition table to restore."
                        : "Every partition TestDisk found is already in the partition table, so there is nothing to restore." + (deeper ? "" : " Use Deeper search if a partition is still missing.");
            }
            catch (Exception ex) { MainForm.Error(this, "The analysis failed.", ex); status.Text = "Analysis failed."; }
            finally { SetBusy(false, status.Text); }
        }

        async Task WriteAsync()
        {
            if (last == null || lastDisk == null || last.Found.Count == 0) return;
            var list = string.Join("\n", last.Found.Select(p => $"  • {p.Type}  {FileTypes.FormatSize(p.SizeBytes)}  {(string.IsNullOrEmpty(p.Label) ? "" : "[" + p.Label + "]")}"));
            var ok = MessageBox.Show(this,
                $"Write this partition table to {lastDisk.Title}?\n\n{list}\n\n" +
                "The existing partition table will be replaced. Make sure no programs are using the disk. This cannot be undone without a backup.",
                "Write partition table", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (ok != DialogResult.OK) return;
            SetBusy(true, "Writing the partition table…");
            try
            {
                await RecoveryService.BackupPartitionTableAsync(lastDisk, CancellationToken.None);
                var r = await RecoveryService.AnalyzePartitionsAsync(lastDisk, lastDeeper, true, null, CancellationToken.None);
                last = r;
                Fill(foundGrid, r.Found);
                if (r.WriteDone)
                {
                    status.Text = "Partition table written. Restart the computer (or reconnect the drive) to use the recovered partitions.";
                    MessageBox.Show(this, status.Text, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    status.Text = "TestDisk did not write the partition table. Open the log for details.";
                    MessageBox.Show(this, status.Text, "Not written", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex) { MainForm.Error(this, "Writing the partition table failed.", ex); }
            finally { SetBusy(false, status.Text); }
        }

        async Task BackupAsync()
        {
            DiskInfo d;
            try { d = await SelectedDiskAsync(); } catch { return; }
            if (d == null) return;
            SetBusy(true, "Backing up the partition table…");
            try
            {
                var dir = await RecoveryService.BackupPartitionTableAsync(d, CancellationToken.None);
                status.Text = "Partition table saved to " + System.IO.Path.Combine(dir, "backup.log");
                MainForm.OpenFolder(dir);
            }
            catch (Exception ex) { MainForm.Error(this, "Backup failed.", ex); }
            finally { SetBusy(false, status.Text); }
        }

        void ShowLog()
        {
            if (last == null) return;
            var f = new Form { Text = "TestDisk log", Size = new Size(Theme.S(900), Theme.S(640)), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false };
            var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f), Text = last.RawLog.Replace("\r\n", "\n").Replace("\n", "\r\n") };
            f.Controls.Add(tb);
            f.ShowDialog(this);
        }

        static void Fill(DataGridView g, List<PartitionInfo> parts)
        {
            g.Rows.Clear();
            if (parts == null) return;
            foreach (var p in parts)
                g.Rows.Add(p.Number > 0 ? p.Number.ToString() : "", Status(p.Status), p.Type, p.FileSystemInfo ?? p.FileSystem,
                    p.StartSector.ToString("N0"), FileTypes.FormatSize(p.SizeBytes), p.Label ?? "");
        }

        static string Status(char c)
        {
            switch (c)
            {
                case '*': return "Bootable";
                case 'P': return "Primary";
                case 'L': return "Logical";
                case 'E': return "Extended (container)";
                case 'D': return "Deleted";
                default: return "Found";
            }
        }
    }
}
