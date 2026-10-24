namespace TipToy.Designer.Compiler;

/// <summary>
/// Die Web-App kommt ohne den C#-Übersetzer aus (Roslyn würde sie mehr als verdoppeln).
/// Projekte mit C#-Programm verwenden die gespeicherten, zuletzt übersetzten Skripte.
/// </summary>
public class ProgramCompiler
{
    public static bool IsAvailable => false;

    public static CompileResult Compile(string source)
    {
        var result = new CompileResult();
        result.Errors.Add(new CompileError(0, 0, "C#-Programme lassen sich nur in der Desktop-Version übersetzen."));
        return result;
    }
}
