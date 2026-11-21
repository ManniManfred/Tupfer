using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace Tupfer.Designer.Services;

/// <summary>
/// Eine SVG-Grafik als Bild (Svg.Skia). Sie bleibt eine Vektorgrafik: Auf dem Bildschirm, beim Drucken und im
/// PDF wird sie in der jeweiligen Auflösung gezeichnet, also in jeder Größe scharf. <see cref="Size"/> ist
/// die Größe der Zeichnung in CSS-Pixeln (96 je Zoll).
/// </summary>
public sealed class SvgImage : IImage
{
    /// <summary>Hält das Bild am Leben, das <see cref="SKSvg"/> besitzt.</summary>
    readonly SKSvg _svg;
    readonly SKPicture _picture;

    SvgImage(SKSvg svg, SKPicture picture)
    {
        _svg = svg;
        _picture = picture;
        var r = picture.CullRect;
        Size = new Size(r.Width, r.Height);
    }

    public Size Size { get; }

    /// <summary>Lädt eine SVG-Datei; wirft bei einer ungültigen oder leeren Grafik.</summary>
    public static SvgImage Load(string path)
    {
        var svg = new SKSvg();
        // Skripte in fremden Grafiken nie ausführen
        svg.Settings.EnableJavaScript = false;
        svg.Settings.EnableExternalJavaScript = false;
        if (InterFallback.Value is { } inter)
            svg.Settings.TypefaceProviders = [inter];
        SKPicture? picture;
        try
        {
            picture = svg.Load(path);
        }
        catch
        {
            svg.Dispose();
            throw;
        }
        if (picture == null || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
        {
            svg.Dispose();
            throw new FormatException("Keine gültige SVG-Grafik (leer oder ohne Größe).");
        }
        return new SvgImage(svg, picture);
    }

    /// <summary>
    /// Im Browser (und ohne Systemschriften) kennt Skia nur eine Ersatzschrift in fester Breite; dann wird jeder
    /// Text in Inter gesetzt, der Schrift der Oberfläche. null, wenn es Systemschriften gibt.
    /// </summary>
    static readonly Lazy<ITypefaceProvider?> InterFallback = new(() =>
        OperatingSystem.IsBrowser() || SKFontManager.Default.FontFamilyCount == 0 ? new InterTypefaceProvider() : null);

    sealed class InterTypefaceProvider : ITypefaceProvider
    {
        readonly SKTypeface? _regular = Open("Inter-Regular.ttf"), _bold = Open("Inter-Bold.ttf");

        static SKTypeface? Open(string file)
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/" + file));
                return SKTypeface.FromStream(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public SKTypeface? FromFamilyName(string fontFamily, SKFontStyleWeight weight, SKFontStyleWidth width, SKFontStyleSlant slant) =>
            (weight >= SKFontStyleWeight.SemiBold ? _bold : _regular) ?? _regular;
    }

    /// <summary>true für Dateien, die als SVG-Grafik geladen werden.</summary>
    public static bool IsSvg(string path) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Zeichnet den Ausschnitt <paramref name="sourceRect"/> (in <see cref="Size"/>) in <paramref name="destRect"/>.</summary>
    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
            return;
        var transform = Matrix.CreateTranslation(-sourceRect.X, -sourceRect.Y)
                        * Matrix.CreateScale(destRect.Width / sourceRect.Width, destRect.Height / sourceRect.Height)
                        * Matrix.CreateTranslation(destRect.X, destRect.Y);
        using (context.PushClip(destRect))
        using (context.PushTransform(transform))
            context.Custom(new DrawOperation(new Rect(Size), _picture));
    }

    /// <summary>Zeichnet das Skia-Bild direkt auf die Leinwand (Bildschirm wie <c>RenderTargetBitmap</c>).</summary>
    sealed class DrawOperation(Rect bounds, SKPicture picture) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) =>
            other is DrawOperation o && ReferenceEquals(o.Picture, picture) && o.Bounds == bounds;

        SKPicture Picture => picture;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            int saved = canvas.Save();
            var cull = picture.CullRect;
            canvas.ClipRect(new SKRect(0, 0, cull.Width, cull.Height));
            canvas.Translate(-cull.Left, -cull.Top);
            canvas.DrawPicture(picture);
            canvas.RestoreToCount(saved);
        }

        public void Dispose()
        {
        }
    }
}
