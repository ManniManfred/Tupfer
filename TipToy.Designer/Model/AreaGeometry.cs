namespace TipToy.Designer.Model;

/// <summary>
/// Geometrie der OID-Flächen: Umriss als Polygonzug (Bögen und Ellipsen werden fein unterteilt),
/// Punkt-in-Fläche, Überlappung. Alle Angaben in mm.
/// </summary>
public static class AreaGeometry
{
    /// <summary>Größte Abweichung des Polygonzugs von Bögen und Ellipsen (mm).</summary>
    public const double Tolerance = 0.02;

    /// <summary>Umriss der Fläche in Seitenkoordinaten (mm), ohne Wiederholung des ersten Punkts.</summary>
    public static List<(double X, double Y)> Outline(AreaEntry a, double tolerance = Tolerance) =>
        Outline(a.Shape, a.X, a.Y, a.Width, a.Height, a.Points, tolerance);

    public static List<(double X, double Y)> Outline(AreaShape shape, double x, double y, double width, double height,
        IReadOnlyList<AreaPoint>? points, double tolerance = Tolerance)
    {
        switch (shape)
        {
            case AreaShape.Ellipse:
            {
                double rx = width / 2, ry = height / 2, r = Math.Max(rx, ry);
                int n = Segments(r, 2 * Math.PI, tolerance);
                return Enumerable.Range(0, n).Select(i =>
                {
                    double t = 2 * Math.PI * i / n;
                    return (x + rx + rx * Math.Cos(t), y + ry + ry * Math.Sin(t));
                }).ToList();
            }
            case AreaShape.Polygon when points is { Count: >= 2 }:
            {
                var result = new List<(double, double)>();
                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    var q = points[(i + 1) % points.Count];
                    result.Add((x + p.X, y + p.Y));
                    foreach (var (ax, ay) in ArcPoints(p.X, p.Y, q.X, q.Y, p.Bulge, tolerance))
                        result.Add((x + ax, y + ay));
                }
                return result;
            }
            default:
                return [(x, y), (x + width, y), (x + width, y + height), (x, y + height)];
        }
    }

    /// <summary>Anzahl der Teilstücke, damit ein Bogen mit Radius r und Winkel höchstens <paramref name="tolerance"/> abweicht.</summary>
    static int Segments(double radius, double angle, double tolerance)
    {
        if (radius <= tolerance)
            return Math.Max(3, (int)Math.Ceiling(Math.Abs(angle) / (Math.PI / 2)));
        double step = 2 * Math.Acos(1 - tolerance / radius);
        return Math.Clamp((int)Math.Ceiling(Math.Abs(angle) / step), 3, 2000);
    }

    /// <summary>Scheitelpunkt der Kante p→q (Mitte des Bogens bzw. der Strecke).</summary>
    public static (double X, double Y) Apex(double px, double py, double qx, double qy, double bulge)
    {
        double dx = qx - px, dy = qy - py;
        double s = bulge / 2; // Stichhöhe / Sehnenlänge
        // Normale nach links in Laufrichtung (y nach unten): (dy, -dx)
        return ((px + qx) / 2 + dy * s, (py + qy) / 2 - dx * s);
    }

    /// <summary>Bulge der Kante p→q, deren Bogen durch den Punkt a geht (Abstand von der Sehne als Stichhöhe).</summary>
    public static double BulgeThrough(double px, double py, double qx, double qy, double ax, double ay)
    {
        double dx = qx - px, dy = qy - py;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-12)
            return 0;
        // Vorzeichenbehafteter Abstand von a zur Sehne, positiv links
        double side = ((ax - px) * dy - (ay - py) * dx) / Math.Sqrt(len2);
        double b = 2 * side / Math.Sqrt(len2);
        return Math.Clamp(b, -MaxBulge, MaxBulge);
    }

    /// <summary>Höchstens ein Dreiviertelkreis (tan(270° / 4)), sonst wird der Bogen riesig.</summary>
    public const double MaxBulge = 2.414;

    /// <summary>Teilt den Bogen mit diesem Bulge in der Mitte: Bulge jeder Hälfte.</summary>
    public static double HalfBulge(double bulge) => Math.Tan(Math.Atan(bulge) / 2);

    /// <summary>Punkte auf dem Bogen p→q ohne p und q (leer für eine gerade Kante).</summary>
    public static IEnumerable<(double X, double Y)> ArcPoints(double px, double py, double qx, double qy, double bulge,
        double tolerance = Tolerance)
    {
        double dx = qx - px, dy = qy - py;
        double chord = Math.Sqrt(dx * dx + dy * dy);
        if (Math.Abs(bulge) < 1e-9 || chord < 1e-9)
            yield break;
        double angle = 4 * Math.Atan(bulge); // vorzeichenbehafteter Bogenwinkel
        double radius = chord / (2 * Math.Sin(Math.Abs(angle) / 2));
        // Mittelpunkt: vom Scheitel um den Radius zurück zur Sehne hin
        var (ax, ay) = Apex(px, py, qx, qy, bulge);
        double nx = dy / chord, ny = -dx / chord; // Normale nach links
        double sign = Math.Sign(bulge);
        double cx = ax - nx * sign * radius, cy = ay - ny * sign * radius;
        double start = Math.Atan2(py - cy, px - cx);
        // y zeigt nach unten: wachsende Winkel laufen im Uhrzeigersinn, ein Linksbogen also positiv
        double sweep = angle;
        int n = Segments(radius, angle, tolerance);
        for (int i = 1; i < n; i++)
        {
            double t = start + sweep * i / n;
            yield return (cx + radius * Math.Cos(t), cy + radius * Math.Sin(t));
        }
    }

    /// <summary>Umschließendes Rechteck eines Polygonzugs.</summary>
    public static (double X, double Y, double Width, double Height) Bounds(IReadOnlyList<(double X, double Y)> outline)
    {
        double minX = outline.Min(p => p.X), minY = outline.Min(p => p.Y);
        return (minX, minY, outline.Max(p => p.X) - minX, outline.Max(p => p.Y) - minY);
    }

    /// <summary>Liegt der Punkt im Polygonzug (gerade-ungerade-Regel)?</summary>
    public static bool Contains(IReadOnlyList<(double X, double Y)> outline, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            var (xi, yi) = outline[i];
            var (xj, yj) = outline[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Bereiche einer waagerechten Linie, die innerhalb des Polygonzugs liegen
    /// (Paare von x-Werten, aufsteigend sortiert).
    /// </summary>
    public static List<(double From, double To)> Spans(IReadOnlyList<(double X, double Y)> outline, double y)
    {
        var xs = new List<double>();
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            var (xi, yi) = outline[i];
            var (xj, yj) = outline[j];
            if ((yi > y) != (yj > y))
                xs.Add((xj - xi) * (y - yi) / (yj - yi) + xi);
        }
        xs.Sort();
        var spans = new List<(double, double)>();
        for (int i = 0; i + 1 < xs.Count; i += 2)
            spans.Add((xs[i], xs[i + 1]));
        return spans;
    }

    /// <summary>Überlappen sich zwei Polygonzüge (Berühren zählt nicht)?</summary>
    public static bool Overlaps(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        var (ax, ay, aw, ah) = Bounds(a);
        var (bx, by, bw, bh) = Bounds(b);
        if (!(ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah))
            return false;
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
                if (Cross(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count]))
                    return true;
        // Keine Kanten schneiden sich: eine Fläche liegt ganz in der anderen oder sie sind getrennt.
        // Einen Punkt etwas innerhalb nehmen (Mitte einer Kante reicht nicht bei Berührung)
        return ContainsInterior(a, b) || ContainsInterior(b, a);
    }

    static bool ContainsInterior(IReadOnlyList<(double X, double Y)> outer, IReadOnlyList<(double X, double Y)> inner)
    {
        var (x, y, w, h) = Bounds(inner);
        // Einen Punkt im Inneren von inner suchen: Mitte des ersten Bereichs auf halber Höhe
        var spans = Spans(inner, y + h / 2);
        if (spans.Count == 0)
            return false;
        var (from, to) = spans[0];
        return Contains(outer, (from + to) / 2, y + h / 2);
    }

    /// <summary>Schneiden sich die Strecken p1p2 und q1q2 echt (nicht nur in einem Endpunkt oder kollinear)?</summary>
    static bool Cross((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) q1, (double X, double Y) q2)
    {
        static double Orient((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        const double eps = 1e-9;
        double d1 = Orient(q1, q2, p1), d2 = Orient(q1, q2, p2), d3 = Orient(p1, p2, q1), d4 = Orient(p1, p2, q2);
        return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps))
            && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
    }
}
