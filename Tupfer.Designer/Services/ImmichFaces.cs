using System.Buffers.Binary;
using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.ViewModels;

namespace Tupfer.Designer.Services;

/// <summary>Ein Gesicht aus Immich auf einem eingefügten Bild: Fläche in Seitenkoordinaten (mm), Name aus Immich (leer = unbekannt).</summary>
public sealed record ImmichFaceArea(List<AreaPoint> Outline, string Name, Rect SourcePixels);

/// <summary>
/// Rechnet die Gesichter, die Immich zu einem Foto kennt, auf das eingefügte Bild um. Immich gibt die Rahmen
/// in Pixeln des richtig gedrehten (und ggf. in Immich bearbeiteten) Bilds der Größe imageWidth × imageHeight an.
/// Das eingefügte Bild kann eine andere Auflösung haben (Vorschau oder Original), seine Pixel können laut EXIF
/// gedreht gespeichert sein (der Designer zeigt sie ungedreht und dreht stattdessen das Element), und es kann
/// zugeschnitten, skaliert und gedreht auf der Seite liegen.
/// </summary>
public static class ImmichFaces
{
    /// <summary>
    /// EXIF-Orientierung (1–8) eines JPEG; 1, wenn keine angegeben ist oder es kein JPEG ist.
    /// Andere Formate (PNG, WebP) liefert Immich ohnehin richtig gedreht.
    /// </summary>
    public static int ReadOrientation(ReadOnlySpan<byte> data)
    {
        if (data is not [0xFF, 0xD8, ..])
            return 1;
        int pos = 2;
        while (pos + 4 <= data.Length && data[pos] == 0xFF)
        {
            byte marker = data[pos + 1];
            if (marker is 0xD9 or 0xDA)
                break;
            int length = data[pos + 2] << 8 | data[pos + 3];
            if (length < 2 || pos + 2 + length > data.Length)
                break;
            var segment = data.Slice(pos + 4, length - 2);
            if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
                return TiffOrientation(segment[6..]);
            pos += 2 + length;
        }
        return 1;
    }

