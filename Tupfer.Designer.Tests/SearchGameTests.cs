using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Tupfer.Designer.ViewModels;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Suchspiel: „Wo ist …?“ fragen, richtige Fläche antippen.</summary>
public class SearchGameTests
{
    static List<List<AreaPoint>> Square(double x) => [[new(x, 10), new(x + 10, 10), new(x + 10, 20), new(x, 20)]];

    static (MainWindowViewModel Vm, ProjectViewModel Project) Familie()
    {
        var vm = new MainWindowViewModel();
        var project = vm.Project;
        var page = project.SelectedPage ?? project.Pages[0];
        vm.AddPersonAreas(page, [Square(0), Square(20), Square(40)], ["Anni", "Jörg", "Kati"]);
        // Eine Fläche mit Bedingung spielt nicht mit
        project.AddScript("zaehler", "$n == 1? P(anni)\nP(kati)");
        page.Areas.Add(new AreaViewModel(project) { Label = "Zähler", X = 60, Y = 10, Width = 10, Height = 10, Script = "zaehler" });
        return (vm, project);
    }

    static PenSimulator Pen(ProjectViewModel project)
    {
        var pen = new PenSimulator();
        pen.Load(project.ToFile(Path.GetTempPath()));
        pen.Start();
        return pen;
    }

    [Fact]
    public void NurFlaechenDieEinfachAbspielenSpielenMit()
    {
        var (_, project) = Familie();
        var candidates = SearchGame.Candidates(project);
        Assert.Equal(["anni", "joerg", "kati"], candidates.Select(c => c.Script.Name));
        Assert.All(candidates, c => Assert.False(c.InGame));
    }

    [Fact]
    public void ZweiRundenBisAlleGefundenSind()
    {
        var (_, project) = Familie();
        var targets = SearchGame.Candidates(project).Select(c => new SearchGame.Target(c.Script, c.Area.Label)).ToList();
        SearchGame.Apply(project, targets, new Random(1));
        Assert.Equal("Wo ist Jörg?", project.Speak.Single(s => s.Name == "wo_joerg").Text);

        var pen = Pen(project);
        // Außerhalb des Spiels wie bisher
        Assert.Equal(["anni"], pen.Tap("anni").Media);
        Assert.Equal(["suchspiel_lupe"], pen.Tap(SearchGame.AskScript).Media);

        var start = pen.Tap(SearchGame.StartScript).Media;
        Assert.Equal("suchspiel_los", start[0]);
        var question = start[^1];
        var rounds = new List<List<string>> { new() };
        for (int asked = 0; asked < 6; asked++)
        {
            var wanted = question["wo_".Length..];
            rounds[^1].Add(wanted);
            var wrong = targets.Select(t => t.Script.Name).First(n => n != wanted);
            Assert.Equal([wrong, question], pen.Tap(wrong).Media);
            Assert.Equal([question], pen.Tap(SearchGame.AskScript).Media);
            var right = pen.Tap(wanted).Media;
            Assert.StartsWith("richtig", right[0]);
            if (asked == 2)
            {
                Assert.Equal("suchspiel_geschafft", right[^1]);
                Assert.Equal(0, pen.Registers["$ziel"]);
                Assert.Equal(["anni"], pen.Tap("anni").Media);
                rounds.Add([]);
                question = pen.Tap(SearchGame.StartScript).Media[^1];
            }
            else if (asked == 5)
                Assert.Equal("suchspiel_geschafft", right[^1]);
            else
                question = right[^1];
        }
        // Jede Runde fragt jeden einmal, die zweite in anderer Reihenfolge
        Assert.All(rounds, r => Assert.Equal(["anni", "joerg", "kati"], r.Order()));
        Assert.NotEqual(rounds[0], rounds[1]);
    }

    [Fact]
    public void ErneuernNimmtFlaechenWiederRaus()
    {
        var (_, project) = Familie();
        var candidates = SearchGame.Candidates(project);
        SearchGame.Apply(project, candidates.Select(c => new SearchGame.Target(c.Script, c.Area.Label)).ToList(), new Random(2));
        int scripts = project.Scripts.Count, speak = project.Speak.Count;

        var again = SearchGame.Candidates(project);
        Assert.All(again, c => Assert.True(c.InGame));
        var added = SearchGame.Apply(project, [new(again[0].Script, "Änni"), new(again[2].Script, "Kati")], new Random(3));

        Assert.Empty(added);
        Assert.Equal(scripts, project.Scripts.Count);
        Assert.Equal(speak, project.Speak.Count);
        Assert.Equal("P(joerg)", project.Scripts.Single(s => s.Name == "joerg").Text);
        Assert.Equal("Wo ist Änni?", project.Speak.Single(s => s.Name == "wo_anni").Text);
        Assert.DoesNotContain("wo_joerg", project.Scripts.Single(s => s.Name == SearchGame.QuestionScript).Text);
        Assert.Equal(["joerg"], Pen(project).Tap("joerg").Media);
    }

    [Fact]
    public void SpielLaesstSichBauen()
    {
        var (_, project) = Familie();
        SearchGame.Apply(project, SearchGame.Candidates(project).Select(c => new SearchGame.Target(c.Script, c.Area.Label)).ToList(), new Random(4));
        var gme = GmeBuilder.Build(project.ToFile(Path.GetTempPath()), Path.GetTempPath(), (_, _) => Audio.Wav(new short[100]));
        Assert.NotEmpty(gme);
    }

    [Fact]
    public void SymboleDesSpielsSindOhneSchwarz()
    {
        foreach (var symbol in new[] { Symbols.Search, Symbols.Ask })
        {
            Assert.DoesNotContain("#000", symbol.Svg);
            Assert.Equal(AreaTarget.Script, symbol.Target);
        }
        Assert.Equal(SearchGame.StartScript, Symbols.Search.Script);
        Assert.Equal(SearchGame.AskScript, Symbols.Ask.Script);
    }
}
