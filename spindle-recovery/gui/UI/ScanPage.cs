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
    /// <summary>Runs a scan, shows live progress and lets the user review and recover what was found.</summary>
    public class ScanPage : PageBase
    {
        // header
        readonly FlatButton backBtn = FlatButton.Create("All devices", ButtonStyle.Secondary, 130);
        readonly FlatButton stopBtn = FlatButton.Create("Stop", ButtonStyle.Danger, 100);
        // progress
        readonly Card progressCard = new Card();
        readonly Label phaseLabel = new Label();
        readonly Label percentLabel = new Label();
        readonly ModernProgress progress = new ModernProgress();
        readonly Label detailLabel = new Label();
        // tiles
        readonly FlowLayoutPanel tiles = new FlowLayoutPanel();
        readonly Dictionary<FileCategory, CategoryTile> tileMap = new Dictionary<FileCategory, CategoryTile>();
        // results
        readonly Card resultsCard = new Card { Padding = new Padding(Theme.S(1)) };
        readonly ComboBox sourceFilter = new ComboBox();
        readonly TextBox search = new TextBox();
        readonly CheckBox selectAll = new CheckBox();
        readonly DataGridView grid = new DataGridView();
        readonly Panel preview = new Panel();
        readonly PictureBox previewImage = new PictureBox();
        readonly Label previewName = new Label();
        readonly Label previewInfo = new Label();
        readonly FlatButton previewOpen = FlatButton.Create("Open", ButtonStyle.Secondary, 100);
        // bottom
        readonly Panel bottom = new Panel();
        readonly Label selectionLabel = new Label();
        readonly FlatButton recoverBtn = FlatButton.Create("Recover", ButtonStyle.Primary, 170, Icons.Kind.Recover);
        readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer { Interval = 500 };

        readonly List<FoundFile> all = new List<FoundFile>();
        List<FoundFile> view = new List<FoundFile>();
        readonly HashSet<string> vaultSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FileCategory? categoryFilter;
        int sortColumn = -1; bool sortAsc = true;
        bool viewDirty;

        DiskInfo disk; PartitionInfo part;
        DeepScanOptions deep;
        List<PartitionInfo> quickTargets;
        CancellationTokenSource cts;
        string stopFile;
        volatile DeepScanProgress lastProgress;
        DateTime passStart; int passSeen = -1;
        bool deepRunning;
        volatile bool quickRunning;
        double quickProgress = -1;
        int previewToken;
        int quickFound;

        public bool IsBusy { get; private set; }
        public bool HasSession { get; private set; }
        public string CurrentVault => deep?.VaultDir;

        public ScanPage() : base("Scanning", "")
        {
            backBtn.Click += (s, e) => { if (!IsBusy || ConfirmStop()) MainForm.Instance.Show(MainForm.Instance.Devices); };
            stopBtn.Click += (s, e) => Stop();
            Header.Controls.Add(backBtn);
            Header.Controls.Add(stopBtn);
            Header.Resize += (s, e) =>
            {
                stopBtn.Location = new Point(Header.Width - stopBtn.Width, Theme.S(4));
                backBtn.Location = new Point((stopBtn.Visible ? stopBtn.Left - Theme.S(10) : Header.Width) - backBtn.Width, Theme.S(4));
            };
            TitleLabel.AutoEllipsis = true;

            BuildProgressCard();
            BuildTiles();
            BuildResults();
            BuildBottom();

            Body.Controls.Add(resultsCard);
            Body.Controls.Add(bottom);
            Body.Controls.Add(Spacer(Theme.S(14)));
            Body.Controls.Add(tiles);
            Body.Controls.Add(Spacer(Theme.S(14)));
            Body.Controls.Add(progressCard);

            uiTimer.Tick += (s, e) => Tick();
        }

        static Control Spacer(int h) => new Panel { Dock = DockStyle.Top, Height = h, BackColor = Theme.Background };

        void BuildProgressCard()
        {
            progressCard.Dock = DockStyle.Top;
            progressCard.Height = Theme.S(104);
            phaseLabel.Font = Theme.Section; phaseLabel.ForeColor = Theme.Text; phaseLabel.AutoSize = false; phaseLabel.BackColor = Color.White;
            phaseLabel.Location = new Point(Theme.S(22), Theme.S(18)); phaseLabel.Height = Theme.S(26);
            percentLabel.Font = Theme.Font(15f, FontStyle.Bold); percentLabel.ForeColor = Theme.Accent; percentLabel.AutoSize = false;
            percentLabel.TextAlign = ContentAlignment.MiddleRight; percentLabel.BackColor = Color.White;
            percentLabel.Size = new Size(Theme.S(120), Theme.S(30));
            progress.Location = new Point(Theme.S(22), Theme.S(52));
            progress.BackColor = Color.White;
            detailLabel.Font = Theme.Small; detailLabel.ForeColor = Theme.TextMuted; detailLabel.AutoSize = false; detailLabel.BackColor = Color.White;
            detailLabel.Location = new Point(Theme.S(22), Theme.S(72)); detailLabel.Height = Theme.S(22);
            progressCard.Controls.AddRange(new Control[] { phaseLabel, percentLabel, progress, detailLabel });
            progressCard.Resize += (s, e) =>
            {
                int w = progressCard.Width - Theme.S(44);
                percentLabel.Location = new Point(progressCard.Width - Theme.S(22) - percentLabel.Width, Theme.S(14));
                phaseLabel.Width = percentLabel.Left - phaseLabel.Left - Theme.S(10);
                progress.Width = w;
                detailLabel.Width = w;
            };
        }

        void BuildTiles()
        {
            tiles.Dock = DockStyle.Top;
            tiles.Height = Theme.S(84);
            tiles.WrapContents = false;
            tiles.BackColor = Theme.Background;
            tiles.Padding = new Padding(0);
            foreach (FileCategory c in Enum.GetValues(typeof(FileCategory)))
            {
                var t = new CategoryTile(c) { Margin = new Padding(0, 0, Theme.S(12), 0) };
                t.Click += (s, e) =>
                {
                    categoryFilter = categoryFilter == c ? (FileCategory?)null : c;
                    foreach (var kv in tileMap) { kv.Value.Selected = categoryFilter == kv.Key; kv.Value.Invalidate(); }
                    RebuildView();
                };
                tileMap[c] = t;
                tiles.Controls.Add(t);
            }
            tiles.Resize += (s, e) =>
            {
                int n = tileMap.Count;
                int w = (tiles.Width - Theme.S(12) * (n - 1)) / n;
                w = Math.Max(Theme.S(120), w);
                foreach (var t in tileMap.Values) t.Width = w;
                if (tileMap.Count > 0) tileMap[FileCategory.Other].Margin = new Padding(0);
            };
        }

        void BuildResults()
        {
            resultsCard.Dock = DockStyle.Fill;
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(Theme.S(1)) };
            resultsCard.Controls.Add(inner);

            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(52), BackColor = Color.White };
            selectAll.Text = "Select all";
            selectAll.AutoSize = true;
            selectAll.Location = new Point(Theme.S(16), Theme.S(16));
            selectAll.BackColor = Color.White;
            selectAll.CheckedChanged += (s, e) =>
            {
                foreach (var f in view) f.Checked = selectAll.Checked;
                grid.Invalidate();
                UpdateSelectionLabel();
            };
            sourceFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceFilter.Items.AddRange(new object[] { "All found files", "Deleted or lost", "Existing files", "Reconstructed (Deep Scan)" });
            sourceFilter.SelectedIndex = 0;
            sourceFilter.Width = Theme.S(210);
            sourceFilter.Location = new Point(Theme.S(130), Theme.S(12));
            sourceFilter.SelectedIndexChanged += (s, e) => RebuildView();
            search.Width = Theme.S(240);
            search.Font = Theme.Body;
            SetCue(search, "Search file names");
            search.TextChanged += (s, e) => RebuildView();
            bar.Controls.AddRange(new Control[] { selectAll, sourceFilter, search });
            bar.Resize += (s, e) => search.Location = new Point(bar.Width - search.Width - Theme.S(16), Theme.S(13));
            var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

            // preview
            preview.Dock = DockStyle.Right;
            preview.Width = Theme.S(262);
            preview.BackColor = Color.FromArgb(250, 251, 253);
            preview.Padding = new Padding(Theme.S(16));
            var pvSep = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border };
            preview.Controls.Add(pvSep);
            previewImage.SizeMode = PictureBoxSizeMode.Zoom;
            previewImage.BackColor = Color.FromArgb(240, 242, 247);
            previewImage.Location = new Point(Theme.S(16), Theme.S(16));
            previewImage.Size = new Size(Theme.S(230), Theme.S(150));
            previewImage.Paint += PaintPreviewPlaceholder;
            previewName.Font = Theme.BodyBold; previewName.ForeColor = Theme.Text; previewName.AutoSize = false; previewName.AutoEllipsis = true;
            previewName.Location = new Point(Theme.S(16), Theme.S(174)); previewName.Size = new Size(Theme.S(230), Theme.S(22));
            previewInfo.Font = Theme.Small; previewInfo.ForeColor = Theme.TextMuted; previewInfo.AutoSize = false;
            previewInfo.Location = new Point(Theme.S(16), Theme.S(198)); previewInfo.Size = new Size(Theme.S(230), Theme.S(170));
            previewOpen.Location = new Point(Theme.S(16), Theme.S(370));
            previewOpen.Height = Theme.S(32);
            previewOpen.Visible = false;
            previewOpen.Click += (s, e) => OpenPreviewFile();
            preview.Controls.AddRange(new Control[] { previewImage, previewName, previewInfo, previewOpen });

            BuildGrid();

            inner.Controls.Add(grid);
            inner.Controls.Add(preview);
            inner.Controls.Add(sep);
            inner.Controls.Add(bar);
        }

        static void SetCue(TextBox box, string text)
        {
            try
            {
                if (EngineHost.IsWindows) NativeMethods.SendMessage(box.Handle, 0x1501, (IntPtr)1, text);
            }
            catch { }
        }

        void BuildGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.VirtualMode = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(238, 241, 246);
            grid.BackgroundColor = Color.White;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Theme.S(36);
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White, ForeColor = Theme.TextMuted, Font = Theme.Font(8.5f, FontStyle.Bold),
                SelectionBackColor = Color.White, SelectionForeColor = Theme.TextMuted, Padding = new Padding(Theme.S(6), 0, 0, 0),
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White, ForeColor = Theme.Text, Font = Theme.Body,
                SelectionBackColor = Theme.RowSelected, SelectionForeColor = Theme.Text, Padding = new Padding(Theme.S(6), 0, 0, 0),
            };
            grid.RowTemplate.Height = Theme.S(34);
            grid.ReadOnly = false;

            var chk = new DataGridViewCheckBoxColumn { Name = "chk", HeaderText = "", Width = Theme.S(36), Resizable = DataGridViewTriState.False };
            grid.Columns.Add(chk);
            AddCol("Name", 260);
            AddCol("Type", 80);
            AddCol("Size", 90).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            AddCol("Date modified", 120);
            AddCol("Status", 140);
            AddCol("Location", 200);
            foreach (DataGridViewColumn c in grid.Columns) { c.SortMode = DataGridViewColumnSortMode.Programmatic; if (c.Name != "chk") c.ReadOnly = true; }

            grid.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
                var f = view[e.RowIndex];
                switch (e.ColumnIndex)
                {
                    case 0: e.Value = f.Checked; break;
                    case 1: e.Value = f.Name; break;
                    case 2: e.Value = FileTypes.Describe(f.Extension); break;
                    case 3: e.Value = FileTypes.FormatSize(f.Size); break;
                    case 4: e.Value = f.Modified?.ToString("g") ?? ""; break;
                    case 5: e.Value = f.StatusText; break;
                    case 6: e.Value = f.Source == FoundSource.QuickScan ? (f.Partition?.DisplayName + " " + f.Folder) : f.Folder; break;
                }
            };
            grid.CellValuePushed += (s, e) =>
            {
                if (e.ColumnIndex == 0 && e.RowIndex >= 0 && e.RowIndex < view.Count)
                {
                    view[e.RowIndex].Checked = e.Value is bool b && b;
                    UpdateSelectionLabel();
                }
            };
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex == 5 && e.RowIndex >= 0 && e.RowIndex < view.Count)
                {
                    var f = view[e.RowIndex];
                    e.CellStyle.ForeColor = StatusColor(f);
                    e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
                }
                if ((e.ColumnIndex == 2 || e.ColumnIndex == 4 || e.ColumnIndex == 6 || e.ColumnIndex == 3) && e.RowIndex >= 0)
                {
                    e.CellStyle.ForeColor = Theme.TextMuted;
                    e.CellStyle.SelectionForeColor = Theme.TextMuted;
                }
            };
            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space)
                {
                    var rows = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).Where(i => i < view.Count).ToList();
                    if (rows.Count == 0) return;
                    bool target = !view[rows[0]].Checked;
                    foreach (var i in rows) view[i].Checked = target;
                    grid.Invalidate();
                    UpdateSelectionLabel();
                    e.Handled = true;
                }
            };
            grid.ColumnHeaderMouseClick += (s, e) =>
            {
                if (e.ColumnIndex == 0) return;
                if (sortColumn == e.ColumnIndex) sortAsc = !sortAsc; else { sortColumn = e.ColumnIndex; sortAsc = true; }
                foreach (DataGridViewColumn c in grid.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
                grid.Columns[sortColumn].HeaderCell.SortGlyphDirection = sortAsc ? SortOrder.Ascending : SortOrder.Descending;
                RebuildView();
            };
            grid.SelectionChanged += (s, e) => ShowPreview();
            grid.Resize += (s, e) => SizeColumns();
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenPreviewFile(); };
        }

        void SizeColumns()
        {
            // Name and Location share whatever the fixed-width columns leave over (60/40).
            int avail = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
            int fixedW = 0;
            foreach (DataGridViewColumn c in grid.Columns) if (c.Name != "Name" && c.Name != "Location") fixedW += c.Width;
            int rest = Math.Max(Theme.S(200), avail - fixedW);
            grid.Columns["Name"].Width = rest * 6 / 10;
            grid.Columns["Location"].Width = rest - grid.Columns["Name"].Width;
        }

        static Color StatusColor(FoundFile f)
        {
            if (f.Source == FoundSource.DeepScan) return Color.FromArgb(124, 58, 237);
            if (!f.Deleted) return Theme.TextMuted;
            switch (f.Chance)
            {
                case RecoveryChance.Good: return Theme.Success;
                case RecoveryChance.PartlyOverwritten: return Theme.Warning;
                case RecoveryChance.Overwritten: case RecoveryChance.Unsupported: return Theme.Danger;
                default: return Theme.Accent;
            }
        }

        DataGridViewTextBoxColumn AddCol(string name, int width, DataGridViewAutoSizeColumnMode mode = DataGridViewAutoSizeColumnMode.None)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = name.ToUpperInvariant(), Width = Theme.S(width), AutoSizeMode = mode, MinimumWidth = Theme.S(60) };
            grid.Columns.Add(c);
            return c;
        }

        void BuildBottom()
        {
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Theme.S(76);
            bottom.Padding = new Padding(0, Theme.S(14), 0, 0);
            bottom.BackColor = Theme.Background;
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(0) };
            bottom.Controls.Add(card);
            selectionLabel.Font = Theme.Body; selectionLabel.ForeColor = Theme.TextMuted; selectionLabel.AutoSize = false; selectionLabel.BackColor = Color.White;
            selectionLabel.Location = new Point(Theme.S(20), Theme.S(18)); selectionLabel.Size = new Size(Theme.S(600), Theme.S(24));
            recoverBtn.Click += async (s, e) => await RecoverAsync();
            card.Controls.Add(selectionLabel);
            card.Controls.Add(recoverBtn);
            card.Resize += (s, e) => recoverBtn.Location = new Point(card.Width - recoverBtn.Width - Theme.S(10), (card.Height - recoverBtn.Height) / 2);
            UpdateSelectionLabel();
        }

        // ------------------------------------------------------------------ scan lifecycle

        public void Start(DiskInfo disk, PartitionInfo part, List<PartitionInfo> quickTargets, DeepScanOptions deep)
        {
            this.disk = disk; this.part = part; this.quickTargets = quickTargets; this.deep = deep;
            all.Clear(); view.Clear(); vaultSeen.Clear();
            categoryFilter = null; quickFound = 0;
            foreach (var t in tileMap.Values) { t.Count = 0; t.Bytes = 0; t.Selected = false; t.Invalidate(); }
            grid.RowCount = 0;
            selectAll.Checked = false;
            search.Text = "";
            sourceFilter.SelectedIndex = 0;
            ClearPreview();
            HasSession = true;
            var name = part != null ? part.DisplayName + " on " + disk.Title : disk.Title;
            TitleLabel.Text = "Scanning " + name;
            SubtitleLabel.Text = "Files appear below as they are found. You can review and recover them while the scan runs.";
            _ = RunAsync();
        }

        /// <summary>Shows the results of a saved scan without scanning again.</summary>
        public void LoadSaved(string id)
        {
            var r = ScanStore.Load(id);
            disk = r.disk; part = r.scanned; quickTargets = new List<PartitionInfo>();
            deep = r.vault != null ? new DeepScanOptions { Disk = r.disk, Partition = r.scanned, VaultDir = r.vault } : null;
            all.Clear(); vaultSeen.Clear();
            all.AddRange(r.files);
            foreach (var f in r.files) if (f.Source == FoundSource.DeepScan) vaultSeen.Add(f.FullPath);
            categoryFilter = null;
            foreach (var t in tileMap.Values) { t.Selected = false; t.Invalidate(); }
            selectAll.Checked = false; search.Text = ""; sourceFilter.SelectedIndex = 0;
            ClearPreview();
            HasSession = true;
            IsBusy = false;
            stopBtn.Visible = false;
            OnResizeHeader();
            long bytes = r.files.Sum(f => f.Size);
            TitleLabel.Text = $"Saved scan — {r.files.Count:N0} files ({FileTypes.FormatSize(bytes)})";
            bool sourceThere = File.Exists(disk.Path) || disk.Path.StartsWith(@"\\.\");
            SubtitleLabel.Text = r.raw.Title + " · scanned " + r.raw.Created.ToString("g") +
                (sourceThere ? "" : " · source not connected: only reconstructed files can be recovered");
            phaseLabel.Text = r.raw.Stopped ? "Saved scan (stopped before it finished)" : "Saved scan";
            percentLabel.Text = "";
            progress.Indeterminate = false;
            progress.Value = r.raw.Stopped ? 0 : 1;
            detailLabel.Text = $"{r.files.Count(f => f.Source == FoundSource.QuickScan):N0} files from the file system · " +
                               $"{r.files.Count(f => f.Source == FoundSource.DeepScan):N0} reconstructed" + (r.vault != null ? " in " + r.vault : "");
            UpdateTiles();
            RebuildView();
        }

        async Task RunAsync()
        {
            IsBusy = true;
            stopBtn.Visible = true;
            stopBtn.Enabled = true;
            recoverBtn.Enabled = true;
            Header.PerformLayout();
            cts = new CancellationTokenSource();
            uiTimer.Start();
            bool stopped = false;
            var errors = new List<string>();
            try
            {
                int i = 0;
                foreach (var qp in quickTargets)
                {
                    if (cts.IsCancellationRequested) break;
                    i++;
                    phaseLabel.Text = $"Quick Scan — reading the file system of {qp.DisplayName}";
                    var where = qp.IsNtfs ? "the NTFS Master File Table" : "the FAT/exFAT directory tables";
                    detailLabel.Text = (quickTargets.Count > 1 ? $"Partition {i} of {quickTargets.Count} · " : "") + "Looking for deleted file records in " + where;
                    progress.Indeterminate = !qp.IsNtfs;
                    progress.Value = 0;
                    percentLabel.Text = "";
                    quickProgress = -1;
                    quickRunning = qp.IsNtfs;
                    try
                    {
                        var files = await RecoveryService.QuickScanAsync(qp, cts.Token, f => quickProgress = f);
                        quickFound += files.Count;
                        lock (all) all.AddRange(files);
                        viewDirty = true;
                    }
                    catch (OperationCanceledException) { stopped = true; }
                    catch (Exception ex) { errors.Add("Quick Scan of " + qp.DisplayName + ": " + ex.Message); }
                    quickRunning = false;
                }
                progress.Indeterminate = false;

                if (deep != null && !cts.IsCancellationRequested)
                {
                    stopFile = Path.Combine(EngineHost.AppDataDir, "photorec.stop");
                    deepRunning = true;
                    passSeen = -1;
                    phaseLabel.Text = "Deep Scan — preparing";
                    detailLabel.Text = "Reconstructed files are saved to " + deep.VaultDir;
                    try
                    {
                        await RecoveryService.DeepScanAsync(deep, p => lastProgress = p, stopFile, cts.Token);
                    }
                    catch (OperationCanceledException) { stopped = true; }
                    catch (Exception ex) { errors.Add("Deep Scan: " + ex.Message); }
                    deepRunning = false;
                    PollVault();
                }
            }
            finally
            {
                IsBusy = false;
                uiTimer.Stop();
                progress.Indeterminate = false;
                try { if (stopFile != null && File.Exists(stopFile)) { stopped = true; File.Delete(stopFile); } } catch { }
                Tick();
                Finish(stopped || (cts?.IsCancellationRequested ?? false), errors);
            }
        }

        void Finish(bool stopped, List<string> errors)
        {
            stopBtn.Visible = false;
            Header.PerformLayout();
            OnResizeHeader();
            long bytes = 0; int count;
            lock (all) { count = all.Count; foreach (var f in all) bytes += f.Size; }
            TitleLabel.Text = (stopped ? "Scan stopped — " : "Scan complete — ") + $"{count:N0} files found ({FileTypes.FormatSize(bytes)})";
            SubtitleLabel.Text = "Tick the files you want back, then choose Recover. Save them to a different drive.";
            phaseLabel.Text = stopped ? "Scan stopped" : "Scan complete";
            percentLabel.Text = stopped ? "" : "100%";
            if (!stopped) progress.Value = 1;
            var parts = new List<string>();
            if (quickTargets.Count > 0) parts.Add($"Quick Scan: {quickFound:N0} files listed");
            if (deep != null) parts.Add($"Deep Scan: {all.Count(f => f.Source == FoundSource.DeepScan):N0} files reconstructed in {deep.VaultDir}");
            detailLabel.Text = string.Join("   ·   ", parts);
            if (count > 0)
            {
                try { lock (all) ScanStore.Save(disk, part, deep?.VaultDir, all, stopped); }
                catch (Exception ex) { errors.Add("The scan results could not be saved: " + ex.Message); }
            }
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n\n", errors), "Some steps reported a problem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else if (count == 0 && !stopped)
                MessageBox.Show(this, "No recoverable files were found with the chosen methods." +
                    (deep == null ? "\n\nTry a Deep Scan, which searches every sector for file signatures." : ""),
                    "Nothing found", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnResizeHeader()
        {
            stopBtn.Location = new Point(Header.Width - stopBtn.Width, Theme.S(4));
            backBtn.Location = new Point((stopBtn.Visible ? stopBtn.Left - Theme.S(10) : Header.Width) - backBtn.Width, Theme.S(4));
            TitleLabel.AutoSize = false;
            TitleLabel.Size = new Size(Math.Max(Theme.S(200), backBtn.Left - Theme.S(20)), Theme.S(38));
        }

        bool ConfirmStop()
        {
            if (MessageBox.Show(this, "Stop the scan in progress? Files found so far stay available.", "Spindle Recovery",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            Stop();
            return true;
        }

        void Stop()
        {
            stopBtn.Enabled = false;
            phaseLabel.Text = "Stopping…";
            if (deepRunning && stopFile != null)
            {
                try { File.WriteAllText(stopFile, "stop"); } catch { }
                // fall back to killing the engine if it does not stop by itself
                var c = cts;
                Task.Delay(8000).ContinueWith(_ => { try { c?.Cancel(); } catch { } });
            }
            else cts?.Cancel();
        }

        public void StopForExit()
        {
            try { if (stopFile != null) File.WriteAllText(stopFile, "stop"); } catch { }
            try { cts?.Cancel(); } catch { }
        }

        void Tick()
        {
            OnResizeHeader();
            if (quickRunning && quickProgress >= 0)
            {
                progress.Value = quickProgress;
                percentLabel.Text = $"{quickProgress * 100:0}%";
            }
            if (deepRunning)
            {
                var p = lastProgress;
                if (p != null)
                {
                    if (p.Pass != passSeen) { passSeen = p.Pass; passStart = DateTime.Now; }
                    var frac = p.Fraction;
                    progress.Value = frac;
                    percentLabel.Text = $"{frac * 100:0}%";
                    long secSize = deep.Disk.SectorSize > 0 ? deep.Disk.SectorSize : 512;
                    phaseLabel.Text = p.Pass == 0 ? "Deep Scan — analysing the disk layout" : "Deep Scan — searching for file signatures";
                    var eta = "";
                    var elapsed = DateTime.Now - passStart;
                    if (frac > 0.01 && elapsed.TotalSeconds > 5)
                    {
                        var left = TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - frac) / frac);
                        eta = left.TotalHours >= 1 ? $" · about {(int)left.TotalHours} h {left.Minutes} min left" : left.TotalMinutes >= 1 ? $" · about {(int)left.TotalMinutes} min left" : " · less than a minute left";
                    }
                    detailLabel.Text = $"{FileTypes.FormatSize(p.Sector * secSize)} of {FileTypes.FormatSize(p.TotalSectors * secSize)} read{eta} · {p.FilesFound:N0} files reconstructed";
                }
                PollVault();
            }
            if (viewDirty) { viewDirty = false; UpdateTiles(); RebuildView(); }
        }

        void PollVault()
        {
            if (deep == null) return;
            List<FoundFile> fresh;
            try { fresh = RecoveryService.EnumerateVault(deep.VaultDir, vaultSeen).ToList(); }
            catch { return; }
            if (fresh.Count == 0) return;
            lock (all) all.AddRange(fresh);
            viewDirty = true;
        }

        void UpdateTiles()
        {
            var counts = new Dictionary<FileCategory, (int n, long b)>();
            lock (all)
                foreach (var f in all)
                {
                    counts.TryGetValue(f.Category, out var v);
                    counts[f.Category] = (v.n + 1, v.b + f.Size);
                }
            foreach (var kv in tileMap)
            {
                counts.TryGetValue(kv.Key, out var v);
                if (kv.Value.Count != v.n || kv.Value.Bytes != v.b) { kv.Value.Count = v.n; kv.Value.Bytes = v.b; kv.Value.Invalidate(); }
            }
        }

        // ------------------------------------------------------------------ results view

        void RebuildView()
        {
            IEnumerable<FoundFile> q;
            lock (all) q = all.ToList();
            if (categoryFilter.HasValue) q = q.Where(f => f.Category == categoryFilter.Value);
            switch (sourceFilter.SelectedIndex)
            {
                case 1: q = q.Where(f => f.Deleted || f.Source == FoundSource.DeepScan); break;
                case 2: q = q.Where(f => f.Source == FoundSource.QuickScan && !f.Deleted); break;
                case 3: q = q.Where(f => f.Source == FoundSource.DeepScan); break;
            }
            var term = search.Text.Trim();
            if (term.Length > 0) q = q.Where(f => f.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            if (sortColumn > 0)
            {
                Func<FoundFile, IComparable> key;
                switch (sortColumn)
                {
                    case 2: key = f => f.Extension ?? ""; break;
                    case 3: key = f => f.Size; break;
                    case 4: key = f => f.Modified ?? DateTime.MinValue; break;
                    case 5: key = f => f.StatusText; break;
                    case 6: key = f => f.Folder ?? ""; break;
                    default: key = f => f.Name ?? ""; break;
                }
                q = sortAsc ? q.OrderBy(key) : q.OrderByDescending(key);
            }
            var selectedFile = CurrentFile();
            view = q.ToList();
            grid.RowCount = 0;
            grid.RowCount = view.Count;
            if (selectedFile != null)
            {
                int idx = view.IndexOf(selectedFile);
                if (idx >= 0 && idx < grid.RowCount)
                {
                    grid.ClearSelection();
                    grid.Rows[idx].Selected = true;
                }
            }
            grid.Invalidate();
            UpdateSelectionLabel();
        }

        FoundFile CurrentFile()
        {
            if (grid.SelectedRows.Count == 0) return null;
            int i = grid.SelectedRows[0].Index;
            return i >= 0 && i < view.Count ? view[i] : null;
        }

        void UpdateSelectionLabel()
        {
            int n = 0; long b = 0;
            lock (all) foreach (var f in all) if (f.Checked) { n++; b += f.Size; }
            selectionLabel.Text = n == 0
                ? $"{view.Count:N0} files shown. Tick the files you want to recover."
                : $"{n:N0} files selected · {FileTypes.FormatSize(b)}";
            recoverBtn.Text = n == 0 ? "Recover" : $"Recover {n:N0}";
        }

        // ------------------------------------------------------------------ preview

        static readonly HashSet<string> imageExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "gif", "bmp", "ico", "tif", "tiff" };

        void ClearPreview()
        {
            var old = previewImage.Image;
            previewImage.Image = null;
            old?.Dispose();
            previewName.Text = "";
            previewInfo.Text = "Select a file to see its details.";
            previewOpen.Visible = false;
        }

        void ShowPreview()
        {
            var f = CurrentFile();
            if (f == null) { ClearPreview(); return; }
            var old = previewImage.Image;
            previewImage.Image = null;
            old?.Dispose();
            previewName.Text = f.Name;
            var lines = new List<string>
            {
                "Type:  " + FileTypes.Describe(f.Extension),
                "Size:  " + FileTypes.FormatSize(f.Size),
                "Status:  " + f.StatusText,
            };
            if (f.Modified.HasValue) lines.Add("Modified:  " + f.Modified.Value.ToString("g"));
            if (f.Source == FoundSource.QuickScan)
            {
                lines.Add("Original folder:  " + f.Folder);
                lines.Add("");
                lines.Add(!f.Deleted ? "This file still exists on the source drive." : ChanceExplanation(f));
                if (f.Name.StartsWith("_")) lines.Add("FAT replaces the first letter of deleted short names with \"_\".");
            }
            else
            {
                lines.Add("");
                lines.Add("Rebuilt from its content signature. Already saved in the recovery vault; Recover copies it to your chosen folder.");
            }
            previewInfo.Text = string.Join("\n", lines);
            var need = TextRenderer.MeasureText(previewInfo.Text, previewInfo.Font, new Size(previewInfo.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
            previewInfo.Height = Math.Max(Theme.S(20), Math.Min(need + Theme.S(4), preview.Height - previewInfo.Top - Theme.S(56)));
            previewOpen.Top = previewInfo.Bottom + Theme.S(8);
            previewOpen.Visible = f.Source == FoundSource.DeepScan;
            int token = ++previewToken;
            bool canRead = f.Source == FoundSource.DeepScan || f.Ntfs != null;
            if (canRead && imageExt.Contains(f.Extension ?? "") && f.Size < 40_000_000 && f.Chance != RecoveryChance.Unsupported)
                LoadPreviewAsync(f, token);
            previewImage.Invalidate();
        }

        static string ChanceExplanation(FoundFile f)
        {
            switch (f.Chance)
            {
                case RecoveryChance.Good: return "Recovery chance: good. None of its disk space has been reused since it was deleted.";
                case RecoveryChance.PartlyOverwritten: return $"Recovery chance: partial. About {f.Ntfs?.OverwrittenFraction * 100:0}% of its disk space now belongs to other files, so parts will be damaged.";
                case RecoveryChance.Overwritten: return "Recovery chance: poor. Its disk space has been reused by other files. Try Deep Scan for an older copy.";
                case RecoveryChance.Unsupported: return "NTFS-compressed or encrypted (EFS) file: Quick Scan cannot rebuild it. Deep Scan may find it.";
                default: return "Deleted entry. It will be recovered with its original name; if the space was reused the content may be damaged.";
            }
        }

        /// <summary>Decodes a picture in the background, straight from the vault or from the source disk.</summary>
        async void LoadPreviewAsync(FoundFile f, int token)
        {
            try
            {
                var bytes = await RecoveryService.ReadHeadAsync(f, (int)Math.Min(f.Size, 40_000_000));
                if (bytes == null || token != previewToken) return;
                Bitmap bmp = await System.Threading.Tasks.Task.Run(() =>
                {
                    using (var ms = new MemoryStream(bytes))
                    using (var img = Image.FromStream(ms))
                    {
                        var max = previewImage.Size;
                        double scale = Math.Min(1.0, Math.Min((double)max.Width * 2 / img.Width, (double)max.Height * 2 / img.Height));
                        return new Bitmap(img, Math.Max(1, (int)(img.Width * scale)), Math.Max(1, (int)(img.Height * scale)));
                    }
                });
                if (token != previewToken) { bmp.Dispose(); return; }
                var old = previewImage.Image;
                previewImage.Image = bmp;
                old?.Dispose();
            }
            catch { /* not decodable (damaged or not a picture) */ }
        }

        void PaintPreviewPlaceholder(object sender, PaintEventArgs e)
        {
            if (previewImage.Image != null) return;
            var f = CurrentFile();
            var g = e.Graphics;
            Theme.HighQuality(g);
            var cat = f?.Category ?? FileCategory.Other;
            var color = f == null ? Theme.TextFaint : Theme.CategoryColors[(int)cat];
            float s = Theme.S(56f);
            Icons.Draw(g, Icons.ForCategory(cat), new RectangleF((previewImage.Width - s) / 2, (previewImage.Height - s) / 2 - Theme.S(8), s, s), color);
            if (f != null && f.Source == FoundSource.QuickScan && f.Ntfs == null)
            {
                var t = "Preview after recovery";
                using (var b = new SolidBrush(Theme.TextMuted))
                {
                    var sz = g.MeasureString(t, Theme.Small);
                    g.DrawString(t, Theme.Small, b, (previewImage.Width - sz.Width) / 2, previewImage.Height - Theme.S(30));
                }
            }
        }

        void OpenPreviewFile()
        {
            var f = CurrentFile();
            if (f == null || f.Source != FoundSource.DeepScan || !File.Exists(f.FullPath)) return;
            try
            {
                if (EngineHost.IsWindows) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(f.FullPath) { UseShellExecute = true });
                else System.Diagnostics.Process.Start("xdg-open", f.FullPath);
            }
            catch (Exception ex) { MainForm.Error(this, "The file could not be opened.", ex); }
        }

        // ------------------------------------------------------------------ recovery

        async Task RecoverAsync()
        {
            List<FoundFile> chosen;
            lock (all) chosen = all.Where(f => f.Checked).ToList();
            if (chosen.Count == 0)
            {
                var cur = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).Where(i => i < view.Count).Select(i => view[i]).ToList();
                if (cur.Count == 0)
                {
                    MessageBox.Show(this, "Tick the boxes next to the files you want to recover.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                chosen = cur;
            }
            string dest;
            using (var fb = new FolderBrowserDialog { Description = "Choose where to save the recovered files (use a different drive)", ShowNewFolderButton = true })
            {
                if (fb.ShowDialog(this) != DialogResult.OK) return;
                dest = fb.SelectedPath;
            }
            if (ScanOptionsDialog.IsOnSourceDrive(dest, disk, part) &&
                MessageBox.Show(this, "This folder is on the drive you are recovering from. Saving there can overwrite other lost files.\n\nSave there anyway?",
                    "Same drive", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            dest = Path.Combine(dest, "Recovered " + DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
            Directory.CreateDirectory(dest);
            bool wasBusy = IsBusy;
            recoverBtn.Enabled = false;
            var oldPhase = phaseLabel.Text;
            int ok = 0, failed = 0;
            try
            {
                var carved = chosen.Where(f => f.Source == FoundSource.DeepScan).ToList();
                int done = 0;
                foreach (var f in carved)
                {
                    done++;
                    if (!wasBusy) { phaseLabel.Text = $"Recovering {done:N0} of {chosen.Count:N0}"; progress.Value = (double)done / chosen.Count; }
                    try
                    {
                        var dir = Path.Combine(dest, "Reconstructed", string.IsNullOrEmpty(f.Extension) ? "other" : f.Extension);
                        Directory.CreateDirectory(dir);
                        var target = UniquePath(Path.Combine(dir, f.Name));
                        await Task.Run(() => File.Copy(f.FullPath, target));
                        ok++;
                    }
                    catch { failed++; }
                }
                foreach (var grp in chosen.Where(f => f.Source == FoundSource.QuickScan).GroupBy(f => f.Partition))
                {
                    if (!wasBusy) { phaseLabel.Text = $"Recovering files from {grp.Key.DisplayName}"; progress.Indeterminate = true; }
                    var sub = Path.Combine(dest, SafeName(grp.Key.DisplayName));
                    try
                    {
                        var r = await RecoveryService.RecoverQuickFilesAsync(grp.Key, grp, sub, CancellationToken.None);
                        ok += r.Ok; failed += r.Failed;
                    }
                    catch { failed += grp.Count(); }
                    progress.Indeterminate = false;
                }
            }
            finally
            {
                recoverBtn.Enabled = true;
                if (!wasBusy) phaseLabel.Text = oldPhase;
            }
            var msg = $"{ok:N0} file{(ok == 1 ? "" : "s")} recovered to:\n{dest}" + (failed > 0 ? $"\n\n{failed:N0} could not be recovered (unreadable or overwritten)." : "") + "\n\nOpen the folder now?";
            if (MessageBox.Show(this, msg, "Recovery finished", MessageBoxButtons.YesNo, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information) == DialogResult.Yes)
                MainForm.OpenFolder(dest);
        }

        static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().Length == 0 ? "Partition" : s.Trim();
        }

        static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                var p = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(p)) return p;
            }
        }
    }

    static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
