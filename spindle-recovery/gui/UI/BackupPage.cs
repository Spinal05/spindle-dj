using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpindleRecovery.Engine;

namespace SpindleRecovery.UI
{
    /// <summary>Creates a sector-by-sector image of a disk or partition so recovery can run on the copy.</summary>
    public class BackupPage : PageBase
    {
        class Source
        {
            public DiskInfo Disk; public PartitionInfo Part;
            public override string ToString() => Part == null
                ? $"{Disk.Title}  —  whole device, {FileTypes.FormatSize(Disk.SizeBytes)}"
                : $"    {Part.DisplayName}  —  {Part.FileSystem}, {FileTypes.FormatSize(Part.SizeBytes)}";
        }

        readonly ComboBox sourceBox = new ComboBox();
        readonly TextBox targetBox = new TextBox();
        readonly FlatButton browseBtn = FlatButton.Create("Browse…", ButtonStyle.Secondary, 100);
        readonly FlatButton startBtn = FlatButton.Create("Create image", ButtonStyle.Primary, 170, Icons.Kind.Backup);
        readonly FlatButton cancelBtn = FlatButton.Create("Cancel", ButtonStyle.Danger, 110);
        readonly FlatButton scanBtn = FlatButton.Create("Scan this image", ButtonStyle.Secondary, 170, Icons.Kind.Search);
        readonly ModernProgress progress = new ModernProgress();
        readonly Label status = new Label();
        readonly List<Source> sources = new List<Source>();
        CancellationTokenSource cts;

        public BackupPage() : base("Byte-to-byte Backup",
            "Copy a failing or important drive into an image file first, then recover from the copy.")
        {
            var card = new Card { Dock = DockStyle.Top, Height = Theme.S(310) };
            int x = Theme.S(24), y = Theme.S(22);
            Label L(string t, int top) { var l = MakeLabel(t, Theme.BodyBold, Theme.Text); l.BackColor = Color.White; l.Location = new Point(x, top); return l; }
            card.Controls.Add(L("Source drive or partition", y));
            sourceBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceBox.Font = Theme.Body;
            sourceBox.Location = new Point(x, y + Theme.S(26));
            sourceBox.Width = Theme.S(620);
            card.Controls.Add(sourceBox);
            y += Theme.S(76);
            card.Controls.Add(L("Save image as", y));
            targetBox.Font = Theme.Body;
            targetBox.Location = new Point(x, y + Theme.S(27));
            targetBox.Width = Theme.S(510);
            browseBtn.Height = Theme.S(30);
            browseBtn.Location = new Point(targetBox.Right + Theme.S(10), y + Theme.S(24));
            card.Controls.Add(targetBox);
            card.Controls.Add(browseBtn);
            y += Theme.S(80);
            startBtn.Location = new Point(x, y);
            cancelBtn.Location = new Point(startBtn.Right + Theme.S(10), y);
            scanBtn.Location = new Point(startBtn.Right + Theme.S(10), y);
            cancelBtn.Visible = false;
            scanBtn.Visible = false;
            card.Controls.AddRange(new Control[] { startBtn, cancelBtn, scanBtn });
            y += Theme.S(60);
            progress.Location = new Point(x, y);
            progress.BackColor = Color.White;
            status.Font = Theme.Small; status.ForeColor = Theme.TextMuted; status.AutoSize = false; status.BackColor = Color.White;
            status.Location = new Point(x, y + Theme.S(20));
            status.Size = new Size(Theme.S(900), Theme.S(40));
            card.Controls.Add(progress);
            card.Controls.Add(status);
            card.Resize += (s, e) => { progress.Width = status.Width = card.Width - 2 * x; };

            var info = new Card { Dock = DockStyle.Top, Height = Theme.S(84) };
            var t = new WrapLabel
            {
                Text = "Why image first? Every read of a dying drive can make it worse. An image is read once, start to finish; " +
                       "unreadable sectors are retried individually and filled with zeros so the copy stays usable. " +
                       "The source is opened read-only. Save the image to a different drive with enough free space.",
                Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Color.White, Location = new Point(x, Theme.S(18)),
            };
            info.Controls.Add(t);
            info.Resize += (s, e) => t.Width = info.Width - 2 * x;

            Body.Controls.Add(info);
            Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Theme.S(16), BackColor = Theme.Background });
            Body.Controls.Add(card);

            browseBtn.Click += (s, e) => Browse();
            startBtn.Click += async (s, e) => await StartAsync();
            cancelBtn.Click += (s, e) => cts?.Cancel();
            scanBtn.Click += (s, e) =>
            {
                MainForm.Instance.Show(MainForm.Instance.Devices);
                MainForm.Instance.Devices.AttachImage(targetBox.Text);
            };

