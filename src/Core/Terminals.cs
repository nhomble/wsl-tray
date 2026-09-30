using System;
using System.Collections.Generic;
using System.IO;

namespace WslTray.Core
{
    // Terminal presets: command templates with {name} placeholder. Everything here is pure except RealAvail.
    static class Terminals
    {
        public const string Wt = "wt", Console = "console", PowerShell = "powershell", Pwsh = "pwsh",
            WezTerm = "wezterm", Alacritty = "alacritty", Custom = "custom";
        // Display order in menu/combo (Custom is always last).
        public static readonly string[] AllIds = { Wt, WezTerm, Alacritty, Pwsh, Console, PowerShell, Custom };
        // First-run / unset / stale preference order.
        public static readonly string[] SeedOrder = { Wt, WezTerm, Alacritty, Pwsh, Console };
        public const string Placeholder = "{name}";

        // Returns null for unset, "auto" (legacy) or unknown values.
        public static string Normalize(string id)
        {
            foreach (string x in AllIds) if (string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }

        public static string Label(string id)
        {
            switch (id)
            {
                case Wt: return "Windows Terminal";
                case Console: return "Console window (cmd)";
                case PowerShell: return "Windows PowerShell window";
                case Pwsh: return "PowerShell 7 window";
                case WezTerm: return "WezTerm";
                case Alacritty: return "Alacritty";
                case Custom: return "Custom command...";
            }
            return id;
        }

        public static string Seed(Func<string, bool> avail)
        {
            foreach (string id in SeedOrder) if (IsAvailable(id, avail)) return id;
            return Console;
        }

        // Usable = detected as installed; a custom command must also be valid and its program found.
        static bool Usable(string id, string custom, Func<string, bool> avail)
        {
            if (id == Custom) return ValidateCustom(custom) == null && avail(ExeOf(custom));
            return IsAvailable(id, avail);
        }

        public static TermResolution Resolve(string stored, string custom, Func<string, bool> avail)
        {
            TermResolution r = new TermResolution();
            string n = Normalize(stored);
            if (n != null && Usable(n, custom, avail))
            {
                r.Id = n; r.Write = stored != n; // fix casing only
                return r;
            }
            r.Id = Seed(avail); r.Write = true;
            if (n != null) r.StaleLabel = Label(n).Replace("...", "");
            return r;
        }

        public static string Template(string id, string custom, Func<string, bool> avail)
        {
            switch (id)
            {
                case Wt: return "wt.exe wsl.exe -d \"{name}\" --cd ~";
                case Console: return "cmd.exe /c start \"WSL - {name}\" wsl.exe -d \"{name}\" --cd ~";
                case PowerShell: return "cmd.exe /c start \"WSL - {name}\" powershell.exe -NoExit -Command wsl.exe -d \"{name}\" --cd ~";
                case WezTerm:
                    return ((avail("wezterm-gui.exe") || !avail("wezterm.exe")) ? "wezterm-gui.exe" : "wezterm.exe") + " start -- wsl.exe -d \"{name}\" --cd ~";
                case Alacritty: return "alacritty.exe -e wsl.exe -d \"{name}\" --cd ~";
                case Pwsh: return "cmd.exe /c start \"WSL - {name}\" pwsh.exe -NoExit -Command wsl.exe -d \"{name}\" --cd ~";
                case Custom: return custom;
            }
            return null;
        }

        // First token of a template (honours a leading quoted path).
        public static string ExeOf(string t)
        {
            if (t == null) return "";
            t = t.Trim();
            if (t.StartsWith("\""))
            {
                int e = t.IndexOf('"', 1);
                return e < 0 ? t.Substring(1) : t.Substring(1, e - 1);
            }
            int sp = t.IndexOf(' ');
            return sp < 0 ? t : t.Substring(0, sp);
        }

        public static string ArgsOf(string t)
        {
            if (t == null) return "";
            t = t.Trim();
            int start;
            if (t.StartsWith("\""))
            {
                int e = t.IndexOf('"', 1);
                start = e < 0 ? t.Length : e + 1;
            }
            else
            {
                int sp = t.IndexOf(' ');
                start = sp < 0 ? t.Length : sp;
            }
            return t.Substring(start).Trim();
        }

        // null = valid, otherwise the reason.
        public static string ValidateCustom(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Trim().Length == 0) return "The custom command is empty.";
            if (t.IndexOf(Placeholder, StringComparison.Ordinal) < 0)
                return "The custom command must contain the " + Placeholder + " placeholder (the distro name), e.g.  wt.exe wsl.exe -d \"{name}\"";
            string exe = ExeOf(t);
            if (exe.Length == 0) return "The custom command has no program to run.";
            if (exe.IndexOf(Placeholder, StringComparison.Ordinal) >= 0) return "The program name itself cannot contain " + Placeholder + ".";
            return null;
        }

