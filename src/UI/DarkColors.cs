using System.Drawing;
using System.Windows.Forms;

namespace WslTray.UI
{
    // Dark palette for the status strip (the only ToolStrip that is themed in dark mode).
    class DarkColors : ProfessionalColorTable
    {
        public override Color StatusStripGradientBegin { get { return Color.FromArgb(32, 32, 32); } }
        public override Color StatusStripGradientEnd { get { return Color.FromArgb(32, 32, 32); } }
    }
}
