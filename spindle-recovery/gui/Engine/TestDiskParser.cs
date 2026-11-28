using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpindleRecovery.Engine
{
    /// <summary>Parses the text that TestDisk prints with /list and writes to testdisk.log.</summary>
    public static class TestDiskParser
    {
        // "Disk \\.\PhysicalDrive0 - 500 GB / 465 GiB - CHS 60801 255 63 (RO)"
        // "Drive E: - 16 GB / 14 GiB - CHS 1946 255 63"
        // TestDisk truncates this description to a fixed-size buffer, so with a long
        // image path the CHS geometry may be cut off; it then defaults to 255 heads, 63 sectors.
        static readonly Regex DiskLine = new Regex(@"^(?:Disk (?<path>.+?)|Drive (?<letter>[A-Z]):) - (?<size>[\d.]+ [KMGTP]?B) / [\d.]+ [KMGTP]i?B(?: \((?<bytes>\d+) bytes\))?(?: - CHS(?: (?<c>\d+) (?<h>\d+) (?<s>\d+))?)?(?<ro> \(RO\))?",
            RegexOptions.Compiled);
        static readonly Regex SectorSizeLine = new Regex(@"^Sector size:\s*(\d+)", RegexOptions.Compiled);
        static readonly Regex ModelLine = new Regex(@"^Model: (.*?)(?:, S/N:.*)?$", RegexOptions.Compiled);
        // Partition line: " 1 P FAT32 LBA      0  32 33     8  40 32     129024 [NO NAME]"
        static readonly Regex PartLine = new Regex(@"^\s*(?<num>\d+)?\s*(?<status>[*PLEDX ])\s(?<rest>.*?)\s*$", RegexOptions.Compiled);
        static readonly Regex Label = new Regex(@"\s*\[(?<label>[^\]]*)\]\s*$", RegexOptions.Compiled);

        /// <summary>Parses the output of <c>testdisk /list</c> (all disks or one image).</summary>
        public static List<DiskInfo> ParseList(IEnumerable<string> lines)
        {
            var disks = new List<DiskInfo>();
            var byPath = new Dictionary<string, DiskInfo>(StringComparer.OrdinalIgnoreCase);
            DiskInfo current = null;
            bool inTable = false;
            PartitionInfo lastPart = null;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                var m = DiskLine.Match(line);
                if (m.Success)
                {
                    var path = m.Groups["path"].Success ? m.Groups["path"].Value : @"\\.\" + m.Groups["letter"].Value + ":";
                    if (!byPath.TryGetValue(path, out current))
                    {
                        current = new DiskInfo
                        {
                            Path = path,
                            Title = m.Groups["letter"].Success ? "Drive " + m.Groups["letter"].Value + ":" : path,
                            Kind = m.Groups["letter"].Success ? DeviceKind.Volume : (path.StartsWith(@"\\.\") || path.StartsWith("/dev/") ? DeviceKind.Fixed : DeviceKind.Image),
                        };
                        byPath[path] = current;
                        disks.Add(current);
                    }
                    if (m.Groups["c"].Success)
                    {
                        current.Heads = int.Parse(m.Groups["h"].Value);
                        current.SectorsPerTrack = int.Parse(m.Groups["s"].Value);
                    }
                    current.ReadOnly = m.Groups["ro"].Success;
                    if (current.SizeBytes == 0) current.SizeBytes = ParseSize(m.Groups["size"].Value);
                    inTable = false;
                    lastPart = null;
                    continue;
                }
                if (current == null) continue;

                var ss = SectorSizeLine.Match(line);
                if (ss.Success) { current.SectorSize = int.Parse(ss.Groups[1].Value); continue; }
                var mm = ModelLine.Match(line);
                if (mm.Success) { current.Model = mm.Groups[1].Value.Trim(); continue; }

                if (line.Contains("Partition") && line.Contains("Start") && line.Contains("End"))
                {
                    inTable = true;
                    current.Partitions.Clear();
                    continue;
                }
                if (line.StartsWith("Partition table type"))
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0) current.PartitionTable = line.Substring(idx + 1).Trim();
                    continue;
                }
                if (!inTable) continue;
                if (line.Trim().Length == 0) { lastPart = null; continue; }

                var p = ParsePartitionLine(line, current);
                if (p != null)
                {
                    current.Partitions.Add(p);
                    lastPart = p;
                }
                else if (lastPart != null && line.StartsWith("     ") && line.Trim().Length > 0)
                {
                    // second line with file-system details, e.g. "     FAT32, blocksize=512"
                    lastPart.FileSystemInfo = line.Trim();
                }
            }
            return disks;
        }

        /// <summary>Parses one TestDisk partition line (CHS or LBA units). Returns null if the line is not a partition.</summary>
        public static PartitionInfo ParsePartitionLine(string line, DiskInfo disk)
        {
            var m = PartLine.Match(line);
            if (!m.Success) return null;
            var rest = m.Groups["rest"].Value;
            string label = null;
            var lm = Label.Match(rest);
            if (lm.Success)
            {
                label = lm.Groups["label"].Value;
                rest = rest.Substring(0, lm.Index);
            }
            var tokens = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            // count trailing numeric tokens
            int n = 0;
            for (int i = tokens.Length - 1; i >= 0 && IsNumber(tokens[i]); i--) n++;
            long start, size;
            int typeTokens;
            if (n >= 7)
            {
                long c = long.Parse(tokens[tokens.Length - 7]), h = long.Parse(tokens[tokens.Length - 6]), s = long.Parse(tokens[tokens.Length - 5]);
                start = (c * disk.Heads + h) * disk.SectorsPerTrack + s - 1;
                size = long.Parse(tokens[tokens.Length - 1]);
                typeTokens = tokens.Length - 7;
            }
            else if (n >= 3)
            {
                start = long.Parse(tokens[tokens.Length - 3]);
                size = long.Parse(tokens[tokens.Length - 1]);
                typeTokens = tokens.Length - 3;
            }
            else return null;
            if (typeTokens <= 0) return null;
            var type = string.Join(" ", tokens, 0, typeTokens);
            if (size <= 0) return null;
            return new PartitionInfo
            {
                Disk = disk,
                Number = m.Groups["num"].Success ? int.Parse(m.Groups["num"].Value) : 0,
                Status = m.Groups["status"].Value[0],
                Type = type,
                StartSector = start,
                SizeSectors = size,
                Label = label,
            };
        }

        static bool IsNumber(string s)
        {
            foreach (var ch in s) if (ch < '0' || ch > '9') return false;
            return s.Length > 0;
        }

        static long ParseSize(string s)
        {
            var parts = s.Split(' ');
            if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
            switch (parts[1])
            {
                case "KB": return (long)(v * 1e3);
                case "MB": return (long)(v * 1e6);
                case "GB": return (long)(v * 1e9);
                case "TB": return (long)(v * 1e12);
                case "PB": return (long)(v * 1e15);
                default: return (long)v;
            }
        }

        // "X      3 -rwxr-xr-x     0      0    141647 23-Nov-2026 07:49 /DCIM/_.JPG"
        static readonly Regex FileLine = new Regex(@"^(?<del>[X ])\s*(?<inode>\d+)\s(?<mode>[-dlbcps][-rwxsStT]{9})\s+(?<uid>\d+)\s+(?<gid>\d+)\s+(?<size>\d+)\s+(?<date>\d{1,2}-[A-Za-z]{3}-\d{4} \d{2}:\d{2})\s(?<name>.+)$",
            RegexOptions.Compiled);

        /// <summary>Parses a recursive, full-path directory listing from testdisk.log.</summary>
        public static List<FoundFile> ParseFileListing(string log, PartitionInfo partition)
        {
            var files = new List<FoundFile>();
            var seen = new HashSet<string>();
            foreach (var raw in log.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var m = FileLine.Match(line);
                if (!m.Success) continue;
                if (m.Groups["mode"].Value[0] != '-') continue; // regular files only
                var full = m.Groups["name"].Value;
                if (!full.StartsWith("/")) continue;
                if (!seen.Add(full)) continue;
                var slash = full.LastIndexOf('/');
                var name = full.Substring(slash + 1);
                var folder = slash > 0 ? full.Substring(0, slash) : "/";
                DateTime? dt = null;
                if (DateTime.TryParseExact(m.Groups["date"].Value, "d-MMM-yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    dt = d;
                files.Add(new FoundFile
                {
                    Name = name,
                    Folder = folder,
                    FullPath = full,
                    Size = long.Parse(m.Groups["size"].Value),
                    Modified = dt,
                    Deleted = m.Groups["del"].Value == "X",
                    Source = FoundSource.QuickScan,
                    Category = FileTypes.Categorize(name),
                    Extension = System.IO.Path.GetExtension(name).TrimStart('.').ToLowerInvariant(),
                    Partition = partition,
                });
            }
            return files;
        }

        /// <summary>
        /// Parses an analyse/search run: the "Current partition structure" and the
        /// list of partitions TestDisk proposes to write (after "interface_write()").
        /// </summary>
        public static PartitionTableResult ParseAnalysis(string log, DiskInfo disk)
        {
            var r = new PartitionTableResult { RawLog = log };
            List<PartitionInfo> section = null;
            foreach (var raw in log.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("Partition table type:")) r.PartitionTableType = line.Substring(21).Trim();
                else if (line.StartsWith("Partition table type (auto):")) r.PartitionTableType = line.Substring(28).Trim();
                if (line.StartsWith("Disk ") && line.Contains(" - CHS "))
                {
                    var g = Regex.Match(line, @"CHS (\d+) (\d+) (\d+)");
                    if (g.Success) { disk.Heads = int.Parse(g.Groups[2].Value); disk.SectorsPerTrack = int.Parse(g.Groups[3].Value); }
                }
                if (line.StartsWith("write!")) { r.WriteDone = true; continue; }
                if (line.StartsWith("Current partition structure:")) { section = r.Current = new List<PartitionInfo>(); continue; }
                // The last "interface_write()" block holds the partitions TestDisk would write.
                if (line.StartsWith("interface_write()")) { section = r.Found = new List<PartitionInfo>(); continue; }
                if (section == null) continue;
                if (line.Trim().Length == 0) { section = null; continue; }
                var p = ParsePartitionLine(line, disk);
                if (p != null) section.Add(p);
                else if (line.StartsWith("     ") && section.Count > 0) section[section.Count - 1].FileSystemInfo = line.Trim();
            }
            return r;
        }
    }
}
