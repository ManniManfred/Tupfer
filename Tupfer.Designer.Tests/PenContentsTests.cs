using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Produkte auf dem Stift: Kopf der GME-Dateien lesen und doppelte Produkt-IDs finden.</summary>
public class PenContentsTests : IDisposable
{
    readonly string _pen = Directory.CreateTempSubdirectory("tupfer-stift").FullName;

    public void Dispose() => Directory.Delete(_pen, true);

    void WriteGme(string relativePath, uint productId, string comment)
    {
        var project = new ProjectFile { ProductId = productId, Comment = comment };
        project.Scripts.Add(new ScriptEntry { Name = "a", Lines = ["P(hallo)"] });
        project.Speak.Add(new SpeakEntry { Name = "hallo", Text = "Hallo" });
        var path = Path.Combine(_pen, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, GmeBuilder.Build(project, _pen, (_, _) => [1, 2, 3], new DateTime(2026, 10, 23)));
    }

    [Fact]
    public void KopfWirdGelesen()
    {
        WriteGme("buch.gme", 900, "Mein tiptoi-Buch");
        var p = PenContents.ReadHeader(Path.Combine(_pen, "buch.gme"), "buch.gme");
        Assert.Null(p.Error);
        Assert.Equal(900u, p.ProductId);
        Assert.Equal("Mein tiptoi-Buch", p.Comment);
        Assert.Equal(new DateTime(2026, 10, 23), p.Date);
        Assert.Equal(new FileInfo(Path.Combine(_pen, "buch.gme")).Length, p.Size);
    }

    [Fact]
    public void DoppelteProduktIdsWerdenGefunden()
    {
        WriteGme("druckertest.gme", 950, "Druckertest");
        WriteGme("alt/Druckertest-alt.GME", 950, "Druckertest alt");
        WriteGme("anni.gme", 900, "Anni");
        File.WriteAllText(Path.Combine(_pen, "kaputt.gme"), "keine GME");
        File.WriteAllText(Path.Combine(_pen, "notiz.txt"), "keine GME");

        var products = PenContents.ReadProducts(_pen);
        Assert.Equal(["anni.gme", "alt" + Path.DirectorySeparatorChar + "Druckertest-alt.GME", "druckertest.gme", "kaputt.gme"],
            products.Select(p => p.RelativePath));
        Assert.NotNull(products[^1].Error);

        var duplicate = Assert.Single(PenContents.Duplicates(products));
        Assert.Equal(950u, duplicate.Key);

        var text = PenContents.Describe(_pen, products, projectProductId: 900);
        Assert.Contains("doppelte Produkt-IDs", text);
        Assert.Contains("950: alt", text);
        Assert.Contains("Produkt-ID des geöffneten Projekts (900)", text);
        Assert.Contains("keine gültige GME-Datei", text);
    }

    [Fact]
    public void RavensburgerDateienMitGleicherIdSindKeineDoppelten()
    {
        const string official = "CHOMPTECH DATA FORMAT CopyRight 2009 Ver2.10.0901";
        WriteGme("Spielfiguren1.gme", 53, official);
        WriteGme("Spielfiguren2.gme", 53, official);
        WriteGme("eigenes.gme", 68, "Mein Buch");
        WriteGme("WissenQuizzen1.gme", 68, official);

        var products = PenContents.ReadProducts(_pen);
        Assert.Equal(68u, Assert.Single(PenContents.Duplicates(products)).Key);
        Assert.Contains("Mehrere Ravensburger-Dateien je Produkt (so vorgesehen): 53", PenContents.Describe(_pen, products));
    }

    [Fact]
    public void OhneDoppelteKeineWarnung()
    {
        WriteGme("a.gme", 900, "A");
        WriteGme("b.gme", 901, "B");
        var text = PenContents.Describe(_pen, PenContents.ReadProducts(_pen));
        Assert.Contains("Keine doppelten Produkt-IDs.", text);
    }
}
