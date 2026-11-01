using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Services;

/// <summary>
/// Erkennt Personen auf einem Bild und liefert ihre Umrisse als Polygone in Seitenkoordinaten (mm).
/// Ablauf: Gesichter finden (YuNet über <see cref="FaceDetector"/>), dann die Personen in Kacheln
/// passend zur Gesichtsgröße segmentieren (DeepLab v3 über <see cref="Segmenter"/>) und die Maske auf
/// die Gesichter aufteilen: Jede Person wächst von ihrem Gesicht aus über die Maske, Farbkanten und
/// waagerechte Wege kosten mehr. So bekommen auch Personen, die sich berühren, je eine eigene Fläche.
/// Beide Modelle laufen am Desktop mit ONNX Runtime, im Browser mit onnxruntime-web.
/// </summary>
public static class PersonDetector
{
    /// <summary>Kantenlänge des Eingangs der Segmentierung (Pixel).</summary>
    public const int Size = 257;

    /// <summary>
    /// Segmentierung: RGB-Bild (Size × Size × 3 Byte, Zeile für Zeile) → Wahrscheinlichkeit „Person“
    /// (Size × Size Byte, 0 … 255). null = auf dieser Plattform nicht verfügbar.
    /// </summary>
    public static Func<byte[], Task<byte[]>>? Segmenter { get; set; }

    /// <summary>
    /// Gesichtserkennung: RGB-Bild, Breite, Höhe (Vielfache von 32) → je Ankerpunkt fünf Werte, siehe
    /// <see cref="DecodeFaces"/>. null = nicht verfügbar (dann ohne Trennung der Personen).
    /// </summary>
    public static Func<byte[], int, int, Task<float[]>>? FaceDetector { get; set; }

    /// <summary>Kleinste Person als Anteil am Bild, wenn ohne Gesichter gesucht wird.</summary>
    public const double MinShare = 0.003;

    /// <summary>Größte Abweichung des vereinfachten Umrisses von der Maske (Pixel der Maske).</summary>
    public const double Tolerance = 1.2;

    /// <summary>Längere Seite des Bilds für die Gesichtserkennung (Pixel).</summary>
    public const int FaceInput = 1280;

    /// <summary>Mindestwert eines Gesichts (0 … 1).</summary>
    public const float MinFaceScore = 0.8f;

    /// <summary>Kantenlänge einer Kachel der Segmentierung in Gesichtshöhen.</summary>
    const double TileFaces = 10;

    /// <summary>Höchstens so viele Kacheln (sonst werden sie größer).</summary>
    const int MaxTiles = 48;

    /// <summary>Höchstens so viele Pixel hat die längere Seite der Maske.</summary>
    const int MaxCanvas = 1600;

    /// <summary>Ab dieser mittleren Wahrscheinlichkeit gehört ein Pixel zu einer Person.</summary>
    const double MinProbability = 0.3;

    /// <summary>Ein Gesicht (Rahmen in Pixeln).</summary>
    public readonly record struct Face(double X, double Y, double Width, double Height, double Score)
    {
        public double CenterX => X + Width / 2;
        public double Bottom => Y + Height;
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
        if (faces.Count == 0)
            return await WithoutFacesAsync(segment, render, width, height);

        // Kacheln über den Bereich der Personen; Personen sind darin etwa 10 Gesichtshöhen groß
        double faceHeight = Median(faces.Select(f => f.Height));
        double x0 = Math.Max(0, faces.Min(f => f.X - 2 * f.Width)), x1 = Math.Min(width, faces.Max(f => f.X + 3 * f.Width));
        double y0 = Math.Max(0, faces.Min(f => f.Y - f.Height)), y1 = Math.Min(height, faces.Max(f => f.Y + 9 * f.Height));
        double tile = TileFaces * faceHeight;
        static int Count(double length, double tile) => length <= tile ? 1 : (int)Math.Ceiling((length - tile) / (tile / 2)) + 1;
        while (Count(x1 - x0, tile) * Count(y1 - y0, tile) > MaxTiles)
            tile *= 1.25;
        int nx = Count(x1 - x0, tile), ny = Count(y1 - y0, tile);
        static double Position(double start, double length, double tile, int i, int n) =>
            n == 1 ? start + (length - tile) / 2 : start + i * (length - tile) / (n - 1);

        // Maske: Wahrscheinlichkeiten der Kacheln gemittelt; ein Pixel der Maske = cell Pixel des Bilds
        double cell = Math.Max(tile / Size, Math.Max(width, height) / MaxCanvas);
        int w = Math.Max(1, (int)Math.Ceiling(width / cell)), h = Math.Max(1, (int)Math.Ceiling(height / cell));
        var sum = new int[w * h];
        var count = new int[w * h];
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                double tx = Position(x0, x1 - x0, tile, i, nx), ty = Position(y0, y1 - y0, tile, j, ny);
                var probability = await segment(render(new Rect(tx, ty, tile, tile), Size, Size));
                double step = tile / Size;
                for (int py = 0; py < Size; py++)
                {
                    int cy = (int)Math.Floor((ty + (py + 0.5) * step) / cell);
                    if (cy < 0 || cy >= h)
                        continue;
                    for (int px = 0; px < Size; px++)
                    {
                        int cx = (int)Math.Floor((tx + (px + 0.5) * step) / cell);
                        if (cx < 0 || cx >= w)
                            continue;
                        sum[cy * w + cx] += probability[py * Size + px];
                        count[cy * w + cx]++;
                    }
                }
            }
        var mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = count[i] > 0 && sum[i] >= MinProbability * 255 * count[i];
        var rgb = render(new Rect(0, 0, w * cell, h * cell), w, h);
        var scaled = faces.Select(f => new Face(f.X / cell, f.Y / cell, f.Width / cell, f.Height / cell, f.Score)).ToList();

