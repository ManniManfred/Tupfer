// Anbindung an Büro (Dokumentenverwaltung, github.com/ManniManfred/Buero): Büro liefert den Designer unter
// /tupfer/ aus und öffnet ihn in einem iframe mit ?buero=<Dokument>#token=<Anmeldung>. Gelesen und gespeichert
// wird über die HTTP-API von Büro (gleicher Server, Adressen relativ zu /tupfer/). Der Desktop-Client von Büro
// lädt die Oberfläche von einem anderen Ursprung: Meldungen an Büro gehen deshalb nur per postMessage.

const params = new URLSearchParams(location.search);
const TOKEN_KEY = 'tupfer-buero-token';

// Anmeldung aus dem Fragment übernehmen (wird nicht an den Server geschickt) und aus der Adresse entfernen;
// in sessionStorage bleibt sie für das Neuladen der Seite erhalten
let token = '';
{
    const hash = new URLSearchParams(location.hash.slice(1));
    try {
        if (hash.has('token'))
            sessionStorage.setItem(TOKEN_KEY, hash.get('token'));
        token = sessionStorage.getItem(TOKEN_KEY) ?? '';
    } catch {
        token = hash.get('token') ?? '';
    }
    if (location.hash)
        history.replaceState(null, '', location.pathname + location.search);
}

/** true, wenn der Designer aus Büro heraus geöffnet wurde. */
export function available() {
    return params.has('buero') && window.parent !== window;
}

/** Beim Start zu öffnendes Dokument in Büro, 0 wenn keins. */
export function startDocument() {
    const id = Number(params.get('buero'));
    return Number.isInteger(id) && id > 0 ? id : 0;
}

function api(path, init = {}) {
    const headers = { ...init.headers };
    if (token)
        headers.Authorization = 'Bearer ' + token;
    return fetch(new URL('../api/' + path, location.href), { ...init, headers });
}

async function fail(response, what) {
    let text = `${response.status} ${response.statusText}`.trim();
    try {
        text = (await response.json()).error || text;
    } catch { }
    if (response.status === 401)
        text = 'nicht (mehr) angemeldet – bitte Büro neu laden und erneut anmelden';
    throw new Error(`Büro: ${what}: ${text}`);
}

/** Angaben zum Dokument (JSON wie in Büro: id, title, original_filename, version, collection_ids …), leer, wenn es fehlt. */
export async function info(id) {
    const response = await api(`documents/${id}`);
    if (response.status === 404)
        return '';
    if (!response.ok)
        await fail(response, `Dokument ${id}`);
    return JSON.stringify(await response.json());
}

/** Inhalt der aktuellen Version als Base64, oder null, wenn es das Dokument nicht gibt. */
export async function read(id) {
    const response = await api(`documents/${id}/file`);
    if (response.status === 404)
        return null;
    if (!response.ok)
        await fail(response, `Dokument ${id}`);
    const blob = await response.blob();
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result.slice(reader.result.indexOf(',') + 1));
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(blob);
    });
}

let pending = null;

/** Übergibt die Daten für saveVersion und create (JSImport erlaubt keine Span-Parameter mit Task). */
export function setPending(data) {
    pending = data.slice();
}

function form(name, extra = {}) {
    const data = new FormData();
    for (const [key, value] of Object.entries(extra))
        data.append(key, value);
    data.append('file', new Blob([pending], { type: 'application/zip' }), name);
    pending = null;
    return data;
}

/**
 * Speichert die übergebenen Daten als neue Version des Dokuments. basis: Version, auf der die Änderungen beruhen
 * (Büro lehnt ab, wenn es inzwischen eine neuere gibt), <= 0: ohne Prüfung.
 * Liefert die neue Versionsnummer als Text, 'geaendert' (neuere Version vorhanden) oder 'fehlt' (Dokument gelöscht).
 */
export async function saveVersion(id, name, basis) {
    const query = basis > 0 ? `?basis=${basis}` : '';
    const response = await api(`documents/${id}/versions${query}`, { method: 'POST', body: form(name) });
    if (response.status === 409)
        return 'geaendert';
    if (response.status === 404)
        return 'fehlt';
    if (!response.ok)
        await fail(response, name);
    return String((await response.json()).version);
}

/**
 * Legt ein neues Dokument mit den übergebenen Daten an, in den Sammlungen (IDs durch Komma getrennt).
 * Liefert JSON { id, name, version, duplicate } (duplicate: genau diesen Inhalt gab es schon, id ist dieses Dokument).
 */
export async function create(name, collections) {
    const extra = collections ? { collection_ids: collections } : {};
    const response = await api('documents', { method: 'POST', body: form(name, extra) });
    if (!response.ok)
        await fail(response, name);
    const [result] = await response.json();
    return JSON.stringify({
        id: result.document.id, name: result.document.original_filename,
        version: result.document.version, duplicate: result.duplicate,
    });
}

/** Meldung an Büro (Büro zeigt sie an und lädt die Liste neu). */
export function notify(text, id) {
    window.parent.postMessage({ quelle: 'tupfer', art: 'gespeichert', text, dokument: id }, '*');
}

/**
 * Büro fragt vor dem Schließen nach ungespeicherten Änderungen: { quelle: 'buero', art: 'geaendert?' },
 * Antwort { quelle: 'tupfer', art: 'status', geaendert }.
 */
export function onModifiedQuery(isModified) {
    window.addEventListener('message', e => {
        if (e.source !== window.parent || e.data?.quelle !== 'buero' || e.data.art !== 'geaendert?')
            return;
        let modified = true;
        try {
            modified = isModified();
        } catch { }
        e.source.postMessage({ quelle: 'tupfer', art: 'status', geaendert: modified }, '*');
    });
}

/** Fragt nach einem Namen (vorbelegt); leer bei Abbruch. */
export function askName(text, name) {
    return window.prompt(text, name)?.trim() ?? '';
}

/** Rückfrage mit OK und Abbrechen. */
export function confirm(text) {
    return window.confirm(text);
}

/** Zuordnung der Arbeitsverzeichnisse zu Dokumenten in Büro (JSON) für das nächste Laden der Seite. */
export function saveState(json) {
    try {
        localStorage.setItem('tupfer-buero', json);
    } catch { }
}

export function loadState() {
    try {
        return localStorage.getItem('tupfer-buero') ?? '';
    } catch {
        return '';
    }
}
