using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SpindleRecovery.Engine
{
    /// <summary>Random-access, sector-aligned reads from a disk, partition device or image file. Read-only.</summary>
    public sealed class SectorReader : IDisposable
    {
        readonly Stream stream;
        readonly int align;
        public SectorReader(string path, int sectorSize = 512)
        {
            stream = DiskImager.OpenSource(path);
            align = sectorSize > 0 ? sectorSize : 512;
        }

        /// <summary>Reads <paramref name="count"/> bytes at <paramref name="offset"/>; bytes past the end read as zero.</summary>
        public byte[] Read(long offset, int count)
        {
            long start = offset / align * align;
            long end = (offset + count + align - 1) / align * align;
            var buf = new byte[end - start];
            lock (stream)
            {
                stream.Position = start;
                int got = 0;
                while (got < buf.Length)
                {
                    int n;
                    try { n = stream.Read(buf, got, buf.Length - got); }
                    catch (IOException) { n = 0; }
                    if (n <= 0) break;
                    got += n;
                }
            }
            if (start == offset && buf.Length == count) return buf;
            var r = new byte[count];
            Buffer.BlockCopy(buf, (int)(offset - start), r, 0, count);
            return r;
        }

        public void Dispose() => stream.Dispose();
    }

    /// <summary>A contiguous range of clusters (Lcn &lt; 0 means sparse / not allocated).</summary>
    public struct DataRun
    {
        public long Vcn, Lcn, Length;
        public DataRun(long vcn, long lcn, long length) { Vcn = vcn; Lcn = lcn; Length = length; }
    }

    public enum RecoveryChance { Unknown, Good, PartlyOverwritten, Overwritten, Unsupported }

    /// <summary>One file record from the NTFS Master File Table.</summary>
    public class NtfsEntry
    {
        public long Index;
        public ushort Sequence;
        public bool InUse, IsDirectory;
        public long ParentIndex = -1;
        public ushort ParentSequence;
        public string Name;
        public int NameRank = -1;            // Win32/POSIX names beat DOS 8.3 names
        public DateTime? Modified;
        public long Size, InitializedSize;
        public byte[] ResidentData;
        public List<DataRun> Runs;
        public bool Compressed, Encrypted;
        public RecoveryChance Chance = RecoveryChance.Unknown;
        public double OverwrittenFraction;
        public string Path;
    }

    /// <summary>
    /// Minimal, read-only NTFS reader: scans the MFT for live and deleted file records,
    /// rebuilds their paths and reads their contents. Nothing is ever written to the volume.
    /// </summary>
    public sealed class NtfsVolume
    {
        readonly SectorReader dev;
        readonly long partOffset;
        public int BytesPerSector { get; private set; }
        public int ClusterSize { get; private set; }
        public int RecordSize { get; private set; }
        public long TotalClusters { get; private set; }
        public string Label { get; private set; }
        List<DataRun> mftRuns;
        long mftSize;
        byte[] bitmap; // $Bitmap: one bit per cluster, 1 = in use now

        NtfsVolume(SectorReader dev, long partOffset) { this.dev = dev; this.partOffset = partOffset; }

        public static bool LooksLikeNtfs(byte[] boot) =>
            boot.Length >= 512 && Encoding.ASCII.GetString(boot, 3, 8) == "NTFS    " && boot[510] == 0x55 && boot[511] == 0xAA;

        public static NtfsVolume Open(SectorReader dev, long partitionOffset)
        {
            var boot = dev.Read(partitionOffset, 512);
            if (!LooksLikeNtfs(boot)) throw new InvalidDataException("This partition does not contain an NTFS boot sector.");
            var v = new NtfsVolume(dev, partitionOffset);
            v.BytesPerSector = BitConverter.ToUInt16(boot, 0x0B);
            int spc = boot[0x0D];
            if (spc > 0x80) spc = 1 << (256 - spc);
            if (v.BytesPerSector == 0 || spc == 0) throw new InvalidDataException("Invalid NTFS geometry.");
            v.ClusterSize = v.BytesPerSector * spc;
            v.TotalClusters = BitConverter.ToInt64(boot, 0x28) / spc;
            long mftLcn = BitConverter.ToInt64(boot, 0x30);
            int cpr = unchecked((sbyte)boot[0x40]);
            v.RecordSize = cpr > 0 ? cpr * v.ClusterSize : 1 << -cpr;
            if (v.RecordSize < 256 || v.RecordSize > 65536) throw new InvalidDataException("Invalid MFT record size.");

            // Record 0 describes the MFT itself (which may be fragmented).
            var rec0 = v.dev.Read(v.partOffset + mftLcn * v.ClusterSize, v.RecordSize);
            if (!ApplyFixups(rec0, v.RecordSize)) throw new InvalidDataException("The MFT could not be read (damaged first record).");
            var mftEntry = ParseRecord(rec0, 0);
            if (mftEntry?.Runs == null || mftEntry.Runs.Count == 0) throw new InvalidDataException("The MFT has no data runs.");
            v.mftRuns = mftEntry.Runs;
            v.mftSize = mftEntry.Size;
            return v;
        }

        public long RecordCount => mftSize / RecordSize;

        /// <summary>Reads every MFT record. Returns live and deleted regular files with full paths.</summary>
        public List<NtfsEntry> Scan(Action<double> progress, CancellationToken token)
        {
            var entries = new Dictionary<long, NtfsEntry>();
            var extensions = new List<KeyValuePair<long, NtfsEntry>>();
            long total = RecordCount;
            const int batch = 1024;
            for (long i = 0; i < total; i += batch)
            {
                token.ThrowIfCancellationRequested();
                int n = (int)Math.Min(batch, total - i);
                var chunk = ReadMft(i * RecordSize, n * RecordSize);
                for (int k = 0; k < n; k++)
                {
                    var rec = new byte[RecordSize];
                    Buffer.BlockCopy(chunk, k * RecordSize, rec, 0, RecordSize);
                    if (!ApplyFixups(rec, RecordSize)) continue;
                    var e = ParseRecord(rec, i + k);
                    if (e == null) continue;
                    long baseRef = BitConverter.ToInt64(rec, 0x20) & 0xFFFFFFFFFFFFL;
                    if (baseRef != 0) { extensions.Add(new KeyValuePair<long, NtfsEntry>(baseRef, e)); continue; }
                    entries[e.Index] = e;
                }
                progress?.Invoke((double)Math.Min(total, i + batch) / total);
            }
            // Attribute-list extensions: merge extra $DATA runs into their base record.
            foreach (var kv in extensions)
            {
                long baseIndex = kv.Key;
                var ext = kv.Value;
                if (!entries.TryGetValue(baseIndex, out var b) || ext.Runs == null) continue;
                if (b.Runs == null) b.Runs = new List<DataRun>();
                b.Runs.AddRange(ext.Runs);
                b.Runs.Sort((x, y) => x.Vcn.CompareTo(y.Vcn));
                if (b.Size == 0) { b.Size = ext.Size; b.InitializedSize = ext.InitializedSize; }
                if (b.Name == null) b.Name = ext.Name;
            }

            LoadBitmap();
            var result = new List<NtfsEntry>();
            foreach (var e in entries.Values)
            {
                if (e.IsDirectory || e.Name == null || e.Index < 24) continue;
                e.Path = BuildPath(e, entries);
                // NTFS metadata files ($MFT, $LogFile, $Extend\$UsnJrnl, ...) live in the root and start with '$'.
                if (e.Path.StartsWith("/$")) continue;
                Rate(e);
                result.Add(e);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return result;
        }

        byte[] ReadMft(long offset, int count)
        {
            // Map MFT byte offsets through the MFT's own data runs.
            var r = new byte[count];
            int done = 0;
            while (done < count)
            {
                long vcn = (offset + done) / ClusterSize;
                int within = (int)((offset + done) % ClusterSize);
                var run = FindRun(mftRuns, vcn);
                long runEndBytes = (run.Vcn + run.Length) * (long)ClusterSize;
                int take = (int)Math.Min(count - done, runEndBytes - (offset + done));
                if (take <= 0) break;
                if (run.Lcn >= 0)
                {
                    var data = dev.Read(partOffset + (run.Lcn + (vcn - run.Vcn)) * ClusterSize + within, take);
                    Buffer.BlockCopy(data, 0, r, done, take);
                }
                done += take;
            }
            return r;
        }

        static DataRun FindRun(List<DataRun> runs, long vcn)
        {
            foreach (var r in runs) if (vcn >= r.Vcn && vcn < r.Vcn + r.Length) return r;
            return new DataRun(vcn, -1, 1);
        }

        /// <summary>Undo the update-sequence protection: the last two bytes of each 512-byte stride.</summary>
        static bool ApplyFixups(byte[] rec, int size)
        {
            if (rec[0] != 'F' || rec[1] != 'I' || rec[2] != 'L' || rec[3] != 'E') return false;
            int usaOff = BitConverter.ToUInt16(rec, 4), usaCount = BitConverter.ToUInt16(rec, 6);
            if (usaOff + usaCount * 2 > size || usaCount < 2) return false;
            ushort usn = BitConverter.ToUInt16(rec, usaOff);
            for (int i = 1; i < usaCount; i++)
            {
                int pos = i * 512 - 2;
                if (pos + 1 >= size) break;
                if (BitConverter.ToUInt16(rec, pos) != usn) return false; // torn write
                rec[pos] = rec[usaOff + i * 2];
                rec[pos + 1] = rec[usaOff + i * 2 + 1];
            }
            return true;
        }

        static NtfsEntry ParseRecord(byte[] rec, long index)
        {
            var e = new NtfsEntry { Index = index };
            e.Sequence = BitConverter.ToUInt16(rec, 0x10);
            int flags = BitConverter.ToUInt16(rec, 0x16);
            e.InUse = (flags & 1) != 0;
            e.IsDirectory = (flags & 2) != 0;
            int off = BitConverter.ToUInt16(rec, 0x14);
            int used = Math.Min(rec.Length, (int)BitConverter.ToUInt32(rec, 0x18));
            if (used <= 0) used = rec.Length;
            while (off + 16 <= used)
            {
                uint type = BitConverter.ToUInt32(rec, off);
                if (type == 0xFFFFFFFF) break;
                int len = (int)BitConverter.ToUInt32(rec, off + 4);
                if (len <= 0 || off + len > rec.Length) break;
                bool nonResident = rec[off + 8] != 0;
                int nameLen = rec[off + 9];
                int aflags = BitConverter.ToUInt16(rec, off + 12);
                try
                {
                    if (type == 0x10 && !nonResident) // $STANDARD_INFORMATION
                    {
                        int vo = off + BitConverter.ToUInt16(rec, off + 20);
                        e.Modified = FromFileTime(BitConverter.ToInt64(rec, vo + 8));
                    }
                    else if (type == 0x30 && !nonResident) // $FILE_NAME
                    {
                        int vo = off + BitConverter.ToUInt16(rec, off + 20);
                        long parent = BitConverter.ToInt64(rec, vo);
                        int nlen = rec[vo + 0x40];
                        int ns = rec[vo + 0x41];
                        int rank = ns == 2 ? 0 : 1; // DOS 8.3 names only as a fallback
                        if (rank > e.NameRank)
                        {
                            e.NameRank = rank;
                            e.Name = Encoding.Unicode.GetString(rec, vo + 0x42, nlen * 2);
                            e.ParentIndex = parent & 0xFFFFFFFFFFFFL;
                            e.ParentSequence = (ushort)(parent >> 48);
                            if (e.Modified == null) e.Modified = FromFileTime(BitConverter.ToInt64(rec, vo + 0x10));
                        }
                    }
                    else if (type == 0x80 && nameLen == 0) // unnamed $DATA
                    {
                        e.Compressed = (aflags & 0x0001) != 0;
                        e.Encrypted = (aflags & 0x4000) != 0;
                        if (!nonResident)
                        {
                            int vlen = (int)BitConverter.ToUInt32(rec, off + 16);
                            int vo = off + BitConverter.ToUInt16(rec, off + 20);
                            e.ResidentData = new byte[vlen];
                            Buffer.BlockCopy(rec, vo, e.ResidentData, 0, vlen);
                            e.Size = e.InitializedSize = vlen;
                        }
                        else
                        {
                            long startVcn = BitConverter.ToInt64(rec, off + 16);
                            int runOff = off + BitConverter.ToUInt16(rec, off + 32);
                            if (startVcn == 0)
                            {
                                e.Size = BitConverter.ToInt64(rec, off + 48);
                                e.InitializedSize = BitConverter.ToInt64(rec, off + 56);
                            }
                            var runs = DecodeRuns(rec, runOff, off + len, startVcn);
                            if (e.Runs == null) e.Runs = runs; else e.Runs.AddRange(runs);
                        }
                    }
                }
                catch (ArgumentException) { /* malformed attribute, keep what we have */ }
                catch (IndexOutOfRangeException) { }
                off += len;
            }
            return e;
        }

        static List<DataRun> DecodeRuns(byte[] rec, int pos, int end, long vcn)
        {
            var runs = new List<DataRun>();
            long lcn = 0;
            while (pos < end && rec[pos] != 0)
            {
                int h = rec[pos];
                int lenBytes = h & 0x0F, offBytes = h >> 4;
                if (lenBytes == 0 || lenBytes > 8 || offBytes > 8 || pos + 1 + lenBytes + offBytes > end) break;
                long length = 0;
                for (int i = 0; i < lenBytes; i++) length |= (long)rec[pos + 1 + i] << (8 * i);
                if (offBytes == 0)
                {
                    runs.Add(new DataRun(vcn, -1, length)); // sparse
                }
                else
                {
                    long delta = 0;
                    for (int i = 0; i < offBytes; i++) delta |= (long)rec[pos + 1 + lenBytes + i] << (8 * i);
                    if ((rec[pos + lenBytes + offBytes] & 0x80) != 0) delta -= 1L << (8 * offBytes); // sign-extend
                    lcn += delta;
                    runs.Add(new DataRun(vcn, lcn, length));
                }
                vcn += length;
                pos += 1 + lenBytes + offBytes;
            }
            return runs;
        }

        static DateTime? FromFileTime(long ft)
        {
            if (ft <= 0) return null;
            try { return DateTime.FromFileTimeUtc(ft).ToLocalTime(); } catch { return null; }
        }

        public const string OrphanFolder = "Lost folders";

        static string BuildPath(NtfsEntry e, Dictionary<long, NtfsEntry> all)
        {
            var parts = new List<string> { e.Name };
            var cur = e;
            for (int depth = 0; depth < 256; depth++)
            {
                if (cur.ParentIndex == 5) break; // root directory
                if (!all.TryGetValue(cur.ParentIndex, out var parent) || parent.Name == null ||
                    (parent.Sequence != cur.ParentSequence && parent.InUse))
                {
                    // The parent folder's record was reused: keep the file, under a recognisable folder.
                    parts.Add("Folder #" + cur.ParentIndex);
                    parts.Add(OrphanFolder);
                    break;
                }
                parts.Add(parent.Name);
                cur = parent;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }

        void LoadBitmap()
        {
            if (bitmap != null) return;
            try
            {
                var rec = ReadMft(6L * RecordSize, RecordSize);
                if (!ApplyFixups(rec, RecordSize)) return;
                var e = ParseRecord(rec, 6);
                bitmap = e.ResidentData ?? ReadRuns(e.Runs, e.Size, e.Size, CancellationToken.None);
            }
            catch { bitmap = null; }
        }

        bool ClusterInUse(long lcn)
        {
            if (bitmap == null || lcn < 0) return false;
            long b = lcn >> 3;
            return b < bitmap.Length && (bitmap[b] & (1 << (int)(lcn & 7))) != 0;
        }

        /// <summary>Estimates how much of a deleted file has since been overwritten.</summary>
        void Rate(NtfsEntry e)
        {
            if (e.Compressed || e.Encrypted) { e.Chance = RecoveryChance.Unsupported; return; }
            if (e.InUse) { e.Chance = RecoveryChance.Good; return; }
            if (e.ResidentData != null || e.Size == 0) { e.Chance = RecoveryChance.Good; return; }
            if (e.Runs == null || e.Runs.Count == 0) { e.Chance = RecoveryChance.Overwritten; return; }
            if (bitmap == null) { e.Chance = RecoveryChance.Unknown; return; }
            long needed = (e.Size + ClusterSize - 1) / ClusterSize, checkedC = 0, used = 0;
            foreach (var r in e.Runs)
            {
                if (r.Lcn < 0) continue;
                for (long c = 0; c < r.Length && checkedC < needed; c++, checkedC++)
                    if (ClusterInUse(r.Lcn + c)) used++;
            }
            e.OverwrittenFraction = checkedC == 0 ? 0 : (double)used / checkedC;
            e.Chance = used == 0 ? RecoveryChance.Good : used >= checkedC ? RecoveryChance.Overwritten : RecoveryChance.PartlyOverwritten;
        }

        byte[] ReadRuns(List<DataRun> runs, long size, long initialized, CancellationToken token)
        {
            if (size > int.MaxValue) throw new IOException("File too large to read into memory.");
            var ms = new MemoryStream((int)size);
            CopyRuns(runs, size, initialized, ms, token);
            return ms.ToArray();
        }

        /// <summary>Copies a file's contents to <paramref name="output"/>, following its data runs.</summary>
        public void CopyTo(NtfsEntry e, Stream output, CancellationToken token)
        {
            if (e.Compressed || e.Encrypted) throw new NotSupportedException("NTFS-compressed or EFS-encrypted files cannot be recovered by Quick Scan; use Deep Scan.");
            if (e.ResidentData != null) { output.Write(e.ResidentData, 0, (int)Math.Min(e.ResidentData.Length, e.Size)); return; }
            CopyRuns(e.Runs ?? new List<DataRun>(), e.Size, e.InitializedSize > 0 ? e.InitializedSize : e.Size, output, token);
        }

        /// <summary>Reads at most <paramref name="max"/> bytes of a file (for previews).</summary>
        public byte[] ReadHead(NtfsEntry e, int max)
        {
            var ms = new MemoryStream();
            var limited = new NtfsEntry { ResidentData = e.ResidentData, Runs = e.Runs, Size = Math.Min(e.Size, max), InitializedSize = Math.Min(e.InitializedSize > 0 ? e.InitializedSize : e.Size, max) };
            CopyTo(limited, ms, CancellationToken.None);
            return ms.ToArray();
        }

        void CopyRuns(List<DataRun> runs, long size, long initialized, Stream output, CancellationToken token)
        {
            long written = 0;
            var zero = new byte[ClusterSize * 64];
            foreach (var r in runs)
            {
                for (long c = 0; c < r.Length && written < size; )
                {
                    token.ThrowIfCancellationRequested();
                    long clusters = Math.Min(r.Length - c, 256); // 1 MiB with 4 KiB clusters
                    int bytes = (int)Math.Min(clusters * ClusterSize, size - written);
                    byte[] data;
                    if (r.Lcn < 0 || written >= initialized) data = bytes <= zero.Length ? zero : new byte[bytes];
                    else data = dev.Read(partOffset + (r.Lcn + c) * ClusterSize, bytes);
                    if (written < initialized && written + bytes > initialized && r.Lcn >= 0)
                        Array.Clear(data, (int)(initialized - written), (int)(written + bytes - initialized));
                    output.Write(data, 0, bytes);
                    written += bytes;
                    c += clusters;
                }
                if (written >= size) break;
            }
            while (written < size) // runs ended early (e.g. truncated record): pad with zeros
            {
                int bytes = (int)Math.Min(zero.Length, size - written);
                output.Write(zero, 0, bytes);
                written += bytes;
            }
        }
    }
}
