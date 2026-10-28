using System.Runtime.InteropServices.JavaScript;

namespace Tupfer.Designer;

/// <summary>Aufrufe in wwwroot/nextcloud.js (wird in main.js als Modul „nextcloud“ angemeldet).</summary>
static partial class NextcloudInterop
{
    /// <summary>true, wenn der Designer in der Nextcloud-App läuft.</summary>
    [JSImport("available", "nextcloud")]
    public static partial bool Available();

    /// <summary>Beim Start zu öffnende Datei in Nextcloud (?datei=…), sonst leer.</summary>
    [JSImport("startFile", "nextcloud")]
    public static partial string StartFile();

    /// <summary>Beim Start neu anzulegendes Projekt in Nextcloud (?neu=…), sonst leer.</summary>
    [JSImport("startNew", "nextcloud")]
    public static partial string StartNew();

    /// <summary>Dateiauswahl von Nextcloud (Endungen durch Komma getrennt, oder ein Ordner); leer bei Abbruch.</summary>
    [JSImport("pick", "nextcloud")]
    public static partial Task<string> PickAsync(string title, string extensions, bool folder, string start);

    /// <summary>Fragt nach einem Namen; leer bei Abbruch.</summary>
    [JSImport("askName", "nextcloud")]
    public static partial string AskName(string text, string name);

    [JSImport("confirm", "nextcloud")]
    public static partial bool Confirm(string text);

    /// <summary>Kurze Meldung von Nextcloud.</summary>
    [JSImport("notify", "nextcloud")]
    public static partial void Notify(string text);

    /// <summary>Inhalt der Datei als Base64, oder null, wenn es sie nicht gibt.</summary>
    [JSImport("read", "nextcloud")]
    public static partial Task<string?> ReadAsync(string path);

    /// <summary>Namen der Dateien im Ordner als JSON-Array.</summary>
    [JSImport("list", "nextcloud")]
    public static partial Task<string> ListAsync(string path);

    [JSImport("exists", "nextcloud")]
    public static partial Task<bool> ExistsAsync(string path);

    /// <summary>Übergibt die Daten für <see cref="WriteAsync"/> (JSImport erlaubt keine Span-Parameter mit Task).</summary>
    [JSImport("setPending", "nextcloud")]
    public static partial void SetPending([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>Schreibt die übergebenen Daten in die Datei; fehlende Ordner werden angelegt.</summary>
    [JSImport("write", "nextcloud")]
    public static partial Task WriteAsync(string path);

    [JSImport("saveState", "nextcloud")]
    public static partial void SaveState(string json);

    [JSImport("loadState", "nextcloud")]
    public static partial string LoadState();
}
