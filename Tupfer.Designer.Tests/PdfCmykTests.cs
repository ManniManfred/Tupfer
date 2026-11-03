using System.IO.Compression;
using System.Text;
using Tupfer.Designer.Oid;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>PDF-Ausgabe mit Bildern in CMYK ohne Schwarzanteil.</summary>
public class PdfCmykTests
{
    [Fact]
    public void RgbWirdOhneSchwarzanteilUmgerechnet()
    {
        var cmyk = PdfWriter.ToCmykWithoutBlack([0, 0, 0, 255, 255, 255, 10, 128, 200]);
        Assert.Equal(new byte[] { 255, 255, 255, 0, 0, 0, 0, 0, 245, 127, 55, 0 }, cmyk);
    }

    /// <summary>Schreibt eine Seite mit Hintergrund und Code und liefert die PDF-Objekte samt entpackten Strömen.</summary>
    static List<(string Dict, byte[] Data)> WritePage(bool cmyk)
    {
        var file = Path.GetTempFileName();
        try
        {
            var bg = new RgbImage(2, 1, [0, 0, 0, 30, 60, 90]);
            var page = new PdfPageSpec { WidthMm = 50, HeightMm = 50, Background = bg };
            page.Areas.Add(new OidArea(10, 10, 20, 20, 1000));
            page.Frames.Add((10, 10, 20, 20));
            PdfWriter.Write(file, [page], 1200, 2, cmyk);

            var bytes = File.ReadAllBytes(file);
            var text = Encoding.Latin1.GetString(bytes);
            var result = new List<(string, byte[])>();
            for (int pos = text.IndexOf("\nstream\n"); pos >= 0; pos = text.IndexOf("\nstream\n", pos + 1))
            {
                string dict = text[text.LastIndexOf(" obj\n", pos)..pos];
                int start = pos + 8, end = text.IndexOf("\nendstream", start);
                byte[] data = bytes[start..end];
                if (dict.Contains("/FlateDecode"))
                {
                    var raw = new MemoryStream();
                    using (var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress))
                        z.CopyTo(raw);
                    data = raw.ToArray();
                }
                result.Add((dict, data));
            }
            return result;
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void CmykPdfEnthältSchwarzNurInDenCodes()
    {
        var objects = WritePage(cmyk: true);
        var all = string.Join("\n", objects.Select(o => o.Dict + Encoding.Latin1.GetString(o.Data)));
        Assert.DoesNotContain("/DeviceRGB", all);
        Assert.DoesNotContain("/DeviceGray", all);

        var bg = Assert.Single(objects, o => o.Dict.Contains("/DeviceCMYK"));
        Assert.Equal(new byte[] { 255, 255, 255, 0, 225, 195, 165, 0 }, bg.Data);

        Assert.Contains(objects, o => o.Dict.Contains("/ImageMask true"));
        var pattern = Assert.Single(objects, o => o.Dict.Contains("/Type /Pattern"));
        Assert.StartsWith("0 0 0 1 k ", Encoding.ASCII.GetString(pattern.Data));
        Assert.Contains(objects, o => Encoding.Latin1.GetString(o.Data).Contains("0.4 0.4 0.4 0 K"));
    }

    [Fact]
    public void OhneCmykBleibtDieAusgabeUnverändert()
    {
        var objects = WritePage(cmyk: false);
        Assert.Contains(objects, o => o.Dict.Contains("/DeviceRGB"));
        Assert.Contains(objects, o => o.Dict.Contains("/DeviceGray") && o.Dict.Contains("/Mask [1 1]"));
        Assert.DoesNotContain(objects, o => o.Dict.Contains("/DeviceCMYK") || o.Dict.Contains("/ImageMask"));
    }
}
