using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpindleRecovery.Engine
{
    /// <summary>
    /// Locates (or extracts) the TestDisk / PhotoRec engines and runs them.
    /// The engines are embedded in SpindleRecovery.exe and extracted on first use
    /// to %LOCALAPPDATA%\SpindleRecovery\engine\&lt;hash&gt;\.
    /// </summary>
    public static class EngineHost
    {
        public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        public static string AppDataDir
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpindleRecovery");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SessionsDir
        {
            get
            {
                var dir = Path.Combine(AppDataDir, "sessions");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        static string _engineDir;

        /// <summary>Directory that contains testdisk(.exe) and photorec(.exe).</summary>
        public static string EngineDir
        {
            get
            {
                if (_engineDir != null) return _engineDir;
                // Developer override (also used to drive the native Linux engines when testing under Mono)
                var overrideDir = Environment.GetEnvironmentVariable("SPINDLE_ENGINE_DIR");
                if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
                    return _engineDir = overrideDir;
                _engineDir = ExtractEmbeddedEngines();
                return _engineDir;
            }
        }

        public static string TestDiskPath => Path.Combine(EngineDir, IsWindows ? "testdisk.exe" : "testdisk");
        public static string PhotoRecPath => Path.Combine(EngineDir, IsWindows ? "photorec.exe" : "photorec");

        static string ExtractEmbeddedEngines()
        {
            var asm = Assembly.GetExecutingAssembly();
            var names = new[] { "testdisk.exe", "photorec.exe" };
            var blobs = new Dictionary<string, byte[]>();
            using (var sha = SHA256.Create())
            {
                var all = new MemoryStream();
                foreach (var n in names)
                {
                    using (var s = asm.GetManifestResourceStream("engine." + n))
                    {
                        if (s == null) throw new InvalidOperationException("Embedded engine missing: " + n);
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        blobs[n] = ms.ToArray();
                        all.Write(blobs[n], 0, blobs[n].Length);
                    }
                }
                var hash = BitConverter.ToString(sha.ComputeHash(all.ToArray())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                var dir = Path.Combine(AppDataDir, "engine", hash);
                Directory.CreateDirectory(dir);
                foreach (var kv in blobs)
                {
                    var target = Path.Combine(dir, kv.Key);
                    if (!File.Exists(target) || new FileInfo(target).Length != kv.Value.Length)
                        File.WriteAllBytes(target, kv.Value);
                }
                return dir;
            }
        }

        /// <summary>Creates a fresh, time-stamped working folder for an engine run (holds its log).</summary>
        public static string NewSessionDir(string kind)
        {
            var dir = Path.Combine(SessionsDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + kind);
            int i = 1;
            var baseDir = dir;
            while (Directory.Exists(dir)) dir = baseDir + "-" + (i++);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Runs an engine and streams its standard output line by line.
        /// Returns the process exit code. Cancellation kills the process.
        /// </summary>
        public static Task<int> RunAsync(string exe, IList<string> args, string workingDir,
            Action<string> onLine, CancellationToken token, IDictionary<string, string> env = null)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = JoinArgs(args),
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                if (env != null)
                    foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) onLine?.Invoke(e.Data); };
                    p.Start();
                    p.StandardInput.Close();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    using (token.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                    {
                        p.WaitForExit();
                    }
                    p.WaitForExit(); // flush async readers
                    return p.ExitCode;
                }
            });
        }

        /// <summary>Quotes arguments using the MSVC runtime rules.</summary>
        public static string JoinArgs(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) { sb.Append(a); continue; }
                sb.Append('"');
                int bs = 0;
                foreach (var c in a)
                {
                    if (c == '\\') { bs++; continue; }
                    if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                    sb.Append('\\', bs); bs = 0; sb.Append(c);
                }
                sb.Append('\\', bs * 2);
                sb.Append('"');
            }
            return sb.ToString();
        }

        public static string ReadLog(string sessionDir, string name)
        {
            var p = Path.Combine(sessionDir, name);
            if (!File.Exists(p)) return "";
            using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                return sr.ReadToEnd();
        }
    }
}
