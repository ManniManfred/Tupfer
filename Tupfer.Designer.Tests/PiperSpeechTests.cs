using System.Text.Json.Nodes;
using Tupfer.Designer.Gme;
using Xunit;

namespace Tupfer.Designer.Tests;

/// <summary>Lautschrift-IDs der natürlichen Stimmen (Piper) aus der Tabelle der jeweiligen Stimme.</summary>
public class PiperSpeechTests
{
    // Ausgabe von piper_phonemize für „Hallo! Ich bin die Oma.“ (de, eingebaute Tabelle mit 152 Einträgen)
    static readonly long[] DefaultIds =
        [1, 0, 20, 0, 120, 0, 14, 0, 24, 0, 27, 0, 122, 0, 4, 0, 2, 1, 0, 74, 0, 16, 0, 140, 0, 3, 0, 15, 0, 74, 0, 26, 0, 3, 0,
         17, 0, 21, 0, 122, 0, 3, 0, 120, 0, 27, 0, 122, 0, 25, 0, 51, 0, 122, 0, 10, 0, 2];
    static readonly string[] Phonemes =
        ["h", "ˈ", "a", "l", "o", "ː", "!", "ɪ", "c", "̧", " ", "b", "ɪ", "n", " ", "d", "i", "ː", " ", "ˈ", "o", "ː", "m", "ɑ", "ː", "."];

    /// <summary>Kleine Tabelle wie in einer .onnx.json, mit anderen IDs als die eingebaute und ohne „̧“ (wie de_DE-ramona-low).</summary>
    static JsonNode Config() => JsonNode.Parse("""
        {
          "audio": { "sample_rate": 16000 },
          "espeak": { "voice": "de" },
          "num_speakers": 1,
          "phoneme_id_map": {
            "_": [0], "^": [1], "$": [2], " ": [3], "!": [4], ".": [5],
            "a": [10], "b": [11], "c": [12], "d": [13], "h": [14], "i": [15], "l": [16], "m": [17], "n": [18], "o": [19],
            "ɑ": [20], "ɪ": [21], "ˈ": [22], "ː": [23]
          }
        }
        """)!;

    [Fact]
    public void Ids_aus_der_Tabelle_der_Stimme()
    {
        var ids = PiperSpeech.PhonemeIds(Config(), (DefaultIds, Phonemes));

        // Zwei Sätze, je mit ^ … $, nach jedem Phonem eine Pause; „̧“ kennt die Stimme nicht
        long[] expected =
        [
            1, 0, 14, 0, 22, 0, 10, 0, 16, 0, 19, 0, 23, 0, 4, 0, 2,
            1, 0, 21, 0, 12, 0, 3, 0, 11, 0, 21, 0, 18, 0, 3, 0, 13, 0, 15, 0, 23, 0, 3, 0, 22, 0, 19, 0, 23, 0, 17, 0, 20, 0, 23, 0, 5, 0, 2,
        ];
        Assert.Equal(expected, ids);
        Assert.All(ids, id => Assert.InRange(id, 0, 23));
    }

    [Fact]
    public void Ohne_Satzgrenzen_ein_Satz()
    {
        // Passen IDs und Phoneme nicht zusammen, wird alles ein Satz
        var ids = PiperSpeech.PhonemeIds(Config(), ([1, 0, 99, 0, 2], ["h", "a"]));
        Assert.Equal([1, 0, 14, 0, 10, 0, 2], ids);
    }

    [Fact]
    public void Ohne_Tabelle_die_IDs_von_piper_phonemize()
    {
        var config = JsonNode.Parse("""{ "espeak": { "voice": "de" } }""")!;
        Assert.Equal(DefaultIds, PiperSpeech.PhonemeIds(config, (DefaultIds, Phonemes)));
    }
}
