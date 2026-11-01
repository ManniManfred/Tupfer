using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Services;

/// <summary>
/// Erkennt Personen auf einem Bild und liefert ihre Umrisse als Polygone in Seitenkoordinaten (mm).
/// Die Instanz-Segmentierung RF-DETR-Seg (über <see cref="Segmenter"/>) liefert jede Person mit eigener
/// Maske, auch wenn sich Personen berühren oder verdecken. Bei großen Gruppen sind die Personen auf dem
/// ganzen Bild zu klein; dann wird zusätzlich in Kacheln gesucht, deren Größe sich nach den Gesichtern
/// richtet (YuNet über <see cref="FaceDetector"/>). Gesichter ohne gefundene Person holen schwächere
/// Treffer dazu. Beide Modelle laufen am Desktop mit ONNX Runtime, im Browser mit onnxruntime-web.
/// </summary>
public static class PersonDetector
{
    /// <summary>Kantenlänge des Eingangs der Segmentierung (Pixel).</summary>
    public const int Size = 384;

    /// <summary>Kantenlänge einer Maske der Segmentierung (Pixel).</summary>
    public const int MaskSize = Size / 4;

    /// <summary>Zahl der Kandidaten je Lauf der Segmentierung.</summary>
    public const int Candidates = 100;

    /// <summary>
    /// Segmentierung: RGB-Bild (Size × Size × 3 Byte, Zeile für Zeile) → je Kandidat der Wert „Person“
    /// (0 … 1) und seine Maske (MaskSize × MaskSize Byte: Logit · 16 + 128, über 128 = gehört dazu).
    /// null = auf dieser Plattform nicht verfügbar.
    /// </summary>
    public static Func<byte[], Task<(float[] Scores, byte[] Masks)>>? Segmenter { get; set; }

    /// <summary>
    /// Gesichtserkennung: RGB-Bild, Breite, Höhe (Vielfache von 32) → je Ankerpunkt fünf Werte, siehe
    /// <see cref="DecodeFaces"/>. null = nicht verfügbar (dann nur auf dem ganzen Bild).
    /// </summary>
    public static Func<byte[], int, int, Task<float[]>>? FaceDetector { get; set; }

    /// <summary>Größte Abweichung des vereinfachten Umrisses von der Maske (Pixel der Maske).</summary>
    public const double Tolerance = 1.2;

    /// <summary>Längere Seite des Bilds für die Gesichtserkennung (Pixel).</summary>
    public const int FaceInput = 1280;

    /// <summary>Mindestwert eines Gesichts (0 … 1).</summary>
    public const float MinFaceScore = 0.8f;

    /// <summary>Mindestwert einer Person (0 … 1).</summary>
    public const double MinScore = 0.4;

    /// <summary>Mindestwert einer Person, deren Gesicht erkannt wurde, wenn sonst niemand dort ist.</summary>
    public const double MinScoreWithFace = 0.15;

    /// <summary>Kantenlänge einer Kachel in Gesichtshöhen (eine stehende Person hat etwa acht).</summary>
    const double TileFaces = 12;

    /// <summary>Kacheln nur, wenn sie höchstens diesen Anteil der längeren Bildseite haben.</summary>
    const double MaxTileShare = 0.6;

    /// <summary>Höchstens so viele Kacheln (sonst werden sie größer).</summary>
    const int MaxTiles = 16;

    /// <summary>Höchstens so viele Pixel hat die längere Seite der Masken im ganzen Bild.</summary>
    const int MaxCanvas = 1200;

    /// <summary>
    /// Zwei Masken, deren Schnitt mindestens diesen Anteil ihrer Vereinigung hat, sind dieselbe Person
    /// (aus zwei Läufen). Ein Kind auf dem Arm liegt ganz in der Maske des Erwachsenen, bleibt aber eigene Person.
    /// </summary>
    const double SamePerson = 0.5;

    /// <summary>Anteil ihrer Maske, den eine Person nach der Zuteilung der Pixel mindestens behalten muss.</summary>
    const double MinVisible = 0.5;

    /// <summary>Ein Gesicht (Rahmen in Pixeln).</summary>
    public readonly record struct Face(double X, double Y, double Width, double Height, double Score)
    {
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;
        public double Bottom => Y + Height;
    }

