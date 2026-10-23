using System.Globalization;
using System.Text;
using TipToy.Designer.Model;
using YamlDotNet.RepresentationModel;

namespace TipToy.Designer.Services;

/// <summary>Import und Export von tttool-YAML-Dateien (inkl. *.codes.yaml).</summary>
public static class TttoolYaml
{
    static readonly HashSet<string> KnownKeys = new()
    {
        "product-id", "comment", "welcome", "media-path", "gme-lang", "init", "language",
        "scripts", "speak", "scriptcodes", "replay", "stop",
    };

    public static string CodesPathFor(string yamlPath) =>
        Path.Combine(Path.GetDirectoryName(yamlPath) ?? "", Path.GetFileNameWithoutExtension(yamlPath) + ".codes.yaml");

    public static ProjectFile Import(string yamlPath)
    {
        var project = new ProjectFile();
        var root = LoadMapping(yamlPath);
        var extra = new YamlMappingNode();

        foreach (var (keyNode, value) in root.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? "";
            switch (key)
            {
                case "product-id": project.ProductId = uint.Parse(Scalar(value), CultureInfo.InvariantCulture); break;
                case "comment": project.Comment = Scalar(value); break;
                case "welcome": project.Welcome = Scalar(value); break;
                case "media-path": project.MediaPath = Scalar(value); break;
                case "gme-lang": project.GmeLang = Scalar(value); break;
                case "init": project.Init = Scalar(value); break;
                case "language": project.Language = Scalar(value); break;
                case "scripts":
                    foreach (var (name, lines) in ((YamlMappingNode)value).Children)
                        project.Scripts.Add(new ScriptEntry { Name = Scalar(name), Lines = Lines(lines) });
                    break;
                case "speak":
                    ReadSpeak(value, project);
                    break;
                case "scriptcodes":
                case "replay":
                case "stop":
                    break; // wird unten gemeinsam mit der codes.yaml gelesen
                default:
                    extra.Add(keyNode, value);
                    break;
            }
        }

        ReadCodes(root, project);
        var codesPath = CodesPathFor(yamlPath);
        if (File.Exists(codesPath))
            ReadCodes(LoadMapping(codesPath), project);

        if (extra.Children.Count > 0)
        {
            var sb = new StringWriter();
            new YamlStream(new YamlDocument(extra)).Save(sb, assignAnchors: false);
            project.ExtraYaml = sb.ToString().Replace("...", "").Trim();
        }

        project.Pages.Add(new PageEntry { Name = "Seite 1" });
        return project;
    }

    static YamlMappingNode LoadMapping(string path)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(path))
            stream.Load(reader);
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode m)
            return new YamlMappingNode();
        return m;
    }

    static string Scalar(YamlNode node) => node switch
    {
        YamlScalarNode s => s.Value ?? "",
        YamlSequenceNode seq => string.Join(", ", seq.Children.Select(Scalar)),
        _ => node.ToString(),
    };

    static List<string> Lines(YamlNode node) => node switch
    {
        YamlSequenceNode seq => seq.Children.Select(Scalar).ToList(),
        _ => new List<string> { Scalar(node) },
    };

    static void ReadSpeak(YamlNode node, ProjectFile project)
    {
        IEnumerable<YamlMappingNode> maps = node switch
        {
            YamlMappingNode m => new[] { m },
            YamlSequenceNode seq => seq.Children.OfType<YamlMappingNode>(),
            _ => Array.Empty<YamlMappingNode>(),
        };
        foreach (var map in maps)
            foreach (var (k, v) in map.Children)
            {
                var name = Scalar(k);
                if (name == "language" || project.Speak.Any(s => s.Name == name))
                    continue;
                project.Speak.Add(new SpeakEntry { Name = name, Text = Scalar(v) });
            }
    }

    static void ReadCodes(YamlMappingNode root, ProjectFile project)
    {
        foreach (var (keyNode, value) in root.Children)
        {
            switch (Scalar(keyNode))
            {
                case "replay": project.Replay = int.Parse(Scalar(value), CultureInfo.InvariantCulture); break;
                case "stop": project.Stop = int.Parse(Scalar(value), CultureInfo.InvariantCulture); break;
                case "scriptcodes":
                    foreach (var (name, code) in ((YamlMappingNode)value).Children)
                    {
                        var script = project.Scripts.FirstOrDefault(s => s.Name == Scalar(name));
                        if (script != null)
                            script.Code = int.Parse(Scalar(code), CultureInfo.InvariantCulture);
                    }
                    break;
            }
        }
    }

    /// <summary>Schreibt die tttool-YAML-Datei und die zugehörige *.codes.yaml.</summary>
    public static void Export(ProjectFile project, string yamlPath)
    {
        CodeAssigner.Assign(project);

        var sb = new StringBuilder();
        sb.AppendLine($"product-id: {project.ProductId}");
        AppendScalar(sb, "comment", project.Comment);
        AppendScalar(sb, "welcome", project.Welcome);
        AppendScalar(sb, "media-path", project.MediaPath);
        AppendScalar(sb, "gme-lang", project.GmeLang);
        AppendScalar(sb, "init", project.Init);
        AppendScalar(sb, "language", project.Language);

        if (project.Scripts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("scripts:");
            foreach (var s in project.Scripts)
            {
                sb.AppendLine($"  {Quote(s.Name)}:");
                foreach (var line in s.Lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                    sb.AppendLine($"  - {Quote(line.Trim())}");
            }
        }

        if (project.Speak.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("speak:");
            foreach (var s in project.Speak)
                sb.AppendLine($"  {Quote(s.Name)}: {Quote(s.Text)}");
        }

        if (!string.IsNullOrWhiteSpace(project.ExtraYaml))
        {
            sb.AppendLine();
            sb.AppendLine(project.ExtraYaml.Trim());
        }

        File.WriteAllText(yamlPath, sb.ToString(), new UTF8Encoding(false));

        var codes = new StringBuilder();
        codes.AppendLine("# Zuordnung von Skriptnamen zu OID-Codes (erzeugt von TipToy.Designer).");
        codes.AppendLine("# Nicht löschen, sonst vergibt tttool beim nächsten Lauf eventuell andere Codes.");
        codes.AppendLine($"replay: {project.Replay}");
        codes.AppendLine($"stop: {project.Stop}");
        codes.AppendLine("scriptcodes:");
        foreach (var s in project.Scripts.Where(s => s.Code.HasValue && !int.TryParse(s.Name, out _)).OrderBy(s => s.Name, StringComparer.Ordinal))
            codes.AppendLine($"  {Quote(s.Name)}: {s.Code}");
        File.WriteAllText(CodesPathFor(yamlPath), codes.ToString(), new UTF8Encoding(false));
    }

    static void AppendScalar(StringBuilder sb, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{key}: {Quote(value.Trim())}");
    }

    static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
}
