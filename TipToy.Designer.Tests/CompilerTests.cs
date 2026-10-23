using TipToy.Designer.Compiler;
using TipToy.Designer.Services;
using Xunit;

namespace TipToy.Designer.Tests;

public class CompilerTests
{
    static string Programm(string datei) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Programme", datei));

    static StiftEmulator Emulator(CompileResult r) =>
        new(r.Scripts.Select(s => (s.Name, (IEnumerable<string>)s.Lines)), r.Init);

    static CompileResult Uebersetze(string quelltext)
    {
        var r = ProgramCompiler.Compile(quelltext);
        Assert.True(r.Success, string.Join("\n", r.Errors));
        return r;
    }

    /// <summary>Viele zufällige Tipps: Audio und alle Felder müssen wie beim echten C#-Programm sein.</summary>
    [Theory]
    [InlineData("TicTacToe.cs")]
    [InlineData("Konstrukte.cs")]
    [InlineData("Schleifen.cs")]
    public void Verhaelt_sich_wie_natives_CSharp(string datei)
    {
        var quelltext = Programm(datei);
        var r = Uebersetze(quelltext);
        var stift = Emulator(r);
        var nativ = new NativesProgramm(quelltext);
        var zufall = new Random(42);

        for (int tipp = 0; tipp < 3000; tipp++)
        {
            var oid = r.EntryPoints[zufall.Next(r.EntryPoints.Count)];
            nativ.Tippe(oid);
            stift.Tippe(oid);
            foreach (var (feld, wert) in nativ.Felder())
                Assert.True(wert == stift.Register.GetValueOrDefault("$" + feld), $"Tipp {tipp} ({oid}): {feld} nativ={wert}, Stift={stift.Register.GetValueOrDefault("$" + feld)}");
            Assert.Equal(nativ.Abgespielt, stift.Abgespielt);
        }
    }

    [Fact]
    public void TicTacToe_klingt_wie_die_Original_YAML()
    {
        var r = Uebersetze(Programm("TicTacToe.cs"));
        var original = TttoolYaml.Import(Path.Combine(AppContext.BaseDirectory, "Programme", "tic-tac-toi.yaml"));
        var stiftOriginal = new StiftEmulator(original.Scripts.Select(s => (s.Name, (IEnumerable<string>)s.Lines)), original.Init);
        var stiftCSharp = Emulator(r);
        var zufall = new Random(7);
        for (int tipp = 0; tipp < 3000; tipp++)
        {
            var oid = r.EntryPoints[zufall.Next(r.EntryPoints.Count)];
            stiftOriginal.Tippe(oid);
            stiftCSharp.Tippe(oid);
        }
        Assert.Equal(stiftOriginal.Abgespielt, stiftCSharp.Abgespielt);
    }

    [Theory]
    [InlineData("TicTacToe.cs")]
    [InlineData("Konstrukte.cs")]
    [InlineData("Schleifen.cs")]
    public void Hoechstens_acht_Befehle_pro_Zeile(string datei)
    {
        var r = Uebersetze(Programm(datei));
        foreach (var zeile in r.Scripts.SelectMany(s => s.Lines))
            Assert.True(Gme.ScriptParser.ParseLine(zeile).Commands.Count <= ProgramCompiler.MaxCommandsPerLine, zeile);
    }

    [Fact]
    public void Gleiche_Hilfsskripte_werden_zusammengefasst()
    {
        var r = Uebersetze(Programm("TicTacToe.cs"));
        Assert.Equal(9, r.EntryPoints.Count);
        Assert.True(r.Scripts.Count <= 16, $"{r.Scripts.Count} Skripte");
        Assert.Equal("$zug := 1", r.Init);
    }

    [Fact]
    public void Random_liefert_Werte_im_Bereich()
    {
        var r = Uebersetze("""
            class W : TipToi
            {
                int wurf;
                [Oid] void Wuerfeln() { wurf = Random(6) + 1; }
            }
            """);
        var stift = Emulator(r);
        var gesehen = new HashSet<ushort>();
        for (int i = 0; i < 600; i++)
        {
            stift.Tippe("Wuerfeln");
            var w = stift.Register["$wurf"];
            Assert.InRange(w, (ushort)1, (ushort)6);
            gesehen.Add(w);
        }
        Assert.Equal(6, gesehen.Count);
    }