        return await Task.Run(() =>
        {
            var label = Partition(mask, rgb, w, h, scaled);
            int persons = AssignRest(mask, label, w, h, scaled);
            return PersonOutlines(label, w, h, persons)
                .Select(person => person.Select(o => o.Select(p => (p.X * cell, p.Y * cell)).ToList()).ToList())
                .Where(person => person.Count > 0)
                .ToList();
        });
    }

    /// <summary>Ohne Gesichter: das ganze Bild auf einmal segmentieren; jede Fläche ist eine Person.</summary>
    static async Task<List<List<List<(double X, double Y)>>>> WithoutFacesAsync(
        Func<byte[], Task<byte[]>> segment, Renderer render, double width, double height)
    {
        // Oben links in das Quadrat eingepasst
        double aspect = width / height;
        int w = aspect >= 1 ? Size : Math.Max(1, (int)Math.Round(Size * aspect));
        int h = aspect >= 1 ? Math.Max(1, (int)Math.Round(Size / aspect)) : Size;
        var probability = await segment(render(new Rect(0, 0, width * Size / w, height * Size / h), Size, Size));
        var mask = probability.Select(p => (byte)(p >= 128 ? 1 : 0)).ToArray();
        return Outlines(mask, Size, w, h)
            .Select(o => new List<List<(double X, double Y)>> { o.Select(p => (p.X / w * width, p.Y / h * height)).ToList() })
            .ToList();
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

    /// <summary>Senkrechte Schritte kosten so viel wie waagerechte mal diesem Faktor (Körper stehen aufrecht).</summary>
    const double VerticalCost = 0.5;

    /// <summary>Weiter als so viele Gesichtsbreiten neben der Mitte des Gesichts wird es teurer …</summary>
    const double BodyWidth = 1.5;

    /// <summary>… und zwar um diesen Faktor.</summary>
    const double OutsideCost = 4;

    /// <summary>Farbunterschied (Mittel über R, G, B), der einen Schritt doppelt so teuer macht.</summary>
    const double ColorStep = 8;

    /// <summary>
    /// Teilt die Maske auf die Gesichter auf (Rahmen in Pixeln der Maske): Von jedem Gesicht aus wächst
    /// eine Fläche über die Maske, jeder Pixel geht an das Gesicht mit dem billigsten Weg (Dijkstra).
    /// Ein Schritt kostet mehr über Farbkanten, waagerecht und seitlich neben dem Körper; über den Kopf
    /// hinaus nach oben geht es nicht. Die Wege werden in Gesichtshöhen gemessen, damit kleine und
    /// große Personen gleich weit reichen. Liefert je Pixel die Nummer des Gesichts, −1 = keine Person.
    /// </summary>
    public static int[] Partition(bool[] mask, byte[] rgb, int width, int height, IReadOnlyList<Face> faces)
    {
        int n = width * height;
        var inside = (bool[])mask.Clone();
        var label = new int[n];
        var cost = new double[n];
        Array.Fill(label, -1);
        Array.Fill(cost, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        for (int k = 0; k < faces.Count; k++)
        {
            var f = faces[k];
            for (int y = Math.Max(0, (int)f.Y); y < Math.Min(height, (int)Math.Ceiling(f.Bottom)); y++)
                for (int x = Math.Max(0, (int)f.X); x < Math.Min(width, (int)Math.Ceiling(f.X + f.Width)); x++)
                {
                    int i = y * width + x;
                    inside[i] = true;
                    cost[i] = 0;
                    label[i] = k;
                    queue.Enqueue(i, 0);
                }
        }
        double diagonal = Math.Sqrt(1 + VerticalCost * VerticalCost);
        while (queue.TryDequeue(out int i, out double d))
        {
            if (d > cost[i])
                continue;
            int k = label[i], x = i % width, y = i / width;
            var f = faces[k];
            double top = f.Y - f.Height / 2;
            foreach (var (dx, dy) in Ring)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || ny < top)
                    continue;
                int j = ny * width + nx;
                if (!inside[j])
                    continue;
                double step = dx == 0 ? VerticalCost : dy == 0 ? 1 : diagonal;
                int color = Math.Abs(rgb[3 * i] - rgb[3 * j]) + Math.Abs(rgb[3 * i + 1] - rgb[3 * j + 1]) + Math.Abs(rgb[3 * i + 2] - rgb[3 * j + 2]);
                double c = step * (1 + color / (3 * ColorStep)) / f.Height;
                if (Math.Abs(nx + 0.5 - f.CenterX) > BodyWidth * f.Width)
                    c *= OutsideCost;
                if (d + c < cost[j])
                {
                    cost[j] = d + c;
                    label[j] = k;
                    queue.Enqueue(j, d + c);
                }
            }
        }
        return label;
    }

    /// <summary>
    /// Teile der Maske, die keinem Gesicht zugeteilt wurden (etwa Beine unter einem Schild), gehen an
    /// das Gesicht darüber, das waagerecht am besten passt. Große Teile ohne passendes Gesicht werden
    /// eigene Personen (von hinten), kleine fallen weg. Liefert die Zahl der Personen.
    /// </summary>
    public static int AssignRest(bool[] mask, int[] label, int width, int height, IReadOnlyList<Face> faces)
    {
        int persons = faces.Count;
        if (faces.Count == 0)
            return 0;
        double faceArea = Median(faces.Select(f => f.Width * f.Height));
        var seen = new bool[mask.Length];
        var queue = new Queue<int>();
        var pixels = new List<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || label[start] >= 0 || seen[start])
                continue;
            pixels.Clear();
            seen[start] = true;
            queue.Enqueue(start);
            int minX = width, maxX = 0, minY = height, maxY = 0;
            double sumX = 0;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % width, y = i / width;
                pixels.Add(i);
                sumX += x + 0.5;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                foreach (var (dx, dy) in Ring)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    int j = ny * width + nx;
                    if (mask[j] && label[j] < 0 && !seen[j])
                    {
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }
            if (pixels.Count < Math.Max(12, faceArea / 2))
                continue;
            double center = sumX / pixels.Count;
            int best = -1;
            double bestScore = double.PositiveInfinity;
            for (int k = 0; k < faces.Count; k++)
            {
                var f = faces[k];
                double offset = Math.Abs(f.CenterX - center);
                if (f.Y + f.Height / 2 > maxY || offset > (maxX - minX + 1) / 2.0 + BodyWidth * f.Width)
                    continue;
                double score = offset / f.Width + 0.25 * Math.Max(0, minY - f.Bottom) / f.Height;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = k;
                }
            }
            if (best < 0 && pixels.Count >= 3 * faceArea)
                best = persons++;
            if (best >= 0)
                foreach (int i in pixels)
                    label[i] = best;
        }
        return persons;
    }

    /// <summary>Umrisse je Person (Pixel der Maske), von links nach rechts.</summary>
    static List<List<List<(double X, double Y)>>> PersonOutlines(int[] label, int width, int height, int persons)
    {
        var minX = Enumerable.Repeat(width, persons).ToArray();
        var minY = Enumerable.Repeat(height, persons).ToArray();
        var maxX = new int[persons];
        var maxY = new int[persons];
        for (int i = 0; i < label.Length; i++)
            if (label[i] is int k and >= 0)
            {
                int x = i % width, y = i / width;
                minX[k] = Math.Min(minX[k], x);
                maxX[k] = Math.Max(maxX[k], x);
                minY[k] = Math.Min(minY[k], y);
                maxY[k] = Math.Max(maxY[k], y);
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
            var outlines = Outlines(mask, w, w, h, minShare: 0)
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
