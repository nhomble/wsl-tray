using System;
using System.Windows.Forms;
using WslTray.Interop;

namespace WslTray.UI
{
    // ListView with double buffering and the Explorer visual style (applied whenever the handle is (re)created).
    class ThemedList : ListView
    {
        public string Theme = "Explorer";
        public ThemedList() { DoubleBuffered = true; }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                Native.SetWindowTheme(Handle, Theme, null);
                if (Theme != "Explorer") Native.SetWindowTheme(Native.SendMessage(Handle, 0x101F /*LVM_GETHEADER*/, IntPtr.Zero, IntPtr.Zero), "DarkMode_ItemsView", null);
            }
            catch (Exception) { }
        }
    }
}
