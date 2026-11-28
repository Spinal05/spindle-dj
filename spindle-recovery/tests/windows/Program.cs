// End-to-end tests on real Windows, run by CI (.github/workflows/spindle-recovery.yml).
// The workflow attaches two virtual disks (FAT32 and NTFS), writes files, deletes some with
// Windows itself, and lists them in expected.txt as  fs|relative/path|md5|deleted(0/1).
// These tests then drive the real SpindleRecovery.exe code and its embedded engines against
// the raw \\.\PhysicalDriveN devices.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using SpindleRecovery.Engine;

static class WinTests
{
    static int pass, fail;
    static readonly List<string> report = new List<string>();

    static void Section(string name) { Console.WriteLine(); Console.WriteLine("== " + name); report.Add("\n**" + name + "**\n"); }

    static bool Check(bool ok, string what)
    {
        if (ok) pass++; else fail++;
        Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what);
        report.Add((ok ? "- ✅ " : "- ❌ ") + what);
        return ok;
    }

    static string Md5(string file)
    {
        using (var m = MD5.Create())
        using (var fs = File.OpenRead(file))
            return BitConverter.ToString(m.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
    }

    class Expected { public string Fs, Path, Md5; public bool Deleted; }

    static int Main(string[] args)
    {
        string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        int fatDisk = int.Parse(Arg("--fat-disk"));
        int ntfsDisk = int.Parse(Arg("--ntfs-disk"));
        int fmtDisk = Arg("--fmt-disk") != null ? int.Parse(Arg("--fmt-disk")) : -1;
        var expected = File.ReadAllLines(Arg("--expected")).Where(l => l.Contains("|")).Select(l =>
        {
            var p = l.Split('|');
            return new Expected { Fs = p[0], Path = "/" + p[1].Replace('\\', '/'), Md5 = p[2].ToLowerInvariant(), Deleted = p[3] == "1" };
        }).ToList();
        var outDir = Arg("--out");
        Directory.CreateDirectory(outDir);

        try { Run(fatDisk, ntfsDisk, expected, outDir); if (fmtDisk >= 0) RunFormatted(fmtDisk, expected, outDir); }
        catch (Exception ex) { Check(false, "unexpected exception: " + ex); }
        try
        {
            // keep every engine log for the CI artifact
            var logs = Path.Combine(outDir, "engine-logs");
            foreach (var d in Directory.GetDirectories(EngineHost.SessionsDir))
                foreach (var f in Directory.GetFiles(d, "*.*"))
                {
                    var t = Path.Combine(logs, Path.GetFileName(d));
                    Directory.CreateDirectory(t);
                    File.Copy(f, Path.Combine(t, Path.GetFileName(f)), true);
                }
        }
        catch (Exception ex) { Console.WriteLine("could not collect logs: " + ex.Message); }

        Console.WriteLine();
        Console.WriteLine($"{pass} passed, {fail} failed");
        report.Insert(0, $"### Spindle Recovery on Windows: {pass} passed, {fail} failed\n");
        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrEmpty(summary)) File.AppendAllLines(summary, report);
        return fail == 0 ? 0 : 1;
    }

    /// <summary>A volume quick-formatted by Windows: rebuild the old folders from leftover MFT records.</summary>
    static void RunFormatted(int diskNo, List<Expected> expected, string outDir)
    {
        Section("Folder rebuild after a Windows quick format (Format-Volume)");
        var disks = RecoveryService.ListDevicesAsync(CancellationToken.None).Result;
        var disk = disks.FirstOrDefault(d => d.Path.Equals(@"\\.\PhysicalDrive" + diskNo, StringComparison.OrdinalIgnoreCase));
        var part = disk?.Partitions.FirstOrDefault(p => p.IsNtfs);
        if (!Check(part != null, "formatted NTFS partition found")) return;
        var quick = RecoveryService.QuickScanAsync(part, CancellationToken.None).Result;
        Check(quick.Count(f => !f.Deleted) == 0, $"the formatted volume looks empty to the file system ({quick.Count} entries)");
        // raw check: how many MFT record signatures are on the partition at all?
        int signatures = 0, nonZeroChunks = 0;
        using (var dev = new SectorReader(disk.Path, disk.SectorSize))
        {
            long off = part.StartSector * disk.SectorSize, len = part.SizeBytes;
            for (long pos = 0; pos < len; pos += 1 << 20)
            {
                var buf = dev.Read(off + pos, (int)Math.Min(1 << 20, len - pos));
                if (buf.Any(b => b != 0)) nonZeroChunks++;
                for (int o = 0; o + 4 <= buf.Length; o += 512)
                    if (buf[o] == 'F' && buf[o + 1] == 'I' && buf[o + 2] == 'L' && buf[o + 3] == 'E') signatures++;
            }
            Console.WriteLine($"         raw read: {signatures} 'FILE' record signatures, {nonZeroChunks} non-empty MiB of {len >> 20}");
            var v = NtfsVolume.OpenOrAssume(dev, off, len);
            Console.WriteLine($"         volume: cluster {v.ClusterSize}, record {v.RecordSize}, live MFT {v.HasMft}, records {(v.HasMft ? v.RecordCount : 0)}");
        }
        var started = DateTime.UtcNow;
        var lost = RecoveryService.NtfsLostRecordsAsync(part, CancellationToken.None).Result;
        var mine = expected.Where(e => e.Fs == "fmt").ToList();
        foreach (var f in lost.Take(5)) Console.WriteLine($"         e.g. {f.FullPath}  {f.Size} bytes  {f.Chance}");
        Console.WriteLine($"         lost folders: {lost.Count(f => f.FullPath.Contains("/" + NtfsVolume.OrphanFolder + "/"))} files grouped under '{NtfsVolume.OrphanFolder}'");

        // Recover everything that was found, then match by content: names repeat across folders here.
        var dest = Path.Combine(outDir, "formatted");
        var res = RecoveryService.RecoverQuickFilesAsync(part, lost, dest, CancellationToken.None).Result;
        var recoveredMd5 = new HashSet<string>(Directory.Exists(dest) ? Directory.GetFiles(dest, "*", SearchOption.AllDirectories).Select(Md5) : new string[0]);
        int byContent = mine.Count(e => recoveredMd5.Contains(e.Md5));
        int fullPath = mine.Count(e => lost.Any(x => string.Equals(x.FullPath, e.Path, StringComparison.OrdinalIgnoreCase)));
        int byName = mine.Count(e => lost.Any(x => string.Equals(x.Name, Path.GetFileName(e.Path), StringComparison.OrdinalIgnoreCase)));
        Console.WriteLine($"         {res.Ok} files written; of {mine.Count} originals: {byContent} recovered byte-for-byte, {fullPath} with their full original path");
        report.Add($"  - after a Windows quick format: **{byContent} of {mine.Count}** files recovered byte-for-byte, **{fullPath}** with their full original folder path");

        Check(byContent > 0, $"{byContent} of {mine.Count} original files recovered byte-for-byte after the format");
        var good = lost.Where(f => f.Chance == RecoveryChance.Good).ToList();
        int goodExact = good.Count(f =>
        {
            var p = Path.Combine(dest, f.FullPath.TrimStart('/').Replace('/', '\\'));
            return File.Exists(p) && mine.Any(e => e.Md5 == Md5(p));
        });
        int goodOurs = good.Count(f => mine.Any(e => string.Equals(Path.GetFileName(e.Path), f.Name, StringComparison.OrdinalIgnoreCase) && e.Md5 != null));
        Check(goodExact >= goodOurs * 9 / 10, $"files rated 'good' are intact ({goodExact} of {goodOurs} test files rated good matched byte-for-byte)");
    }

    static void Run(int fatDiskNo, int ntfsDiskNo, List<Expected> expected, string outDir)
    {
        var token = CancellationToken.None;

        // ------------------------------------------------------------------ engines & security
        Section("Engine extraction and integrity (runs elevated)");
        var engineDir = EngineHost.EngineDir;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Check(engineDir.StartsWith(Path.Combine(pf, "Spindle Recovery"), StringComparison.OrdinalIgnoreCase), "engines extracted under " + engineDir);
        var acl = Directory.GetAccessControl(Path.Combine(pf, "Spindle Recovery"));
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var writeRights = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete |
                          FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes;
        bool nonAdminWrite = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(r => r.AccessControlType == AccessControlType.Allow &&
                      (r.IdentityReference.Equals(users) || r.IdentityReference.Equals(authUsers) || r.IdentityReference.Equals(everyone)) &&
                      (r.FileSystemRights & writeRights) != 0);
        Check(!nonAdminWrite, "standard users cannot write to the engine folder");
        Check(acl.AreAccessRulesProtected, "engine folder does not inherit permissions");

        var td = EngineHost.TestDiskPath;
        var good = Md5(td);
        File.WriteAllText(td, "MZ tampered");
        using (EngineHost.LockVerifiedEngine(td))
        {
            Check(Md5(td) == good, "a modified testdisk.exe is detected and restored before launch");
            bool blocked;
            try { File.AppendAllText(td, "x"); blocked = false; } catch (IOException) { blocked = true; } catch (UnauthorizedAccessException) { blocked = true; }
            Check(blocked, "the engine cannot be modified while it is locked for launch");
        }

        // ------------------------------------------------------------------ device detection
        Section("Device detection (testdisk.exe /list + WMI)");
        var disks = RecoveryService.ListDevicesAsync(token).Result;
        foreach (var d in disks) Console.WriteLine($"   {d.Path,-22} {d.Title} [{d.Kind}] {FileTypes.FormatSize(d.SizeBytes)}, {d.Partitions.Count} partition(s)");
        var fatDisk = disks.FirstOrDefault(d => d.Path.Equals(@"\\.\PhysicalDrive" + fatDiskNo, StringComparison.OrdinalIgnoreCase));
        var ntfsDisk = disks.FirstOrDefault(d => d.Path.Equals(@"\\.\PhysicalDrive" + ntfsDiskNo, StringComparison.OrdinalIgnoreCase));
        if (!Check(fatDisk != null && ntfsDisk != null, "both virtual disks detected as physical drives")) return;
        var fatPart = fatDisk.Partitions.FirstOrDefault(p => p.IsFat);
        var ntfsPart = ntfsDisk.Partitions.FirstOrDefault(p => p.IsNtfs);
        Check(fatPart != null, "FAT32 partition found: " + fatPart?.Type + " " + fatPart?.FileSystemInfo);
        Check(ntfsPart != null, "NTFS partition found: " + ntfsPart?.Type + " " + ntfsPart?.FileSystemInfo);
        Check(fatPart?.DriveLetter == "Y:" && ntfsPart?.DriveLetter == "Z:", $"drive letters matched through WMI ({fatPart?.DriveLetter}, {ntfsPart?.DriveLetter})");
        Check(!disks.Any(d => d.Kind == DeviceKind.Volume && (d.Path.EndsWith("Y:") || d.Path.EndsWith("Z:"))), "volumes shown once (as partitions)");
        if (fatPart == null || ntfsPart == null) return;

        // ------------------------------------------------------------------ quick scans
        foreach (var (part, fs) in new[] { (fatPart, "fat"), (ntfsPart, "ntfs") })
        {
            Section($"Quick Scan + recovery on {fs.ToUpperInvariant()} ({part.Disk.Path}, partition {part.Number})");
            var files = RecoveryService.QuickScanAsync(part, token).Result;
            var mine = expected.Where(e => e.Fs == fs).ToList();
            Check(files.Count > 0, $"{files.Count} files listed, {files.Count(f => f.Deleted)} deleted");
            var toRecover = new List<FoundFile>();
            foreach (var e in mine)
            {
                var f = files.FirstOrDefault(x => string.Equals(x.FullPath, e.Path, StringComparison.OrdinalIgnoreCase));
                if (Check(f != null && f.Deleted == e.Deleted, $"{(e.Deleted ? "deleted" : "existing")} file listed: {e.Path}" + (f != null && fs == "ntfs" ? $" (chance: {f.Chance})" : "")) && e.Deleted)
                    toRecover.Add(f);
            }
            var dest = Path.Combine(outDir, fs);
            var res = RecoveryService.RecoverQuickFilesAsync(part, toRecover, dest, token).Result;
            Check(res.Ok == toRecover.Count && res.Failed == 0, $"recovered {res.Ok}/{toRecover.Count} deleted files");
            foreach (var e in mine.Where(x => x.Deleted))
            {
                var p = Path.Combine(dest, e.Path.TrimStart('/').Replace('/', '\\'));
                if (!Check(File.Exists(p) && Md5(p) == e.Md5, "byte-for-byte: " + e.Path) && File.Exists(p))
                {
                    var f = files.First(x => string.Equals(x.FullPath, e.Path, StringComparison.OrdinalIgnoreCase));
                    var got = File.ReadAllBytes(p);
                    Console.WriteLine($"         listed size {f.Size}, recovered {got.Length} bytes, md5 {Md5(p)}, expected {e.Md5}");
                    Console.WriteLine("         first bytes: " + BitConverter.ToString(got, 0, Math.Min(16, got.Length)));
                    int nz = got.Count(b => b != 0);
                    Console.WriteLine($"         non-zero bytes: {nz} of {got.Length}");
                }
            }
        }

        // ------------------------------------------------------------------ deep scan
        Section("Deep Scan (photorec.exe on the raw FAT32 partition, pictures only)");
        var vault = Path.Combine(outDir, "vault");
        int progressLines = 0;
        var opts = new DeepScanOptions { Disk = fatDisk, Partition = fatPart, Categories = new HashSet<FileCategory> { FileCategory.Pictures }, VaultDir = vault };
        var started = DateTime.UtcNow;
        var code = RecoveryService.DeepScanAsync(opts, p => progressLines++, Path.Combine(outDir, "stop.flag"), token).Result;
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        var carved = RecoveryService.EnumerateVault(vault, new HashSet<string>()).ToList();
        var carvedMd5 = new HashSet<string>(carved.Select(c => Md5(c.FullPath)));
        Check(code == 0, "photorec.exe exit code 0");
        // PhotoRec reports progress once per second, so a scan shorter than that has nothing to report
        Check(progressLines > 0 || seconds < 2.5, $"{progressLines} progress updates in {seconds:0.0} s");
        foreach (var e in expected.Where(x => x.Fs == "fat" && x.Path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
            Check(carvedMd5.Contains(e.Md5), "carved byte-for-byte: " + e.Path);

        // ------------------------------------------------------------------ partition analysis
        Section("Partition analysis (testdisk.exe analyze on the raw disk)");
        var analysis = RecoveryService.AnalyzePartitionsAsync(fatDisk, false, false, null, token).Result;
        var real = analysis.Found.Where(p => p.Status != 'E').ToList();
        Check(analysis.Current.Count == 1 && real.Count == 1, $"current: {analysis.Current.Count}, found: {real.Count} (+{analysis.Found.Count - real.Count} extended container)");
        Check(analysis.Missing.Count == 0, "nothing reported missing, so Write stays disabled");
        if (!Check(real.Count == 1 && real[0].StartSector == fatPart.StartSector, "found partition matches the table"))
        {
            foreach (var p in analysis.Current) Console.WriteLine($"         current: #{p.Number} {p.Status} {p.Type} start={p.StartSector} size={p.SizeSectors} [{p.Label}] {p.FileSystemInfo}");
            foreach (var p in analysis.Found) Console.WriteLine($"         found:   #{p.Number} {p.Status} {p.Type} start={p.StartSector} size={p.SizeSectors} [{p.Label}] {p.FileSystemInfo}");
            var log = analysis.RawLog ?? "";
            int at = log.IndexOf("Analyse Disk", StringComparison.Ordinal);
            Console.WriteLine("         --- testdisk.log ---");
            foreach (var line in log.Substring(Math.Max(0, at)).Split('\n').Take(60)) Console.WriteLine("         | " + line.TrimEnd('\r'));
        }
        Check(!analysis.WriteDone, "nothing was written");

        // ------------------------------------------------------------------ imaging
        Section("Byte-to-byte backup of the FAT32 partition, then Quick Scan of the image");
        var img = Path.Combine(outDir, "fat-partition.img");
        long bad = -1;
        DiskImager.CreateImage(fatDisk.Path, fatPart.StartSector * fatDisk.SectorSize, fatPart.SizeBytes, fatDisk.SectorSize, img, p => bad = p.BadSectors, token);
        Check(new FileInfo(img).Length == fatPart.SizeBytes && bad == 0, $"image is {FileTypes.FormatSize(new FileInfo(img).Length)}, {bad} unreadable sectors");
        var imgDisk = RecoveryService.OpenImageAsync(img, token).Result;
        var imgPart = imgDisk.Partitions.FirstOrDefault();
        Check(imgPart != null && imgPart.IsFat, "image opens as a FAT32 volume");
        if (imgPart != null)
        {
            var imgFiles = RecoveryService.QuickScanAsync(imgPart, token).Result;
            var want = expected.Where(e => e.Fs == "fat" && e.Deleted).ToList();
            Check(want.All(e => imgFiles.Any(f => f.Deleted && string.Equals(f.FullPath, e.Path, StringComparison.OrdinalIgnoreCase))),
                $"all {want.Count} deleted files are also found in the image");
        }
    }
}
