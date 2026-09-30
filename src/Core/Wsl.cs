using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace WslTray.Core
{
    static class Wsl
    {
        public const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";
        public const int ListTimeoutMs = 5000;

        static string WslExe()
        {
            string o = Environment.GetEnvironmentVariable("WSLTRAY_WSL_EXE");
            return string.IsNullOrEmpty(o) ? "wsl.exe" : o;
        }

        // ---- process helper: runs exe with timeout on the CALLING thread (never call from UI thread). ----
        public static bool Run(string exe, string args, int timeoutMs, out int exitCode, out byte[] output, out bool timedOut)
        {
            exitCode = -1; output = new byte[0]; timedOut = false;
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.EnvironmentVariables["WSL_UTF8"] = "1";
            Process p;
            try { p = Process.Start(psi); } catch (Exception) { return false; }
            using (p)
            {
                MemoryStream ms = new MemoryStream();
                Thread t1 = Drain(p.StandardOutput.BaseStream, ms);
                Thread t2 = Drain(p.StandardError.BaseStream, null);
                if (!p.WaitForExit(timeoutMs))
                {
                    timedOut = true;
                    try { p.Kill(); } catch (Exception) { }
                    try { p.WaitForExit(1000); } catch (Exception) { }
                }
                else exitCode = p.ExitCode;
                t1.Join(500); t2.Join(500); // a grandchild could hold the pipe open; threads are background
                lock (ms) { output = ms.ToArray(); }
                return !timedOut;
            }
        }

        static Thread Drain(Stream s, MemoryStream sink)
        {
            Thread t = new Thread(delegate ()
            {
                byte[] buf = new byte[4096];
                try
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        if (sink != null) lock (sink) { sink.Write(buf, 0, n); }
                }
                catch (Exception) { }
            });
            t.IsBackground = true; t.Start();
            return t;
        }

        // ---- parsers ----
        public static string Decode(byte[] b)
        {
            if (b == null || b.Length == 0) return "";
            string s;
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) s = Encoding.Unicode.GetString(b, 2, b.Length - 2);
            else if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) s = Encoding.UTF8.GetString(b, 3, b.Length - 3);
            else
            {
                int zeros = 0;
                for (int i = 0; i < b.Length; i++) if (b[i] == 0) zeros++;
                s = (zeros * 4 >= b.Length) ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);
            }
            return s.Replace("\0", "").Replace("\uFEFF", "");
        }

        // Non-zero exit (e.g. "There are no running distributions.") => empty set; the sentence is never parsed.
        public static List<string> ParseRunning(int exitCode, byte[] output)
        {
            List<string> r = new List<string>();
            if (exitCode != 0) return r;
            foreach (string line in Decode(output).Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = line.Trim();
                if (n.Length > 0) r.Add(n);
            }
            return r;
        }

        public static string NormalizePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + p.Substring(8);
            if (p.StartsWith(@"\\?\")) return p.Substring(4);
            return p;
        }

        public static string VhdxPathFor(string basePath, string vhdFileName)
        {
            string bp = NormalizePath(basePath);
            if (string.IsNullOrEmpty(bp)) return null;
            string f = string.IsNullOrEmpty(vhdFileName) ? "ext4.vhdx" : vhdFileName;
            try { return Path.IsPathRooted(f) ? f : Path.Combine(bp, f); } catch (Exception) { return null; }
        }

        public static void MapDefault(List<Distro> list, string defaultGuid)
        {
            foreach (Distro d in list)
                d.IsDefault = !string.IsNullOrEmpty(defaultGuid) &&
                              string.Equals(d.Guid, defaultGuid, StringComparison.OrdinalIgnoreCase);
        }

        // ---- data sources ----
        public static List<Distro> ReadRegistry(out string defaultName)
        {
            List<Distro> list = new List<Distro>();
            defaultName = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(LxssKey))
                {
                    if (k == null) return list;
                    string defGuid = k.GetValue("DefaultDistribution") as string;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        using (RegistryKey s = k.OpenSubKey(sub))
                        {
                            if (s == null) continue;
                            string name = s.GetValue("DistributionName") as string;
                            if (string.IsNullOrEmpty(name)) continue;
                            Distro d = new Distro();
                            d.Guid = sub; d.Name = name;
                            object v = s.GetValue("Version");
                            d.Version = (v is int) ? (int)v : 0;
                            d.BasePath = s.GetValue("BasePath") as string;
                            d.VhdFileName = s.GetValue("VhdFileName") as string;
                            d.VhdxPath = VhdxPathFor(d.BasePath, d.VhdFileName);
                            if (d.Version == 2 && d.VhdxPath != null)
                            {
                                try { FileInfo fi = new FileInfo(d.VhdxPath); if (fi.Exists) d.VhdxBytes = fi.Length; } catch (Exception) { }
                            }
                            list.Add(d);
                        }
                    }
                    MapDefault(list, defGuid);
                }
            }
            catch (Exception) { }
            list.Sort(delegate (Distro a, Distro b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            foreach (Distro d in list) if (d.IsDefault) defaultName = d.Name;
            return list;
        }

        public static bool VmInfo(out long ramBytes)
        {
            ramBytes = 0; bool present = false;
            foreach (string n in new string[] { "vmmem", "vmmemWSL" })
            {
                Process[] ps = Process.GetProcessesByName(n);
                foreach (Process p in ps)
                {
                    present = true;
                    try { ramBytes += p.WorkingSet64; } catch (Exception) { }
                    p.Dispose();
                }
            }
            return present;
        }

        // Must be called off the UI thread.
        public static Snapshot Collect()
        {
            Snapshot s = new Snapshot();
            s.Distros = ReadRegistry(out s.DefaultName);
            s.VmPresent = VmInfo(out s.VmRamBytes);
            if (!s.VmPresent) return s; // VM down => nothing running; avoid calling wsl.exe (could start the VM)
            int code; byte[] outp; bool to;
            Stopwatch sw = Stopwatch.StartNew();
            bool ok = Run(WslExe(), "-l --running -q", ListTimeoutMs, out code, out outp, out to);
            s.WslCallMs = sw.ElapsedMilliseconds;
            if (to) { s.Running = null; s.WslCall = "timeout"; }
            else if (!ok) { s.Running = null; s.WslCall = "error"; }
            else { s.Running = ParseRunning(code, outp); s.WslCall = "ok"; }
            return s;
        }

        public static string FormatSize(long b)
        {
            if (b < 0) return "";
            if (b >= 1L << 30) return string.Format("{0:0.0} GB", b / (double)(1L << 30));
            return string.Format("{0:0} MB", b / (double)(1L << 20));
        }
    }
}