    /// <summary>
    /// Eine gefundene Person: Wert und Maske als Logits (Logit · 16 + 128) im Rechteck
    /// <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/> × <see cref="Height"/> (Pixel der Maske des ganzen Bilds).
    /// </summary>
    public sealed record Candidate(double Score, int X, int Y, int Width, int Height, byte[] Logits)
    {
        /// <summary>Pixel, die zur Person gehören.</summary>
        public int Area { get; } = Logits.Count(v => v > 128);

        public bool Covers(int x, int y) =>
            x >= X && y >= Y && x < X + Width && y < Y + Height && Logits[(y - Y) * Width + x - X] > 128;
    }

    /// <summary>
    /// Zeichnet einen Bereich des sichtbaren Bilds (Pixel; außerhalb des Bilds schwarz) auf
    /// Breite × Höhe Pixel; liefert die RGB-Daten Zeile für Zeile.
    /// </summary>
    public delegate byte[] Renderer(Rect region, int width, int height);

    /// <summary>
    /// Umrisse der Personen auf dem sichtbaren Ausschnitt des Bilds, in Seitenkoordinaten (mm): je Person
    /// eine oder mehrere Flächen (mehrere, wenn etwas vor der Person steht), von links nach rechts.
    /// </summary>
    public static async Task<List<List<List<AreaPoint>>>> DetectAsync(ElementViewModel image)
    {
        if (Segmenter == null)
            throw new PlatformNotSupportedException("Die Personenerkennung ist hier nicht verfügbar.");
        if (image.Image is not { } bitmap)
            throw new InvalidOperationException($"Das Bild „{image.FileName}“ fehlt.");
        if (image.Width <= 0 || image.Height <= 0)
            return [];

        // Sichtbarer Ausschnitt in Pixeln des Bilds, gestreckt auf das Seitenverhältnis des Rahmens
        var size = bitmap.Size;
        var source = new Rect(image.CropX * size.Width, image.CropY * size.Height, image.CropWidth * size.Width, image.CropHeight * size.Height);
        double viewWidth = Math.Max(1, source.Width), viewHeight = viewWidth * image.Height / image.Width;
        byte[] Render(Rect region, int width, int height)
        {
            var clip = region.Intersect(new Rect(0, 0, viewWidth, viewHeight));
            if (clip.Width <= 0 || clip.Height <= 0)
                return new byte[width * height * 3];
            double sx = width / region.Width, sy = height / region.Height;
            var destination = new Rect((clip.X - region.X) * sx, (clip.Y - region.Y) * sy, clip.Width * sx, clip.Height * sy);
            var from = new Rect(source.X + clip.X / viewWidth * source.Width, source.Y + clip.Y / viewHeight * source.Height,
                clip.Width / viewWidth * source.Width, clip.Height / viewHeight * source.Height);
            return ImageConverter.RenderRegion(bitmap, from, destination, width, height);
        }

        var persons = await DetectAsync(Render, viewWidth, viewHeight);
        var entry = image.ToEntry(null);
        return persons.Select(person => person.Select(outline => outline.Select(p =>
        {
            var q = ElementViewModel.ToPage(entry, new Point(
                Math.Clamp(p.X / viewWidth, 0, 1) * image.Width, Math.Clamp(p.Y / viewHeight, 0, 1) * image.Height));
            return new AreaPoint(Math.Round(q.X, 2), Math.Round(q.Y, 2));
        }).ToList()).ToList()).ToList();
    }

