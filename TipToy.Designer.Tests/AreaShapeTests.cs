using System.IO.Compression;
using TipToy.Designer.Model;
using TipToy.Designer.Oid;
using TipToy.Designer.Services;
using TipToy.Designer.ViewModels;
using Xunit;

namespace TipToy.Designer.Tests;

/// <summary>OID-Flächen als Kreis/Ellipse und Polygon mit Kreisbögen.</summary>
public class AreaShapeTests
{
    static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Flächeninhalt eines Polygonzugs (Gaußsche Trapezformel).</summary>
    static double AreaOf(IReadOnlyList<(double X, double Y)> outline)
    {
        double sum = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var (x1, y1) = outline[i];
            var (x2, y2) = outline[(i + 1) % outline.Count];
            sum += x1 * y2 - x2 * y1;
        }
        return Math.Abs(sum) / 2;
    }

    static ProjectViewModel NewProject()
    {
        var project = new ProjectViewModel(_ => { });
        var page = new PageViewModel(project) { Name = "Seite 1" };
        project.Pages.Add(page);
        project.SelectedPage = page;
        return project;
    }

    [Fact]
    public void HalbkreisNachLinks()
    {
        // Von (0,0) nach (10,0) mit Bulge 1: Halbkreis um (5,0), links in Laufrichtung = oben (y < 0)
        var arc = AreaGeometry.ArcPoints(0, 0, 10, 0, 1).ToList();
        Assert.NotEmpty(arc);
        Assert.All(arc, p =>
        {
            Assert.Equal(5, Distance(p, (5, 0)), 6);
            Assert.True(p.Y < 0);
        });
        Assert.Equal((5.0, -5.0), AreaGeometry.Apex(0, 0, 10, 0, 1));
        Assert.Contains(arc, p => Distance(p, (5, -5)) < 0.05);
    }

    [Fact]
    public void BogenDurchPunkt()
    {
        double bulge = AreaGeometry.BulgeThrough(0, 0, 20, 0, 10, 3);
        var (ax, ay) = AreaGeometry.Apex(0, 0, 20, 0, bulge);
        Assert.Equal(10, ax, 6);
        Assert.Equal(3, ay, 6);
        // Bogen liegt jetzt unten (rechts in Laufrichtung)
        Assert.True(bulge < 0);
        Assert.All(AreaGeometry.ArcPoints(0, 0, 20, 0, bulge), p => Assert.True(p.Y > 0));
    }

    [Fact]
    public void HalberBogenLiegtAufDemKreis()
    {
        double bulge = 0.6, half = AreaGeometry.HalfBulge(bulge);
        var (ax, ay) = AreaGeometry.Apex(0, 0, 10, 0, bulge);
        // Scheitel der ersten Hälfte liegt auf demselben Kreis wie der ganze Bogen
        var whole = AreaGeometry.ArcPoints(0, 0, 10, 0, bulge, 0.0001).ToList();
        var (hx, hy) = AreaGeometry.Apex(0, 0, ax, ay, half);
        // Abstand der Stützpunkte ist etwa 0,07 mm
        Assert.True(whole.Min(p => Distance(p, (hx, hy))) < 0.05);
    }

    [Fact]
    public void KreisflaecheStimmt()
    {
        var outline = AreaGeometry.Outline(AreaShape.Ellipse, 10, 10, 20, 20, null);
        // Der Polygonzug liegt höchstens 0,02 mm innerhalb des Kreises
        Assert.Equal(Math.PI * 100, AreaOf(outline), 2 * Math.PI * 10 * AreaGeometry.Tolerance);
        Assert.All(outline, p => Assert.Equal(10, Distance(p, (20, 20)), 6));
    }

    [Fact]
    public void PolygonRahmenUmfasstBogen()
    {
        var area = new AreaViewModel(NewProject());
        // Unterkante von (0,10) nach (10,10)? Hier: Oberkante (0,0)->(10,0) als Halbkreis nach oben
        area.SetPolygon([new(20, 30, 1), new(30, 30), new(30, 40), new(20, 40)]);
        Assert.Equal(20, area.X, 2);
        Assert.Equal(25, area.Y, 2);       // Bogen reicht 5 mm über die Ecken hinaus
        Assert.Equal(10, area.Width, 2);
        Assert.Equal(15, area.Height, 2);
        Assert.Equal(new AreaPoint(0, 5, 1), area.Points[0]);
        Assert.True(area.Contains(25, 27));
        Assert.False(area.Contains(20.5, 25.5));
    }

    [Fact]
    public void BreiteSkaliertPolygon()
    {
        var area = new AreaViewModel(NewProject());
        area.SetPolygon([new(0, 0), new(10, 0), new(5, 10)]);
        area.Width = 20;
        Assert.Equal([new(0, 0), new(20, 0), new(10, 10)], area.Points);
        area.X = 5;
        Assert.Equal(new AreaPoint(25, 0), area.PagePoints.ToList()[1]);
    }

    [Fact]
    public void FormWechseln()
    {
        var area = new AreaViewModel(NewProject()) { X = 1, Y = 2, Width = 10, Height = 5 };
        area.ChangeShape(AreaShape.Polygon);
        Assert.Equal([new(0, 0), new(10, 0), new(10, 5), new(0, 5)], area.Points);
        area.ChangeShape(AreaShape.Ellipse);
        Assert.Empty(area.Points);
        Assert.Equal((1.0, 2.0, 10.0, 5.0), (area.X, area.Y, area.Width, area.Height));
    }

    [Fact]
    public void Ueberlappung()
    {
        var circle1 = AreaGeometry.Outline(AreaShape.Ellipse, 0, 0, 10, 10, null);
        var circle2 = AreaGeometry.Outline(AreaShape.Ellipse, 10, 0, 10, 10, null);  // berührt
        var circle3 = AreaGeometry.Outline(AreaShape.Ellipse, 8, 0, 10, 10, null);   // überlappt
        var inner = AreaGeometry.Outline(AreaShape.Ellipse, 3, 3, 4, 4, null);       // liegt innen
        var rect1 = AreaGeometry.Outline(AreaShape.Rectangle, 0, 0, 10, 10, null);
        var rect2 = AreaGeometry.Outline(AreaShape.Rectangle, 10, 0, 10, 10, null);  // Kante an Kante
        // Rahmen überlappen, Kreise nicht: (9.5, 9.5) liegt in beiden Rahmen
        var corner = AreaGeometry.Outline(AreaShape.Ellipse, 9, 9, 10, 10, null);

        Assert.False(AreaGeometry.Overlaps(circle1, circle2));
        Assert.True(AreaGeometry.Overlaps(circle1, circle3));
        Assert.True(AreaGeometry.Overlaps(circle1, inner));
        Assert.True(AreaGeometry.Overlaps(inner, circle1));
        Assert.False(AreaGeometry.Overlaps(rect1, rect2));
        Assert.False(AreaGeometry.Overlaps(circle1, corner));
    }

    [Fact]
    public void SpeichernUndLaden()
    {
        var file = new ProjectFile();
        file.Pages.Add(new PageEntry
        {
            Areas =
            {
                new AreaEntry { X = 1, Y = 2, Width = 3, Height = 4 },
                new AreaEntry { Shape = AreaShape.Ellipse, X = 5, Y = 5, Width = 10, Height = 10 },
                new AreaEntry { Shape = AreaShape.Polygon, Points = [new(0, 0, 0.5), new(10, 0), new(5, 8)] },
            },
        });
        var path = Path.Combine(Path.GetTempPath(), $"formen-{Guid.NewGuid():N}{ProjectStore.Extension}");
        try
        {
            ProjectStore.Save(file, path);
            var json = File.ReadAllText(path);
            // Rechtecke und gerade Kanten bleiben im alten Format
            Assert.Equal(2, json.Split("\"Shape\"").Length - 1);
            Assert.Equal(1, json.Split("\"Bulge\"").Length - 1);

            var areas = ProjectStore.Load(path).Pages[0].Areas;
            Assert.Equal(AreaShape.Rectangle, areas[0].Shape);
            Assert.Null(areas[0].Points);
            Assert.Equal(AreaShape.Ellipse, areas[1].Shape);
            Assert.Equal([new(0, 0, 0.5), new(10, 0), new(5, 8)], areas[2].Points);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RueckgaengigBeiPolygon()
    {
        var project = NewProject();
        var page = project.Pages[0];
        var area = new AreaViewModel(project);
        area.SetPolygon([new(0, 0), new(10, 0), new(5, 10)]);
        page.Areas.Add(area);

        page.Areas[0].SetPolygon([new(0, 0), new(10, 0, 0.5), new(5, 10)]);
        project.History.Undo();
        Assert.True(page.Areas[0].IsPolygon);
        Assert.Equal(0, page.Areas[0].Points[1].Bulge);
        project.History.Redo();
        Assert.Equal(0.5, page.Areas[0].Points[1].Bulge);
    }

    [Fact]
    public void PngMitKreis()
    {
        int Black(string file)
        {
            // Bilddaten entpacken: 1 Bit pro Pixel, Zeilen mit Filterbyte, 0 = schwarz
            var bytes = File.ReadAllBytes(file);
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            var idat = new MemoryStream();
            for (int pos = 8; pos < bytes.Length;)
            {
                int len = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
                if (System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4) == "IDAT")
                    idat.Write(bytes, pos + 8, len);
                pos += 12 + len;
            }
            idat.Position = 0;
            var raw = new MemoryStream();
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
                z.CopyTo(raw);
            var data = raw.ToArray();
            int stride = (width + 7) / 8, count = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if ((data[y * (stride + 1) + 1 + x / 8] & (0x80 >> (x % 8))) == 0)
                        count++;
            return count;
        }

        var rectFile = Path.GetTempFileName();
        var circleFile = Path.GetTempFileName();
        try
        {
            PngWriter.WriteOidPng(rectFile, 1000, 20, 20, 600, 2);
            var circle = AreaGeometry.Outline(AreaShape.Ellipse, 0, 0, 20, 20, null);
            PngWriter.WriteOidPng(circleFile, 1000, 20, 20, 600, 2, circle);
            double ratio = (double)Black(circleFile) / Black(rectFile);
            Assert.Equal(Math.PI / 4, ratio, 0.02);
        }
        finally
        {
            File.Delete(rectFile);
            File.Delete(circleFile);
        }
    }
}
