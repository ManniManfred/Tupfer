using TipToy.Designer.ViewModels;

namespace TipToy.Designer;

/// <summary>
/// Beispielprojekte aus <c>examples/</c>, die mit der Web-App unter <c>examples/</c> ausgeliefert werden
/// (siehe .csproj). Beim Öffnen werden sie vom Server in ein neues Arbeitsverzeichnis geladen.
/// </summary>
static class Examples
{
    /// <summary>Id (für ?beispiel=…), Titel, Projektdatei und weitere Dateien, relativ zu <c>examples/</c>.</summary>
    static readonly (string Id, string Title, string Main, string[] Files)[] List =
    [
        ("tic-tac-toi", "Tic-Tac-Toe", "tic-tac-toi/tic-tac-toi.tiptoy.json", ["tic-tac-toi/tic-tac-toi.png"]),
        ("tic-tac-toi-csharp", "Tic-Tac-Toe als C#-Programm", "tic-tac-toi-csharp/tic-tac-toi-csharp.tiptoy.json", ["tic-tac-toi/tic-tac-toi.png"]),
    ];

    public static IReadOnlyList<ExampleProject> Create(Uri baseUri)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUri, "examples/") };
        return List.Select(e => new ExampleProject(e.Id, e.Title, () => FetchAsync(http, e.Main, e.Files))).ToList();
    }

    static async Task<string> FetchAsync(HttpClient http, string main, string[] files)
    {
        // Ordnerstruktur beibehalten, damit relative Pfade wie ../tic-tac-toi/… stimmen
        var dir = BrowserFileDialogs.NewWorkDir();
        foreach (var file in files.Prepend(main))
        {
            var path = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(file));
        }
        var mainPath = Path.Combine(dir, main);
        Environment.CurrentDirectory = Path.GetDirectoryName(mainPath)!;
        return mainPath;
    }
}
