// Tests for the TestDisk/PhotoRec output parsers, run against real engine output
// (fixtures/). Build and run with tests/run-tests.sh (Mono) — no test framework needed.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using SpindleRecovery.Engine;

static class ParserTests
{
    static int failures, passes;
    static string Fixtures;

    static void Check(bool ok, string what)
    {
        if (ok) { passes++; Console.WriteLine("  ok    " + what); }
        else { failures++; Console.WriteLine("  FAIL  " + what); }
    }

    static string[] Lines(string name) => File.ReadAllLines(Path.Combine(Fixtures, name));
    static string Text(string name) => File.ReadAllText(Path.Combine(Fixtures, name));

    static int Main(string[] args)
    {
        Fixtures = args.Length > 0 ? args[0] : "fixtures";

        Console.WriteLine("testdisk /list of an MBR image");
        var d = TestDiskParser.ParseList(Lines("list-card.txt")).Single();
        Check(d.Path == "card.img", "disk path");
        Check(d.Heads == 255 && d.SectorsPerTrack == 63, "geometry 255/63");
        Check(d.SectorSize == 512, "sector size");
        Check(d.SizeBytes == 134000000L, "size from '134 MB' (got " + d.SizeBytes + ")");
        Check(d.Partitions.Count == 1, "one partition");
        var p = d.Partitions[0];
        Check(p.Number == 1 && p.Status == 'P', "partition #1 primary");
        Check(p.Type == "FAT32 LBA", "type with space in name");
        Check(p.StartSector == 2048, "CHS 0/32/33 -> LBA 2048");
        Check(p.SizeSectors == 260096, "size in sectors");
        Check(p.Label == "CAMERA_SD", "label");
        Check(p.FileSystemInfo == "FAT32, blocksize=512", "file system detail line");
        Check(p.SupportsQuickScan, "FAT supports Quick Scan");

        Console.WriteLine("testdisk /list with a long path (description truncated before the CHS numbers)");
        d = TestDiskParser.ParseList(Lines("list-card-longpath.txt")).Single();
        Check(d.Partitions.Count == 1 && d.Partitions[0].StartSector == 2048, "partition still parsed with default geometry");

        Console.WriteLine("testdisk /list of a raw volume (no partition table)");
        d = TestDiskParser.ParseList(Lines("list-vol.txt")).Single();
        Check(d.Partitions.Count == 1 && d.Partitions[0].Number == 0, "unnumbered partition");
        Check(d.Partitions[0].Type == "FAT32" && d.Partitions[0].StartSector == 0, "type ending in a digit, start 0");
        Check(RecoveryService.BuildPhotoRecCommand(new DeepScanOptions { Disk = d, Partition = d.Partitions[0] }).StartsWith("options,"),
            "no partition number sent to PhotoRec for a raw volume");

        Console.WriteLine("Windows /list output (physical drives, GPT in sectors, drive letter)");
        var disks = TestDiskParser.ParseList(Lines("list-windows.txt"));
        Check(disks.Count == 3, "three devices");
        Check(disks[0].Path == @"\\.\PhysicalDrive0" && disks[0].Model == "Samsung SSD 970 EVO Plus 500GB", "path and model (S/N stripped)");
        Check(disks[0].Partitions.Count == 3, "three GPT partitions");
        Check(disks[0].Partitions[1].Type == "MS Data" && disks[0].Partitions[1].StartSector == 239616, "LBA units parsed");
        Check(disks[0].Partitions[1].FileSystem == "NTFS" && disks[0].Partitions[1].IsNtfs && disks[0].Partitions[1].SupportsQuickScan, "NTFS uses the built-in MFT reader for Quick Scan");
        Check(!disks[0].Partitions[0].SupportsQuickScan, "EFI partition without a detected file system: Deep Scan only");
        Check(disks[1].Partitions[0].Status == '*' && disks[1].Partitions[0].Label == "USB DRIVE", "bootable FAT32 on USB");
        Check(disks[2].Kind == DeviceKind.Volume && disks[2].Path == @"\\.\E:" && disks[2].ReadOnly, "drive letter volume, read-only flag");

        Console.WriteLine("Quick Scan listing (list,recursive,fullpathname)");
        var cardDisk = TestDiskParser.ParseList(Lines("list-card.txt")).Single();
        var files = TestDiskParser.ParseFileListing(Text("quick-card.log"), cardDisk.Partitions[0]);
        Check(files.Count == 15, "15 regular files (directories skipped)");
        Check(files.Count(f => f.Deleted) == 6, "6 deleted files");
        var thesis = files.Single(f => f.Name == "Thesis draft.docx");
        Check(thesis.Deleted && thesis.Folder == "/Documents" && thesis.FullPath == "/Documents/Thesis draft.docx", "deleted long file name keeps folder and spaces");
        Check(thesis.Category == FileCategory.Documents && thesis.Size == 20254, "category and size");
        Check(files.Single(f => f.Name == "Track01.mp3").Category == FileCategory.Audio, "audio category");
        Check(files.All(f => f.Modified.HasValue), "dates parsed");

        Console.WriteLine("Partition analysis (analyze,search) of a disk whose table was wiped");
        var lostDisk = new DiskInfo { Path = "lostpart.img" };
        var r = TestDiskParser.ParseAnalysis(Text("analyze-lost.log"), lostDisk);
        Check(r.Current.Count == 0, "current table is empty");
        Check(r.Found.Count == 1, "one partition found");
        Check(r.Found[0].Type == "FAT32" && r.Found[0].StartSector == 2048 && r.Found[0].Label == "CAMERA_SD", "found partition details");
        Check(r.PartitionTableType == "Intel", "partition table type");
        Check(!r.WriteDone, "nothing written");
        Check(r.Missing.Count == 1, "the lost partition is reported missing (Write enabled)");

        Console.WriteLine("Partition analysis of a healthy Windows-formatted FAT32 disk (captured on Windows CI)");
        var winDisk = new DiskInfo { Path = @"\\.\PhysicalDrive2" };
        var w = TestDiskParser.ParseAnalysis(Text("analyze-windows-fat32.log"), winDisk);
        Check(w.Current.Count == 1 && w.Current[0].StartSector == 128, "current FAT32 partition at sector 128");
        Check(w.Found.Count == 2 && w.Found[0].Status == 'E' && w.Found[1].Status == 'L', "TestDisk proposes extended + logical");
        Check(w.Missing.Count == 0, "extended container is not counted as a lost partition (Write stays disabled)");

        Console.WriteLine("PhotoRec command line");
        var opts = new DeepScanOptions
        {
            Disk = cardDisk, Partition = cardDisk.Partitions[0], FreeSpaceOnly = true,
            Categories = new HashSet<FileCategory> { FileCategory.Pictures },
        };
        var cmd = RecoveryService.BuildPhotoRecCommand(opts);
        Check(cmd.StartsWith("1,options,paranoid,keep_corrupted_file_no,fileopt,everything,disable,"), "partition, options, file selection");
        Check(cmd.Contains("jpg,enable") && !cmd.Contains("pdf,enable"), "only picture formats enabled");
        Check(cmd.EndsWith(",freespace,search"), "free space only");
        opts.Partition = null; opts.Categories = null;
        Check(RecoveryService.BuildPhotoRecCommand(opts) == "partition_none,options,paranoid,keep_corrupted_file_no,wholespace,search", "whole device, every format");

        Console.WriteLine("File types");
        var known = new HashSet<string>(Lines("photorec-formats.txt").Select(l => l.Trim()).Where(l => l.Length > 0));
        var unknown = FileTypes.Formats.SelectMany(f => f.Families).Where(f => !known.Contains(f)).Distinct().ToList();
        Check(unknown.Count == 0, "every format family exists in PhotoRec 7.2" + (unknown.Count > 0 ? ": missing " + string.Join(", ", unknown) : ""));
        Check(FileTypes.DisplayOrder.Length == Enum.GetValues(typeof(FileCategory)).Length &&
              FileTypes.DisplayOrder.All(c => FileTypes.Formats.Any(f => f.Category == c)), "every category is shown and has formats");
        Check(FileTypes.Formats.Select(f => f.Id).Distinct().Count() == FileTypes.Formats.Length, "format ids are unique");
        var misfiled = FileTypes.Formats.SelectMany(f => f.Extensions.Where(e => FileTypes.Categorize("x." + e) != f.Category).Select(e => f.Id + ":" + e)).ToList();
        Check(misfiled.Count == 0, "every format's extensions belong to its category" + (misfiled.Count > 0 ? ": " + string.Join(", ", misfiled) : ""));
        Check(FileTypes.Categorize("Inbox.pst") == FileCategory.Email && FileTypes.Categorize("archive.mbox") == FileCategory.Email, "Outlook PST and MBOX are Email");
        Check(FileTypes.Categorize("Thesis.docx") == FileCategory.Documents && FileTypes.Categorize("IMG_0001.HEIC") == FileCategory.Pictures &&
              FileTypes.Categorize("clip.MTS") == FileCategory.Video && FileTypes.Categorize("song.m4a") == FileCategory.Audio, "common extensions categorised");
        Check(FileTypes.DisplayName(FileCategory.Audio) == "Music" && FileTypes.DisplayName(FileCategory.Video) == "Videos", "friendly names");
        Check(FileTypes.Describe(new[] { FileCategory.Video, FileCategory.Pictures }) == "Pictures, Videos", "chosen types listed in display order");
        var emailCmd = RecoveryService.BuildPhotoRecCommand(new DeepScanOptions { Disk = cardDisk, Partition = cardDisk.Partitions[0], Categories = new HashSet<FileCategory> { FileCategory.Email } });
        Check(emailCmd.Contains("pst,enable") && emailCmd.Contains("tx?,enable") && !emailCmd.Contains("jpg,enable"), "Email-only Deep Scan enables mail formats only");
        Check(!RecoveryService.BuildPhotoRecCommand(new DeepScanOptions { Disk = cardDisk, Categories = FileTypes.AllCategories() }).Contains("fileopt"),
            "all types chosen: PhotoRec searches every format");

        Console.WriteLine("Format picker selection");
        Func<string, FoundFile> F = name => new FoundFile { Name = name, Extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant(), Category = FileTypes.Categorize(name) };
        var photos = new FileSelection(new[] { "jpg", "heic" });
        Check(photos.Families.SequenceEqual(new[] { "jpg", "mov" }), "JPEG + HEIC enable PhotoRec's jpg and mov families");
        Check(photos.Includes(F("IMG_0001.JPG")) && photos.Includes(F("IMG_0002.heic")), "chosen formats are kept");
        Check(!photos.Includes(F("logo.png")) && !photos.Includes(F("clip.mp4")) && !photos.Includes(F("notes.txt")),
            "other formats are left out, even ones PhotoRec finds through the same family (MP4 via mov)");
        Check(photos.Describe() == "Pictures (JPEG photos, HEIC / HEIF (iPhone))", "summary names the chosen formats: " + photos.Describe());
        var pics = FileSelection.FromCategories(new[] { FileCategory.Pictures });
        Check(pics.IsWholeCategory(FileCategory.Pictures) && pics.Includes(F("drawing.xcf")) && !pics.Includes(F("song.mp3")),
            "a whole category keeps every file in it, including extensions not in the picker");
        Check(pics.Describe() == "Pictures", "whole category summary");
        var jpgCmd = RecoveryService.BuildPhotoRecCommand(new DeepScanOptions { Disk = cardDisk, Partition = cardDisk.Partitions[0], Selection = new FileSelection(new[] { "jpg" }) });
        Check(jpgCmd.Contains(",disable,jpg,enable,") && !jpgCmd.Contains("png,enable"), "JPEG-only Deep Scan: " + jpgCmd);
        Check(!RecoveryService.BuildPhotoRecCommand(new DeepScanOptions { Disk = cardDisk, Selection = FileSelection.All() }).Contains("fileopt"),
            "everything chosen: PhotoRec searches all 480+ formats");

        Console.WriteLine("Argument quoting");
        Check(EngineHost.JoinArgs(new[] { "/d", @"C:\My Files\recup_dir", "/cmd", @"\\.\PhysicalDrive1", "search" })
              == @"/d ""C:\My Files\recup_dir"" /cmd \\.\PhysicalDrive1 search", "paths with spaces quoted, device path untouched");
        Check(EngineHost.JoinArgs(new[] { @"C:\dir with space\" }) == @"""C:\dir with space\\""", "trailing backslash doubled inside quotes");

        NtfsTests();
        FormattedNtfsTests();
        ScanStoreTests();

        Console.WriteLine();
        Console.WriteLine($"{passes} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    static string Md5(byte[] b) { using (var m = MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

    static string ExtractNtfsImage()
    {
        var img = Path.Combine(Path.GetTempPath(), "spindle-ntfs-" + Guid.NewGuid().ToString("N") + ".img");
        using (var gz = new GZipStream(File.OpenRead(Path.Combine(Fixtures, "ntfs-small.img.gz")), CompressionMode.Decompress))
        using (var o = File.Create(img)) gz.CopyTo(o);
        return img;
    }

    // ntfs-small.img: NTFS volume (no partition table) where ntfs-3g deleted the folder /Photos/Trip
    // (2 pictures), a PDF with a non-ASCII name, a resident 14-byte note and a 2-fragment file.
    static void NtfsTests()
    {
        Console.WriteLine("NTFS Master File Table reader");
        var img = ExtractNtfsImage();
        try
        {
            using (var dev = new SectorReader(img))
            {
                var vol = NtfsVolume.Open(dev, 0);
                Check(vol.ClusterSize == 1024 && vol.RecordSize == 1024, "boot sector geometry");
                var all = vol.Scan(null, CancellationToken.None);
                var deleted = all.Where(e => !e.InUse).ToList();
                Check(all.Any(e => e.InUse && e.Path == "/Docs/keep.txt"), "existing file listed");
                Check(!all.Any(e => e.Path.StartsWith("/$")), "NTFS metadata files hidden");
                var expected = new Dictionary<string, string>
                {
                    ["/Photos/Trip/IMG_0042.JPG"] = "a9709e615ccda0ad8329718658dfa3a8",
                    ["/Photos/Trip/beach.png"] = "e0cff21e93055dc137bb9b89bd112de0",
                    ["/Docs/Facture \u00e9t\u00e9.pdf"] = "05ae992df52ea0b8f5c31c0b30aa69d7",
                    ["/Docs/note.txt"] = "04c3f7e3c46477b77fae6e4e17b8fa9a",
                    ["/Docs/frag.dat"] = "e9943b589559b8aea02adaa8ac55c3f3",
                };
                foreach (var kv in expected)
                {
                    var e = deleted.FirstOrDefault(x => x.Path == kv.Key);
                    if (e == null) { Check(false, "deleted file found: " + kv.Key); continue; }
                    var ms = new MemoryStream();
                    vol.CopyTo(e, ms, CancellationToken.None);
                    Check(Md5(ms.ToArray()) == kv.Value, "recovered byte-for-byte: " + kv.Key + (e.Runs == null ? " (resident)" : $" ({e.Runs.Count} run(s))"));
                }
                Check(deleted.Single(x => x.Path == "/Docs/frag.dat").Runs.Count >= 2, "fragmented file has several runs");
                Check(deleted.Where(x => x.Path.StartsWith("/Photos/")).All(x => x.Chance == RecoveryChance.Good), "recovery chance: good (space not reused)");
                var head = vol.ReadHead(deleted.Single(x => x.Path.EndsWith("IMG_0042.JPG")), 3);
                Check(head.Length == 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF, "preview reads the JPEG header");
            }
        }
        finally { File.Delete(img); }
    }

    static string ExtractFixture(string gz)
    {
        var img = Path.Combine(Path.GetTempPath(), "spindle-" + Guid.NewGuid().ToString("N") + ".img");
        using (var g = new GZipStream(File.OpenRead(Path.Combine(Fixtures, gz)), CompressionMode.Decompress))
        using (var o = File.Create(img)) g.CopyTo(o);
        return img;
    }

    // ntfs-formatted.img: an NTFS volume with two folders of files that was then quick-formatted
    // (mkntfs -Q), so the current file system is empty but the old MFT records are still on disk.
    static void FormattedNtfsTests()
    {
        Console.WriteLine("Folder rebuild after a quick format (old MFT records)");
        var expected = new Dictionary<string, string>
        {
            ["/Photos/Wedding 2024/IMG_0000.jpg"] = "a5fcf6debbdcc01aaa4dc9dfb5c8ae5a",
            ["/Photos/Wedding 2024/IMG_0001.jpg"] = "d1257603c6ddfe4d025ffbd1704c1997",
            ["/Photos/Wedding 2024/IMG_0002.jpg"] = "127228f69f29a44df329110a44420774",
            ["/Photos/Wedding 2024/IMG_0003.jpg"] = "729c2563079562376d6e75b69f7116cf",
            ["/Photos/Wedding 2024/IMG_0004.jpg"] = "33524888fb81b58c89ed064d36d48934",
            ["/Photos/Wedding 2024/IMG_0005.jpg"] = "7a9de0c88391d4fa1791f1ffa40f196f",
            ["/Work/Reports/Q1 report.txt"] = "21a7bcccf0205e7f7555f3f714d8e3b5",
            ["/Work/Reports/Q2 report.txt"] = "1e6d5ed61979ec068b9fa863984b38a6",
            ["/Work/Reports/Q3 report.txt"] = "61fb16dee9dd6e610be2a278cdea39a1",
        };
        var img = ExtractFixture("ntfs-formatted.img.gz");
        var home = Path.Combine(Path.GetTempPath(), "spindle-fmt-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", home);
        try
        {
            long len = new FileInfo(img).Length;
            using (var dev = new SectorReader(img))
            {
                var vol = NtfsVolume.Open(dev, 0, len);
                Check(vol.Scan(null, CancellationToken.None).Count == 0, "the current (new) file system is empty");
            }
            var disk = new DiskInfo { Path = img, Title = "formatted.img", Kind = DeviceKind.Image, SectorSize = 512 };
            var part = new PartitionInfo { Disk = disk, Number = 0, Status = 'P', Type = "NTFS", FileSystemInfo = "NTFS", SizeSectors = len / 512 };
            disk.Partitions.Add(part);
            var lost = RecoveryService.NtfsLostRecordsAsync(part, CancellationToken.None).Result;
            Check(expected.Keys.All(k => lost.Any(f => f.FullPath == k)), $"all {expected.Count} files found with their original folders ({lost.Count} lost records)");
            Check(lost.All(f => f.Source == FoundSource.LostRecords && f.Deleted && f.StatusText.StartsWith("Lost")), "marked as lost after format");
            var dest = Path.Combine(home, "out");
            var res = RecoveryService.RecoverQuickFilesAsync(part, lost.Where(f => expected.ContainsKey(f.FullPath)), dest, CancellationToken.None).Result;
            Check(res.Ok == expected.Count && res.Failed == 0, $"recovered {res.Ok}/{expected.Count}");
            Check(expected.All(kv => File.Exists(Path.Combine(dest, kv.Key.TrimStart('/'))) &&
                                     Md5(File.ReadAllBytes(Path.Combine(dest, kv.Key.TrimStart('/')))) == kv.Value),
                  "byte-for-byte, in their original folders");

            // the primary boot sector is destroyed: the backup copy at the end of the volume is used
            using (var fs = new FileStream(img, FileMode.Open, FileAccess.Write)) fs.Write(new byte[4096], 0, 4096);
            using (var dev = new SectorReader(img))
            {
                var vol = NtfsVolume.Open(dev, 0, len);
                Check(vol.UsedBackupBootSector, "damaged boot sector: the backup boot sector is used");
            }
            // and the volume is RAW (backup boot sector gone too): default geometry still finds the files
            using (var fs = new FileStream(img, FileMode.Open, FileAccess.Write)) { fs.Position = len - 1024; fs.Write(new byte[1024], 0, 1024); }
            var raw = RecoveryService.NtfsLostRecordsAsync(part, CancellationToken.None).Result;
            Check(expected.Keys.All(k => raw.Any(f => f.FullPath == k)), "RAW volume (both boot sectors gone): files and folders still rebuilt");
            using (var dev = new SectorReader(img))
            {
                var vol = NtfsVolume.OpenOrAssume(dev, 0, len);
                vol.CarveRecords(len, null, CancellationToken.None);
                Check(vol.ClusterSize == 1024 && !vol.GeometryAssumed, "RAW volume: 1 KiB cluster size worked out from the $MFT record (got " + vol.ClusterSize + ")");
            }
            var dest2 = Path.Combine(home, "raw");
            var res2 = RecoveryService.RecoverQuickFilesAsync(part, raw.Where(f => expected.ContainsKey(f.FullPath)), dest2, CancellationToken.None).Result;
            Check(res2.Ok == expected.Count && expected.All(kv => Md5(File.ReadAllBytes(Path.Combine(dest2, kv.Key.TrimStart('/')))) == kv.Value),
                  "RAW volume: contents recovered byte-for-byte");
        }
        finally
        {
            File.Delete(img);
            try { Directory.Delete(home, true); } catch { }
        }
    }

    static void ScanStoreTests()
    {
        Console.WriteLine("Saved scans (save, list, reload)");
        var home = Path.Combine(Path.GetTempPath(), "spindle-store-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", home); // where Mono puts LocalApplicationData
        var img = ExtractNtfsImage();
        try
        {
            var disk = new DiskInfo { Path = img, Title = "test.img", Kind = DeviceKind.Image, SectorSize = 512 };
            var part = new PartitionInfo { Disk = disk, Number = 0, Status = 'P', Type = "NTFS", FileSystemInfo = "NTFS", Label = "TESTVOL", SizeSectors = 12288 };
            disk.Partitions.Add(part);
            List<FoundFile> files;
            using (var dev = new SectorReader(img))
                files = NtfsVolume.Open(dev, 0).Scan(null, CancellationToken.None).Select(e => new FoundFile
                {
                    Name = e.Name, FullPath = e.Path, Folder = Path.GetDirectoryName(e.Path), Size = e.Size, Deleted = !e.InUse,
                    Source = FoundSource.QuickScan, Partition = part, Ntfs = e, Chance = e.Chance, Modified = e.Modified,
                }).ToList();
            var id = ScanStore.Save(disk, part, null, files, false);
            var meta = ScanStore.List().FirstOrDefault(m => m.Id == id);
            Check(meta != null && meta.Files == files.Count && meta.Deleted == files.Count(f => f.Deleted), "listed with counts");
            var r = ScanStore.Load(id);
            Check(r.files.Count == files.Count && r.scanned != null && r.scanned.Label == "TESTVOL", "reloaded files and partition");
            var note = r.files.Single(f => f.FullPath == "/Docs/frag.dat");
            Check(note.Ntfs != null && note.Ntfs.Runs.Count >= 2 && note.Chance == RecoveryChance.Good, "NTFS runs and rating survive the round trip");
            var dest = Path.Combine(home, "out");
            var res = RecoveryService.RecoverQuickFilesAsync(r.scanned, r.files.Where(f => f.Deleted), dest, CancellationToken.None).Result;
            Check(res.Ok == 7 && res.Failed == 0, "recovery from a reloaded scan (" + res.Ok + " ok)");
            Check(Md5(File.ReadAllBytes(Path.Combine(dest, "Docs", "frag.dat"))) == "e9943b589559b8aea02adaa8ac55c3f3", "reloaded scan recovers byte-for-byte");
            ScanStore.Delete(id);
            Check(!ScanStore.List().Any(m => m.Id == id), "deleted");
        }
        finally
        {
            File.Delete(img);
            try { Directory.Delete(home, true); } catch { }
        }
    }
}
