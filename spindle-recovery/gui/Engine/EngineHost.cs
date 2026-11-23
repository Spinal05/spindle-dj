using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
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
        static bool _usingOverride;

        // Engines embedded in this (signed) executable, and their SHA-256, by file name.
        static Dictionary<string, byte[]> _embedded;
        static Dictionary<string, byte[]> _embeddedHash;

        /// <summary>Directory that contains testdisk(.exe) and photorec(.exe).</summary>
        public static string EngineDir
        {
            get
            {
                if (_engineDir != null) return _engineDir;
                // Developer override, used to drive native Linux engines when testing under Mono.
                // Never honoured on Windows: the app runs elevated, and any user-level program can
                // set an environment variable, which would let it substitute its own binaries.
                if (!IsWindows)
                {
                    var overrideDir = Environment.GetEnvironmentVariable("SPINDLE_ENGINE_DIR");
                    if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
                    {
                        _usingOverride = true;
                        return _engineDir = overrideDir;
                    }
                }
                _engineDir = ExtractEmbeddedEngines();
                return _engineDir;
            }
        }

        public static string TestDiskPath => Path.Combine(EngineDir, IsWindows ? "testdisk.exe" : "testdisk");
        public static string PhotoRecPath => Path.Combine(EngineDir, IsWindows ? "photorec.exe" : "photorec");

        static void LoadEmbedded()
        {
            if (_embedded != null) return;
            var asm = Assembly.GetExecutingAssembly();
            var blobs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var hashes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var sha = SHA256.Create())
                foreach (var n in new[] { "testdisk.exe", "photorec.exe" })
                    using (var s = asm.GetManifestResourceStream("engine." + n))
                    {
                        if (s == null) throw new InvalidOperationException("Embedded engine missing: " + n);
                        var ms = new MemoryStream();
                        s.CopyTo(ms);
                        blobs[n] = ms.ToArray();
                        hashes[n] = sha.ComputeHash(blobs[n]);
                    }
            _embeddedHash = hashes;
            _embedded = blobs;
        }

        /// <summary>
        /// Where engines are extracted. On Windows this is under Program Files, which standard
        /// users cannot write to, so a non-elevated process cannot plant or replace a binary
        /// that this elevated application will later run.
        /// </summary>
        static string EngineRoot
        {
            get
            {
                if (IsWindows)
                {
                    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    if (!string.IsNullOrEmpty(pf)) return Path.Combine(pf, "Spindle Recovery");
                }
                return AppDataDir;
            }
        }

        static string ExtractEmbeddedEngines()
        {
            LoadEmbedded();
            var all = new List<byte>();
            foreach (var h in _embeddedHash.OrderBy(k => k.Key, StringComparer.Ordinal)) all.AddRange(h.Value);
            string id;
            using (var sha = SHA256.Create())
                id = BitConverter.ToString(sha.ComputeHash(all.ToArray())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            var root = EngineRoot;
            Directory.CreateDirectory(root);
            if (IsWindows) SecureDirectory(root);
            var dir = Path.Combine(root, "engine", id);
            Directory.CreateDirectory(dir);
            foreach (var kv in _embedded)
            {
                var target = Path.Combine(dir, kv.Key);
                if (!FileMatches(target, _embeddedHash[kv.Key])) WriteEngine(target, kv.Value);
            }
            return dir;
        }

        /// <summary>Administrators and SYSTEM: full control. Users: read and execute. No inherited entries.</summary>
        static void SecureDirectory(string dir)
        {
            try
            {
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                sec.SetOwner(admins);
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(dir, sec);
            }
            catch
            {
                // Program Files is already admin-only; the hash check in LockVerifiedEngine is the
                // final safeguard, so a failure to tighten the ACL further is not fatal.
            }
        }

        static bool FileMatches(string path, byte[] expected)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                    return sha.ComputeHash(fs).SequenceEqual(expected);
            }
            catch { return false; }
        }

        static void WriteEngine(string target, byte[] bytes)
        {
            var tmp = target + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        /// <summary>
        /// Verifies an engine against the copy embedded in this executable and returns an open
        /// handle that denies writing, renaming and deleting the file. Keep it open until the
        /// engine has started, so the verified file is the one that runs. Returns null for
        /// engines that are not embedded (developer override on Linux).
        /// </summary>
        public static IDisposable LockVerifiedEngine(string exePath)
        {
            if (_usingOverride) return null; // developer override (Linux/Mono only)
            LoadEmbedded();
            var name = Path.GetFileName(exePath);
            if (!_embeddedHash.TryGetValue(name, out var expected)) return null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                FileStream fs = null;
                try
                {
                    fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using (var sha = SHA256.Create())
                        if (sha.ComputeHash(fs).SequenceEqual(expected)) return fs;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { Directory.CreateDirectory(Path.GetDirectoryName(exePath)); }
                fs?.Dispose();
                WriteEngine(exePath, _embedded[name]); // missing or modified: restore the genuine copy
            }
            throw new System.Security.SecurityException("The recovery engine " + name + " failed its integrity check and could not be restored.");
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

                using (LockVerifiedEngine(exe))
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
