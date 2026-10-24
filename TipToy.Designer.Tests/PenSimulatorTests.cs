using TipToy.Designer.Gme;
using TipToy.Designer.Model;
using Xunit;

namespace TipToy.Designer.Tests;

/// <summary>Stift-Simulation für den Testmodus.</summary>
public class PenSimulatorTests
{
    static PenSimulator Pen(string init, string welcome, params (string Name, string[] Lines)[] scripts)
    {
        var p = new ProjectFile { ProductId = 950, Init = init, Welcome = welcome };
        foreach (var (name, lines) in scripts)
            p.Scripts.Add(new ScriptEntry { Name = name, Lines = lines.ToList() });
        var pen = new PenSimulator();
        pen.Load(p);
        return pen;
    }

    [Fact]
    public void StartSetztRegisterUndSpieltBegruessung()
    {
        var pen = Pen("$punkte := 3", "hallo, los", ("feld", ["P(a)"]));
        var start = pen.Start();
        Assert.Equal(["hallo", "los"], start.Media);
        Assert.Equal(3, pen.Registers["$punkte"]);
    }

    [Fact]
    public void ErsteZutreffendeZeileWirdAusgefuehrt()
    {
        var pen = Pen("", "", ("zaehlen",
        [
            "$n == 2? $n := 0 P(fertig)",
            "$n += 1 P(weiter)",
        ]));
        pen.Start();
        Assert.Equal(["weiter"], pen.Tap("zaehlen").Media);
        Assert.Equal(["weiter"], pen.Tap("zaehlen").Media);
        Assert.Equal(["fertig"], pen.Tap("zaehlen").Media);
        Assert.Equal(0, pen.Registers["$n"]);
        Assert.Equal(["fertig"], pen.LastMedia);
    }

    [Fact]
    public void SprungFolgtNachDerZeile()
    {
        var pen = Pen("", "",
            ("a", ["P(eins) J(b) $x := 1"]),
            ("b", ["$x == 1? P(zwei)", "P(falsch)"]));
        pen.Start();
        var r = pen.Tap("a");
        Assert.Equal(["eins", "zwei"], r.Media);
    }

    [Fact]
    public void AbbruchBeendetZeile()
    {
        var pen = Pen("", "", ("a", ["P(eins) C $x := 5"]));
        pen.Start();
        var r = pen.Tap("a");
        Assert.True(r.Stop);
        Assert.False(pen.Registers.ContainsKey("$x"));
    }

    [Fact]
    public void AbspielVarianten()
    {
        var pen = Pen("", "", ("alle", ["PA(a, b, c)"]), ("eins", ["P(a, b, c)"]), ("zufall", ["T($r, 3)"]));
        pen.Start();
        Assert.Equal(["a", "b", "c"], pen.Tap("alle").Media);
        for (int i = 0; i < 20; i++)
        {
            Assert.Contains(Assert.Single(pen.Tap("eins").Media), new[] { "a", "b", "c" });
            pen.Tap("zufall");
            Assert.InRange(pen.Registers["$r"], 0, 2);
        }
    }

    [Fact]
    public void EndlosschleifeWirdAbgebrochen()
    {
        var pen = Pen("", "", ("a", ["J(a)"]));
        pen.Start();
        Assert.Contains(pen.Tap("a").Trace, t => t.Contains("Endlosschleife"));
    }

    [Fact]
    public void FehlerNenntSkriptUndZeile()
    {
        var ex = Assert.Throws<ScriptParseException>(() => Pen("", "", ("kaputt", ["P(a)", "X(1)"])));
        Assert.Contains("„kaputt“, Zeile 2", ex.Message);
    }
}