    static int TiffOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
            return 1;
        bool little = tiff[0] == 'I' && tiff[1] == 'I';
        if (!little && !(tiff[0] == 'M' && tiff[1] == 'M'))
            return 1;
        int U16(ReadOnlySpan<byte> b) => little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);
        int U32(ReadOnlySpan<byte> b) => (int)Math.Min(int.MaxValue,
            little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b));
        int ifd = U32(tiff[4..]);
        if (ifd < 8 || ifd + 2 > tiff.Length)
            return 1;
        int count = U16(tiff[ifd..]);
        for (int i = 0; i < count; i++)
        {
            int entry = ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length)
                break;
            // Tag 0x0112 (Orientation), Typ SHORT, Wert in den ersten zwei Bytes des Felds
            if (U16(tiff[entry..]) == 0x0112)
            {
                int value = U16(tiff[(entry + 8)..]);
                return value is >= 1 and <= 8 ? value : 1;
            }
        }
        return 1;
    }

    /// <summary>
    /// Drehung des Elements im Uhrzeigersinn (Grad), damit ein Bild mit dieser EXIF-Orientierung richtig herum
    /// erscheint (gespiegelte Orientierungen 2, 4, 5, 7 bleiben gespiegelt).
    /// </summary>
    public static double RotationFor(int orientation) => orientation switch
    {
        3 or 4 => 180,
        5 or 6 => 90,
        7 or 8 => -90,
        _ => 0,
    };

    /// <summary>
    /// Punkt im richtig gedrehten Bild (Anteile 0–1 von Breite und Höhe) → Punkt in den gespeicherten Pixeln
    /// (ebenfalls Anteile) bei der EXIF-Orientierung <paramref name="orientation"/>.
    /// </summary>
    public static Point ToStored(Point shown, int orientation)
    {
        double u = shown.X, v = shown.Y;
        return orientation switch
        {
            2 => new Point(1 - u, v),
            3 => new Point(1 - u, 1 - v),
            4 => new Point(u, 1 - v),
            5 => new Point(v, u),
            6 => new Point(v, 1 - u),
            7 => new Point(1 - v, 1 - u),
            8 => new Point(1 - v, u),
            _ => new Point(u, v),
        };
    }

    /// <summary>
    /// Flächen der Gesichter auf dem Bild-Element <paramref name="image"/> (Rechtecke, auf der Seite ggf. gedreht),
    /// von links nach rechts. Gesichter außerhalb des sichtbaren Ausschnitts entfallen, angeschnittene werden
    /// am Rand des Ausschnitts abgeschnitten. <see cref="ImmichFaceArea.SourcePixels"/> ist der Rahmen im
    /// gespeicherten Bild der Größe <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> (für den Kopf-Ausschnitt).
    /// </summary>
    public static List<ImmichFaceArea> ToAreas(IEnumerable<ImmichFace> faces, ElementEntry image, int orientation,
        double pixelWidth = 1, double pixelHeight = 1)
    {
        var result = new List<(double Order, ImmichFaceArea Area)>();
        foreach (var face in faces)
        {
            if (face.ImageWidth <= 0 || face.ImageHeight <= 0)
                continue;
            // Rahmen als Anteile des gezeigten Bilds → gespeicherte Pixel (bleibt ein achsenparalleles Rechteck)
            var a = ToStored(new Point((double)face.BoundingBoxX1 / face.ImageWidth, (double)face.BoundingBoxY1 / face.ImageHeight), orientation);
            var b = ToStored(new Point((double)face.BoundingBoxX2 / face.ImageWidth, (double)face.BoundingBoxY2 / face.ImageHeight), orientation);
            double s0 = Math.Clamp(Math.Min(a.X, b.X), 0, 1), s1 = Math.Clamp(Math.Max(a.X, b.X), 0, 1);
            double t0 = Math.Clamp(Math.Min(a.Y, b.Y), 0, 1), t1 = Math.Clamp(Math.Max(a.Y, b.Y), 0, 1);

            // Zuschnitt und Skalierung: Anteile des Bilds → ungedrehter Rahmen des Elements (mm)
            double Lx(double s) => (s - image.CropX) / image.CropWidth * image.Width;
            double Ly(double t) => (t - image.CropY) / image.CropHeight * image.Height;
            double x0 = Math.Max(0, Lx(s0)), x1 = Math.Min(image.Width, Lx(s1));
            double y0 = Math.Max(0, Ly(t0)), y1 = Math.Min(image.Height, Ly(t1));
            if (x1 - x0 < 0.5 || y1 - y0 < 0.5)
                continue;

            // Drehung des Elements: Ecken auf die Seite
            var outline = new[] { new Point(x0, y0), new Point(x1, y0), new Point(x1, y1), new Point(x0, y1) }
                .Select(p => ElementViewModel.ToPage(image, p))
                .Select(p => new AreaPoint(Math.Round(p.X, 2), Math.Round(p.Y, 2)))
                .ToList();
            var center = ElementViewModel.ToPage(image, new Point((x0 + x1) / 2, (y0 + y1) / 2));
            var source = new Rect(s0 * pixelWidth, t0 * pixelHeight, (s1 - s0) * pixelWidth, (t1 - t0) * pixelHeight);
            result.Add((center.X, new ImmichFaceArea(outline, face.Person?.Name?.Trim() ?? "", source)));
        }
        return result.OrderBy(r => r.Order).Select(r => r.Area).ToList();
    }

    /// <summary>
    /// Ordnet die Gesichter aus Immich den lokal erkannten Personen zu (je Person höchstens ein Gesicht):
    /// bevorzugt der Person, in deren Umriss die Mitte des Gesichts liegt und deren Oberkante ihr am nächsten
    /// ist (der Kopf ist oben), sonst einer Person, deren Rahmen das Gesicht knapp verfehlt. Liefert je Gesicht
    /// den Index der Person oder -1.
    /// </summary>
    public static int[] MatchFaces(IReadOnlyList<List<List<AreaPoint>>> persons, IReadOnlyList<ImmichFaceArea> faces)
    {
        var candidates = new List<(double Cost, int Face, int Person)>();
        for (int f = 0; f < faces.Count; f++)
        {
            var box = AreaGeometry.Bounds(faces[f].Outline.Select(p => (p.X, p.Y)).ToList());
            double cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2, size = Math.Max(box.Width, box.Height);
            for (int p = 0; p < persons.Count; p++)
            {
                var points = persons[p].SelectMany(o => o).Select(q => (q.X, q.Y)).ToList();
                if (points.Count == 0)
                    continue;
                var bounds = AreaGeometry.Bounds(points);
                double top = Math.Abs(cy - bounds.Y);
                if (persons[p].Any(o => AreaGeometry.Contains(o.Select(q => (q.X, q.Y)).ToList(), cx, cy)))
                {
                    candidates.Add((top, f, p));
                    continue;
                }
                // Knapp daneben (z. B. Haare oder Gesicht am Rand der Maske): Abstand zum Rahmen der Person
                double dx = Math.Max(0, Math.Max(bounds.X - cx, cx - (bounds.X + bounds.Width)));
                double dy = Math.Max(0, Math.Max(bounds.Y - cy, cy - (bounds.Y + bounds.Height)));
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance <= size)
                    candidates.Add((1e6 + distance, f, p));
            }
        }
        var result = Enumerable.Repeat(-1, faces.Count).ToArray();
        var taken = new HashSet<int>();
        foreach (var (_, f, p) in candidates.OrderBy(c => c.Cost))
        {
            if (result[f] >= 0 || taken.Contains(p))
                continue;
            result[f] = p;
            taken.Add(p);
        }
        return result;
    }
}
