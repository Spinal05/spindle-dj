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
        readonly FlatButton savedBtn = FlatButton.Create("Saved scans…", ButtonStyle.Secondary, 150, Icons.Kind.Folder);
        readonly List<DiskInfo> images = new List<DiskInfo>();
        readonly FlowLayoutPanel typeChips = new FlowLayoutPanel();
        readonly Label typesLabel = new Label();
        readonly Label typesHint = new Label();
        ChipToggle allChip;
        readonly Dictionary<FileCategory, ChipToggle> chips = new Dictionary<FileCategory, ChipToggle>();
        readonly ToolTip tips = new ToolTip();
        bool syncingChips;
        FlatButton formatsBtn;
        FileSelection selection = FileSelection.All();

        /// <summary>The formats chosen on the home screen (remembered between sessions).</summary>
        public FileSelection Selection => selection;
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
            savedBtn.Location = new Point(imageBtn.Right + Theme.S(10), 0);
            toolbar.Controls.Add(savedBtn);

            list.Dock = DockStyle.Fill;
            listCard.Dock = DockStyle.Fill;
            var inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(2), Theme.S(8), Theme.S(2), Theme.S(8)), BackColor = Color.White };
            inner.Controls.Add(list);
            listCard.Controls.Add(inner);
            listCard.Padding = new Padding(Theme.S(1));

            actionBar.Dock = DockStyle.Bottom;
            actionBar.Height = Theme.S(150);
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

            BuildTypeChips();
            var divider = new Panel { BackColor = Theme.Border, Height = 1 };

            actionCard.Controls.Add(selTitle);
            actionCard.Controls.Add(selInfo);
            actionCard.Controls.Add(method);
            actionCard.Controls.Add(searchBtn);
            actionCard.Controls.Add(divider);
            actionCard.Controls.Add(typesLabel);
            actionCard.Controls.Add(typeChips);
            actionCard.Controls.Add(typesHint);
            Action layoutCard = () =>
            {
                searchBtn.Location = new Point(actionCard.Width - searchBtn.Width - Theme.S(16), Theme.S(14));
                method.Location = new Point(searchBtn.Left - method.Width - Theme.S(12), searchBtn.Top + (searchBtn.Height - method.Height) / 2);
                selTitle.Width = selInfo.Width = Math.Max(Theme.S(100), method.Left - Theme.S(40));
                divider.SetBounds(Theme.S(20), Theme.S(66), actionCard.Width - Theme.S(40), 1);
                typesLabel.Location = new Point(Theme.S(20), Theme.S(83));
                typeChips.Location = new Point(typesLabel.Right + Theme.S(8), Theme.S(76));
                typeChips.Width = actionCard.Width - typeChips.Left - Theme.S(16);
                typeChips.Height = typeChips.GetPreferredSize(new Size(typeChips.Width, 0)).Height;
                typesHint.Location = new Point(typeChips.Left + 2, typeChips.Bottom + Theme.S(2));
                typesHint.Width = typeChips.Width;
                int want = typesHint.Bottom + Theme.S(10) + actionBar.Padding.Top;
                if (Math.Abs(actionBar.Height - want) > 2) actionBar.Height = want;
            };
            actionCard.Resize += (s, e) => layoutCard();
            OnActionCardResize = layoutCard;

            Body.Controls.Add(listCard);
            Body.Controls.Add(actionBar);
            Body.Controls.Add(toolbar);

            list.SelectionChanged += (s, e) => UpdateSelection();
            list.ItemActivated += (s, e) => StartSearch();
            searchBtn.Click += (s, e) => StartSearch();
            refreshBtn.Click += (s, e) => RefreshDevices();
            imageBtn.Click += (s, e) => AttachImage();
            savedBtn.Click += (s, e) => OpenSavedScans();
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

        static string TypesFile => System.IO.Path.Combine(EngineHost.AppDataDir, "file-types.txt");

        void BuildTypeChips()
        {
            typesLabel.Text = "Look for:";
            typesLabel.Font = Theme.BodyBold;
            typesLabel.ForeColor = Theme.Text;
            typesLabel.AutoSize = true;
            typesLabel.BackColor = Color.White;

            typeChips.BackColor = Color.White;
            typeChips.WrapContents = true;
            typeChips.Margin = Padding.Empty;
            typeChips.Padding = Padding.Empty;

            typesHint.Font = Theme.Small;
            typesHint.ForeColor = Theme.TextMuted;
            typesHint.BackColor = Color.White;
            typesHint.AutoSize = false;
            typesHint.Height = Theme.S(20);

            allChip = new ChipToggle("All types", null, Theme.Accent) { Margin = new Padding(0, 0, Theme.S(6), Theme.S(6)) };
            tips.SetToolTip(allChip, "Search for every file type PhotoRec and the file systems know (480+ formats)");
            typeChips.Controls.Add(allChip);
            foreach (var c in FileTypes.DisplayOrder)
            {
                var cat = c;
                var chip = new ChipToggle(FileTypes.DisplayName(c), Icons.ForCategory(c), Theme.CategoryColors[(int)c])
                {
                    Margin = new Padding(0, 0, Theme.S(6), Theme.S(6)),
                };
                tips.SetToolTip(chip, FileTypes.Examples(c) + "\nClick to include or leave out; use Formats… to pick individual formats.");
                chips[c] = chip;
                typeChips.Controls.Add(chip);
                chip.SelectedChanged += (s, e) =>
                {
                    if (syncingChips) return;
                    // a category chip includes or leaves out all of its formats
                    var ids = new HashSet<string>(selection.FormatIds);
                    foreach (var f in FileTypes.Formats.Where(f => f.Category == cat))
                        if (chip.Selected) ids.Add(f.Id); else ids.Remove(f.Id);
                    SetSelection(new FileSelection(ids));
                };
            }
            allChip.SelectedChanged += (s, e) =>
            {
                if (syncingChips) return;
                // "All types" on selects everything; turning it off clears the choice so you can pick a few.
                SetSelection(allChip.Selected ? FileSelection.All() : new FileSelection(new string[0]));
            };
            formatsBtn = FlatButton.Create("Formats…", ButtonStyle.Ghost, 92);
            formatsBtn.Height = Theme.S(32);
            formatsBtn.Margin = new Padding(0, 0, 0, Theme.S(6));
            tips.SetToolTip(formatsBtn, "Pick individual formats, e.g. only JPEG and HEIC photos");
            formatsBtn.Click += (s, e) => PickFormats();
            typeChips.Controls.Add(formatsBtn);

            SetSelection(LoadSelection(), save: false);
        }

        static FileSelection LoadSelection()
        {
            try
            {
                if (!System.IO.File.Exists(TypesFile)) return FileSelection.All();
                var ids = new HashSet<string>();
                foreach (var raw in System.IO.File.ReadAllLines(TypesFile))
                {
                    var line = raw.Trim();
                    if (FileTypes.FormatsById.ContainsKey(line)) ids.Add(line);
                    else if (Enum.TryParse(line, out FileCategory c) && Enum.IsDefined(typeof(FileCategory), c))
                        foreach (var f in FileTypes.Formats.Where(f => f.Category == c)) ids.Add(f.Id); // older versions saved categories
                }
                return ids.Count == 0 ? FileSelection.All() : new FileSelection(ids);
            }
            catch { return FileSelection.All(); }
        }

        void PickFormats()
        {
            using (var dlg = new FormatPickerDialog(selection))
                if (dlg.ShowDialog(this) == DialogResult.OK) SetSelection(dlg.Selection);
        }

        /// <summary>Updates the chips, hint and search button for a new format choice.</summary>
        public void SetSelection(FileSelection sel, bool save = true)
        {
            selection = sel;
            syncingChips = true;
            allChip.Selected = sel.IsEverything;
            foreach (var kv in chips)
            {
                int n = sel.CountIn(kv.Key), total = FileTypes.Formats.Count(f => f.Category == kv.Key);
                kv.Value.Selected = n > 0;
                kv.Value.SetLabel(FileTypes.DisplayName(kv.Key) + (n > 0 && n < total ? $" {n}/{total}" : ""));
            }
            syncingChips = false;
            typeChips.PerformLayout();
            typesHint.ForeColor = sel.IsEmpty ? Theme.Danger : Theme.TextMuted;
            typesHint.Text = sel.IsEmpty
                ? "Choose at least one kind of file to look for."
                : sel.IsEverything
                    ? "Every kind of file will be searched for. Pick fewer types for a faster scan with less to sort through."
                    : "Only " + sel.Describe() + " will be searched for and shown.";
            searchBtn.Enabled = list.SelectedDisk != null && !sel.IsEmpty;
            if (save) try { System.IO.File.WriteAllLines(TypesFile, sel.FormatIds.OrderBy(x => x)); } catch { }
            actionBar.PerformLayout();
            OnActionCardResize?.Invoke();
        }

        Action OnActionCardResize;

        void OpenSavedScans()
        {
            using (var dlg = new SavedScansDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId == null) return;
                if (MainForm.Instance.Scan.IsBusy)
                {
                    MessageBox.Show(this, "Stop the running scan before opening a saved one.", "Saved scans", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    MainForm.Instance.Scan.LoadSaved(dlg.SelectedId);
                    MainForm.Instance.Show(MainForm.Instance.Scan);
                }
                catch (Exception ex) { MainForm.Error(this, "The saved scan could not be opened.", ex); }
            }
        }

        void UpdateSelection()
        {
            var d = list.SelectedDisk;
            var p = list.SelectedPartition;
            searchBtn.Enabled = d != null && !selection.IsEmpty;
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
            var sel = selection;
            if (sel.IsEmpty)
            {
                MessageBox.Show(this, "Choose at least one kind of file to look for (Pictures, Videos, Music, Documents…).",
                    "No file types chosen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var quickTargets = (p != null ? new List<PartitionInfo> { p } : d.Partitions).Where(x => x.SupportsQuickScan).ToList();
            if (m == ScanMethod.QuickScan && quickTargets.Count == 0)
            {
                MessageBox.Show(this, "Quick Scan reads the NTFS, FAT or exFAT file system to list deleted files with their original names.\n\n" +
                    "This selection has no NTFS/FAT/exFAT partition, so use Deep Scan (or All recovery methods) instead.",
                    "Quick Scan not available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DeepScanOptions deep = null;
            if (m != ScanMethod.QuickScan)
            {
                using (var dlg = new ScanOptionsDialog(d, p, sel))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    deep = dlg.Options;
                }
                // the dialog may have refined the choice: remember it on the home screen too
                if (!deep.Selection.FormatIds.SetEquals(sel.FormatIds)) SetSelection(deep.Selection);
                sel = deep.Selection;
            }
            MainForm.Instance.Scan.Start(d, p, m == ScanMethod.DeepScan ? new List<PartitionInfo>() : quickTargets, deep, sel);
            MainForm.Instance.Show(MainForm.Instance.Scan);
        }
    }
}
