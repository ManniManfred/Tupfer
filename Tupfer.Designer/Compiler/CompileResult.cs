namespace Tupfer.Designer.Compiler;

public record CompileError(int Line, int Column, string Message)
{
    public override string ToString() => Line > 0 ? $"Zeile {Line}, Spalte {Column}: {Message}" : Message;
}

public record CompiledScript(string Name, List<string> Lines);

public class CompileResult
{
    public List<CompiledScript> Scripts { get; } = new();

    /// <summary>Namen der Skripte zum Antippen ([Oid]-Methoden); alle anderen sind Hilfsskripte.</summary>
    public List<string> EntryPoints { get; } = new();

    /// <summary>Startwerte der Register im tttool-Format, z. B. "$zug := 1".</summary>
    public string Init { get; set; } = "";

    public List<CompileError> Errors { get; } = new();

    public bool Success => Errors.Count == 0;
}
