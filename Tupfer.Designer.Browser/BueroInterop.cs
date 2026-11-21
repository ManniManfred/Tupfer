using System.Runtime.InteropServices.JavaScript;

namespace Tupfer.Designer;

/// <summary>Aufrufe in wwwroot/buero.js (wird in main.js als Modul „buero“ angemeldet).</summary>
static partial class BueroInterop
{
    /// <summary>true, wenn der Designer aus Büro heraus geöffnet wurde (?buero=…).</summary>
    [JSImport("available", "buero")]
    public static partial bool Available();

    /// <summary>Beim Start zu öffnendes Dokument in Büro, 0 wenn keins.</summary>
    [JSImport("startDocument", "buero")]
    public static partial int StartDocument();

    /// <summary>Angaben zum Dokument als JSON, leer, wenn es das Dokument nicht gibt.</summary>
    [JSImport("info", "buero")]
    public static partial Task<string> InfoAsync(int id);

    /// <summary>Inhalt der aktuellen Version als Base64, oder null, wenn es das Dokument nicht gibt.</summary>
    [JSImport("read", "buero")]
    public static partial Task<string?> ReadAsync(int id);

    /// <summary>Übergibt die Daten für <see cref="SaveVersionAsync"/> und <see cref="CreateAsync"/>.</summary>
    [JSImport("setPending", "buero")]
    public static partial void SetPending([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    /// <summary>
    /// Speichert die übergebenen Daten als neue Version. <paramref name="basis"/>: Version, auf der die Änderungen
    /// beruhen (0: ohne Prüfung). Liefert die neue Versionsnummer, „geaendert“ oder „fehlt“.
    /// </summary>
    [JSImport("saveVersion", "buero")]
    public static partial Task<string> SaveVersionAsync(int id, string name, int basis);

    /// <summary>Legt ein neues Dokument an (Sammlungen durch Komma getrennt). Liefert JSON { id, name, version, duplicate }.</summary>
    [JSImport("create", "buero")]
    public static partial Task<string> CreateAsync(string name, string collections);

    /// <summary>Meldung an Büro (wird dort angezeigt, die Liste neu geladen).</summary>
    [JSImport("notify", "buero")]
    public static partial void Notify(string text, int id);

    /// <summary>Fragt nach einem Namen; leer bei Abbruch.</summary>
    [JSImport("askName", "buero")]
    public static partial string AskName(string text, string name);

    [JSImport("confirm", "buero")]
    public static partial bool Confirm(string text);

    /// <summary>Meldet, wie Büro erfährt, ob es ungespeicherte Änderungen gibt (Rückfrage beim Schließen).</summary>
    [JSImport("onModifiedQuery", "buero")]
    public static partial void OnModifiedQuery([JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> isModified);

    [JSImport("saveState", "buero")]
    public static partial void SaveState(string json);

    [JSImport("loadState", "buero")]
    public static partial string LoadState();
}
