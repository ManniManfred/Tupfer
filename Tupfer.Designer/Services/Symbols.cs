using Tupfer.Designer.Model;

namespace Tupfer.Designer.Services;

/// <summary>
/// Symbole für die Sondercodes: Start (Anmelden des Produkts), Wiederholen und Stopp. Jedes ist eine
/// runde SVG-Grafik in mittleren Farben (ohne Schwarz, damit der Stift die OID-Punkte darauf liest).
/// </summary>
public static class Symbols
{
    /// <summary>Durchmesser eines eingefügten Symbols (mm); groß genug zum sicheren Antippen.</summary>
    public const double SizeMm = 15;

    /// <summary>Ein Symbol; <paramref name="Script"/> ist das Skript der Fläche bei <see cref="AreaTarget.Script"/>.</summary>
    public record Symbol(AreaTarget Target, string Label, string FileName, string Svg, string? Script = null);

    static string Circle(string color, string inner) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{SizeMm}mm" height="{SizeMm}mm" viewBox="0 0 100 100">
          <circle cx="50" cy="50" r="48" fill="{color}"/>
          {inner}
        </svg>
        """;

    public static readonly Symbol Start = new(AreaTarget.Product, "Start", "symbol-start.svg",
        Circle("#2e9d4a", """<path d="M39 28 L74 50 L39 72 Z" fill="#ffffff" stroke="#ffffff" stroke-width="6" stroke-linejoin="round"/>"""));

    public static readonly Symbol Replay = new(AreaTarget.Replay, "Wiederholen", "symbol-wiederholen.svg",
        Circle("#2b7bd6", """
          <path d="M27.5 37 A26 26 0 1 0 50 24" fill="none" stroke="#ffffff" stroke-width="9" stroke-linecap="round"/>
          <path d="M53 11 L53 37 L36 24 Z" fill="#ffffff" stroke="#ffffff" stroke-width="3" stroke-linejoin="round"/>
        """));

    public static readonly Symbol Stop = new(AreaTarget.Stop, "Stopp", "symbol-stopp.svg",
        Circle("#d64541", """<rect x="32" y="32" width="36" height="36" rx="4" fill="#ffffff"/>"""));

    public static readonly IReadOnlyList<Symbol> All = [Start, Replay, Stop];

    /// <summary>Lupe des Suchspiels: startet es.</summary>
    public static readonly Symbol Search = new(AreaTarget.Script, "Suchspiel", "symbol-suchspiel.svg",
        Circle("#e8892b", """
          <circle cx="44" cy="44" r="17" fill="none" stroke="#ffffff" stroke-width="8"/>
          <path d="M57 57 L73 73" fill="none" stroke="#ffffff" stroke-width="11" stroke-linecap="round"/>
        """), "suchspiel");

    /// <summary>Fragezeichen des Suchspiels: wiederholt die Frage.</summary>
    public static readonly Symbol Ask = new(AreaTarget.Script, "Nochmal fragen", "symbol-nochmal-fragen.svg",
        Circle("#8e5bd0", """
          <path d="M37 38 A13 13 0 1 1 56 49 C51 52 50 55 50 61" fill="none" stroke="#ffffff" stroke-width="9" stroke-linecap="round"/>
          <circle cx="50" cy="76" r="6" fill="#ffffff"/>
        """), "nochmal");

    public static Symbol For(AreaTarget target) => All.First(s => s.Target == target);

    /// <summary>
    /// Legt die Grafik in <paramref name="dir"/> ab (eine vorhandene gleiche Datei wird weiterbenutzt)
    /// und liefert ihren Pfad.
    /// </summary>
    public static string WriteFile(Symbol symbol, string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, symbol.FileName);
        if (File.Exists(path) && File.ReadAllText(path) == symbol.Svg)
            return path;
        if (File.Exists(path))
            path = WorkFiles.UniquePath(dir, symbol.FileName);
        File.WriteAllText(path, symbol.Svg);
        return path;
    }
}
