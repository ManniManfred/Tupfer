using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Services;

/// <summary>
/// Erkennt Personen auf einem Bild und liefert ihre Umrisse als Polygone in Seitenkoordinaten (mm).
/// Die Segmentierung macht DeepLab v3 (MediaPipe, Pascal VOC) über <see cref="Segmenter"/>: am Desktop
/// mit ONNX Runtime, im Browser mit onnxruntime-web. Personen, die sich berühren, ergeben einen
/// gemeinsamen Umriss.
/// </summary>
public static class PersonDetector
{
    /// <summary>Kantenlänge des Modell-Eingangs (Pixel).</summary>
    public const int Size = 257;

    /// <summary>
    /// Segmentierung: RGB-Bild (Size × Size × 3 Byte, Zeile für Zeile) → Maske (Size × Size Byte, 1 = Person).
    /// null = auf dieser Plattform nicht verfügbar.
    /// </summary>
    public static Func<byte[], Task<byte[]>>? Segmenter { get; set; }

    /// <summary>Kleinste Person als Anteil am Bild (kleinere Flecken werden ignoriert).</summary>
    public const double MinShare = 0.003;

    /// <summary>Größte Abweichung des vereinfachten Umrisses von der Maske (Pixel des Modells).</summary>
    public const double Tolerance = 1.2;

    /// <summary>Umrisse der Personen auf dem sichtbaren Ausschnitt des Bilds, in Seitenkoordinaten (mm).</summary>
    public static async Task<List<List<AreaPoint>>> DetectAsync(ElementViewModel image)
    {
        if (Segmenter is not { } segment)
            throw new PlatformNotSupportedException("Die Personenerkennung ist hier nicht verfügbar.");
        if (image.Image is not { } bitmap)
            throw new InvalidOperationException($"Das Bild „{image.FileName}“ fehlt.");
        if (image.Width <= 0 || image.Height <= 0)
            return [];

        // Sichtbarer Ausschnitt mit dem Seitenverhältnis des Rahmens, oben links in das Quadrat eingepasst
        double aspect = image.Width / image.Height;
        int w = aspect >= 1 ? Size : Math.Max(1, (int)Math.Round(Size * aspect));
        int h = aspect >= 1 ? Math.Max(1, (int)Math.Round(Size / aspect)) : Size;
        var size = bitmap.Size;
        var source = new Rect(image.CropX * size.Width, image.CropY * size.Height, image.CropWidth * size.Width, image.CropHeight * size.Height);
        var rgb = ImageConverter.RenderRegion(bitmap, source, w, h, Size, Size);

        var mask = await segment(rgb);
        var entry = image.ToEntry(null);
        return Outlines(mask, Size, w, h).Select(outline => outline.Select(p =>
        {
            var q = ElementViewModel.ToPage(entry, new Point(p.X / w * image.Width, p.Y / h * image.Height));
            return new AreaPoint(Math.Round(q.X, 2), Math.Round(q.Y, 2));
        }).ToList()).ToList();
    }

    static readonly (int X, int Y)[] Ring = [(-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1)];

