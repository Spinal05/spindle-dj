using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    public enum ScanMethod { AllMethods, QuickScan, DeepScan }

    /// <summary>Home screen: pick a disk, partition or image and start searching.</summary>
    public class DevicesPage : PageBase
    {
        readonly Card listCard = new Card { Padding = new Padding(Theme.S(1)) };
        readonly DeviceList list = new DeviceList();
        readonly Panel actionBar = new Panel();
        readonly Label selTitle = new Label();
        readonly Label selInfo = new Label();
        readonly ComboBox method = new ComboBox();
        readonly FlatButton searchBtn = FlatButton.Create("Search for lost data", ButtonStyle.Primary, 210, Icons.Kind.Search);
        readonly FlatButton refreshBtn = FlatButton.Create("Refresh", ButtonStyle.Secondary, 110);
        readonly FlatButton imageBtn = FlatButton.Create("Attach disk image…", ButtonStyle.Secondary, 180, Icons.Kind.Image);
        readonly List<DiskInfo> images = new List<DiskInfo>();
        List<DiskInfo> devices = new List<DiskInfo>();

        /// <summary>Disk images attached in this session (shared with the other tools).</summary>
        public IReadOnlyList<DiskInfo> AttachedImages => images;
        bool refreshing;

        public DevicesPage() : base("Data Recovery", "Choose the drive, partition or disk image that held your lost files.")
        {
            var toolbar = new Panel { Dock = DockStyle.Top, Height = Theme.S(52), BackColor = Theme.Background };
            refreshBtn.Location = new Point(0, 0);
            imageBtn.Location = new Point(refreshBtn.Right + Theme.S(10), 0);
            toolbar.Controls.Add(refreshBtn);
            toolbar.Controls.Add(imageBtn);

            list.Dock = DockStyle.Fill;
            listCard.Dock = DockStyle.Fill;
            var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(2), Theme.S(8), Theme.S(2), Theme.S(8)), BackColor = Color.White };
            inner.Controls.Add(list);
            listCard.Controls.Add(inner);
            listCard.Padding = new Padding(Theme.S(1));

            actionBar.Dock = DockStyle.Bottom;
            actionBar.Height = Theme.S(92);
            actionBar.BackColor = Theme.Background;
            actionBar.Padding = new Padding(0, Theme.S(16), 0, 0);
            var actionCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(20), 0, Theme.S(16), 0) };
            actionBar.Controls.Add(actionCard);

            selTitle.Font = Theme.Section; selTitle.ForeColor = Theme.Text; selTitle.AutoSize = false;
            selTitle.Location = new Point(Theme.S(20), Theme.S(12)); selTitle.Size = new Size(Theme.S(460), Theme.S(24));
            selTitle.BackColor = Color.White;
            selInfo.Font = Theme.Small; selInfo.ForeColor = Theme.TextMuted; selInfo.AutoSize = false;
            selInfo.Location = new Point(Theme.S(20), Theme.S(38)); selInfo.Size = new Size(Theme.S(560), Theme.S(20));
            selInfo.BackColor = Color.White;

            method.DropDownStyle = ComboBoxStyle.DropDownList;
            method.Font = Theme.Body;
            method.Width = Theme.S(220);
            method.FlatStyle = FlatStyle.System;
            method.Items.AddRange(new object[] { "All recovery methods", "Quick Scan (file system)", "Deep Scan (file signatures)" });
            method.SelectedIndex = 0;

            actionCard.Controls.Add(selTitle);
            actionCard.Controls.Add(selInfo);
            actionCard.Controls.Add(method);
            actionCard.Controls.Add(searchBtn);
            actionCard.Resize += (s, e) =>
            {
                searchBtn.Location = new Point(actionCard.Width - searchBtn.Width - Theme.S(16), (actionCard.Height - searchBtn.Height) / 2);
                method.Location = new Point(searchBtn.Left - method.Width - Theme.S(12), (actionCard.Height - method.Height) / 2);
                selTitle.Width = selInfo.Width = Math.Max(Theme.S(100), method.Left - Theme.S(40));
            };

            Body.Controls.Add(listCard);
            Body.Controls.Add(actionBar);
            Body.Controls.Add(toolbar);

            list.SelectionChanged += (s, e) => UpdateSelection();
            list.ItemActivated += (s, e) => StartSearch();
            searchBtn.Click += (s, e) => StartSearch();
            refreshBtn.Click += (s, e) => RefreshDevices();
            imageBtn.Click += (s, e) => AttachImage();
            UpdateSelection();
        }

        public async void RefreshDevices()
        {
            if (refreshing) return;
            refreshing = true;
            refreshBtn.Enabled = false;
            list.EmptyText = "Looking for storage devices…";
            list.Invalidate();
            try
            {
                devices = await RecoveryService.ListDevicesAsync(CancellationToken.None);
                if (devices.Count == 0)
                    list.EmptyText = EngineHost.IsWindows
                        ? "No storage devices were found. Spindle Recovery needs administrator rights to read disks."
                        : "No storage devices were found. Run as root, or attach a disk image.";
                list.SetDisks(devices.Concat(images).ToList());
            }
            catch (Exception ex)
            {
                list.EmptyText = "Device detection failed: " + ex.Message;
                list.SetDisks(images.ToList());
            }
            finally
            {
                refreshing = false;
                refreshBtn.Enabled = true;
            }
        }

        public async void AttachImage(string path = null)
        {
            if (path == null)
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "Attach a disk image",
                    Filter = "Disk images (*.img;*.dd;*.raw;*.bin;*.iso;*.vhd;*.dmg)|*.img;*.dd;*.raw;*.bin;*.iso;*.vhd;*.dmg|All files (*.*)|*.*",
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    path = dlg.FileName;
                }
            }
            try
            {
                var d = await RecoveryService.OpenImageAsync(path, CancellationToken.None);
                images.RemoveAll(x => string.Equals(x.Path, d.Path, StringComparison.OrdinalIgnoreCase));
                images.Add(d);
                list.SetDisks(devices.Concat(images).ToList());
                list.Select(d);
            }
            catch (Exception ex) { MainForm.Error(this, "The disk image could not be opened.", ex); }
        }

        void UpdateSelection()
        {
            var d = list.SelectedDisk;
            var p = list.SelectedPartition;
            searchBtn.Enabled = d != null;
            if (d == null)
            {
                selTitle.Text = "Nothing selected";
                selInfo.Text = "Select a storage device or partition above.";
                return;
            }
            if (p != null)
            {
                selTitle.Text = p.DisplayName + "  —  " + FileTypes.FormatSize(p.SizeBytes);
                selInfo.Text = $"{p.FileSystem} partition on {d.Title}" +
                    (p.SupportsQuickScan ? "  ·  Quick Scan + Deep Scan available" : "  ·  Deep Scan (file signatures)");
            }
            else
            {
                selTitle.Text = d.Title + "  —  " + FileTypes.FormatSize(d.SizeBytes);
                selInfo.Text = d.Partitions.Count > 0
                    ? $"Whole device ({d.Partitions.Count(x => x.Status != 'E')} partitions) · searches every sector, including lost partitions"
                    : "Whole device · no partition table found, every sector will be searched";
            }
        }

        void StartSearch()
        {
            var d = list.SelectedDisk;
            if (d == null) return;
            var p = list.SelectedPartition;
            var m = (ScanMethod)method.SelectedIndex;
            var quickTargets = (p != null ? new List<PartitionInfo> { p } : d.Partitions).Where(x => x.SupportsQuickScan).ToList();
            if (m == ScanMethod.QuickScan && quickTargets.Count == 0)
            {
                MessageBox.Show(this, "Quick Scan reads the FAT/exFAT file system to list deleted files with their original names.\n\n" +
                    "This selection has no FAT/exFAT partition, so use Deep Scan (or All recovery methods) instead.",
                    "Quick Scan not available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DeepScanOptions deep = null;
            if (m != ScanMethod.QuickScan)
            {
                using (var dlg = new ScanOptionsDialog(d, p))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    deep = dlg.Options;
                }
            }
            MainForm.Instance.Scan.Start(d, p, m == ScanMethod.DeepScan ? new List<PartitionInfo>() : quickTargets, deep);
            MainForm.Instance.Show(MainForm.Instance.Scan);
        }
    }
}
