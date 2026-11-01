using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Xunit;

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

    /// <summary>Kandidat mit rechteckiger Maske (Logit 200 innen, 60 außen) im Rechteck x, y, w × h.</summary>
    static PersonDetector.Candidate Box(double score, int x, int y, int w, int h, int x0, int y0, int x1, int y1, byte inside = 200)
    {
        var logits = new byte[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                logits[j * w + i] = x + i >= x0 && x + i < x1 && y + j >= y0 && y + j < y1 ? inside : (byte)60;
        return new PersonDetector.Candidate(score, x, y, w, h, logits);
    }

    [Fact]
    public void PersonenAuswaehlenUndZuteilen()
    {
        // Erwachsener mit Kind auf dem Arm (Kind liegt ganz in seiner Maske, Logits dort höher),
        // dieselbe Person nochmal aus einer Kachel, ein schwacher Kandidat mit Gesicht und einer ohne
        var adult = Box(0.9, 0, 0, 40, 80, 5, 5, 35, 75);
        var child = Box(0.7, 10, 30, 20, 20, 10, 30, 30, 50, inside: 250);
        var duplicate = Box(0.8, 0, 0, 40, 80, 6, 6, 35, 76);
        var withFace = Box(0.2, 50, 0, 30, 80, 55, 5, 75, 75);
        var weak = Box(0.2, 90, 0, 20, 40, 92, 2, 108, 38);
        var persons = PersonDetector.Select([weak, child, duplicate, adult, withFace], [(65, 10)]);
        Assert.Equal([adult, child, withFace], persons);

        var label = PersonDetector.Assign(persons, 120, 80);
        Assert.Equal(0, label[10 * 120 + 20]);
        Assert.Equal(1, label[40 * 120 + 20]);
        Assert.Equal(2, label[40 * 120 + 65]);
        Assert.Equal(-1, label[40 * 120 + 100]);
        Assert.Equal(-1, label[2 * 120 + 2]);
    }

    [Fact]
    public async Task PersonenAufEinemBildErkennen()
    {
        // Bild 1200 × 800 mit zwei „Personen“, die sich berühren, und kleinen Gesichtern, so dass auch in
        // Kacheln gesucht wird. Die Segmentierung findet jede Person, die ganz oder teilweise im
        // Ausschnitt liegt. Erwartet: zwei Personen von links nach rechts, keine doppelt, auch nicht
        // die in Kacheln abgeschnittenen (ein Maskenpixel des ganzen Bilds sind 12,5 Bildpixel).
        const double width = 1200, height = 800;
        var people = new[] { new Rect(400, 200, 100, 300), new Rect(500, 220, 120, 320) };
        var regions = new List<Rect>();
        byte[] Render(Rect region, int w, int h)
        {
            regions.Add(region); // die Segmentierung bekommt die Bereiche in derselben Reihenfolge
            return new byte[w * h * 3];
        }
        var segmenter = PersonDetector.Segmenter;
        var detector = PersonDetector.FaceDetector;
        try
        {
            int n = PersonDetector.MaskSize, run = 0;
            PersonDetector.Segmenter = rgb =>
            {
                var region = regions[run++ + 1]; // der erste Bereich war für die Gesichter
                var scores = new float[PersonDetector.Candidates];
                var masks = new byte[PersonDetector.Candidates * n * n];
                for (int k = 0; k < people.Length; k++)
                {
                    scores[k] = 0.9f;
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < n; x++)
                        {
                            var p = new Point(region.X + (x + 0.5) * region.Width / n, region.Y + (y + 0.5) * region.Height / n);
                            masks[k * n * n + y * n + x] = (byte)(people[k].Contains(p) ? 200 : 60);
                        }
                }
                return Task.FromResult((scores, masks));
            };
            PersonDetector.FaceDetector = (rgb, w, h) =>
            {
                double s = w / width; // Gesichter in Pixeln des Eingangs
                return Task.FromResult(FaceOutput(w, h, (540 * s, 225 * s, 20 * s, 20 * s, 0.9f), (440 * s, 205 * s, 20 * s, 20 * s, 0.95f)));
            };
            var persons = await PersonDetector.DetectAsync(Render, width, height);
            Assert.True(regions.Count > 2, "auch Kacheln");
            Assert.Equal(2, persons.Count);
            for (int k = 0; k < 2; k++)
            {
                var outline = Assert.Single(persons[k]);
                Assert.InRange(outline.Min(p => p.X), people[k].X - 8, people[k].X + 8);
                Assert.InRange(outline.Max(p => p.X), people[k].Right - 8, people[k].Right + 8);
                Assert.InRange(outline.Min(p => p.Y), people[k].Y - 8, people[k].Y + 8);
                Assert.InRange(outline.Max(p => p.Y), people[k].Bottom - 8, people[k].Bottom + 8);
            }
        }
        finally
        {
            PersonDetector.Segmenter = segmenter;
            PersonDetector.FaceDetector = detector;
        }
    }
}
