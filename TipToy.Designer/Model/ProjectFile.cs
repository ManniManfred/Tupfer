using System.Text.Json.Serialization;

namespace TipToy.Designer.Model;

/// <summary>Speicherformat eines Designer-Projekts (*.tiptoy.json).</summary>
public class ProjectFile
{
    public int Version { get; set; } = 1;
    public uint ProductId { get; set; } = 900;
    public string Comment { get; set; } = "";
    public string Language { get; set; } = "de";
    public string GmeLang { get; set; } = "";
    public string Init { get; set; } = "";
    public string Welcome { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public string ExtraYaml { get; set; } = "";

    /// <summary>C#-Programm, aus dem Skripte erzeugt werden (leer = keins).</summary>
    public string Program { get; set; } = "";
    public int? Replay { get; set; }
    public int? Stop { get; set; }

    public string TttoolPath { get; set; } = "tttool";

    /// <summary>GME mit dem externen tttool statt mit dem eingebauten Builder erstellen.</summary>
    public bool UseTttool { get; set; }
    public int Dpi { get; set; } = 1200;
    public int PixelSize { get; set; } = 2;

    public List<ScriptEntry> Scripts { get; set; } = new();
    public List<SpeakEntry> Speak { get; set; } = new();
    public List<PageEntry> Pages { get; set; } = new();
}

public class ScriptEntry
{
    public string Name { get; set; } = "";
    public int? Code { get; set; }
    public List<string> Lines { get; set; } = new();

    /// <summary>Vom C#-Programm erzeugt (wird beim Übersetzen ersetzt).</summary>
    public bool Generated { get; set; }

    /// <summary>Hilfsskript des C#-Programms (wird nur per Sprung erreicht, nicht gedruckt).</summary>
    public bool Helper { get; set; }
}

public class SpeakEntry
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

public class PageEntry
{
    public string Name { get; set; } = "";
    public double WidthMm { get; set; } = 210;
    public double HeightMm { get; set; } = 297;

    /// <summary>Nur noch zum Laden alter Projekte: wird beim Öffnen in ein Bildelement umgewandelt.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundImage { get; set; }

    /// <summary>Bilder und Texte in Zeichenreihenfolge (das letzte liegt oben).</summary>
    public List<ElementEntry> Elements { get; set; } = new();
    public List<AreaEntry> Areas { get; set; } = new();
}

public enum ElementKind
{
    Image,
    Text,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Ein Bild oder Text auf der Seite. X/Y/Width/Height (mm) beschreiben den ungedrehten Rahmen,
/// gedreht wird um dessen Mitte.
/// </summary>
public class ElementEntry
{
    public ElementKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Drehung im Uhrzeigersinn in Grad.</summary>
    public double Rotation { get; set; }

    // Bild
    public string ImagePath { get; set; } = "";

    /// <summary>Sichtbarer Ausschnitt des Bilds als Anteil (0–1) von Breite und Höhe.</summary>
    public double CropX { get; set; }
    public double CropY { get; set; }
    public double CropWidth { get; set; } = 1;
    public double CropHeight { get; set; } = 1;

    // Text
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 24;
    public string FontFamily { get; set; } = "";
    public string Color { get; set; } = "#000000";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public TextAlign Align { get; set; }
}

public enum AreaTarget
{
    Script,
    Product,
    Replay,
    Stop,
}

public enum AreaShape
{
    Rectangle,
    Ellipse,
    Polygon,
}

/// <summary>
/// Eckpunkt eines Polygons in mm, relativ zur oberen linken Ecke der Fläche.
/// <paramref name="Bulge"/> beschreibt die Kante zum nächsten Punkt: 0 = gerade, sonst ein Kreisbogen
/// mit Bulge = tan(Bogenwinkel / 4) = 2 · Stichhöhe / Sehnenlänge (wie in DXF). Positiv wölbt
/// sich der Bogen nach links (in Laufrichtung, y nach unten), negativ nach rechts.
/// </summary>
public record AreaPoint(double X, double Y,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double Bulge = 0);

/// <summary>
/// Eine OID-Fläche. X/Y/Width/Height (mm) sind der umschließende Rahmen; bei einer Ellipse
/// ist sie in diesen Rahmen eingepasst, ein Polygon wird durch <see cref="Points"/> beschrieben.
/// </summary>
public class AreaEntry
{
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AreaShape Shape { get; set; }

    /// <summary>Nur bei <see cref="AreaShape.Polygon"/>: die Eckpunkte (mindestens drei).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AreaPoint>? Points { get; set; }

    public AreaTarget Target { get; set; }
    public string Script { get; set; } = "";
}
