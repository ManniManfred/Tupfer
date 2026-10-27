using System.Runtime.InteropServices.JavaScript;

namespace Tupfer.Designer;

/// <summary>Aufrufe in wwwroot/tupfer.js (wird in main.js als Modul „tupfer“ angemeldet).</summary>
static partial class BrowserInterop
{
    /// <summary>Bietet die Daten als Datei zum Herunterladen an.</summary>
    [JSImport("download", "tupfer")]
    public static partial void Download(string fileName, [JSMarshalAs<JSType.MemoryView>] Span<byte> data, string mimeType);

    /// <summary>
    /// Druckt ein PDF über den Druckdialog des Browsers; ohne eingebaute PDF-Anzeige (z. B. auf dem Handy)
    /// wird es stattdessen in einem neuen Tab geöffnet. true = Druckdialog.
    /// </summary>
    [JSImport("printPdf", "tupfer")]
    public static partial bool PrintPdf([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>true, wenn der Browser einen Speichern-Dialog hat (Chrome, Edge am PC; nicht Firefox, Safari, Handy).</summary>
    [JSImport("canPickSaveFile", "tupfer")]
    public static partial bool CanPickSaveFile();

    /// <summary>Öffnet den Speichern-Dialog des Browsers; false bei Abbruch.</summary>
    [JSImport("pickSaveFile", "tupfer")]
    public static partial Task<bool> PickSaveFileAsync(string suggestedName);

    /// <summary>Übergibt die Daten für <see cref="WritePickedFileAsync"/> (JSImport erlaubt keine Span-Parameter mit Task).</summary>
    [JSImport("setPendingFile", "tupfer")]
    public static partial void SetPendingFile([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>Schreibt die übergebenen Daten in die gewählte Datei, liefert ihren Namen.</summary>
    [JSImport("writePickedFile", "tupfer")]
    public static partial Task<string> WritePickedFileAsync();

    /// <summary>Sichert den Bearbeitungsstand (JSON) in IndexedDB.</summary>
    [JSImport("autosaveSave", "tupfer")]
    public static partial Task AutoSaveSaveAsync(string json);

    /// <summary>Übergibt die Bilder und Audiodateien (ZIP) für <see cref="AutoSaveSaveMediaAsync"/>.</summary>
    [JSImport("autosaveSetMedia", "tupfer")]
    public static partial void AutoSaveSetMedia([JSMarshalAs<JSType.MemoryView>] Span<byte> zip);

    /// <summary>Sichert die übergebenen Bilder und Audiodateien in IndexedDB.</summary>
    [JSImport("autosaveSaveMedia", "tupfer")]
    public static partial Task AutoSaveSaveMediaAsync();

    /// <summary>Gesicherter Bearbeitungsstand (JSON) oder null.</summary>
    [JSImport("autosaveLoad", "tupfer")]
    public static partial Task<string?> AutoSaveLoadAsync();

    /// <summary>Gesicherte Bilder und Audiodateien (ZIP) als Base64, leer wenn keine.</summary>
    [JSImport("autosaveLoadMedia", "tupfer")]
    public static partial Task<string> AutoSaveLoadMediaAsync();

    /// <summary>Setzt den Titel des Browser-Tabs.</summary>
    [JSImport("setTitle", "tupfer")]
    public static partial void SetTitle(string title);

    /// <summary>Öffnet die Adresse in einem neuen Tab.</summary>
    [JSImport("openUrl", "tupfer")]
    public static partial void OpenUrl(string url);

    /// <summary>Lädt die Sprachausgabe (meSpeak) und die Stimme für die Sprache.</summary>
    [JSImport("loadSpeech", "tupfer")]
    public static partial Task LoadSpeechAsync(string language);

    /// <summary>Startet die Wiedergabe von Audiodaten (MIME-Typ wie „audio/wav“); eine laufende wird beendet.</summary>
    [JSImport("playAudio", "tupfer")]
    public static partial void PlayAudio([JSMarshalAs<JSType.MemoryView>] Span<byte> data, string mimeType);

    /// <summary>Ist fertig, wenn die zuletzt gestartete Wiedergabe zu Ende ist oder abgebrochen wurde.</summary>
    [JSImport("waitAudio", "tupfer")]
    public static partial Task WaitAudioAsync();

    /// <summary>Beendet eine laufende Wiedergabe.</summary>
    [JSImport("stopAudio", "tupfer")]
    public static partial void StopAudio();

    /// <summary>Startet eine Aufnahme vom Mikrofon (fragt beim ersten Mal nach der Erlaubnis).</summary>
    [JSImport("startRecording", "tupfer")]
    public static partial Task StartRecordingAsync();

    /// <summary>Beendet die Aufnahme; liefert eine WAV-Datei (mono, 16 Bit, 22050 Hz) als Base64.</summary>
    [JSImport("stopRecording", "tupfer")]
    public static partial Task<string> StopRecordingAsync(int sampleRate);

    /// <summary>
    /// Spricht den Text mit einer natürlichen Stimme (Piper, Id wie in Gme.Voices; lädt sie beim ersten
    /// Mal, 20–110 MB), liefert WAV-Daten Base64-kodiert. Wirft, wenn das Laden scheitert.
    /// </summary>
    [JSImport("speakNatural", "tupfer")]
    public static partial Task<string> SpeakNaturalAsync(string voice, string text);

    /// <summary>Spricht den Text, liefert WAV-Daten. Vorher muss <see cref="LoadSpeechAsync"/> aufgerufen worden sein.</summary>
    [JSImport("speak", "tupfer")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static partial byte[] Speak(string language, string text);
}
