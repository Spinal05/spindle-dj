using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SpindleRecovery.Engine
{
    /// <summary>
    /// Saves finished scans so their results can be reopened later without scanning again.
    /// Each scan is stored as &lt;id&gt;.scan.gz (compressed JSON) plus a small &lt;id&gt;.meta.json for listing.
    /// </summary>
    public static class ScanStore
    {
        [DataContract] public class PartDto
        {
            [DataMember] public int Number; [DataMember] public string Status; [DataMember] public string Type;
            [DataMember] public long Start; [DataMember] public long Size; [DataMember] public string Label;
            [DataMember] public string FsInfo; [DataMember] public string Letter;
        }

        [DataContract] public class FileDto
        {
            [DataMember] public string Name; [DataMember] public string Folder; [DataMember] public string Path;
            [DataMember] public long Size; [DataMember] public long ModifiedTicks; [DataMember] public bool Deleted;
            [DataMember] public int Source; [DataMember] public int Category; [DataMember] public string Ext;
            [DataMember] public int Chance; [DataMember] public int Part = -1;
            // NTFS record data needed to recover the file later
            [DataMember] public long[] Runs; [DataMember] public string Resident; [DataMember] public long InitSize;
            [DataMember] public bool Compressed; [DataMember] public bool Encrypted; [DataMember] public double Overwritten;
        }

        [DataContract] public class SavedScan
        {
            [DataMember] public int Version = 1;
            [DataMember] public string Id;
            [DataMember] public DateTime Created;
            [DataMember] public string Title;
            [DataMember] public bool Stopped;
            [DataMember] public string DiskPath; [DataMember] public string DiskTitle; [DataMember] public string DiskModel;
            [DataMember] public int DiskKind; [DataMember] public long DiskSize; [DataMember] public int SectorSize;
            [DataMember] public int Heads; [DataMember] public int Spt;
            [DataMember] public List<PartDto> Partitions = new List<PartDto>();
            [DataMember] public int ScannedPartition = -1;
            [DataMember] public string VaultDir;
            [DataMember] public List<FileDto> Files = new List<FileDto>();
        }

        [DataContract] public class Meta
        {
            [DataMember] public string Id; [DataMember] public DateTime Created; [DataMember] public string Title;
            [DataMember] public int Files; [DataMember] public int Deleted; [DataMember] public long Bytes; [DataMember] public bool Stopped;
            public override string ToString() => Title;
        }

        public static string Dir
        {
            get
            {
                var d = System.IO.Path.Combine(EngineHost.AppDataDir, "scans");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string Save(DiskInfo disk, PartitionInfo scanned, string vaultDir, IList<FoundFile> files, bool stopped)
        {
            var s = new SavedScan
            {
                Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"),
                Created = DateTime.Now,
                Title = (scanned != null ? scanned.DisplayName + " on " : "") + disk.Title,
                Stopped = stopped,
                DiskPath = disk.Path, DiskTitle = disk.Title, DiskModel = disk.Model, DiskKind = (int)disk.Kind,
                DiskSize = disk.SizeBytes, SectorSize = disk.SectorSize, Heads = disk.Heads, Spt = disk.SectorsPerTrack,
                VaultDir = vaultDir,
            };
            var partIndex = new Dictionary<PartitionInfo, int>();
            foreach (var p in disk.Partitions)
            {
                partIndex[p] = s.Partitions.Count;
                s.Partitions.Add(new PartDto { Number = p.Number, Status = p.Status.ToString(), Type = p.Type, Start = p.StartSector, Size = p.SizeSectors, Label = p.Label, FsInfo = p.FileSystemInfo, Letter = p.DriveLetter });
            }
            if (scanned != null && partIndex.TryGetValue(scanned, out var si)) s.ScannedPartition = si;
            foreach (var f in files)
            {
                var d = new FileDto
                {
                    Name = f.Name, Folder = f.Folder, Path = f.FullPath, Size = f.Size, ModifiedTicks = f.Modified?.Ticks ?? 0,
                    Deleted = f.Deleted, Source = (int)f.Source, Category = (int)f.Category, Ext = f.Extension, Chance = (int)f.Chance,
                };
                if (f.Partition != null && partIndex.TryGetValue(f.Partition, out var pi)) d.Part = pi;
                if (f.Ntfs != null)
                {
                    var e = f.Ntfs;
                    d.Runs = e.Runs != null ? e.Runs.SelectMany(r => new[] { r.Vcn, r.Lcn, r.Length }).ToArray() : new long[0];
                    if (e.ResidentData != null) d.Resident = Convert.ToBase64String(e.ResidentData);
                    d.InitSize = e.InitializedSize; d.Compressed = e.Compressed; d.Encrypted = e.Encrypted; d.Overwritten = e.OverwrittenFraction;
                }
                s.Files.Add(d);
            }
            var path = System.IO.Path.Combine(Dir, s.Id + ".scan.gz");
            using (var fs = File.Create(path))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
                new DataContractJsonSerializer(typeof(SavedScan)).WriteObject(gz, s);
            var meta = new Meta
            {
                Id = s.Id, Created = s.Created, Title = s.Title, Files = files.Count, Stopped = stopped,
                Deleted = files.Count(f => f.Deleted), Bytes = files.Sum(f => f.Size),
            };
            using (var fs = File.Create(System.IO.Path.Combine(Dir, s.Id + ".meta.json")))
                new DataContractJsonSerializer(typeof(Meta)).WriteObject(fs, meta);
            return s.Id;
        }

        public static List<Meta> List()
        {
            var list = new List<Meta>();
            foreach (var f in Directory.GetFiles(Dir, "*.meta.json"))
            {
                try
                {
                    using (var fs = File.OpenRead(f))
                        list.Add((Meta)new DataContractJsonSerializer(typeof(Meta)).ReadObject(fs));
                }
                catch { /* skip unreadable entries */ }
            }
            return list.OrderByDescending(m => m.Created).ToList();
        }

        public static void Delete(string id)
        {
            foreach (var ext in new[] { ".scan.gz", ".meta.json" })
            {
                var p = System.IO.Path.Combine(Dir, id + ext);
                if (File.Exists(p)) File.Delete(p);
            }
        }

        /// <summary>Loads a saved scan and rebuilds the disk, partitions and found files.</summary>
        public static (DiskInfo disk, PartitionInfo scanned, string vault, List<FoundFile> files, SavedScan raw) Load(string id)
        {
            SavedScan s;
            using (var fs = File.OpenRead(System.IO.Path.Combine(Dir, id + ".scan.gz")))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                s = (SavedScan)new DataContractJsonSerializer(typeof(SavedScan)).ReadObject(gz);
            var disk = new DiskInfo
            {
                Path = s.DiskPath, Title = s.DiskTitle, Model = s.DiskModel, Kind = (DeviceKind)s.DiskKind, SizeBytes = s.DiskSize,
                SectorSize = s.SectorSize > 0 ? s.SectorSize : 512, Heads = s.Heads, SectorsPerTrack = s.Spt,
            };
            foreach (var p in s.Partitions)
                disk.Partitions.Add(new PartitionInfo
                {
                    Disk = disk, Number = p.Number, Status = string.IsNullOrEmpty(p.Status) ? ' ' : p.Status[0], Type = p.Type,
                    StartSector = p.Start, SizeSectors = p.Size, Label = p.Label, FileSystemInfo = p.FsInfo, DriveLetter = p.Letter,
                });
            var files = new List<FoundFile>(s.Files.Count);
            foreach (var d in s.Files)
            {
                var f = new FoundFile
                {
                    Name = d.Name, Folder = d.Folder, FullPath = d.Path, Size = d.Size,
                    Modified = d.ModifiedTicks > 0 ? new DateTime(d.ModifiedTicks) : (DateTime?)null,
                    Deleted = d.Deleted, Source = (FoundSource)d.Source, Category = (FileCategory)d.Category, Extension = d.Ext,
                    Chance = (RecoveryChance)d.Chance,
                    Partition = d.Part >= 0 && d.Part < disk.Partitions.Count ? disk.Partitions[d.Part] : null,
                };
                if (d.Runs != null || d.Resident != null)
                {
                    var e = new NtfsEntry
                    {
                        Name = d.Name, Path = d.Path, Size = d.Size, InitializedSize = d.InitSize, InUse = !d.Deleted,
                        Compressed = d.Compressed, Encrypted = d.Encrypted, OverwrittenFraction = d.Overwritten, Chance = f.Chance,
                        ResidentData = d.Resident != null ? Convert.FromBase64String(d.Resident) : null,
                    };
                    if (d.Runs != null)
                    {
                        e.Runs = new List<DataRun>();
                        for (int i = 0; i + 2 < d.Runs.Length; i += 3) e.Runs.Add(new DataRun(d.Runs[i], d.Runs[i + 1], d.Runs[i + 2]));
                    }
                    f.Ntfs = e;
                }
                files.Add(f);
            }
            var scanned = s.ScannedPartition >= 0 && s.ScannedPartition < disk.Partitions.Count ? disk.Partitions[s.ScannedPartition] : null;
            return (disk, scanned, s.VaultDir, files, s);
        }
    }
}