    [Theory]
    [InlineData("[Oid] void A() { while (a < 3) a++; }", 3, "while")]
    [InlineData("[Oid] void A() { for (int i = 0; i < a; i++) Play(\"x\"); }", 3, "Grenze muss konstant")]
    [InlineData("[Oid] void A() { for (int i = 0; i < 1000; i++) a++; }", 3, "mehr als 100")]
    [InlineData("[Oid] void A() { for (int i = 0; i < 3; i++) i = 5; }", 3, "Schleifenvariable")]
    [InlineData("[Oid] void A() { for (int i = 0; i < 3; i++) { if (a == i) break; } }", 3, "break")]
    [InlineData("[Oid] void A() { for (int i = 0; i < 3; i *= 2) a++; }", 3, "Schritt")]
    [InlineData("[Oid] void A() { B(); } void B() { A(); }", 3, "Rekursion")]
    [InlineData("[Oid] void A() { Sag(\"x\"); }", 3, "Unbekannte Methode")]
    [InlineData("[Oid] void A() { a = b; }", 3, "„b“ ist unbekannt")]
    [InlineData("[Oid] void A() { a = 70000; }", 3, "zwischen 0 und 65535")]
    [InlineData("string s;", 3, "nicht unterstützt")]
    [InlineData("int Wert() { return 1; }", 3, "void")]
    [InlineData("[Oid] void A() { a = 1 }", 3, ";")]
    public void Fehler_werden_mit_Zeile_gemeldet(string member, int zeile, string text)
    {
        var r = ProgramCompiler.Compile($"class F : TipToi\n{{\n    {member}\n    int a;\n}}");
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Line == zeile && e.Message.Contains(text));
    }

    [Fact]
    public void Schleifenvariable_ist_im_Rumpf_konstant()
    {
        var r = Uebersetze("""
            class S : TipToi
            {
                int summe;
                [Oid] void A() { summe = 0; for (int i = 1; i <= 4; i++) { summe += i; if (i == 3) Play("drei"); } }
            }
            """);
        // Bedingungen auf i sind schon beim Übersetzen entschieden: eine einzige Zeile ohne Sprung
        var zeile = Assert.Single(Assert.Single(r.Scripts).Lines);
        Assert.Equal("$summe := 0 $summe += 1 $summe += 2 $summe += 3 P(drei) $summe += 4", zeile);
    }

    [Fact]
    public void Ohne_Oid_Methode_gibt_es_einen_Hinweis()
    {
        var r = ProgramCompiler.Compile("class F : TipToi { int a; }");
        Assert.Contains(r.Errors, e => e.Message.Contains("[Oid]"));
    }

    [Fact]
    public void Platzhalterklassen_fuer_die_IDE_werden_ignoriert()
    {
        var r = Uebersetze("""
            public class OidAttribute : System.Attribute { }
            public abstract class TipToi { protected void Play(params string[] a) { } }
            class S : TipToi { [Oid] void A() => Play("hallo welt"); }
            """);
        Assert.Equal("P(\"hallo welt\")", Assert.Single(r.Scripts).Lines.Single());
    }

    [Fact]
    public void Aus_dem_Programm_entsteht_eine_GME_Datei()
    {
        var r = Uebersetze(Programm("TicTacToe.cs"));
        var projekt = new Model.ProjectFile { ProductId = 901, Init = r.Init, Welcome = "player1" };
        projekt.Scripts.AddRange(r.Scripts.Select(s => new Model.ScriptEntry { Name = s.Name, Lines = s.Lines, Generated = true }));
        var original = TttoolYaml.Import(Path.Combine(AppContext.BaseDirectory, "Programme", "tic-tac-toi.yaml"));
        projekt.Speak.AddRange(original.Speak);
        var gme = Gme.GmeBuilder.Build(projekt, AppContext.BaseDirectory, (sprache, text) => new byte[] { 0x4F, 0x67, 0x67, 0x53 });
        Assert.Equal(901u, BitConverter.ToUInt32(gme, 0x14));
        Assert.All(projekt.Scripts, s => Assert.NotNull(s.Code));
    }
}
