using System;
using System.Collections.Generic;

namespace SpindleRecovery.Engine
{
    public enum DeviceKind { Fixed, Removable, Volume, Image }

    /// <summary>A disk, volume or image file that can be scanned.</summary>
    public class DiskInfo
    {
        public string Path;            // \\.\PhysicalDrive0, \\.\E:, or C:\images\card.img
        public string Title;           // "Samsung SSD 970 EVO" or "Drive E:"
        public string Model;
        public long SizeBytes;
        public int SectorSize = 512;
        public int Heads = 255, SectorsPerTrack = 63;
        public bool ReadOnly;
        public DeviceKind Kind;
        public string Interface;       // USB, SCSI, ...
        public string PartitionTable;  // Intel/MBR, EFI GPT, None
        public List<PartitionInfo> Partitions = new List<PartitionInfo>();

        public override string ToString() => Title;
    }

    public class PartitionInfo
    {
        public DiskInfo Disk;
        public int Number;             // TestDisk order number, 0 when none (raw volume)
        public char Status;            // P primary, * bootable, L logical, E extended, D deleted
        public string Type;            // "FAT32 LBA", "HPFS - NTFS", ...
        public long StartSector;
        public long SizeSectors;
        public string Label;
        public string FileSystemInfo;  // "FAT32, blocksize=4096"
        public string DriveLetter;     // "E:" when mounted (Windows)

        public long SizeBytes => SizeSectors * (Disk?.SectorSize ?? 512);

        public string FileSystem
        {
            get
            {
                if (!string.IsNullOrEmpty(FileSystemInfo))
                {
                    var comma = FileSystemInfo.IndexOf(',');
                    return (comma > 0 ? FileSystemInfo.Substring(0, comma) : FileSystemInfo).Trim();
                }
                return Type;
            }
        }

        public bool IsNtfs => ((Type ?? "") + " " + (FileSystemInfo ?? "")).ToUpperInvariant().Contains("NTFS");

        public bool IsFat
        {
            get
            {
                var t = ((Type ?? "") + " " + (FileSystemInfo ?? "")).ToUpperInvariant();
                return t.Contains("FAT") && !t.Contains("NTFS");
            }
        }

        /// <summary>Quick Scan (original names and folders): FAT/exFAT via TestDisk, NTFS via the built-in MFT reader.</summary>
        public bool SupportsQuickScan => IsFat || IsNtfs;

        public string DisplayName
        {
            get
            {
                var name = !string.IsNullOrEmpty(Label) && Label != "NO NAME" ? Label : (DriveLetter != null ? "Local Disk" : "Partition " + Number);
                return DriveLetter != null ? $"{name} ({DriveLetter})" : name;
            }
        }
    }

    public enum FoundSource { QuickScan, DeepScan }

    public enum FileCategory { Pictures, Video, Audio, Documents, Archives, Other }

    /// <summary>A file found by a scan.</summary>
    public class FoundFile
    {
        public string Name;
        public string Folder;          // original folder for quick scan, "Reconstructed\jpg" for deep scan
        public string FullPath;        // path as TestDisk lists it (quick scan) / file on disk (deep scan)
        public long Size;
        public DateTime? Modified;
        public bool Deleted;
        public FoundSource Source;
        public FileCategory Category;
        public string Extension;
        public bool Checked;
        public PartitionInfo Partition; // quick scan origin
        public RecoveryChance Chance;   // estimated from the file system's free-space map (NTFS)
        public NtfsEntry Ntfs;          // MFT record, for NTFS quick-scan results

        public string ChanceText
        {
            get
            {
                switch (Chance)
                {
                    case RecoveryChance.Good: return "good";
                    case RecoveryChance.PartlyOverwritten: return "partly overwritten";
                    case RecoveryChance.Overwritten: return "overwritten";
                    case RecoveryChance.Unsupported: return "compressed/encrypted";
                    default: return null;
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (Source == FoundSource.DeepScan) return "Reconstructed";
                if (!Deleted) return "Existing";
                var c = ChanceText;
                return c == null ? "Deleted" : "Deleted · " + c;
            }
        }
    }

    public class PartitionTableResult
    {
        public List<PartitionInfo> Current = new List<PartitionInfo>();
        public List<PartitionInfo> Found = new List<PartitionInfo>();
        public string PartitionTableType;
        public bool WriteDone;
        public string RawLog;
    }
}
