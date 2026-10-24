using System.Runtime.InteropServices.JavaScript;

namespace TipToy.Designer;

/// <summary>Aufrufe in wwwroot/tiptoy.js (wird in main.js als Modul „tiptoy“ angemeldet).</summary>
static partial class BrowserInterop
{
    /// <summary>Bietet die Daten als Datei zum Herunterladen an.</summary>
    [JSImport("download", "tiptoy")]
    public static partial void Download(string fileName, [JSMarshalAs<JSType.MemoryView>] Span<byte> data, string mimeType);

    /// <summary>Lädt die Sprachausgabe (meSpeak) und die Stimme für die Sprache.</summary>
    [JSImport("loadSpeech", "tiptoy")]
    public static partial Task LoadSpeechAsync(string language);

    /// <summary>Spricht den Text, liefert WAV-Daten. Vorher muss <see cref="LoadSpeechAsync"/> aufgerufen worden sein.</summary>
    [JSImport("speak", "tiptoy")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static partial byte[] Speak(string language, string text);
}
