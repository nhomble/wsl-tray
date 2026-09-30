using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

// Generates wsl.ico (PNG-compressed entries): green circle with a white 'W'.
static class MkIco
{
    static byte[] Png(int s)
    {
        using (Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(34, 139, 87)))
            using (Font f = new Font("Segoe UI", s * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                g.FillEllipse(b, 0.5f, 0.5f, s - 1f, s - 1f);
                g.DrawString("W", f, Brushes.White, new RectangleF(0, s * 0.03f, s, s), sf);
            }
            using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }
    static int Main(string[] a)
    {
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] data = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) data[i] = Png(sizes[i]);
        using (BinaryWriter w = new BinaryWriter(File.Create(a[0])))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(data[i].Length); w.Write(off); off += data[i].Length;
            }
            for (int i = 0; i < sizes.Length; i++) w.Write(data[i]);
        }
        return 0;
    }
}
