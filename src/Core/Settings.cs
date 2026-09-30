using System;
using Microsoft.Win32;

namespace WslTray.Core
{
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "WslTray";
        public static bool IsEnabled()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) { return k != null && k.GetValue(ValueName) != null; } }
            catch (Exception) { return false; }
        }
        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(ValueName, "\"" + System.Reflection.Assembly.GetEntryAssembly().Location + "\"");
                else k.DeleteValue(ValueName, false);
            }
        }
        const string SettingsKey = @"Software\WslTray";
        public static bool GetNotify()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey)) { return k != null && (k.GetValue("Notify") is int) && (int)k.GetValue("Notify") == 1; } }
            catch (Exception) { return false; }
        }
        public static void SetNotify(bool on)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey)) { k.SetValue("Notify", on ? 1 : 0, RegistryValueKind.DWord); } }
            catch (Exception) { }
        }
        static string GetTerminalRaw()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey)) { return k == null ? null : k.GetValue("Terminal") as string; } }
            catch (Exception) { return null; }
        }
        // Current explicit choice (no side effects). Unset/"auto"/unknown -> what seeding would pick.
        public static string GetTerminal()
        {
            string n = Terminals.Normalize(GetTerminalRaw());
            return n ?? Terminals.Seed(Terminals.RealAvail);
        }
        // Resolve unset / "auto" / stale values and write the result back so the setting is always explicit.
        public static TermResolution ResolveTerminal()
        {
            TermResolution r = Terminals.Resolve(GetTerminalRaw(), GetTerminalCustom(), Terminals.RealAvail);
            if (r.Write) SetTerminal(r.Id);
            return r;
        }
        public static void SetTerminal(string id)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey)) { k.SetValue("Terminal", Terminals.Normalize(id) ?? Terminals.Console, RegistryValueKind.String); } }
            catch (Exception) { }
        }
        public static string GetTerminalCustom()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey)) { return (k == null ? null : k.GetValue("TerminalCustom") as string) ?? ""; } }
            catch (Exception) { return ""; }
        }
        public static void SetTerminalCustom(string t)
        {
            try { using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey)) { k.SetValue("TerminalCustom", t ?? "", RegistryValueKind.String); } }
            catch (Exception) { }
        }
    }
}