    /// <summary>
    /// Personen auf einem Bild der Größe <paramref name="width"/> × <paramref name="height"/> (Pixel):
    /// je Person ihre Umrisse in Pixeln, von links nach rechts.
    /// </summary>
    public static async Task<List<List<List<(double X, double Y)>>>> DetectAsync(Renderer render, double width, double height)
    {
        var segment = Segmenter ?? throw new PlatformNotSupportedException("Die Personenerkennung ist hier nicht verfügbar.");
        var faces = FaceDetector is { } detect ? await FindFacesAsync(detect, render, width, height) : [];

        // Das ganze Bild (auf das Quadrat gestreckt, so ist das Modell trainiert) und bei kleinen
        // Personen Kacheln über den Bereich um die Gesichter
        var regions = new List<Rect> { new(0, 0, width, height) };
        if (faces.Count > 0)
        {
            double faceHeight = Median(faces.Select(f => f.Height));
            double x0 = Math.Max(0, faces.Min(f => f.X - 2 * f.Width)), x1 = Math.Min(width, faces.Max(f => f.X + 3 * f.Width));
            double y0 = Math.Max(0, faces.Min(f => f.Y - f.Height)), y1 = Math.Min(height, faces.Max(f => f.Y + 9 * f.Height));
            double tile = TileFaces * faceHeight;
            static int Count(double length, double tile) => length <= tile ? 1 : (int)Math.Ceiling((length - tile) / (tile / 2)) + 1;
            while (Count(x1 - x0, tile) * Count(y1 - y0, tile) > MaxTiles)
                tile *= 1.25;
            static double Position(double start, double length, double tile, int i, int n) =>
                n == 1 ? start + (length - tile) / 2 : start + i * (length - tile) / (n - 1);
            if (tile <= MaxTileShare * Math.Max(width, height))
            {
                int nx = Count(x1 - x0, tile), ny = Count(y1 - y0, tile);
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                        regions.Add(new Rect(Position(x0, x1 - x0, tile, i, nx), Position(y0, y1 - y0, tile, j, ny), tile, tile));
            }
        }

        // Masken aller Läufe im gemeinsamen Raster; ein Pixel = cell Pixel des Bilds
        double cell = Math.Max(width, height) / MaxCanvas;
        int w = Math.Max(1, (int)Math.Ceiling(width / cell)), h = Math.Max(1, (int)Math.Ceiling(height / cell));
        var centers = faces.Select(f => ((int)(f.CenterX / cell), (int)(f.CenterY / cell))).ToList();
        var candidates = new List<Candidate>();
        for (int r = 0; r < regions.Count; r++)
        {
            var (scores, masks) = await segment(render(regions[r], Size, Size));
            candidates.AddRange(ToCandidates(scores, masks, regions[r], r > 0, width, height, cell, w, h, centers));
        }

        return await Task.Run(() =>
        {
            var persons = Select(candidates, centers);
            var label = Assign(persons, w, h);
            // Wer nach der Zuteilung weniger als die Hälfte seiner Maske behält, war nur ein Stück
            // einer anderen Person
            var kept = new int[persons.Count];
            foreach (int k in label)
                if (k >= 0)
                    kept[k]++;
            if (Enumerable.Range(0, persons.Count).Any(k => kept[k] < MinVisible * persons[k].Area))
            {
                persons = persons.Where((p, k) => kept[k] >= MinVisible * p.Area).ToList();
                label = Assign(persons, w, h);
            }
            return PersonOutlines(label, w, h, persons.Count)
                .Select(person => person.Select(o => o.Select(p => (p.X * cell, p.Y * cell)).ToList()).ToList())
                .ToList();
        });
    }

    /// <summary>
    /// Kandidaten eines Laufs als Masken im gemeinsamen Raster (<paramref name="cell"/> Pixel des Bilds je
    /// Pixel, <paramref name="width"/> × <paramref name="height"/> Pixel des Bilds). Schwache Kandidaten
    /// bleiben nur, wenn sie ein Gesicht bedecken; aus Kacheln fallen Personen weg, die an einer Kante
    /// im Bild abgeschnitten sind (sie liegen ganz in einer anderen Kachel oder im ganzen Bild).
    /// </summary>
    static IEnumerable<Candidate> ToCandidates(float[] scores, byte[] masks, Rect region, bool tile,
        double width, double height, double cell, int canvasWidth, int canvasHeight, IReadOnlyList<(int X, int Y)> faces)
    {
        const int n = MaskSize;
        for (int q = 0; q < Math.Min(Candidates, scores.Length); q++)
        {
            if (scores[q] < MinScoreWithFace)
                continue;
            int offset = q * n * n;
            int minX = n, maxX = -1, minY = n, maxY = -1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (masks[offset + y * n + x] > 128)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
            if (maxX < 0 || (maxX - minX + 1) * (maxY - minY + 1) < 4)
                continue;
            if (tile && ((minX <= 1 && region.X > 1) || (maxX >= n - 2 && region.Right < width - 1)
                || (minY <= 1 && region.Y > 1) || (maxY >= n - 2 && region.Bottom < height - 1)))
                continue;

            // Rechteck im Raster, Logits bilinear
            double sx = region.Width / n, sy = region.Height / n;
            int x0 = Math.Max(0, (int)((region.X + minX * sx) / cell) - 1), x1 = Math.Min(canvasWidth, (int)Math.Ceiling((region.X + (maxX + 1) * sx) / cell) + 1);
            int y0 = Math.Max(0, (int)((region.Y + minY * sy) / cell) - 1), y1 = Math.Min(canvasHeight, (int)Math.Ceiling((region.Y + (maxY + 1) * sy) / cell) + 1);
            if (x1 <= x0 || y1 <= y0)
                continue;
            var logits = new byte[(x1 - x0) * (y1 - y0)];
            for (int y = y0; y < y1; y++)
            {
                double my = Math.Clamp(((y + 0.5) * cell - region.Y) / sy - 0.5, 0, n - 1);
                int ya = (int)my, yb = Math.Min(ya + 1, n - 1);
                double fy = my - ya;
                for (int x = x0; x < x1; x++)
                {
                    double mx = Math.Clamp(((x + 0.5) * cell - region.X) / sx - 0.5, 0, n - 1);
                    int xa = (int)mx, xb = Math.Min(xa + 1, n - 1);
                    double fx = mx - xa;
                    double v = (masks[offset + ya * n + xa] * (1 - fx) + masks[offset + ya * n + xb] * fx) * (1 - fy)
                        + (masks[offset + yb * n + xa] * (1 - fx) + masks[offset + yb * n + xb] * fx) * fy;
                    logits[(y - y0) * (x1 - x0) + x - x0] = (byte)Math.Round(v);
                }
            }
            var candidate = new Candidate(scores[q], x0, y0, x1 - x0, y1 - y0, logits);
            if (candidate.Area > 0 && (scores[q] >= MinScore || faces.Any(f => candidate.Covers(f.X, f.Y))))
                yield return candidate;
        }
    }

