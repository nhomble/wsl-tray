using System.Text;

namespace WslTray.Core
{
    static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                else if (c < 32) sb.Append(string.Format("\\u{0:x4}", (int)c)); else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
        public static string Probe(Snapshot s)
        {
            StringBuilder b = new StringBuilder();
            b.Append("{\n  \"distros\": [\n");
            for (int i = 0; i < s.Distros.Count; i++)
            {
                Distro d = s.Distros[i];
                b.Append("    {\"name\": " + Str(d.Name) + ", \"version\": " + d.Version + ", \"default\": " + (d.IsDefault ? "true" : "false")
                    + ", \"running\": " + (s.Running == null ? "null" : (s.Running.Contains(d.Name) ? "true" : "false"))
                    + ", \"vhdxPath\": " + Str(d.VhdxPath) + ", \"vhdxBytes\": " + d.VhdxBytes + "}");
                b.Append(i < s.Distros.Count - 1 ? ",\n" : "\n");
            }
            b.Append("  ],\n  \"version\": " + Str(AppVersion.Display()) + ",\n  \"default\": " + Str(s.DefaultName) + ",\n");
            b.Append("  \"vmPresent\": " + (s.VmPresent ? "true" : "false") + ",\n  \"vmRamBytes\": " + s.VmRamBytes + ",\n");
            b.Append("  \"running\": ");
            if (s.Running == null) b.Append("null");
            else
            {
                b.Append("[");
                for (int i = 0; i < s.Running.Count; i++) b.Append((i > 0 ? ", " : "") + Str(s.Running[i]));
                b.Append("]");
            }
            b.Append(",\n  \"wslCall\": " + Str(s.WslCall) + ",\n  \"wslCallMs\": " + s.WslCallMs + ",\n  \"terminal\": " + Str(Startup.ResolveTerminal().Id) + "\n}\n");
            return b.ToString();
        }
    }
}
