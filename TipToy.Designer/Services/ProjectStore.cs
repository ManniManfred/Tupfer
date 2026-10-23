using System.Text.Json;
using System.Text.Json.Serialization;
using TipToy.Designer.Model;

namespace TipToy.Designer.Services;

public static class ProjectStore
{
    public const string Extension = ".tiptoy.json";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ProjectFile Load(string path) =>
        JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException("Leere Projektdatei.");

    public static void Save(ProjectFile project, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(project, Options));

    /// <summary>Basisname ohne ".tiptoy.json", z. B. "tic-tac-toi".</summary>
    public static string BaseName(string projectPath)
    {
        var name = Path.GetFileName(projectPath);
        return name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^Extension.Length]
            : Path.GetFileNameWithoutExtension(name);
    }
}