            status.Text = "Choose a source and a destination file.";
        }

        bool loadedOnce;
        public override async void OnPageShown()
        {
            if (loadedOnce || cts != null && !cts.IsCancellationRequested && cancelBtn.Visible) return;
            loadedOnce = true;
            await LoadAsync();
        }

        async Task LoadAsync()
        {
            status.Text = "Detecting drives…";
            try
            {
                var disks = await RecoveryService.ListDevicesAsync(CancellationToken.None);
                sources.Clear();
                sourceBox.Items.Clear();
                foreach (var d in disks)
                {
                    sources.Add(new Source { Disk = d });
                    foreach (var p in d.Partitions.Where(p => p.Status != 'E')) sources.Add(new Source { Disk = d, Part = p });
                }
                foreach (var s in sources) sourceBox.Items.Add(s);
                if (sources.Count > 0) sourceBox.SelectedIndex = 0;
                status.Text = sources.Count == 0 ? "No drives detected. Administrator rights are required." : "Choose a source and a destination file.";
            }
            catch (Exception ex) { status.Text = "Drive detection failed: " + ex.Message; }
        }

        void Browse()
        {
            var src = sourceBox.SelectedItem as Source;
            var name = src == null ? "disk" : (src.Part?.DisplayName ?? src.Disk.Title);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            using (var dlg = new SaveFileDialog { Title = "Save disk image", Filter = "Raw disk image (*.img)|*.img|All files (*.*)|*.*", FileName = name + " " + DateTime.Now.ToString("yyyy-MM-dd") + ".img" })
                if (dlg.ShowDialog(this) == DialogResult.OK) targetBox.Text = dlg.FileName;
        }

        async Task StartAsync()
        {
            var src = sourceBox.SelectedItem as Source;
            if (src == null) return;
            var target = targetBox.Text.Trim();
            if (target.Length == 0) { Browse(); target = targetBox.Text.Trim(); if (target.Length == 0) return; }
            if (ScanOptionsDialog.IsOnSourceDrive(target, src.Disk, src.Part))
            {
                MessageBox.Show(this, "The image cannot be saved on the drive being copied. Choose another drive.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            long offset = src.Part == null ? 0 : src.Part.StartSector * src.Disk.SectorSize;
            long length = src.Part == null ? src.Disk.SizeBytes : src.Part.SizeBytes;
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(target));
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (free < length &&
                    MessageBox.Show(this, $"The destination has {FileTypes.FormatSize(free)} free but the image needs {FileTypes.FormatSize(length)}.\n\nContinue anyway?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            catch { }

            cts = new CancellationTokenSource();
            startBtn.Visible = false; scanBtn.Visible = false; cancelBtn.Visible = true;
            sourceBox.Enabled = targetBox.Enabled = browseBtn.Enabled = false;
            var started = DateTime.Now;
            ImagingProgress lastP = null;
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (s, e) =>
            {
                var p = lastP;
                if (p == null) return;
                progress.Value = p.Fraction;
                var secs = (DateTime.Now - started).TotalSeconds;
                var rate = secs > 0 ? p.Done / secs : 0;
                var left = rate > 0 ? TimeSpan.FromSeconds((p.Total - p.Done) / rate) : TimeSpan.Zero;
                status.Text = $"{p.Fraction * 100:0.0}%  ·  {FileTypes.FormatSize(p.Done)} of {FileTypes.FormatSize(p.Total)}  ·  {FileTypes.FormatSize((long)rate)}/s" +
                              (left.TotalSeconds > 1 ? $"  ·  {(int)left.TotalHours:0}:{left.Minutes:00}:{left.Seconds:00} left" : "") +
                              (p.BadSectors > 0 ? $"\n{p.BadSectors:N0} unreadable sectors replaced with zeros" : "");
            };
            timer.Start();
            bool ok = false;
            try
            {
                await Task.Run(() => DiskImager.CreateImage(src.Disk.Path, offset, length, src.Disk.SectorSize, target, p => lastP = p, cts.Token));
                ok = true;
            }
            catch (OperationCanceledException) { status.Text = "Cancelled. The partial image was kept: " + target; }
            catch (Exception ex) { MainForm.Error(this, "Imaging failed.", ex); status.Text = "Imaging failed: " + ex.Message; }
            finally
            {
                timer.Stop();
                timer.Dispose();
                cancelBtn.Visible = false; startBtn.Visible = true;
                sourceBox.Enabled = targetBox.Enabled = browseBtn.Enabled = true;
            }
            if (ok)
            {
                progress.Value = 1;
                status.Text = "Image created: " + target + (lastP != null && lastP.BadSectors > 0 ? $"  ·  {lastP.BadSectors:N0} unreadable sectors" : "");
                scanBtn.Location = new Point(startBtn.Right + Theme.S(10), startBtn.Top);
                scanBtn.Visible = true;
            }
        }
    }
}
