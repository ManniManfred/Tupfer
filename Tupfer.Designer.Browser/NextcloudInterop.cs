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

    [JSImport("exists", "nextcloud")]
    public static partial Task<bool> ExistsAsync(string path);

    /// <summary>Übergibt die Daten für <see cref="WriteAsync"/> (JSImport erlaubt keine Span-Parameter mit Task).</summary>
    [JSImport("setPending", "nextcloud")]
    public static partial void SetPending([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>
    /// Schreibt die übergebenen Daten in die Datei; fehlende Ordner werden angelegt. <paramref name="mode"/>:
    /// „neu“ (nur anlegen), „ersetzen“ (nur, wenn seit dem letzten Lesen/Schreiben unverändert) oder „immer“.
    /// Liefert „ok“, „vorhanden“, „geaendert“ oder „unbekannt“ (bei „ersetzen“, wenn der letzte Stand nicht bekannt ist).
    /// </summary>
    [JSImport("write", "nextcloud")]
    public static partial Task<string> WriteAsync(string path, string mode);

    /// <summary>Letzter bekannter Stand (ETag) der Datei, leer, wenn unbekannt.</summary>
    [JSImport("etag", "nextcloud")]
    public static partial string ETag(string path);

    /// <summary>Setzt den bekannten Stand (nach dem Neuladen der Seite).</summary>
    [JSImport("setEtag", "nextcloud")]
    public static partial void SetETag(string path, string etag);

    [JSImport("saveState", "nextcloud")]
    public static partial void SaveState(string json);

    [JSImport("loadState", "nextcloud")]
    public static partial string LoadState();
}
