using System.Runtime.InteropServices.JavaScript;

namespace TipToy.Designer;

/// <summary>Aufrufe in wwwroot/tiptoy.js (wird in main.js als Modul „tiptoy“ angemeldet).</summary>
static partial class BrowserInterop
{
    /// <summary>Bietet die Daten als Datei zum Herunterladen an.</summary>
    [JSImport("download", "tiptoy")]
    public static partial void Download(string fileName, [JSMarshalAs<JSType.MemoryView>] Span<byte> data, string mimeType);

    /// <summary>Setzt den Titel des Browser-Tabs.</summary>
    [JSImport("setTitle", "tiptoy")]
    public static partial void SetTitle(string title);

    /// <summary>Lädt die Sprachausgabe (meSpeak) und die Stimme für die Sprache.</summary>
    [JSImport("loadSpeech", "tiptoy")]
    public static partial Task LoadSpeechAsync(string language);

    /// <summary>Startet die Wiedergabe von Audiodaten (MIME-Typ wie „audio/wav“); eine laufende wird beendet.</summary>
    [JSImport("playAudio", "tiptoy")]
    public static partial void PlayAudio([JSMarshalAs<JSType.MemoryView>] Span<byte> data, string mimeType);

    /// <summary>Ist fertig, wenn die zuletzt gestartete Wiedergabe zu Ende ist oder abgebrochen wurde.</summary>
    [JSImport("waitAudio", "tiptoy")]
    public static partial Task WaitAudioAsync();

    /// <summary>Beendet eine laufende Wiedergabe.</summary>
    [JSImport("stopAudio", "tiptoy")]
    public static partial void StopAudio();

    /// <summary>Startet eine Aufnahme vom Mikrofon (fragt beim ersten Mal nach der Erlaubnis).</summary>
    [JSImport("startRecording", "tiptoy")]
    public static partial Task StartRecordingAsync();

    /// <summary>Beendet die Aufnahme; liefert eine WAV-Datei (mono, 16 Bit, 22050 Hz) als Base64.</summary>
    [JSImport("stopRecording", "tiptoy")]
    public static partial Task<string> StopRecordingAsync(int sampleRate);

    /// <summary>Spricht den Text, liefert WAV-Daten. Vorher muss <see cref="LoadSpeechAsync"/> aufgerufen worden sein.</summary>
    [JSImport("speak", "tiptoy")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static partial byte[] Speak(string language, string text);
}
