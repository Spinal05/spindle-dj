using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SpindleRecovery.Engine
{
    public class DeepScanProgress
    {
        public int Pass;            // 0 = analysing block size, 1+ = carving
        public long Sector;
        public long TotalSectors;
        public int FilesFound;
        public double Fraction => TotalSectors <= 0 ? 0 : Math.Min(1.0, (double)Sector / TotalSectors);
    }

    public class DeepScanOptions
    {
        public DiskInfo Disk;
        public PartitionInfo Partition;            // null = whole device
        public HashSet<FileCategory> Categories;   // null or empty = every format PhotoRec knows
        public bool FreeSpaceOnly;                 // only unallocated space (deleted files) of the partition
        public bool Paranoid = true;
        public bool KeepCorrupted;
        public string VaultDir;                    // where PhotoRec writes reconstructed files
    }

    /// <summary>High-level operations built on the TestDisk / PhotoRec engines.</summary>
    public static class RecoveryService
    {
        static readonly Regex ProgressLine = new Regex(@"^@@PROGRESS pass=(\d+) sector=(\d+) total=(\d+) files=(\d+)", RegexOptions.Compiled);

        // ---------------------------------------------------------------- devices

        public static async Task<List<DiskInfo>> ListDevicesAsync(CancellationToken token)
        {
            var dir = EngineHost.NewSessionDir("list");
            var lines = new List<string>();
            await EngineHost.RunAsync(EngineHost.TestDiskPath, new[] { "/list" }, dir, l => { lock (lines) lines.Add(l); }, token);
            var disks = TestDiskParser.ParseList(lines);
            if (EngineHost.IsWindows)
            {
                try { WindowsDevices.Enrich(disks); } catch { /* WMI is optional */ }
            }
            foreach (var d in disks) FinishDisk(d);
            return disks;
        }

        public static async Task<DiskInfo> OpenImageAsync(string imagePath, CancellationToken token)
        {
            var dir = EngineHost.NewSessionDir("image");
            var lines = new List<string>();
            await EngineHost.RunAsync(EngineHost.TestDiskPath, new[] { "/list", imagePath }, dir, l => { lock (lines) lines.Add(l); }, token);
            var disks = TestDiskParser.ParseList(lines);
            var d = disks.FirstOrDefault();
            if (d == null) throw new IOException("The image could not be opened:\n" + string.Join("\n", lines.Skip(3)));
            d.Kind = DeviceKind.Image;
            d.Path = imagePath;
            d.Title = Path.GetFileName(imagePath);
            d.Model = "Disk image";
            try { d.SizeBytes = new FileInfo(imagePath).Length; } catch { }
            FinishDisk(d);
            return d;
        }

        static void FinishDisk(DiskInfo d)
        {
            if (string.IsNullOrEmpty(d.Title) || d.Title == d.Path)
            {
                var m = Regex.Match(d.Path ?? "", @"PhysicalDrive(\d+)", RegexOptions.IgnoreCase);
                d.Title = !string.IsNullOrEmpty(d.Model) ? d.Model : (m.Success ? "Disk " + m.Groups[1].Value : d.Path);
            }
            foreach (var p in d.Partitions) p.Disk = d;
        }

        // ---------------------------------------------------------------- quick scan

        /// <summary>
        /// Lists every file (existing and deleted) that the file system still references.
        /// Nothing is written to the source device.
        /// </summary>
        public static async Task<List<FoundFile>> QuickScanAsync(PartitionInfo part, CancellationToken token)
        {
            var dir = EngineHost.NewSessionDir("quick");
            var cmd = (part.Number > 0 ? "advanced," + part.Number : "advanced") + ",list,recursive,fullpathname";
            await EngineHost.RunAsync(EngineHost.TestDiskPath, new[] { "/log", "/cmd", part.Disk.Path, cmd }, dir, null, token);
            token.ThrowIfCancellationRequested();
            var log = EngineHost.ReadLog(dir, "testdisk.log");
            return TestDiskParser.ParseFileListing(log, part);
        }

        /// <summary>Copies the selected quick-scan files (original names and folders) to <paramref name="destination"/>.</summary>
        public static async Task<(int ok, int failed)> RecoverQuickFilesAsync(PartitionInfo part, IEnumerable<FoundFile> files, string destination, CancellationToken token)
        {
            Directory.CreateDirectory(destination);
            var dir = EngineHost.NewSessionDir("undelete");
            var listFile = Path.Combine(dir, "selection.txt");
            File.WriteAllText(listFile, string.Join("\n", files.Select(f => f.FullPath)) + "\n", new UTF8Encoding(false));
            var cmd = (part.Number > 0 ? "advanced," + part.Number : "advanced") + ",list,recursive,fullpathname,filecopy";
            // TestDisk copies into its working directory, so run it inside the destination folder.
            var env = new Dictionary<string, string> { ["TESTDISK_COPY_LIST"] = listFile };
            var logInDest = Path.Combine(destination, "testdisk.log");
            bool hadLog = File.Exists(logInDest);
            await EngineHost.RunAsync(EngineHost.TestDiskPath, new[] { "/log", "/cmd", part.Disk.Path, cmd }, destination, null, token, env);
            string log = EngineHost.ReadLog(destination, "testdisk.log");
            try
            {
                File.Copy(logInDest, Path.Combine(dir, "testdisk.log"), true);
                if (!hadLog) File.Delete(logInDest);
            }
            catch { }
            var m = Regex.Matches(log, @"Copy done! (\d+) ok, (\d+) failed");
            if (m.Count == 0) return (0, files.Count());
            var last = m[m.Count - 1];
            return (int.Parse(last.Groups[1].Value), int.Parse(last.Groups[2].Value));
        }

        // ---------------------------------------------------------------- deep scan

        public static string BuildPhotoRecCommand(DeepScanOptions o)
        {
            var parts = new List<string>();
            if (o.Partition == null) parts.Add("partition_none");
            else if (o.Partition.Number > 0) parts.Add(o.Partition.Number.ToString(CultureInfo.InvariantCulture));
            parts.Add("options");
            parts.Add(o.Paranoid ? "paranoid" : "paranoid_no");
            parts.Add(o.KeepCorrupted ? "keep_corrupted_file" : "keep_corrupted_file_no");
            if (o.Categories != null && o.Categories.Count > 0 && o.Categories.Count < Enum.GetValues(typeof(FileCategory)).Length)
            {
                parts.Add("fileopt");
                parts.Add("everything");
                parts.Add("disable");
                var fams = new HashSet<string>();
                foreach (var c in o.Categories)
                    foreach (var f in FileTypes.PhotoRecFamilies[c]) fams.Add(f);
                foreach (var f in fams.OrderBy(x => x, StringComparer.Ordinal)) { parts.Add(f); parts.Add("enable"); }
            }
            parts.Add(o.FreeSpaceOnly && o.Partition != null ? "freespace" : "wholespace");
            parts.Add("search");
            return string.Join(",", parts);
        }

        /// <summary>
        /// Runs PhotoRec signature carving. Reconstructed files are written under
        /// <see cref="DeepScanOptions.VaultDir"/> (recup_dir.1, recup_dir.2, ...).
        /// </summary>
        public static async Task<int> DeepScanAsync(DeepScanOptions o, Action<DeepScanProgress> onProgress, string stopFile, CancellationToken token)
        {
            Directory.CreateDirectory(o.VaultDir);
            var dir = EngineHost.NewSessionDir("deep");
            if (File.Exists(stopFile)) File.Delete(stopFile);
            var env = new Dictionary<string, string> { ["PHOTOREC_STOP_FILE"] = stopFile };
            var args = new[] { "/log", "/d", Path.Combine(o.VaultDir, "recup_dir"), "/cmd", o.Disk.Path, BuildPhotoRecCommand(o) };
            File.WriteAllText(Path.Combine(dir, "command.txt"), EngineHost.PhotoRecPath + " " + EngineHost.JoinArgs(args));
            var code = await EngineHost.RunAsync(EngineHost.PhotoRecPath, args, dir, line =>
            {
                var m = ProgressLine.Match(line);
                if (m.Success)
                    onProgress?.Invoke(new DeepScanProgress
                    {
                        Pass = int.Parse(m.Groups[1].Value),
                        Sector = long.Parse(m.Groups[2].Value),
                        TotalSectors = long.Parse(m.Groups[3].Value),
                        FilesFound = int.Parse(m.Groups[4].Value),
                    });
            }, token, env);
            try { File.Copy(Path.Combine(dir, "photorec.log"), Path.Combine(o.VaultDir, "photorec.log"), true); } catch { }
            return code;
        }

        /// <summary>Enumerates reconstructed files that PhotoRec has written to the vault so far.</summary>
        public static IEnumerable<FoundFile> EnumerateVault(string vaultDir, HashSet<string> alreadySeen)
        {
            if (!Directory.Exists(vaultDir)) yield break;
            foreach (var sub in Directory.GetDirectories(vaultDir, "recup_dir.*"))
            {
                string[] files;
                try { files = Directory.GetFiles(sub); } catch { continue; }
                foreach (var f in files)
                {
                    if (alreadySeen.Contains(f)) continue;
                    var name = Path.GetFileName(f);
                    if (name.Equals("report.xml", StringComparison.OrdinalIgnoreCase)) { alreadySeen.Add(f); continue; }
                    FileInfo fi;
                    try { fi = new FileInfo(f); } catch { continue; }
                    alreadySeen.Add(f);
                    var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
                    yield return new FoundFile
                    {
                        Name = name,
                        Folder = "Reconstructed \\ " + (ext.Length > 0 ? ext.ToUpperInvariant() : "unknown"),
                        FullPath = f,
                        Size = fi.Length,
                        Modified = fi.LastWriteTime,
                        Deleted = true,
                        Source = FoundSource.DeepScan,
                        Category = FileTypes.Categorize(name),
                        Extension = ext,
                    };
                }
            }
        }

        // ---------------------------------------------------------------- partition recovery

        /// <summary>
        /// Analyses the partition table. <paramref name="deeper"/> adds TestDisk's deeper search.
        /// When <paramref name="write"/> is true the partitions found are written to the disk.
        /// </summary>
        public static async Task<PartitionTableResult> AnalyzePartitionsAsync(DiskInfo disk, bool deeper, bool write, Action<string> onLine, CancellationToken token)
        {
            var dir = EngineHost.NewSessionDir(write ? "partwrite" : "analyze");
            var cmd = "analyze" + (deeper ? ",search" : "") + (write ? ",noconfirm,write" : "");
            await EngineHost.RunAsync(EngineHost.TestDiskPath, new[] { "/log", "/cmd", disk.Path, cmd }, dir, onLine, token);
            token.ThrowIfCancellationRequested();
            var log = EngineHost.ReadLog(dir, "testdisk.log");
            var r = TestDiskParser.ParseAnalysis(log, disk);
            r.RawLog = log;
            return r;
        }

        /// <summary>Saves the current partition table with TestDisk's "backup" command (backup.log).</summary>
        public static async Task<string> BackupPartitionTableAsync(DiskInfo disk, CancellationToken token)
        {
            var dir = EngineHost.NewSessionDir("ptbackup");
            await EngineHost.RunAsync(EngineHost.TestDiskPath, new[] { "/log", "/cmd", disk.Path, "analyze,backup" }, dir, null, token);
            return dir;
        }
    }
}
