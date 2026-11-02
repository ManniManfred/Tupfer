// Anbindung an Nextcloud: In der Nextcloud-App „tupfer“ läuft der Designer in einem iframe derselben Adresse
// (nextcloud/tupfer). Dateien des angemeldeten Benutzers werden über WebDAV gelesen und geschrieben,
// Ordner und Dateien mit der Dateiauswahl von Nextcloud gewählt.

/** Das Nextcloud-Fenster um den iframe, oder null außerhalb von Nextcloud. */
function host() {
    try {
        const parent = window.parent;
        return parent !== window && parent.OC && parent.document.getElementById('tupfer-app') ? parent : null;
    } catch {
        return null; // anderer Ursprung
    }
}

/** true, wenn der Designer in der Nextcloud-App läuft. */
export function available() {
    return host() !== null;
}

/** Beim Start zu öffnende Datei (?datei=/Ordner/projekt.tupfer), sonst leer. */
export function startFile() {
    return new URLSearchParams(location.search).get('datei') ?? '';
}

/**
 * Beim Start neu anzulegendes Projekt (?neu=/Ordner/name.tupfer, aus dem Menü „Neu“ der Dateien-App).
 * Die Dateien-App merkt sich den Pfad in sessionStorage; fehlt er dort (z. B. ein fremder Link), wird gefragt.
 */
export function startNew() {
    const path = new URLSearchParams(location.search).get('neu') ?? '';
    if (!path)
        return '';
    let expected = null;
    try {
        expected = sessionStorage.getItem('tupfer-neu');
        sessionStorage.removeItem('tupfer-neu');
    } catch { }
    if (expected === path || window.confirm(`Neues Tupfer-Projekt „${path}“ in Nextcloud anlegen?`))
        return path;
    return '';
}

function user() {
    return host().OC.getCurrentUser().uid;
}

function davUrl(path) {
    return host().OC.linkToRemote('dav') + '/files/' + encodeURIComponent(user())
        + path.split('/').map(encodeURIComponent).join('/');
}

function dav(method, path, init = {}) {
    return fetch(davUrl(path), {
        method,
        credentials: 'same-origin',
        ...init,
        headers: { requesttoken: host().OC.requestToken, ...init.headers },
    });
}

function fail(response, path) {
    throw new Error(`Nextcloud: ${path}: ${response.status} ${response.statusText}`.trim());
}

// Letzter bekannter Stand (ETag) je Datei: Beim Speichern wird nur überschrieben, was seitdem
// niemand anders geändert hat.
const etags = new Map();

function remember(path, response) {
    const tag = response.headers.get('ETag');
    if (tag)
        etags.set(path, tag);
    else
        etags.delete(path);
}

/** Letzter bekannter Stand (ETag) der Datei, leer, wenn unbekannt. */
export function etag(path) {
    return etags.get(path) ?? '';
}

/** Setzt den bekannten Stand (nach dem Neuladen der Seite). */
export function setEtag(path, tag) {
    if (tag)
        etags.set(path, tag);
}

/** Inhalt der Datei als Base64, oder null, wenn es sie nicht gibt. */
export async function read(path) {
    const response = await dav('GET', path);
    if (response.status === 404)
        return null;
    if (!response.ok)
        fail(response, path);
    remember(path, response);
    const blob = await response.blob();
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result.slice(reader.result.indexOf(',') + 1));
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(blob);
    });
}

/** true, wenn es die Datei oder den Ordner gibt. */
export async function exists(path) {
    const response = await dav('PROPFIND', path, { headers: { Depth: '0' } });
    return response.ok;
}

let pending = null;

/** Übergibt die Daten für write (JSImport erlaubt keine Span-Parameter mit Task). */
export function setPending(data) {
    pending = data.slice();
}

/**
 * Schreibt die übergebenen Daten in die Datei; fehlende Ordner werden angelegt.
 * mode: 'neu' – nur, wenn es die Datei noch nicht gibt; 'ersetzen' – nur, wenn sie seit dem letzten Lesen
 * oder Schreiben unverändert ist; 'immer' – auf jeden Fall.
 * Liefert 'ok', 'vorhanden' (bei 'neu'), 'geaendert' oder 'unbekannt' (bei 'ersetzen': Stand der Datei nicht bekannt).
 */
