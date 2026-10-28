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

/** Beim Start zu öffnende Datei (?datei=/Ordner/projekt.tupfer.json), sonst leer. */
export function startFile() {
    return new URLSearchParams(location.search).get('datei') ?? '';
}

/** Beim Start neu anzulegendes Projekt (?neu=/Ordner/name.tupfer.json, aus dem Menü „Neu“ der Dateien-App). */
export function startNew() {
    return new URLSearchParams(location.search).get('neu') ?? '';
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

/** Inhalt der Datei als Base64, oder null, wenn es sie nicht gibt. */
export async function read(path) {
    const response = await dav('GET', path);
    if (response.status === 404)
        return null;
    if (!response.ok)
        fail(response, path);
    const blob = await response.blob();
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result.slice(reader.result.indexOf(',') + 1));
        reader.onerror = () => reject(reader.error);
        reader.readAsDataURL(blob);
    });
}

/** Namen der Dateien (ohne Unterordner) im Ordner als JSON-Array; leer, wenn es ihn nicht gibt. */
export async function list(path) {
    const response = await dav('PROPFIND', path, {
        headers: { Depth: '1', 'Content-Type': 'application/xml' },
        body: '<?xml version="1.0"?><d:propfind xmlns:d="DAV:"><d:prop><d:resourcetype/></d:prop></d:propfind>',
    });
    if (response.status === 404)
        return '[]';
    if (!response.ok)
        fail(response, path);
    const xml = new DOMParser().parseFromString(await response.text(), 'application/xml');
    const names = [];
    for (const entry of xml.getElementsByTagNameNS('DAV:', 'response')) {
        if (entry.getElementsByTagNameNS('DAV:', 'collection').length > 0)
            continue;
        const href = entry.getElementsByTagNameNS('DAV:', 'href')[0]?.textContent ?? '';
        names.push(decodeURIComponent(href.replace(/\/$/, '').split('/').pop()));
    }
    return JSON.stringify(names);
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

/** Schreibt die übergebenen Daten in die Datei; fehlende Ordner werden angelegt. */
export async function write(path) {
    const body = pending;
    pending = null;
    let response = await dav('PUT', path, { body });
    if (response.status === 409 || response.status === 404) {
        await makeFolders(path);
        response = await dav('PUT', path, { body });
    }
    if (!response.ok)
        fail(response, path);
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
    const dialogs = host().OC.dialogs;
    const endings = extensions ? extensions.toLowerCase().split(',') : [];
    return new Promise(resolve => {
        let done = false;
        const finish = path => {
            if (!done) {
                done = true;
                resolve(path || '');
            }
        };
        const options = folder
            ? { allowDirectoryChooser: true }
            : { filter: f => f.mimetype === 'httpd/unix-directory' || endings.some(e => f.name.toLowerCase().endsWith(e)) };
        // Liefert ein Promise, das beim Schließen ohne Auswahl abgelehnt wird
        const result = dialogs.filepicker(title, path => finish(path), false,
            folder ? ['httpd/unix-directory'] : undefined, true, dialogs.FILEPICKER_TYPE_CHOOSE, start || '/', options);
        Promise.resolve(result).then(() => finish(''), () => finish(''));
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
