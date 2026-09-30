using System;
using System.Windows.Forms;
using WslTray.Interop;

namespace WslTray.UI
{
    class MainForm : Form
    {
        public static readonly uint WM_SHOWME = Native.RegisterWindowMessage("WslTray.ShowMe");
        public Action ShowRequested;
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)WM_SHOWME && ShowRequested != null) { ShowRequested(); return; }
            base.WndProc(ref m);
        }
    }
}