    /// <summary>
    /// Wählt die Personen aus: Kandidaten ab <see cref="MinScore"/>, der stärkste zuerst, Doppelte
    /// (dieselbe Person aus mehreren Läufen) fallen weg. Bedeckt keine Person ein Gesicht
    /// (<paramref name="faces"/>, Pixel der Maske), kommt der stärkste schwächere Kandidat dort dazu.
    /// </summary>
    public static List<Candidate> Select(IEnumerable<Candidate> candidates, IReadOnlyList<(int X, int Y)> faces)
    {
        var sorted = candidates.OrderByDescending(c => c.Score).ToList();
        var kept = new List<Candidate>();
        foreach (var c in sorted)
            if (c.Score >= MinScore && kept.All(k => Overlap(c, k) < SamePerson))
                kept.Add(c);
        foreach (var (x, y) in faces)
        {
            if (kept.Any(k => k.Covers(x, y)))
                continue;
            if (sorted.FirstOrDefault(c => c.Score < MinScore && c.Covers(x, y) && kept.All(k => Overlap(c, k) < SamePerson)) is { } found)
                kept.Add(found);
        }
        return kept;
    }

    /// <summary>Gemeinsame Pixel zweier Masken im Verhältnis zu ihrer Vereinigung.</summary>
    static double Overlap(Candidate a, Candidate b)
    {
        int x0 = Math.Max(a.X, b.X), x1 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y0 = Math.Max(a.Y, b.Y), y1 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        int common = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (a.Logits[(y - a.Y) * a.Width + x - a.X] > 128 && b.Logits[(y - b.Y) * b.Width + x - b.X] > 128)
                    common++;
        return common / (double)Math.Max(1, a.Area + b.Area - common);
    }

