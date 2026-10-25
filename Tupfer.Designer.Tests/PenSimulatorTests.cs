using Tupfer.Designer.Gme;
using Tupfer.Designer.Model;
using Tupfer.Designer.Services;
using Xunit;

namespace Tupfer.Designer.Tests;

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

    [Fact]
    public void TicTacToeWartetNachSiegAufStart()
    {
        var pen = new PenSimulator();
        pen.Load(ProjectStore.Load(Path.Combine(AppContext.BaseDirectory, "Programme", "tic-tac-toi.tupfer.json")));
        pen.Start();
        // Kreuz: OL, OM, OR – Kreis: ML, MM
        foreach (var feld in new[] { "feldOL", "feldML", "feldOM", "feldMM" })
            pen.Tap(feld);
        Assert.Equal(["here_now_cross", "player1wins", "new_game"], pen.Tap("feldOR").Media);

        // Weitere Felder starten kein neues Spiel
        Assert.Equal(["game_over"], pen.Tap("feldUL").Media);
        Assert.Equal(["game_over"], pen.Tap("feldOL").Media);

        // Start setzt zurück, Kreuz beginnt
        Assert.Equal(["welcome", "player1"], pen.Start().Media);
        Assert.Equal(["here_now_cross", "player2"], pen.Tap("feldUL").Media);
    }

    [Fact]
    public void TicTacToeUnentschieden()
    {
        var pen = new PenSimulator();
        pen.Load(ProjectStore.Load(Path.Combine(AppContext.BaseDirectory, "Programme", "tic-tac-toi.tupfer.json")));
        pen.Start();
        // X O X / X O O / O X X – kein Dreier
        foreach (var feld in new[] { "feldOL", "feldOM", "feldOR", "feldMM", "feldML", "feldMR", "feldUM", "feldUL" })
            pen.Tap(feld);
        Assert.Equal(["here_now_cross", "draw", "new_game"], pen.Tap("feldUR").Media);
        Assert.Equal(["game_over"], pen.Tap("feldMM").Media);
    }

    [Fact]
    public void TicTacToeLaesstSichBauen()
    {
        var file = ProjectStore.Load(Path.Combine(AppContext.BaseDirectory, "Programme", "tic-tac-toi.tupfer.json"));
        var gme = GmeBuilder.Build(file, AppContext.BaseDirectory, (_, _) => Audio.Wav(new short[100]));
        Assert.NotEmpty(gme);
    }

    [Fact]
    public void MeineLeuteSagtBeimAntippenHallo()
    {
        var file = ProjectStore.Load(Path.Combine(AppContext.BaseDirectory, "Programme", "meine-leute.tupfer.json"));
        var pen = new PenSimulator();
        pen.Load(file);
        Assert.Equal(["willkommen"], pen.Start().Media);
        foreach (var person in new[] { "mama", "papa", "oma", "opa", "geschwister", "ich" })
        {
            Assert.Equal([person], pen.Tap(person).Media);
            Assert.Equal([person], pen.Tap(person).Media);
        }
        Assert.NotEmpty(GmeBuilder.Build(file, AppContext.BaseDirectory, (_, _) => Audio.Wav(new short[100])));
    }

    [Fact]
    public void WoIstFragtNachTierenUndWechseltDieReihenfolge()
    {
        var file = ProjectStore.Load(Path.Combine(AppContext.BaseDirectory, "Programme", "wo-ist.tupfer.json"));
        var pen = new PenSimulator();
        pen.Load(file);
        pen.Start();
        // Ohne Spiel sagt jedes Tier, wer es ist
        Assert.Equal(["das_ist_katze"], pen.Tap("katze").Media);
        Assert.Equal(["lupe_tippen"], pen.Tap("nochmal").Media);

        Assert.Equal(["los", "wo_kuh"], pen.Tap("suchspiel").Media);
        Assert.Equal(["das_ist_hund", "wo_kuh"], pen.Tap("hund").Media);
        Assert.Equal(["wo_kuh"], pen.Tap("nochmal").Media);
        string[] runde1 = ["kuh", "katze", "huhn", "hund", "schaf", "ente", "schwein", "pferd"];
        for (var i = 0; i < runde1.Length; i++)
        {
            var media = pen.Tap(runde1[i]).Media;
            Assert.Equal(2, media.Count);
            Assert.StartsWith("richtig", media[0]);
            Assert.Equal(i + 1 < runde1.Length ? $"wo_{runde1[i + 1]}" : "geschafft", media[1]);
        }
        Assert.Equal(["das_ist_kuh"], pen.Tap("kuh").Media);

        // Die nächste Runde fragt in anderer Reihenfolge
        Assert.Equal(["los", "wo_ente"], pen.Tap("suchspiel").Media);
        Assert.NotEmpty(GmeBuilder.Build(file, AppContext.BaseDirectory, (_, _) => Audio.Wav(new short[100])));
    }
}
