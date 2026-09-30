using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using WslTray.Core;

namespace WslTray.Tests
{
    static class SelfTest
    {
        static int fails, total;
        static void Check(string name, bool ok)
        {
            total++; if (!ok) fails++;
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        }
        static string ChoiceIds(List<TermChoice> l)
        {
            List<string> ids = new List<string>();
            foreach (TermChoice c in l) ids.Add(c.Id);
            return string.Join(",", ids.ToArray());
        }
        static Func<string, bool> Fake(params string[] exes)
        {
            return delegate (string e) { foreach (string x in exes) if (string.Equals(x, e, StringComparison.OrdinalIgnoreCase)) return true; return false; };
        }
        static bool Same(List<string> a, params string[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        public static int Run()
        {
            byte[] u16bom = Cat(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes("Ubuntu\r\ndevbox-wsl\r\n"));
            byte[] u16 = Encoding.Unicode.GetBytes("Ubuntu\r\ndevbox-wsl\r\n");
            byte[] u8 = Encoding.UTF8.GetBytes("Ubuntu\r\ndevbox-wsl\r\n");
            byte[] u8bom = Cat(new byte[] { 0xEF, 0xBB, 0xBF }, u8);
            byte[] lf = Encoding.UTF8.GetBytes("Ubuntu\ndocker-desktop\n\n");
            Check("UTF-16LE with BOM, CRLF", Same(Wsl.ParseRunning(0, u16bom), "Ubuntu", "devbox-wsl"));
            Check("UTF-16LE no BOM, CRLF", Same(Wsl.ParseRunning(0, u16), "Ubuntu", "devbox-wsl"));
            Check("UTF-8, CRLF", Same(Wsl.ParseRunning(0, u8), "Ubuntu", "devbox-wsl"));
            Check("UTF-8 with BOM, CRLF", Same(Wsl.ParseRunning(0, u8bom), "Ubuntu", "devbox-wsl"));
            Check("UTF-8, LF + blank lines", Same(Wsl.ParseRunning(0, lf), "Ubuntu", "docker-desktop"));
            Check("empty output, exit 0", Wsl.ParseRunning(0, new byte[0]).Count == 0);
            Check("null output", Wsl.ParseRunning(0, null).Count == 0);
            Check("non-zero exit + sentence (UTF-8) => empty", Wsl.ParseRunning(1, Encoding.UTF8.GetBytes("There are no running distributions.\r\n")).Count == 0);
            Check("non-zero exit + sentence (UTF-16) => empty", Wsl.ParseRunning(1, Cat(new byte[] { 0xFF, 0xFE }, Encoding.Unicode.GetBytes("Es gibt keine laufenden Distributionen.\r\n"))).Count == 0);
            Check("path \\\\?\\ prefix stripped", Wsl.NormalizePath(@"\\?\C:\a\b") == @"C:\a\b");
            Check("vhdx default file name", Wsl.VhdxPathFor(@"C:\x", null) == @"C:\x\ext4.vhdx");
            Check("vhdx explicit file name", Wsl.VhdxPathFor(@"\\?\C:\x", "d.vhdx") == @"C:\x\d.vhdx");
            List<Distro> l = new List<Distro>();
            Distro a = new Distro(); a.Guid = "{AAA}"; a.Name = "one"; l.Add(a);
            Distro b = new Distro(); b.Guid = "{BBB}"; b.Name = "two"; l.Add(b);
            Wsl.MapDefault(l, "{bbb}");
            Check("default GUID -> name (case-insensitive)", !a.IsDefault && b.IsDefault);
            Check("size format", Wsl.FormatSize(3L << 30) == "3.0 GB" && Wsl.FormatSize(5L << 20) == "5 MB" && Wsl.FormatSize(-1) == "");

            // Timeout logic against a fake slow "wsl": ping.exe sleeps ~30 s; we require kill within ~1.5 s.
            int code; byte[] o; bool to;
            Stopwatch sw = Stopwatch.StartNew();
            bool ok = Wsl.Run("ping.exe", "-n 30 127.0.0.1", 1000, out code, out o, out to);
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine("  (fake slow command: timedOut=" + to + " returned=" + ok + " after " + ms + " ms)");
            Check("timeout: slow command killed and reported", to && !ok && ms < 3500);
            Check("missing executable handled", !Wsl.Run("definitely-not-a-real-exe.exe", "", 1000, out code, out o, out to) && !to);
            TerminalTests();
            Console.WriteLine(string.Format("{0}/{1} passed", total - fails, total));
            return fails == 0 ? 0 : 1;
        }
        static void TerminalTests()
        {
            Func<string, bool> none = delegate (string e) { return false; };
            Func<string, bool> all = delegate (string e) { return true; };
            Func<string, bool> wtOnly = delegate (string e) { return e == "wt.exe"; };
            Func<string, bool> weztermOnly = delegate (string e) { return e == "wezterm.exe"; };

            LaunchPlan p = Terminals.Plan("console", "", "Ubuntu", none);
            Check("console template, normal name", p.Error == null && p.Warning == null && p.Exe == "cmd.exe"
                && p.Args == "/c start \"WSL - Ubuntu\" wsl.exe -d \"Ubuntu\" --cd ~");
            p = Terminals.Plan("console", "", "My Distro 2", none);
            Check("console template, name with spaces quoted", p.Args == "/c start \"WSL - My Distro 2\" wsl.exe -d \"My Distro 2\" --cd ~");
            p = Terminals.Plan("wt", "", "My Distro", all);
            Check("wt template, name with spaces", p.Exe == "wt.exe" && p.Args == "wsl.exe -d \"My Distro\" --cd ~");
            p = Terminals.Plan("powershell", "", "Ubuntu", none);
            Check("powershell template", p.Exe == "cmd.exe" && p.Args == "/c start \"WSL - Ubuntu\" powershell.exe -NoExit -Command wsl.exe -d \"Ubuntu\" --cd ~");
            p = Terminals.Plan("alacritty", "", "Ubuntu", all);
            Check("alacritty template", p.Exe == "alacritty.exe" && p.Args == "-e wsl.exe -d \"Ubuntu\" --cd ~");
            p = Terminals.Plan("wezterm", "", "Ubuntu", weztermOnly);
            Check("wezterm falls back to wezterm.exe when gui exe absent", p.Exe == "wezterm.exe" && p.Args == "start -- wsl.exe -d \"Ubuntu\" --cd ~");
            p = Terminals.Plan("wezterm", "", "Ubuntu", all);
            Check("wezterm prefers wezterm-gui.exe", p.Exe == "wezterm-gui.exe");
            bool allHome = true;
            foreach (string bid in new string[] { "wt", "wezterm", "alacritty", "pwsh", "console", "powershell" })
            {
                LaunchPlan bp = Terminals.Plan(bid, "", "Ubuntu", all);
                if (bp.Args == null || !bp.Args.EndsWith(" --cd ~")) allHome = false;
            }
            Check("every built-in preset opens in the distro home (--cd ~)", allHome);

            p = Terminals.Plan("console", "", "bad\"name", none);
            Check("name with quote rejected", p.Error != null && p.Exe == null);
            Check("name validation: empty/control/trailing backslash rejected",
                Terminals.ValidateName("") != null && Terminals.ValidateName("a\tb") != null && Terminals.ValidateName("a\\") != null
                && Terminals.ValidateName("Ubuntu-22.04") == null);

            Check("normalize: unset/auto/unknown -> null, case-insensitive ids", Terminals.Normalize(null) == null && Terminals.Normalize("auto") == null
                && Terminals.Normalize("bogus") == null && Terminals.Normalize("WT") == "wt" && Terminals.Normalize("PWSH") == "pwsh");
            p = Terminals.Plan("pwsh", "", "Ubuntu", all);
            Check("pwsh template", p.Exe == "cmd.exe" && p.Args == "/c start \"WSL - Ubuntu\" pwsh.exe -NoExit -Command wsl.exe -d \"Ubuntu\" --cd ~");
            p = Terminals.Plan("wt", "", "Ubuntu", none);
            Check("explicit wt missing -> warning + Console fallback", p.Warning != null && p.ResolvedId == "console" && p.Exe == "cmd.exe");
            p = Terminals.Plan("alacritty", "", "Ubuntu", none);
            Check("explicit alacritty missing -> warning + Console fallback", p.Warning != null && p.ResolvedId == "console");

            Check("custom: valid template accepted", Terminals.ValidateCustom("mytty.exe --run wsl.exe -d \"{name}\"") == null);
            Check("custom: missing {name} rejected", Terminals.ValidateCustom("mytty.exe --run wsl.exe") != null);
            Check("custom: empty/whitespace rejected", Terminals.ValidateCustom("") != null && Terminals.ValidateCustom("   ") != null && Terminals.ValidateCustom(null) != null);
            Check("custom: placeholder as program rejected", Terminals.ValidateCustom("{name} x") != null);
            Check("custom: quoted exe path parsed", Terminals.ExeOf("\"C:\\Program Files\\X\\x.exe\" -e {name}") == "C:\\Program Files\\X\\x.exe"
                && Terminals.ArgsOf("\"C:\\Program Files\\X\\x.exe\" -e {name}") == "-e {name}");
            p = Terminals.Plan("custom", "\"C:\\Program Files\\X\\x.exe\" -e wsl.exe -d \"{name}\"", "My Distro", all);
            Check("custom plan expansion", p.Warning == null && p.Exe == "C:\\Program Files\\X\\x.exe" && p.Args == "-e wsl.exe -d \"My Distro\"");
            p = Terminals.Plan("custom", "mytty.exe wsl.exe", "Ubuntu", all);
            Check("custom invalid at launch -> warning + Console", p.Warning != null && p.ResolvedId == "console");
            p = Terminals.Plan("custom", "mytty.exe -d \"{name}\"", "Ubuntu", none);
            Check("custom exe missing -> warning + Console", p.Warning != null && p.ResolvedId == "console");
            p = Terminals.Plan("custom", "mytty.exe -d \"{name}\"", "a\"b", all);
            Check("custom plan still rejects quoted name", p.Error != null);

            Check("availability: console/powershell always, custom offered", Terminals.IsAvailable("console", none)
                && Terminals.IsAvailable("powershell", none) && Terminals.IsAvailable("custom", none));
            Check("availability: wt/wezterm/alacritty/pwsh hidden when absent",
                !Terminals.IsAvailable("wt", none) && !Terminals.IsAvailable("wezterm", none) && !Terminals.IsAvailable("alacritty", none) && !Terminals.IsAvailable("pwsh", none));

            // detection list contents
            Check("detect: nothing installed -> console, powershell, custom", ChoiceIds(Terminals.AvailableChoices(none)) == "console,powershell,custom");
            Check("detect: everything installed -> full display order", ChoiceIds(Terminals.AvailableChoices(all)) == "wt,wezterm,alacritty,pwsh,console,powershell,custom");
            Check("detect: wt + pwsh only", ChoiceIds(Terminals.AvailableChoices(Fake("wt.exe", "pwsh.exe"))) == "wt,pwsh,console,powershell,custom");
            Check("detect: no 'auto' entry ever", ChoiceIds(Terminals.AvailableChoices(all)).IndexOf("auto") < 0);
            Check("detect: wezterm via either exe", ChoiceIds(Terminals.AvailableChoices(Fake("wezterm-gui.exe"))).StartsWith("wezterm,")
                && ChoiceIds(Terminals.AvailableChoices(Fake("wezterm.exe"))).StartsWith("wezterm,"));

            // seeding order: Windows Terminal, WezTerm, Alacritty, PowerShell 7, Console
            Check("seed: all -> wt", Terminals.Seed(all) == "wt");
            Check("seed: no wt -> wezterm", Terminals.Seed(Fake("wezterm.exe", "alacritty.exe", "pwsh.exe")) == "wezterm");
            Check("seed: alacritty before pwsh", Terminals.Seed(Fake("alacritty.exe", "pwsh.exe")) == "alacritty");
            Check("seed: pwsh before console", Terminals.Seed(Fake("pwsh.exe")) == "pwsh");
            Check("seed: nothing -> console", Terminals.Seed(none) == "console");

            // resolve: unset / auto / stale / valid
            TermResolution rs = Terminals.Resolve(null, "", Fake("wt.exe"));
            Check("resolve: unset seeds wt, writes, no message", rs.Id == "wt" && rs.Write && rs.StaleLabel == null);
            rs = Terminals.Resolve("auto", "", Fake("wezterm.exe"));
            Check("resolve: legacy 'auto' migrates silently to first available", rs.Id == "wezterm" && rs.Write && rs.StaleLabel == null);
            rs = Terminals.Resolve("AUTO", "", none);
            Check("resolve: 'AUTO' with nothing installed -> console", rs.Id == "console" && rs.Write && rs.StaleLabel == null);
            rs = Terminals.Resolve("bogus", "", all);
            Check("resolve: unknown value treated as unset", rs.Id == "wt" && rs.Write && rs.StaleLabel == null);
            rs = Terminals.Resolve("alacritty", "", Fake("wt.exe"));
            Check("resolve: stale alacritty -> wt, writes, reports stale", rs.Id == "wt" && rs.Write && rs.StaleLabel == "Alacritty");
            rs = Terminals.Resolve("wt", "", none);
            Check("resolve: stale wt with nothing else -> console + stale", rs.Id == "console" && rs.Write && rs.StaleLabel == "Windows Terminal");
            rs = Terminals.Resolve("pwsh", "", Fake("pwsh.exe", "wt.exe"));
            Check("resolve: valid explicit choice kept, no write", rs.Id == "pwsh" && !rs.Write && rs.StaleLabel == null);
            rs = Terminals.Resolve("console", "", all);
            Check("resolve: explicit console kept even if wt present", rs.Id == "console" && !rs.Write);
            rs = Terminals.Resolve("custom", "mytty.exe -d \"{name}\"", Fake("mytty.exe"));
            Check("resolve: valid custom kept", rs.Id == "custom" && !rs.Write && rs.StaleLabel == null);
            rs = Terminals.Resolve("custom", "mytty.exe -d \"{name}\"", none);
            Check("resolve: custom with missing exe -> stale fallback", rs.Id == "console" && rs.Write && rs.StaleLabel == "Custom command");
            rs = Terminals.Resolve("custom", "", all);
            Check("resolve: custom with empty template -> stale fallback", rs.Id == "wt" && rs.StaleLabel == "Custom command");
            p = Terminals.Plan(null, "", "Ubuntu", wtOnly);
            Check("plan: unset id resolves via seed order", p.ResolvedId == "wt" && p.Warning == null);
            Check("real env: cmd.exe found on PATH", Terminals.OnPath("cmd.exe"));
            Console.WriteLine("  (info: real wt.exe available=" + Terminals.RealAvail("wt.exe") + ", on PATH=" + Terminals.OnPath("wt.exe")
                + ", WindowsApps\\wt.exe exists=" + File.Exists(Path.Combine(Terminals.WindowsAppsDir(), "wt.exe"))
                + ", alias sample winget.exe exists=" + File.Exists(Path.Combine(Terminals.WindowsAppsDir(), "winget.exe")) + ")");
        }

        static byte[] Cat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length); Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
