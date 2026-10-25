using TipToy.Designer.Model;

namespace TipToy.Designer.Gme;

/// <summary>Eine Stimme der natürlichen Sprachausgabe; Id wie in piper-voices, z. B. „de_DE-kerstin-low“ (#Sprecher bei mehreren).</summary>
public record VoiceOption(string Id, string Name);

/// <summary>
/// Stimmen der natürlichen Sprachausgabe (Piper, nur im Browser). Ein Text wird mit seiner eigenen
/// Stimme gesprochen, sonst mit der des Projekts, sonst mit der Standardstimme der Sprache.
/// </summary>
public static class Voices
{
    public static IReadOnlyList<VoiceOption> All { get; } =
    [
        new("de_DE-thorsten-medium", "Thorsten – männlich"),
        new("de_DE-thorsten-high", "Thorsten, beste Qualität – männlich (größer, langsamer)"),
        new("de_DE-kerstin-low", "Kerstin – weiblich"),
        new("de_DE-ramona-low", "Ramona – weiblich"),
        new("de_DE-eva_k-x_low", "Eva – weiblich (einfache Qualität)"),
        new("de_DE-karlsson-low", "Karlsson – männlich"),
        new("de_DE-pavoque-low", "Pavoque – männlich (nur nicht-kommerziell)"),
        new("de_DE-thorsten_emotional-medium#amused", "Thorsten fröhlich"),
        new("de_DE-thorsten_emotional-medium#surprised", "Thorsten überrascht"),
        new("de_DE-thorsten_emotional-medium#whisper", "Thorsten flüsternd"),
        new("de_DE-thorsten_emotional-medium#sleepy", "Thorsten müde"),
        new("de_DE-thorsten_emotional-medium#angry", "Thorsten wütend"),
        new("de_DE-thorsten_emotional-medium#disgusted", "Thorsten angewidert"),
        new("de_DE-thorsten_emotional-medium#drunk", "Thorsten beschwipst"),
        new("en_US-lessac-medium", "Lessac – weiblich, US"),
        new("en_US-amy-medium", "Amy – weiblich, US"),
        new("en_US-ryan-medium", "Ryan – männlich, US"),
        new("en_US-joe-medium", "Joe – männlich, US"),
        new("en_GB-alba-medium", "Alba – weiblich, GB"),
        new("en_GB-cori-medium", "Cori – weiblich, GB"),
        new("en_GB-alan-medium", "Alan – männlich, GB"),
        new("fr_FR-siwis-medium", "Siwis – weiblich"),
        new("fr_FR-tom-medium", "Tom – männlich"),
        new("fr_FR-gilles-low", "Gilles – männlich"),
        new("es_ES-davefx-medium", "Davefx – männlich"),
        new("es_ES-carlfm-x_low", "Carlfm – männlich (einfache Qualität)"),
        new("it_IT-riccardo-x_low", "Riccardo – männlich (einfache Qualität)"),
        new("nl_BE-nathalie-medium", "Nathalie – weiblich, Belgien"),
        new("nl_BE-rdh-medium", "Rdh – männlich, Belgien"),
    ];

    /// <summary>Stimmen für eine Sprache („de“, „en“ …).</summary>
    public static IReadOnlyList<VoiceOption> For(string? language) =>
        All.Where(v => Matches(v.Id, language)).ToList();

    /// <summary>Standardstimme der Sprache (die erste in der Liste) oder null.</summary>
    public static VoiceOption? Default(string? language) => All.FirstOrDefault(v => Matches(v.Id, language));

    static bool Matches(string? voice, string? language) =>
        !string.IsNullOrEmpty(voice) && !string.IsNullOrEmpty(language) && voice.StartsWith(language + "_", StringComparison.Ordinal);

    /// <summary>
    /// Schlüssel für die Sprachausgabe eines Textes: „de“ oder „de:de_DE-kerstin-low“. Eine Stimme, die
    /// nicht zur Sprache passt (z. B. nach dem Wechsel der Sprache), wird ignoriert.
    /// </summary>
    public static string SpeechKey(string? language, string? projectVoice, string? voice)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language;
        var chosen = Matches(voice, lang) ? voice : Matches(projectVoice, lang) ? projectVoice : null;
        return chosen == null ? lang : lang + ":" + chosen;
    }

    public static string SpeechKey(ProjectFile project, SpeakEntry text) => SpeechKey(project.Language, project.Voice, text.Voice);

    /// <summary>Zerlegt einen <see cref="SpeechKey(string?, string?, string?)"/> in Sprache und Stimme (null = Standard).</summary>
    public static (string Language, string? Voice) Parse(string key)
    {
        int i = key.IndexOf(':');
        return i < 0 ? (key, null) : (key[..i], key[(i + 1)..]);
    }
}