    /// <summary>Je Pixel die Person mit dem höchsten Logit (−1 = keine).</summary>
    public static int[] Assign(IReadOnlyList<Candidate> persons, int width, int height)
    {
        var label = new int[width * height];
        var best = new byte[width * height];
        Array.Fill(label, -1);
        Array.Fill(best, (byte)128);
        for (int k = 0; k < persons.Count; k++)
        {
            var c = persons[k];
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    int i = (c.Y + y) * width + c.X + x;
                    byte v = c.Logits[y * c.Width + x];
                    if (v > best[i])
                    {
                        best[i] = v;
                        label[i] = k;
                    }
                }
        }
        return label;
    }

    /// <summary>Gesichter auf dem Bild (Rahmen in Pixeln des Bilds).</summary>
    static async Task<List<Face>> FindFacesAsync(Func<byte[], int, int, Task<float[]>> detect, Renderer render, double width, double height)
    {
        double scale = FaceInput / Math.Max(width, height);
        int w = Math.Max(32, (int)Math.Ceiling(width * scale / 32) * 32), h = Math.Max(32, (int)Math.Ceiling(height * scale / 32) * 32);
        var output = await detect(render(new Rect(0, 0, w / scale, h / scale), w, h), w, h);
        return DecodeFaces(output, w, h, MinFaceScore)
            .Select(f => new Face(f.X / scale, f.Y / scale, f.Width / scale, f.Height / scale, f.Score)).ToList();
    }

    /// <summary>
    /// Wertet die Ausgabe von YuNet aus (siehe tools/personen-modell/gesichter.py): je Ankerpunkt Wert,
    /// dx, dy, log w, log h; Ankerpunkte mit Schrittweite 8, 16 und 32 Pixel, jeweils Zeile für Zeile.
    /// Überlappende Gesichter werden zusammengefasst (das mit dem höchsten Wert bleibt).
    /// </summary>
    public static List<Face> DecodeFaces(float[] output, int width, int height, float minScore)
    {
        var found = new List<Face>();
        int index = 0;
        foreach (int stride in (int[])[8, 16, 32])
        {
            int cols = width / stride, rows = height / stride;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++, index += 5)
                {
                    if (index + 4 >= output.Length || output[index] < minScore)
                        continue;
                    double cx = (c + output[index + 1]) * stride, cy = (r + output[index + 2]) * stride;
                    double w = Math.Exp(output[index + 3]) * stride, h = Math.Exp(output[index + 4]) * stride;
                    found.Add(new Face(cx - w / 2, cy - h / 2, w, h, output[index]));
                }
        }
        var kept = new List<Face>();
        foreach (var face in found.OrderByDescending(f => f.Score))
            if (kept.All(k => Overlap(face, k) < 0.3))
                kept.Add(face);
        return kept;
    }

    /// <summary>Schnitt durch Vereinigung zweier Rahmen.</summary>
    static double Overlap(Face a, Face b)
    {
        double w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        if (w <= 0 || h <= 0)
            return 0;
        return w * h / (a.Width * a.Height + b.Width * b.Height - w * h);
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>Kleinster eigener Teil einer Person als Anteil an ihrer Fläche.</summary>
    const double MinPart = 0.05;

    /// <summary>Umrisse je Person (Pixel der Maske), von links nach rechts; kleine Splitter fallen weg.</summary>
    static List<List<List<(double X, double Y)>>> PersonOutlines(int[] label, int width, int height, int persons)
    {
        var minX = Enumerable.Repeat(width, persons).ToArray();
        var minY = Enumerable.Repeat(height, persons).ToArray();
        var maxX = new int[persons];
        var maxY = new int[persons];
        var area = new int[persons];
        for (int i = 0; i < label.Length; i++)
            if (label[i] is int k and >= 0)
            {
                int x = i % width, y = i / width;
                minX[k] = Math.Min(minX[k], x);
                maxX[k] = Math.Max(maxX[k], x);
                minY[k] = Math.Min(minY[k], y);
                maxY[k] = Math.Max(maxY[k], y);
                area[k]++;
            }
        var result = new List<(double Center, List<List<(double X, double Y)>> Outlines)>();
        for (int k = 0; k < persons; k++)
        {
            if (minX[k] > maxX[k])
                continue;
            int w = maxX[k] - minX[k] + 1, h = maxY[k] - minY[k] + 1;
            var mask = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (label[(minY[k] + y) * width + minX[k] + x] == k)
                        mask[y * w + x] = 1;
            var outlines = Outlines(mask, w, w, h, minShare: MinPart * area[k] / (w * h))
                .Select(o => o.Select(p => (p.X + minX[k], p.Y + minY[k])).ToList()).ToList();
            if (outlines.Count > 0)
                result.Add(((minX[k] + maxX[k]) / 2.0, outlines));
        }
        return result.OrderBy(r => r.Center).Select(r => r.Outlines).ToList();
    }

    static readonly (int X, int Y)[] Ring = [(-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1)];

    /// <summary>
    /// Umrisse der zusammenhängenden Flächen (8er-Nachbarschaft) einer Maske, vereinfacht; Punkte in
    /// Pixeln (Mitte der Randpixel). Ausgewertet wird nur der Bereich <paramref name="width"/> ×
    /// <paramref name="height"/> oben links; Löcher werden mitgefüllt, die größte Fläche kommt zuerst.
    /// </summary>
    public static List<List<(double X, double Y)>> Outlines(byte[] mask, int stride, int width, int height,
        double minShare = 0, double tolerance = Tolerance)
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
