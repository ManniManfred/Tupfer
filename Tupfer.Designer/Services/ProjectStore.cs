using System.Text.Json;
using System.Text.Json.Serialization;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.Services;

public static class ProjectStore
{
    public const string Extension = ".tupfer.json";

    /// <summary>Endung aus der Zeit vor der Umbenennung (TipToy Designer); solche Projekte lassen sich weiter öffnen.</summary>
    public const string OldExtension = ".tiptoy.json";

    public static bool IsProjectFile(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(OldExtension, StringComparison.OrdinalIgnoreCase);

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ProjectFile Load(string path) => FromJson(File.ReadAllText(path));

    public static void Save(ProjectFile project, string path) => File.WriteAllText(path, ToJson(project));

    public static ProjectFile FromJson(string json) =>
        JsonSerializer.Deserialize<ProjectFile>(json, Options) ?? throw new InvalidDataException("Leere Projektdatei.");

    public static string ToJson(ProjectFile project) => JsonSerializer.Serialize(project, Options);

    /// <summary>Basisname ohne ".tupfer.json" (bzw. ".tiptoy.json"), z. B. "tic-tac-toi".</summary>
    public static string BaseName(string projectPath)
    {
        var name = Path.GetFileName(projectPath);
        return IsProjectFile(name)
            ? name[..^Extension.Length] // beide Endungen sind gleich lang
            : Path.GetFileNameWithoutExtension(name);
    }
}