    /// <summary>
    /// Umrisse der zusammenhängenden Flächen (8er-Nachbarschaft) einer Maske, vereinfacht; Punkte in
    /// Pixeln (Mitte der Randpixel). Ausgewertet wird nur der Bereich <paramref name="width"/> ×
    /// <paramref name="height"/> oben links; Löcher werden mitgefüllt, die größte Fläche kommt zuerst.
    /// </summary>
    public static List<List<(double X, double Y)>> Outlines(byte[] mask, int stride, int width, int height,
        double minShare = MinShare, double tolerance = Tolerance)
    {
        var label = new int[width * height];
        bool In(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && mask[y * stride + x] != 0;
        int minPixels = Math.Max(12, (int)(minShare * width * height));
        var result = new List<(int Pixels, List<(double X, double Y)> Outline)>();
        int next = 0;
        var queue = new Queue<(int X, int Y)>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (!In(x, y) || label[y * width + x] != 0)
                    continue;
                // Fläche füllen und zählen; der erste Pixel ist der oberste linke (Start des Umrisses)
                int id = ++next, pixels = 0;
                label[y * width + x] = id;
                queue.Enqueue((x, y));
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    pixels++;
                    foreach (var (dx, dy) in Ring)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (In(nx, ny) && label[ny * width + nx] == 0)
                        {
                            label[ny * width + nx] = id;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                if (pixels < minPixels)
                    continue;
                var traced = Trace((x, y), (px, py) => px >= 0 && py >= 0 && px < width && py < height && label[py * width + px] == id);
                var simple = Simplify(traced.Select(p => (p.X + 0.5, p.Y + 0.5)).ToList(), tolerance);
                if (simple.Count >= 3)
                    result.Add((pixels, simple));
            }
        return result.OrderByDescending(r => r.Pixels).Select(r => r.Outline).ToList();
    }

    /// <summary>Äußerer Umriss einer Fläche (Moore-Nachbarschaft), beginnend beim obersten linken Pixel.</summary>
    static List<(int X, int Y)> Trace((int X, int Y) start, Func<int, int, bool> inside)
    {
        var points = new List<(int X, int Y)>();
        var current = start;
        int back = 0; // links vom Start liegt kein Pixel der Fläche
        (int X, int Y)? first = null;
        for (int guard = 0; guard < 1_000_000; guard++)
        {
            int found = -1;
            for (int k = 1; k <= 8; k++)
            {
                int i = (back + k) % 8;
                if (inside(current.X + Ring[i].X, current.Y + Ring[i].Y))
                {
                    found = i;
                    break;
                }
            }
            if (found < 0)
            {
                points.Add(current); // einzelner Pixel
                break;
            }
            var nextPoint = (current.X + Ring[found].X, current.Y + Ring[found].Y);
            if (first == null)
                first = nextPoint;
            else if (current == start && nextPoint == first)
                break;
            points.Add(current);
            // Neuer Rückweg: die zuletzt geprüfte freie Stelle, vom neuen Punkt aus gesehen
            var free = (X: current.X + Ring[(found + 7) % 8].X, Y: current.Y + Ring[(found + 7) % 8].Y);
            back = Array.IndexOf(Ring, (free.X - nextPoint.Item1, free.Y - nextPoint.Item2));
            current = nextPoint;
        }
        return points;
    }

    /// <summary>Vereinfacht einen geschlossenen Umriss (Douglas-Peucker).</summary>
    public static List<(double X, double Y)> Simplify(List<(double X, double Y)> points, double tolerance)
    {
        if (points.Count < 4)
            return points;
        // In zwei Ketten teilen: vom ersten Punkt zum entferntesten und zurück
        int far = 0;
        double best = -1;
        for (int i = 1; i < points.Count; i++)
        {
            double d = Dist2(points[0], points[i]);
            if (d > best)
            {
                best = d;
                far = i;
            }
        }
        var keep = new bool[points.Count];
        keep[0] = keep[far] = true;
        var closed = points.Append(points[0]).ToList();
        Mark(closed, 0, far, tolerance, keep);
        Mark(closed, far, points.Count, tolerance, keep);
        return points.Where((_, i) => keep[i]).ToList();
    }

    static void Mark(List<(double X, double Y)> p, int from, int to, double tolerance, bool[] keep)
    {
        if (to - from < 2)
            return;
        int index = -1;
        double max = tolerance;
        for (int i = from + 1; i < to; i++)
        {
            double d = SegmentDistance(p[i], p[from], p[to]);
            if (d > max)
            {
                max = d;
                index = i;
            }
        }
        if (index < 0)
            return;
        keep[index] = true;
        Mark(p, from, index, tolerance, keep);
        Mark(p, index, to, tolerance, keep);
    }

    static double Dist2((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    static double SegmentDistance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double len2 = Dist2(a, b);
        if (len2 < 1e-12)
            return Math.Sqrt(Dist2(p, a));
        double t = Math.Clamp(((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / len2, 0, 1);
        return Math.Sqrt(Dist2(p, (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y))));
    }
}
