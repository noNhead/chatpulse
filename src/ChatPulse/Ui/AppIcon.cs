using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChatPulse.Ui;

/// <summary>
/// The application mark, drawn at runtime from the same geometry as <see cref="Controls.LogoMark"/>.
/// Keeps the repository free of binary assets and stops the window icon drifting from the in-app
/// logo. Also writes the <c>.ico</c> the build stamps onto the executable.
/// </summary>
public static class AppIcon
{
    public static BitmapSource CreateBitmap(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Draw(dc, size);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// ICO container holding one PNG per size. Windows has accepted PNG-compressed icon entries
    /// since Vista, which avoids hand-rolling the legacy BMP-with-AND-mask layout.
    /// </summary>
    public static byte[] BuildIco(params int[] sizes)
    {
        var pngs = sizes.Select(size =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(CreateBitmap(size)));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }).ToArray();

        using var output = new MemoryStream();
        using var w = new BinaryWriter(output);

        w.Write((ushort)0);           // reserved
        w.Write((ushort)1);           // type: 1 = icon
        w.Write((ushort)sizes.Length);

        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            // 256 is encoded as 0 in the single-byte dimension fields.
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);         // palette size
            w.Write((byte)0);         // reserved
            w.Write((ushort)1);       // colour planes
            w.Write((ushort)32);      // bits per pixel
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs) w.Write(png);
        w.Flush();
        return output.ToArray();
    }

    private static void Draw(DrawingContext dc, double s)
    {
        var background = new LinearGradientBrush(
            Color.FromRgb(0xB5, 0x7B, 0xFF), Color.FromRgb(0x6E, 0x3B, 0xE8),
            new Point(0, 0), new Point(1, 1));
        background.Freeze();

        dc.DrawRoundedRectangle(background, null, new Rect(0, 0, s, s), s * 0.28, s * 0.28);

        var pen = new Pen(System.Windows.Media.Brushes.White, s * 0.075)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(s * 0.16, s * 0.55), false, false);
            ctx.LineTo(new Point(s * 0.33, s * 0.55), true, false);
            ctx.LineTo(new Point(s * 0.42, s * 0.29), true, false);
            ctx.LineTo(new Point(s * 0.555, s * 0.78), true, false);
            ctx.LineTo(new Point(s * 0.645, s * 0.48), true, false);
            ctx.LineTo(new Point(s * 0.715, s * 0.55), true, false);
            ctx.LineTo(new Point(s * 0.84, s * 0.55), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
