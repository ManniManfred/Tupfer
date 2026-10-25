using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tupfer.Designer.Tests;

/// <summary>Kompiliert ein Stift-Programm als echtes C# und führt es aus (Referenz für den Übersetzer).</summary>
public class NativesProgramm
{
    const string Api = """
        public class OidAttribute : System.Attribute { public OidAttribute() {} public OidAttribute(string n) {} }
        public static class Protokoll { public static System.Collections.Generic.List<string> Audio = new(); }
        public abstract class TipToi
        {
            protected void Play(params string[] a) => Protokoll.Audio.AddRange(a);
            protected void PlayAll(params string[] a) => Protokoll.Audio.AddRange(a);
            protected void PlayRandom(params string[] a) => Protokoll.Audio.Add(a[0]);
            protected void Cancel() {}
        }
        """;

    readonly object _instanz;
    readonly Type _protokoll;

    public NativesProgramm(string quelltext)
    {
        var referenzen = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Programm" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(quelltext), CSharpSyntaxTree.ParseText(Api) }, referenzen,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var ergebnis = compilation.Emit(ms);
        if (!ergebnis.Success)
            throw new InvalidOperationException(string.Join("\n", ergebnis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(ms.ToArray());
        var typ = assembly.GetTypes().First(t => t.BaseType?.Name == "TipToi");
        _instanz = Activator.CreateInstance(typ, true)!;
        _protokoll = assembly.GetType("Protokoll")!;
    }

    public List<string> Abgespielt => (List<string>)_protokoll.GetField("Audio")!.GetValue(null)!;

    public void Tippe(string methode) =>
        _instanz.GetType().GetMethod(methode, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(_instanz, null);

    public Dictionary<string, ushort> Felder() => _instanz.GetType()
        .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .ToDictionary(f => f.Name, f => f.GetValue(_instanz) switch
        {
            bool b => (ushort)(b ? 1 : 0),
            var v => unchecked((ushort)Convert.ToInt64(v)),
        });
}
