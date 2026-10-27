using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Tupfer.Designer.Oid;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Services;

public static class ImageConverter
{
    /// <summary>Zeichnet ein Bild- oder Textelement; <paramref name="scale"/> = Pixel pro mm.</summary>
    public static void DrawElement(DrawingContext ctx, ElementViewModel e, double scale)
    {
        var rect = new Rect(e.X * scale, e.Y * scale, e.Width * scale, e.Height * scale);
        using (ctx.PushTransform(RotationAround(rect.Center, e.Rotation)))
        {
            if (e.Image is { } bmp)
            {
                var size = bmp.Size;
                var source = new Rect(e.CropX * size.Width, e.CropY * size.Height, e.CropWidth * size.Width, e.CropHeight * size.Height);
                ctx.DrawImage(bmp, source, rect);
            }
            else if (e.IsText)
            {
                ctx.DrawText(e.CreateText(scale), rect.TopLeft);
            }
            else if (e.IsShape)
            {
                if (e.Ellipse)
                    ctx.DrawEllipse(e.ColorBrush, null, rect);
                else
                    ctx.FillRectangle(e.ColorBrush, rect);
            }
        }
    }

    /// <summary>Drehung um <paramref name="center"/> im Uhrzeigersinn.</summary>
    public static Matrix RotationAround(Point center, double degrees) =>
        Matrix.CreateTranslation(-center.X, -center.Y)
        * Matrix.CreateRotation(degrees * Math.PI / 180)
        * Matrix.CreateTranslation(center.X, center.Y);

    /// <summary>Rendert Bilder und Texte einer Seite auf weißem Grund. null, wenn die Seite keine hat.</summary>
    public static RgbImage? RenderPage(PageViewModel page, int dpi = 300)
    {
        if (page.Elements.Count == 0)
            return null;
        double scale = dpi / 25.4;
        int w = Math.Max(1, (int)Math.Round(page.WidthMm * scale));
        int h = Math.Max(1, (int)Math.Round(page.HeightMm * scale));

        using var target = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            var rect = new Rect(0, 0, w, h);
            ctx.FillRectangle(Brushes.White, rect);
            using (ctx.PushClip(rect))
                foreach (var e in page.Elements)
                    DrawElement(ctx, e, scale);
        }
        return ToRgb(target);
    }

    static RgbImage ToRgb(RenderTargetBitmap target)
    {
        int w = target.PixelSize.Width, h = target.PixelSize.Height;
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