        public static string ValidateName(string n)
        {
            if (string.IsNullOrEmpty(n)) return "No distro selected.";
            if (n.IndexOf('"') >= 0) return "The distro name contains a double quote (\") and cannot be passed safely to a terminal command.";
            foreach (char c in n) if (c < 32) return "The distro name contains a control character.";
            if (n.EndsWith("\\")) return "The distro name ends with a backslash and cannot be passed safely to a terminal command.";
            return null;
        }

        public static bool IsAvailable(string id, Func<string, bool> avail)
        {
            switch (id)
            {
                case Wt: return avail("wt.exe");
                case WezTerm: return avail("wezterm-gui.exe") || avail("wezterm.exe");
                case Alacritty: return avail("alacritty.exe");
                case Pwsh: return avail("pwsh.exe");
            }
            return true; // console, powershell always; custom is validated separately
        }

        public static LaunchPlan Plan(string id, string custom, string name, Func<string, bool> avail)
        {
            LaunchPlan p = new LaunchPlan();
            string nerr = ValidateName(name);
            if (nerr != null) { p.Error = nerr; return p; }
            string r = Normalize(id) ?? Seed(avail);
            if (r == Custom)
            {
                string verr = ValidateCustom(custom);
                if (verr != null) { p.Warning = "Custom terminal command is invalid: " + verr + "\n\nFalling back to the Console window."; r = Console; }
                else if (!avail(ExeOf(custom)))
                { p.Warning = "The custom terminal program '" + ExeOf(custom) + "' was not found.\n\nFalling back to the Console window."; r = Console; }
            }
            else if (!IsAvailable(r, avail))
            {
                p.Warning = Label(r) + " was not found on this machine (it may have been uninstalled).\n\nFalling back to the Console window.";
                r = Console;
            }
            string t = Template(r, custom, avail);
            p.ResolvedId = r;
            p.Exe = ExeOf(t);
            p.Args = ArgsOf(t).Replace(Placeholder, name);
            return p;
        }

        // ---- real environment ----
        public static string WindowsAppsDir()
        {
            return Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", @"Microsoft\WindowsApps");
        }

        public static bool OnPath(string exe)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string d in path.Split(';'))
            {
                try { if (d.Length > 0 && File.Exists(Path.Combine(d.Trim('"'), exe))) return true; } catch (Exception) { }
            }
            return false;
        }

        static bool ExistsAny(string exe)
        {
            try
            {
                if (Path.IsPathRooted(exe)) return File.Exists(exe) || (Path.GetExtension(exe).Length == 0 && File.Exists(exe + ".exe"));
            }
            catch (Exception) { return false; }
            if (OnPath(exe)) return true;
            return exe.IndexOf('.') < 0 && OnPath(exe + ".exe");
        }

        // Well-known install locations for terminals whose installers don't always add PATH entries.
        static string KnownPath(string exe)
        {
            try
            {
                string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? "";
                string la = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "";
                string f = null;
                switch (exe.ToLowerInvariant())
                {
                    case "wt.exe": f = Path.Combine(WindowsAppsDir(), "wt.exe"); break;
                    case "wezterm-gui.exe": f = Path.Combine(pf, @"WezTerm\wezterm-gui.exe"); break;
                    case "wezterm.exe": f = Path.Combine(pf, @"WezTerm\wezterm.exe"); break;
                    case "alacritty.exe":
                        f = Path.Combine(pf, @"Alacritty\alacritty.exe");
                        if (!File.Exists(f)) f = Path.Combine(la, @"Programs\Alacritty\alacritty.exe");
                        break;
                }
                return f != null && File.Exists(f) ? f : null;
            }
            catch (Exception) { return null; }
        }

        public static bool RealAvail(string exe)
        {
            return ExistsAny(exe) || KnownPath(exe) != null;
        }

        // Full path for a launch when the exe was only found in a well-known location (not on PATH).
        public static string LaunchExe(string exe)
        {
            if (!Path.IsPathRooted(exe) && !OnPath(exe))
            {
                string f = KnownPath(exe);
                if (f != null) return f;
            }
            return exe;
        }

        // Only installed terminals (+ Custom, always offered), in display order.
        public static List<TermChoice> AvailableChoices(Func<string, bool> avail)
        {
            List<TermChoice> l = new List<TermChoice>();
            foreach (string id in AllIds)
                if (IsAvailable(id, avail)) l.Add(new TermChoice(id, Label(id)));
            return l;
        }
    }
}
