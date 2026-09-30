using System.Reflection;
using System.Text.RegularExpressions;

namespace WslTray.Core
{
    // Version text for the footer / --probe. The raw value is the AssemblyInformationalVersion stamped by build.cmd
    // from WSLTRAY_VERSION ("dev" for local builds, e.g. "0.1.1" for releases).
    static class AppVersion
    {
        static readonly Regex Release = new Regex(@"^\d+\.\d+\.\d+([-+.][0-9A-Za-z.+-]*)?$");

        // Pure display rule: null/empty/"dev" -> "dev build"; release-looking -> "v" + value; anything else as-is.
        public static string Format(string raw)
        {
            if (raw == null) return "dev build";
            raw = raw.Trim();
            if (raw.Length == 0 || raw == "dev") return "dev build";
            return Release.IsMatch(raw) ? "v" + raw : raw;
        }

        public static string Display()
        {
            string raw = null;
            try
            {
                Assembly a = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                object[] attrs = a.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (attrs.Length > 0) raw = ((AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion;
            }
            catch { }
            return Format(raw);
        }
    }
}
