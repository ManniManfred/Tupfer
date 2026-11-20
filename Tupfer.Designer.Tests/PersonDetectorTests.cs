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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 1)]
    [InlineData(180, 2)]
    [InlineData(-180, 2)]
    [InlineData(-90, 3)]
    [InlineData(270, 3)]
    [InlineData(360, 0)]
    [InlineData(93, 1)]
    [InlineData(10, 0)]
    public void DrehungWirdAufVierteldrehungenGerundet(double rotation, int quarter) =>
        Assert.Equal(quarter, PersonDetector.QuarterTurns(rotation));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void GedrehtesBildUndRueckrechnungPassenZusammen(int quarter)
    {
        // 3 × 2 Pixel mit unterschiedlichen Werten; jedes Pixel des gedrehten Bilds muss aus dem Pixel
        // des gespeicherten Bilds stammen, auf das ViewRotation seine Mitte zurückrechnet
        const int width = 3, height = 2;
        var rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
            rgb[i * 3] = (byte)(10 + i);
        var rotated = PersonDetector.RotateRgb(rgb, width, height, quarter);
        var view = new PersonDetector.ViewRotation(quarter, width, height);
        Assert.Equal(quarter % 2 == 1 ? height : width, view.Width);
        for (int y = 0; y < (int)view.Height; y++)
            for (int x = 0; x < (int)view.Width; x++)
            {
                var stored = view.ToStored(new Point(x + 0.5, y + 0.5));
                int sx = (int)stored.X, sy = (int)stored.Y;
                Assert.Equal(rgb[(sy * width + sx) * 3], rotated[(y * (int)view.Width + x) * 3]);
            }
        // Ein Bereich der Ansicht wird zum passenden Bereich des gespeicherten Bilds
        var all = view.ToStored(new Rect(0, 0, view.Width, view.Height));
        Assert.Equal(new Rect(0, 0, width, height), all);
    }

    [Fact]
    public void AufDemKopfStehendesFotoWirdAufgerichtetGesucht()
    {
        // Wie ein Handyfoto, das laut EXIF auf dem Kopf gespeichert ist: Das Element ist um 180° gedreht.
        // Die Segmentierung bekommt das Bild aufrecht (oben hell, unten dunkel), und die gefundene Person
        // (obere Hälfte der Ansicht) landet auf der Seite oben, also in der unteren Hälfte des gespeicherten Bilds.
        var dir = Path.Combine(Path.GetTempPath(), "tupfer-gedreht-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var segmenter = PersonDetector.Segmenter;
        var detector = PersonDetector.FaceDetector;
        try
        {
            var file = Path.Combine(dir, "kopf.svg");
            // Gespeichert: oben schwarz, unten weiß
            File.WriteAllText(file, """
                <svg xmlns="http://www.w3.org/2000/svg" width="400" height="200" viewBox="0 0 400 200">
                  <rect x="0" y="0" width="400" height="100" fill="#000000"/>
                  <rect x="0" y="100" width="400" height="100" fill="#ffffff"/>
                </svg>
                """);
            var topBrightness = new List<int>();
            int n = PersonDetector.MaskSize;
            PersonDetector.FaceDetector = null;
            PersonDetector.Segmenter = rgb =>
            {
                int size = PersonDetector.Size;
                topBrightness.Add(rgb[(size / 8 * size + size / 2) * 3]);
                var scores = new float[PersonDetector.Candidates];
                var masks = new byte[PersonDetector.Candidates * n * n];
                scores[0] = 0.9f;
                // Person in der oberen Hälfte der Eingabe (die Eingabe ist quadratisch, das Bild 2:1)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        masks[y * n + x] = (byte)(y >= n * 3 / 10 && y < n * 4 / 10 && x > n / 3 && x < n * 2 / 3 ? 200 : 60);
                return Task.FromResult((scores, masks));
            };
            var (outlines, brightness) = HeadlessSkia.Run(() =>
            {
                var project = new Tupfer.Designer.ViewModels.ProjectViewModel(_ => { });
                var page = new Tupfer.Designer.ViewModels.PageViewModel(project) { Name = "Seite", WidthMm = 210, HeightMm = 297 };
                project.Pages.Add(page);
                var e = new Tupfer.Designer.ViewModels.ElementViewModel(project, ElementKind.Image)
                    { ImagePath = file, X = 10, Y = 10, Width = 100, Height = 50, Rotation = 180 };
                page.Elements.Add(e);
                var t = PersonDetector.DetectAsync(e);
                while (!t.IsCompleted)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(2);
                }
                return (t.Result.SelectMany(p => p.Outlines).ToList(), topBrightness.ToList());
            });
            Assert.True(brightness[0] > 200, $"oben in der Eingabe hell erwartet, war {brightness[0]}");
            var outline = Assert.Single(outlines);
            // Seite: Element 10 … 60 mm hoch; die Person liegt in dessen oberer Hälfte
            Assert.InRange(outline.Average(p => p.Y), 10, 35);
        }
        finally
        {
            PersonDetector.Segmenter = segmenter;
            PersonDetector.FaceDetector = detector;
            Directory.Delete(dir, true);
        }
    }
}
