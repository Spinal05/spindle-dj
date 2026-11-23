// Tests for the TestDisk/PhotoRec output parsers, run against real engine output
// (fixtures/). Build and run with tests/run-tests.sh (Mono) — no test framework needed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Check(disks[0].Partitions[1].FileSystem == "NTFS" && !disks[0].Partitions[1].SupportsQuickScan, "NTFS uses Deep Scan");
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

        Console.WriteLine("Argument quoting");
        Check(EngineHost.JoinArgs(new[] { "/d", @"C:\My Files\recup_dir", "/cmd", @"\\.\PhysicalDrive1", "search" })
              == @"/d ""C:\My Files\recup_dir"" /cmd \\.\PhysicalDrive1 search", "paths with spaces quoted, device path untouched");
        Check(EngineHost.JoinArgs(new[] { @"C:\dir with space\" }) == @"""C:\dir with space\\""", "trailing backslash doubled inside quotes");

        Console.WriteLine();
        Console.WriteLine($"{passes} passed, {failures} failed");
        return failures == 0 ? 0 : 1;
    }
}
