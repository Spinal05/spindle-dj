using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SpindleRecovery.Engine
{
    public class ImagingProgress
    {
        public long Done, Total, BadSectors;
        public double Fraction => Total <= 0 ? 0 : (double)Done / Total;
    }

    /// <summary>
    /// Creates a byte-to-byte image (.img) of a disk or partition. Unreadable sectors are
    /// retried one sector at a time and replaced by zeros so a failing drive can still be imaged.
    /// The source is only ever opened for reading.
    /// </summary>
    public static class DiskImager
    {
        const uint GENERIC_READ = 0x80000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        public static Stream OpenSource(string path)
        {
            if (EngineHost.IsWindows && path.StartsWith(@"\\.\"))
            {
                var h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open " + path);
                return new FileStream(h, FileAccess.Read, 1, false); // bufferSize 1 = unbuffered (0 is rejected by .NET Framework)
            }
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        }

        public static void CreateImage(string source, long offset, long length, int sectorSize, string target,
            Action<ImagingProgress> onProgress, CancellationToken token)
        {
            const int chunk = 1 << 20; // 1 MiB, a multiple of every sector size
            if (sectorSize <= 0) sectorSize = 512;
            var buffer = new byte[chunk];
            var zero = new byte[sectorSize];
            var prog = new ImagingProgress { Total = length };
            var lastReport = DateTime.MinValue;

            using (var src = OpenSource(source))
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20))
            {
                long pos = 0;
                while (pos < length)
                {
                    token.ThrowIfCancellationRequested();
                    int want = (int)Math.Min(chunk, length - pos);
                    // raw devices need sector-aligned transfer sizes
                    int aligned = (want + sectorSize - 1) / sectorSize * sectorSize;
                    if (TryRead(src, offset + pos, buffer, aligned))
                    {
                        dst.Write(buffer, 0, want);
                    }
                    else
                    {
                        // slow path: sector by sector, zero-filling what cannot be read
                        for (int s = 0; s < aligned; s += sectorSize)
                        {
                            int n = (int)Math.Min(sectorSize, want - s);
                            if (n <= 0) break;
                            if (TryRead(src, offset + pos + s, buffer, sectorSize)) dst.Write(buffer, 0, n);
                            else { dst.Write(zero, 0, n); prog.BadSectors++; }
                        }
                    }
                    pos += want;
                    prog.Done = pos;
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 250) { lastReport = DateTime.UtcNow; onProgress?.Invoke(prog); }
                }
                onProgress?.Invoke(prog);
            }
        }

        static bool TryRead(Stream s, long at, byte[] buffer, int count)
        {
            try
            {
                s.Position = at;
                int got = 0;
                while (got < count)
                {
                    int n = s.Read(buffer, got, count - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < count) Array.Clear(buffer, got, count - got);
                return got > 0 || count == 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
