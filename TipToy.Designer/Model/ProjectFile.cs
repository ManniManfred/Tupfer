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
    public string BackgroundImage { get; set; } = "";
    public List<AreaEntry> Areas { get; set; } = new();
}

public enum AreaTarget
{
    Script,
    Product,
    Replay,
    Stop,
}

public class AreaEntry
{
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public AreaTarget Target { get; set; }
    public string Script { get; set; } = "";
}