export async function write(path, mode) {
    const body = pending;
    pending = null;
    const headers = {};
    if (mode === 'neu')
        headers['If-None-Match'] = '*';
    else if (mode === 'ersetzen' && etags.has(path))
        headers['If-Match'] = etags.get(path);
    else if (mode === 'ersetzen' && await exists(path)) {
        // Stand unbekannt (z. B. Browserdaten gelöscht): nicht ungefragt überschreiben
        return 'unbekannt';
    }
    let response = await dav('PUT', path, { body, headers });
    if (response.status === 409 || response.status === 404) {
        await makeFolders(path);
        response = await dav('PUT', path, { body, headers });
    }
    if (response.status === 412)
        return mode === 'neu' ? 'vorhanden' : 'geaendert';
    if (!response.ok)
        fail(response, path);
    remember(path, response);
    if (!etags.has(path))
        remember(path, await dav('HEAD', path));
    return 'ok';
}

async function makeFolders(path) {
    let folder = '';
    for (const part of path.split('/').filter(p => p).slice(0, -1)) {
        folder += '/' + part;
        const response = await dav('MKCOL', folder);
        if (!response.ok && response.status !== 405) // 405: gibt es schon
            fail(response, folder);
    }
}

/**
 * Dateiauswahl von Nextcloud: eine Datei mit einer der Endungen (durch Komma getrennt) oder einen Ordner.
 * Liefert den Pfad, leer bei Abbruch.
 */
export function pick(title, extensions, folder, start) {
    const nc = host();
    const dialogs = nc.OC.dialogs;
    const endings = extensions ? extensions.toLowerCase().split(',') : [];
    return new Promise(resolve => {
        let done = false;
        let timer = 0;
        const finish = path => {
            if (!done) {
                done = true;
                clearInterval(timer);
                resolve(path || '');
            }
        };
        const options = folder
            ? { allowDirectoryChooser: true }
            : { filter: f => f.mimetype === 'httpd/unix-directory' || endings.some(e => f.name.toLowerCase().endsWith(e)) };
        const result = dialogs.filepicker(title, path => finish(path), false,
            folder ? ['httpd/unix-directory'] : undefined, true, dialogs.FILEPICKER_TYPE_CHOOSE, start || '/', options);
        // OC.dialogs.filepicker liefert (Nextcloud 28 bis 33) nichts zurück, auch kein Promise: Würde man auf
        // Promise.resolve(undefined) warten, käme sofort „abgebrochen“, noch bevor der Benutzer etwas wählt.
        // Nur ein echtes Promise abwarten (die Auswahl kommt über den Rückruf davor).
        if (result && typeof result.then === 'function') {
            const later = () => setTimeout(() => finish(''), 0);
            result.then(later, later);
        }
        // Abbrechen erkennen: Der Dialog erscheint und verschwindet ohne Auswahl. Die Auswahl selbst kommt über
        // den Rückruf, bevor sich der Dialog schließt; zur Sicherheit erst etwas später „abgebrochen“ melden.
        let seen = false;
        let gone = 0;
        timer = setInterval(() => {
            if (nc.document.querySelector('.file-picker__main, .file-picker')) {
                seen = true;
                gone = 0;
            } else if (seen && ++gone >= 3) {
                finish('');
            }
        }, 250);
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

/** Kurze Meldung von Nextcloud (unten rechts). */
export function notify(text) {
    host()?.OCP?.Toast?.success(text);
}

/** Zuordnung der Arbeitsverzeichnisse zu Nextcloud-Ordnern (JSON) für das nächste Laden der Seite. */
export function saveState(json) {
    try {
        localStorage.setItem('tupfer-nextcloud-' + user(), json);
    } catch { }
}

export function loadState() {
    try {
        return localStorage.getItem('tupfer-nextcloud-' + user()) ?? '';
    } catch {
        return '';
    }
}
