using System.Reflection;

namespace TipToy.Designer.Oid;

/// <summary>
/// Erzeugung der tiptoi-OID-Punktmuster.
/// Direkte Portierung von OidCode.hs / KnownCodes.hs aus tttool (entropia/tip-toi-reveng).
/// </summary>
public static class OidCode
{
    public const int FirstObjectCode = 1000;

    // Empirisch erkennt der Stift keine OIDs >= 15000
    public const int LastObjectCode = 14999;

    static readonly Lazy<ushort[]> KnownRawCodes = new(LoadKnownRawCodes);

    static ushort[] LoadKnownRawCodes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KnownCodes.txt")
            ?? throw new InvalidOperationException("Ressource KnownCodes.txt fehlt.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim().Split(',').Select(ushort.Parse).ToArray();
    }

    /// <summary>Wandelt eine OID (Produkt- oder Objektcode) in den gedruckten Raw-Code um.</summary>
    public static ushort? ToRawCode(int code)
    {
        var table = KnownRawCodes.Value;
        return code >= 0 && code < table.Length ? table[code] : null;
    }

    static int Checksum(int dec)
    {
        int c1 = (((dec >> 2) ^ (dec >> 8) ^ (dec >> 12) ^ (dec >> 14)) & 0x01) << 1;
        int c2 = c1 | ((dec ^ (dec >> 4) ^ (dec >> 6) ^ (dec >> 10)) & 0x01);
        return c2 ^ 0x02;
    }

    static int DivCeil(int x, int y) => (x - 1) / y + 1;

    /// <summary>Kantenlänge einer Kachel in Pixeln.</summary>
    public static int TilePixelSize(int dpi) => 4 * DivCeil(dpi, 100);

    /// <summary>
    /// Liefert die schwarzen Pixel einer einzelnen Kachel (Größe <see cref="TilePixelSize"/>) für einen Raw-Code.
    /// </summary>
    public static List<(int X, int Y)> TilePixels(ushort rawCode, int dpi = 1200, int pixelSize = 2)
    {
        int spacePerPoint = DivCeil(dpi, 100);
        int s = DivCeil(dpi, 600);
        int ss = DivCeil(dpi, 400);
        int shift = (spacePerPoint - pixelSize) / 2 - 1;
        if (shift < 0)
            throw new ArgumentException("Punkte zu groß. Bitte eine kleinere Pixelgröße wählen.");

        int Quart(int n) => n == 8 ? Checksum(rawCode) : (rawCode >> (2 * n)) & 3;

        var pixels = new List<(int, int)>();

        void Dot(int px, int py, int dx, int dy)
        {
            for (int x = 1; x <= pixelSize; x++)
                for (int y = 1; y <= pixelSize; y++)
                    pixels.Add((px * spacePerPoint + shift + dx + x, py * spacePerPoint + shift + dy + y));
        }

        // Datenpunkte: (3,3), (2,3), (1,3), (3,2), ... (1,1)
        int n = 0;
        for (int m = 3; m >= 1; m--)
            for (int x = 3; x >= 1; x--)
            {
                var (dx, dy) = Quart(n++) switch
                {
                    0 => (s, s),
                    1 => (-s, s),
                    2 => (-s, -s),
                    _ => (s, -s),
                };
                Dot(x, m, dx, dy);
            }

        foreach (var (x, y) in new[] { (0, 0), (1, 0), (2, 0), (3, 0), (0, 1), (0, 3) })
            Dot(x, y, 0, 0);
        Dot(0, 2, ss, 0);

        return pixels;
    }

    /// <summary>
    /// Erzeugt eine Kachel als Bitmaske (true = schwarzer Punkt), Index [y * size + x].
    /// </summary>
    public static bool[] TileMask(ushort rawCode, int dpi = 1200, int pixelSize = 2)
    {
        int size = TilePixelSize(dpi);
        var mask = new bool[size * size];
        foreach (var (x, y) in TilePixels(rawCode, dpi, pixelSize))
            if (x >= 0 && y >= 0 && x < size && y < size)
                mask[y * size + x] = true;
        return mask;
    }

    /// <summary>
    /// Verteilt Objektcodes je Produkt-ID über den gesamten Bereich (wie tttool "codesFor").
    /// </summary>
    public static IEnumerable<int> CodesFor(uint productId)
    {
        uint offsetMax = LastObjectCode - FirstObjectCode - 999;
        // tttool rechnet mit Word32, daher bewusst mit Überlauf
        int offset = (int)(unchecked(productId * 8035u) % (offsetMax + 1));
        for (int c = FirstObjectCode + offset; c <= LastObjectCode; c++)
            yield return c;
        for (int c = FirstObjectCode; c < FirstObjectCode + offset; c++)
            yield return c;
    }
}
