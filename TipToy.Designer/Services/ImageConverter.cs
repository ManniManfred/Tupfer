using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TipToy.Designer.Oid;

namespace TipToy.Designer.Services;

public static class ImageConverter
{
    /// <summary>
    /// Rendert ein Bild auf weißem Grund in ein RGB-Raster. Die Auflösung wird auf
    /// <paramref name="maxDpi"/> bezogen auf die Seitengröße begrenzt, um die PDF klein zu halten.
    /// </summary>
    public static RgbImage ToRgb(Bitmap bitmap, double pageWidthMm, double pageHeightMm, int maxDpi = 300)
    {
        int maxW = (int)Math.Round(pageWidthMm / 25.4 * maxDpi);
        int maxH = (int)Math.Round(pageHeightMm / 25.4 * maxDpi);
        int w = Math.Max(1, Math.Min(bitmap.PixelSize.Width, maxW));
        int h = Math.Max(1, Math.Min(bitmap.PixelSize.Height, maxH));

        using var target = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var rect = new Rect(0, 0, w, h);
            ctx.FillRectangle(Brushes.White, rect);
            ctx.DrawImage(bitmap, rect);
        }

        int stride = w * 4;
        var buffer = new byte[stride * h];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        bool bgra = target.Format == null || target.Format == PixelFormat.Bgra8888;
        var rgb = new byte[w * h * 3];
        for (int i = 0, j = 0; i < buffer.Length; i += 4, j += 3)
        {
            rgb[j] = buffer[bgra ? i + 2 : i];
            rgb[j + 1] = buffer[i + 1];
            rgb[j + 2] = buffer[bgra ? i : i + 2];
        }
        return new RgbImage(w, h, rgb);
    }
}
