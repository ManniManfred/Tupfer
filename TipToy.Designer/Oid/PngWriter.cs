using System.IO.Compression;
using System.Text;

namespace TipToy.Designer.Oid;

/// <summary>
/// Minimaler PNG-Encoder für Schwarz/Weiß-Bilder mit DPI-Angabe (pHYs),
/// damit OID-Codes in der richtigen Größe gedruckt werden.
/// </summary>
public static class PngWriter
{
    /// <summary>
    /// Schreibt eine OID-Fläche der angegebenen Größe (Kachel wird wiederholt). Mit
    /// <paramref name="outline"/> (Polygonzug in mm relativ zur oberen linken Ecke) bleibt alles
    /// außerhalb des Umrisses weiß.
    /// </summary>
    public static void WriteOidPng(string path, int oid, double widthMm, double heightMm, int dpi = 1200, int pixelSize = 2,
        IReadOnlyList<(double X, double Y)>? outline = null)
    {
        var raw = OidCode.ToRawCode(oid) ?? throw new ArgumentException($"Für OID {oid} ist kein Raw-Code bekannt.");
        int tile = OidCode.TilePixelSize(dpi);
        var mask = OidCode.TileMask(raw, dpi, pixelSize);
        int w = (int)Math.Round(widthMm * dpi / 25.4);
        int h = (int)Math.Round(heightMm * dpi / 25.4);
        if (outline is not { Count: >= 3 })
        {
            WriteBilevel(path, w, h, dpi, (x, y) => mask[(y % tile) * tile + x % tile]);
            return;
        }

        // Je Zeile die Bereiche innerhalb des Umrisses (in Pixeln, gemessen in der Pixelmitte)
        double pxPerMm = dpi / 25.4;
        var scaled = outline.Select(p => (p.X * pxPerMm, p.Y * pxPerMm)).ToList();
        int spansRow = -1;
        List<(double From, double To)> spans = [];
        WriteBilevel(path, w, h, dpi, (x, y) =>
        {
            if (y != spansRow)
            {
                spans = Model.AreaGeometry.Spans(scaled, y + 0.5);
                spansRow = y;
            }
            return mask[(y % tile) * tile + x % tile] && spans.Any(s => x + 0.5 >= s.From && x + 0.5 < s.To);
        });
    }

    /// <summary>Schreibt ein 1-Bit-Graustufen-PNG. <paramref name="isBlack"/> liefert true für schwarze Pixel.</summary>
    public static void WriteBilevel(string path, int width, int height, int dpi, Func<int, int, bool> isBlack)
    {
        int stride = (width + 7) / 8;
        var data = new MemoryStream();
        using (var z = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[stride + 1];
            for (int y = 0; y < height; y++)
            {
                Array.Clear(row);
                for (int x = 0; x < width; x++)
                    if (!isBlack(x, y))
                        row[1 + x / 8] |= (byte)(0x80 >> (x % 8)); // 1 = weiß
                z.Write(row);
            }
        }

        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 1;  // Bittiefe
        ihdr[9] = 0;  // Graustufen
        WriteChunk(fs, "IHDR", ihdr);

        uint ppm = (uint)Math.Round(dpi / 0.0254);
        var phys = new byte[9];
        WriteBE(phys, 0, ppm);
        WriteBE(phys, 4, ppm);
        phys[8] = 1; // Meter
        WriteChunk(fs, "pHYs", phys);

        WriteChunk(fs, "IDAT", data.ToArray());
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    static void WriteBE(byte[] b, int offset, uint v)
    {
        b[offset] = (byte)(v >> 24);
        b[offset + 1] = (byte)(v >> 16);
        b[offset + 2] = (byte)(v >> 8);
        b[offset + 3] = (byte)v;
    }

    static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc32(typeBytes, 0xFFFFFFFF);
        crc = Crc32(data, crc) ^ 0xFFFFFFFF;
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] data, uint crc)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
