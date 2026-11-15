using Avalonia;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Namen der bei „Personen erkennen“ angelegten Skripte, Texte und Flächen.</summary>
public class PersonNamesTests
{
    [Theory]
    [InlineData("Anni", "anni")]
    [InlineData("  Jörg  ", "joerg")]
    [InlineData("Änne Müller", "aenne_mueller")]
    [InlineData("Hans-Peter", "hans_peter")]
    [InlineData("Strauß", "strauss")]
    [InlineData("José", "jose")]
    [InlineData("Oma (87)!", "oma_87")]
    [InlineData("3. Kind", "p3_kind")]
    [InlineData("", "")]
    [InlineData("!!!", "")]
    [InlineData(null, "")]
    public void SkriptnameAusDemNamen(string? name, string expected) =>
        Assert.Equal(expected, PersonNames.ToScriptName(name));

    [Fact]
    public void LeereNamenBekommenDenPlatzhalter()
    {
        var names = PersonNames.Assign(["Anni", "", "  ", "Paul"], _ => false);
        Assert.Equal([new("anni", "Anni"), new("person1", "Person 1"), new("person2", "Person 2"), new("paul", "Paul")], names);
    }

    [Fact]
    public void VergebeneNamenWerdenNummeriert()
    {
        var taken = new HashSet<string> { "anni", "person1" };
        var names = PersonNames.Assign(["Anni", "anni", "", "Änni"], taken.Contains);
        Assert.Equal(["anni2", "anni3", "person2", "aenni"], names.Select(n => n.Name));
        Assert.Equal(["Anni", "anni", "Person 2", "Änni"], names.Select(n => n.Label));
    }

    static List<List<AreaPoint>> Square(double x) => [[new(x, 10), new(x + 10, 10), new(x + 10, 20), new(x, 20)]];

    [Fact]
    public void FlaechenSkripteUndTexteHeissenWieDiePerson()
    {
        var vm = new MainWindowViewModel();
        var project = vm.Project;
        var page = project.SelectedPage ?? project.Pages[0];
        // Vorhandenes Skript „anni“ (z. B. aus einem Suchspiel) darf nicht überschrieben oder verdoppelt werden
        project.AddScript("anni", "P(begruessung)");
        int scripts = project.Scripts.Count;

        // Zweite Person ist durch etwas davor geteilt: zwei Flächen, ein Text
        var assigned = vm.AddPersonAreas(page, [Square(0), [.. Square(20), .. Square(40)], Square(60)], ["Anni", "Jörg", ""]);

        Assert.Equal(["anni2", "joerg", "person1"], assigned.Select(a => a.Name));
        Assert.Equal(scripts + 3, project.Scripts.Count);
        Assert.Equal("P(begruessung)", project.Scripts.Single(s => s.Name == "anni").Text);
        foreach (var (name, label) in assigned)
        {
            var speak = project.Speak.Single(s => s.Name == name);
            Assert.Equal(label, speak.Text);
            Assert.Equal($"P({name})", project.Scripts.Single(s => s.Name == name).Text);
        }
        var areas = page.Areas.Where(a => a.IsScript).ToList();
        Assert.Equal(["Anni", "Jörg", "Jörg", "Person 1"], areas.Select(a => a.Label));
        Assert.Equal(["anni2", "joerg", "joerg", "person1"], areas.Select(a => a.Script));
    }

    [Fact]
    public void KopfausschnittObenAnDerPerson()
    {
        Assert.Equal(new Rect(10, 5, 20, 20), PersonDetector.HeadRegion([(10, 5), (30, 5), (30, 85), (10, 85)]));
        // Liegende Person: so hoch wie sie, waagrecht mittig
        Assert.Equal(new Rect(40, 0, 20, 20), PersonDetector.HeadRegion([(0, 0), (100, 0), (100, 20), (0, 20)]));
    }
}
