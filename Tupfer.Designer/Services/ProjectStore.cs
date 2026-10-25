using System.Text.Json;
using System.Text.Json.Serialization;
using Tupfer.Designer.Model;

namespace Tupfer.Designer.Services;

public static class ProjectStore
{
    public const string Extension = ".tupfer.json";

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

    /// <summary>Basisname ohne ".tupfer.json", z. B. "tic-tac-toi".</summary>
    public static string BaseName(string projectPath)
    {
        var name = Path.GetFileName(projectPath);
        return name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^Extension.Length]
            : Path.GetFileNameWithoutExtension(name);
    }
}
