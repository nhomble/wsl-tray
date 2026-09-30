// WslTray - notification-area utility showing WSL2 distros. C# 5 / .NET Framework 4.x, WinForms.
// Switches: --probe (JSON to stdout), --selftest, --autostart on|off
// Layout: src\Program.cs (entry) | src\Core (WSL/terminal/settings logic, no WinForms/Drawing) |
//         src\Interop (P/Invoke) | src\UI (WinForms tray + window) | tests\SelfTest.cs (--selftest).
// Dependencies: UI -> Core -> Interop; enforced by the core-check step in build.cmd.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using WslTray.Core;
using WslTray.Interop;
using WslTray.Tests;
using WslTray.UI;

namespace WslTray
{
    static class Program
    {
        static void WriteOut(string text)
        {
            Native.AttachConsole(-1);
            IntPtr h = Native.GetStdHandle(-11);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                h = Native.CreateFile("CONOUT$", 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return;
            using (FileStream fs = new FileStream(new SafeFileHandle(h, false), FileAccess.Write))
            {
                byte[] b = new UTF8Encoding(false).GetBytes(text);
                fs.Write(b, 0, b.Length); fs.Flush();
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            string a0 = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (a0 == "--probe") { WriteOut(Json.Probe(Wsl.Collect())); return 0; }
            if (a0 == "--selftest")
            {
                StringWriter sw = new StringWriter();
                TextWriter old = Console.Out; Console.SetOut(sw);
                int rc = SelfTest.Run();
                Console.SetOut(old);
                WriteOut(sw.ToString());
                return rc;
            }
            if (a0 == "--autostart")
            {
                string v = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                if (v != "on" && v != "off") { WriteOut("usage: WslTray.exe --autostart on|off\n"); return 2; }
                try { Startup.Set(v == "on"); WriteOut("autostart " + v + "\n"); return 0; }
                catch (Exception ex) { WriteOut("error: " + ex.Message + "\n"); return 1; }
            }

            bool created;
            using (Mutex m = new Mutex(true, @"Local\WslTrayApp", out created))
            {
                if (!created)
                {
                    Native.PostMessage(Native.HWND_BROADCAST, MainForm.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(a0 == "--show"));
                GC.KeepAlive(m);
            }
            return 0;
        }
    }
}
