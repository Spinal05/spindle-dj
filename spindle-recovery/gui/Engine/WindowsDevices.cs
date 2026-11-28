using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace SpindleRecovery.Engine
{
    /// <summary>Adds friendly names, bus type and drive letters (via WMI) to the disks TestDisk detected.</summary>
    public static class WindowsDevices
    {
        class Letter { public string Id, Label, Fs; public long Offset; public int DiskIndex; }

        public static void Enrich(List<DiskInfo> disks)
        {
            var letters = new List<Letter>();
            var drives = new Dictionary<int, ManagementObject>();

            using (var s = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                foreach (ManagementObject d in s.Get())
                {
                    int idx = Convert.ToInt32(d["Index"]);
                    drives[idx] = d;
                    foreach (ManagementObject part in d.GetRelated("Win32_DiskPartition"))
                    {
                        long offset = Convert.ToInt64(part["StartingOffset"] ?? 0L);
                        foreach (ManagementObject ld in part.GetRelated("Win32_LogicalDisk"))
                            letters.Add(new Letter
                            {
                                Id = (string)ld["DeviceID"],
                                Label = ld["VolumeName"] as string,
                                Fs = ld["FileSystem"] as string,
                                Offset = offset,
                                DiskIndex = idx,
                            });
                    }
                }

            var claimedLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var disk in disks)
            {
                var m = Regex.Match(disk.Path, @"PhysicalDrive(\d+)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                int idx = int.Parse(m.Groups[1].Value);
                if (drives.TryGetValue(idx, out var d))
                {
                    var model = (d["Model"] as string ?? "").Trim();
                    if (model.Length > 0) { disk.Model = model; disk.Title = model; }
                    disk.Interface = d["InterfaceType"] as string;
                    var media = d["MediaType"] as string ?? "";
                    if (string.Equals(disk.Interface, "USB", StringComparison.OrdinalIgnoreCase) ||
                        media.IndexOf("Removable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        media.IndexOf("External", StringComparison.OrdinalIgnoreCase) >= 0)
                        disk.Kind = DeviceKind.Removable;
                    try { var size = Convert.ToInt64(d["Size"]); if (size > disk.SizeBytes) disk.SizeBytes = size; } catch { }
                }
                foreach (var p in disk.Partitions)
                {
                    var l = letters.FirstOrDefault(x => x.DiskIndex == idx && x.Offset == p.StartSector * disk.SectorSize);
                    if (l == null) continue;
                    p.DriveLetter = l.Id;
                    if (string.IsNullOrEmpty(p.Label) && !string.IsNullOrEmpty(l.Label)) p.Label = l.Label;
                    if (string.IsNullOrEmpty(p.FileSystemInfo) && !string.IsNullOrEmpty(l.Fs)) p.FileSystemInfo = l.Fs;
                    claimedLetters.Add(l.Id);
                }
            }

            // "Drive E:" entries already shown as a partition of a physical disk are redundant.
            disks.RemoveAll(x => x.Kind == DeviceKind.Volume && claimedLetters.Contains(x.Path.Substring(4, 2)));
            foreach (var v in disks.Where(x => x.Kind == DeviceKind.Volume))
            {
                var letter = v.Path.Substring(4, 2);
                v.Title = "Drive " + letter;
                foreach (var p in v.Partitions) p.DriveLetter = letter;
            }
        }
    }
}
