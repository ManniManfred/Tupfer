using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Xunit;
using Face = Tupfer.Designer.Services.PersonDetector.Face;

namespace Tupfer.Designer.Tests;

/// <summary>Personenerkennung: Gesichter auswerten, Maske auf die Personen aufteilen.</summary>
public class PersonDetectorTests
{
    /// <summary>Ausgabe wie von YuNet: Gesichter (Rahmen in Pixeln) an den Ankerpunkten der Schrittweite 16.</summary>
    static float[] FaceOutput(int width, int height, params (double X, double Y, double W, double H, float Score)[] faces)
    {
        int anchors = (int[])[8, 16, 32] is var strides ? strides.Sum(s => width / s * (height / s)) : 0;
        var output = new float[anchors * 5];
        int offset = width / 8 * (height / 8);
        foreach (var f in faces)
        {
            double cx = f.X + f.W / 2, cy = f.Y + f.H / 2;
            int c = (int)(cx / 16), r = (int)(cy / 16), i = (offset + r * (width / 16) + c) * 5;
            output[i] = f.Score;
            output[i + 1] = (float)(cx / 16 - c);
            output[i + 2] = (float)(cy / 16 - r);
            output[i + 3] = (float)Math.Log(f.W / 16);
            output[i + 4] = (float)Math.Log(f.H / 16);
        }
        return output;
    }

    [Fact]
    public void GesichterAuswerten()
    {
        // Zwei getrennte Gesichter, ein überlappendes mit kleinerem Wert und eins unter der Schwelle
        var output = FaceOutput(128, 96, (8, 16, 24, 32, 0.95f), (70, 30, 30, 36, 0.9f), (20, 16, 24, 32, 0.85f), (100, 60, 10, 12, 0.5f));
        var faces = PersonDetector.DecodeFaces(output, 128, 96, 0.8f);
        Assert.Equal(2, faces.Count);
        Assert.Equal(0.95, faces[0].Score, 3);
        Assert.Equal(8, faces[0].X, 3);
        Assert.Equal(16, faces[0].Y, 3);
        Assert.Equal(24, faces[0].Width, 3);
        Assert.Equal(36, faces[1].Height, 3);
    }

    [Fact]
    public void MaskeAufGesichterAufteilen()
    {
        // Zwei Personen nebeneinander, die sich berühren (eine Fläche in der Maske), mit
        // unterschiedlicher Kleidung; unter der linken ein abgetrennter Teil (Beine hinter etwas),
        // rechts außen eine große Fläche ohne Gesicht (Person von hinten), dazu ein kleiner Fleck
        const int w = 120, h = 100;
        var mask = new bool[w * h];
        var rgb = new byte[w * h * 3];
        void Fill(int x0, int y0, int x1, int y1, byte gray)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    mask[y * w + x] = true;
                    rgb[3 * (y * w + x)] = rgb[3 * (y * w + x) + 1] = rgb[3 * (y * w + x) + 2] = gray;
                }
        }
        Fill(10, 10, 30, 60, 200);
        Fill(30, 15, 50, 70, 40);
        Fill(12, 66, 28, 90, 200);
        Fill(80, 20, 110, 80, 120);
        Fill(60, 90, 63, 93, 120);
        var faces = new List<Face> { new(15, 10, 10, 10, 1), new(35, 15, 10, 10, 1) };

        var label = PersonDetector.Partition(mask, rgb, w, h, faces);
        Assert.Equal(0, label[40 * w + 20]);
        Assert.Equal(1, label[40 * w + 40]);
        Assert.Equal(-1, label[80 * w + 20]);
        // Die Grenze liegt an der Farbkante
        Assert.Equal(0, label[50 * w + 29]);
        Assert.Equal(1, label[50 * w + 30]);

        int persons = PersonDetector.AssignRest(mask, label, w, h, faces);
        Assert.Equal(3, persons);
        Assert.Equal(0, label[80 * w + 20]);
        Assert.Equal(2, label[50 * w + 95]);
        Assert.Equal(-1, label[91 * w + 61]);
    }

    [Fact]
    public async Task PersonenAufEinemBildErkennen()
    {
        // Bild 640 × 480: zwei helle „Personen“, die sich berühren; die Segmentierung sieht helle
        // Pixel als Person. Erwartet: zwei Personen von links nach rechts, jede mit ihrem Umriss
        // (unten darf die kürzere linke etwas in die rechte greifen).
        const double width = 640, height = 480;
        var people = new[] { new Rect(200, 100, 100, 300), new Rect(300, 120, 120, 320) };
        byte[] Render(Rect region, int w, int h)
        {
            var rgb = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Point(region.X + (x + 0.5) * region.Width / w, region.Y + (y + 0.5) * region.Height / h);
                    if (p.X >= width || p.Y >= height)
                        continue;
                    int i = people.ToList().FindIndex(r => r.Contains(p));
                    if (i >= 0)
                        rgb[3 * (y * w + x)] = rgb[3 * (y * w + x) + 1] = rgb[3 * (y * w + x) + 2] = (byte)(i == 0 ? 220 : 160);
                }
            return rgb;
        }
        var segmenter = PersonDetector.Segmenter;
        var detector = PersonDetector.FaceDetector;
        try
        {
            PersonDetector.Segmenter = rgb => Task.FromResult(Enumerable.Range(0, rgb.Length / 3).Select(i => (byte)(rgb[3 * i] > 100 ? 255 : 0)).ToArray());
            PersonDetector.FaceDetector = (rgb, w, h) =>
            {
                double s = w / width; // Gesichter in Pixeln des Eingangs
                return Task.FromResult(FaceOutput(w, h, (330 * s, 125 * s, 40 * s, 40 * s, 0.9f), (230 * s, 105 * s, 40 * s, 40 * s, 0.95f)));
            };
            var persons = await PersonDetector.DetectAsync(Render, width, height);
            Assert.Equal(2, persons.Count);
            for (int k = 0; k < 2; k++)
            {
                var outline = Assert.Single(persons[k]);
                Assert.InRange(outline.Min(p => p.X), people[k].X - 8, people[k].X + 8);
                Assert.InRange(outline.Max(p => p.X), people[k].Right - 8, people[k].Right + 8);
                Assert.InRange(outline.Min(p => p.Y), people[k].Y - 8, people[k].Y + 8);
                Assert.True(AreaGeometry.Contains(outline, people[k].Center.X, people[k].Center.Y));
                Assert.False(AreaGeometry.Contains(outline, people[1 - k].Center.X, people[1 - k].Center.Y));
            }
            Assert.InRange(persons[1][0].Max(p => p.Y), 432, 448);
        }
        finally
        {
            PersonDetector.Segmenter = segmenter;
            PersonDetector.FaceDetector = detector;
        }
    }
}
