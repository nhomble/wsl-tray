using System.Drawing;
using System.Windows.Forms;

namespace WslTray.UI
{
    // Dark palette for the status strip and the tray menu.
    class DarkColors : ProfessionalColorTable
    {
        public override Color StatusStripGradientBegin { get { return Color.FromArgb(32, 32, 32); } }
        public override Color StatusStripGradientEnd { get { return Color.FromArgb(32, 32, 32); } }
        static readonly Color Back = Color.FromArgb(43, 43, 43), Sel = Color.FromArgb(75, 75, 75), Line = Color.FromArgb(85, 85, 85);
        public override Color ToolStripDropDownBackground { get { return Back; } }
        public override Color ImageMarginGradientBegin { get { return Back; } }
        public override Color ImageMarginGradientMiddle { get { return Back; } }
        public override Color ImageMarginGradientEnd { get { return Back; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Sel; } }
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color MenuItemPressedGradientBegin { get { return Sel; } }
        public override Color MenuItemPressedGradientMiddle { get { return Sel; } }
        public override Color MenuItemPressedGradientEnd { get { return Sel; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Line; } }
        public override Color CheckBackground { get { return Sel; } }
        public override Color CheckSelectedBackground { get { return Sel; } }
        public override Color CheckPressedBackground { get { return Sel; } }
    }
}
