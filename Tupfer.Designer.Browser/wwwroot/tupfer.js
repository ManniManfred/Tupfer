// Browser-Funktionen für den Tupfer Designer (Aufruf aus BrowserInterop.cs)

/** Bietet Daten als Datei zum Herunterladen an. */
export function download(fileName, data, mimeType) {
    const blob = new Blob([data.slice()], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}

/**
 * Druckt ein PDF über den Druckdialog des Browsers (unsichtbarer iframe mit der PDF-Anzeige).
 * Ohne eingebaute PDF-Anzeige (z. B. auf dem Handy) wird es in einem neuen Tab geöffnet.
 * Liefert true, wenn der Druckdialog geöffnet wird.
 */
export function printPdf(data) {
    const url = URL.createObjectURL(new Blob([data.slice()], { type: 'application/pdf' }));
    if (navigator.pdfViewerEnabled === false) {
        if (!window.open(url, '_blank'))
            download('seiten.pdf', data, 'application/pdf');
        setTimeout(() => URL.revokeObjectURL(url), 600000);
        return false;
    }
    document.getElementById('tupfer-print')?.remove();
    const frame = document.createElement('iframe');
    frame.id = 'tupfer-print';
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
    // Die PDF-Anzeige braucht nach dem Laden noch einen Moment, bevor sie drucken kann
    frame.onload = () => setTimeout(() => {
        try {
            frame.contentWindow.focus();
            frame.contentWindow.print();
        } catch (e) {
            console.warn('Drucken im iframe nicht möglich:', e);
            window.open(url, '_blank');
        }
    }, 500);
    frame.src = url;
    document.body.appendChild(frame);
    return true;
}

// ---------- Speichern an einem gewählten Ort (z. B. auf dem Stift) ----------

let picked = null;
let pending = null;

/** true, wenn der Browser einen Speichern-Dialog hat (File System Access API). */
export function canPickSaveFile() {
    return typeof window.showSaveFilePicker === 'function';
}

/** Öffnet den Speichern-Dialog; false bei Abbruch. */
export async function pickSaveFile(suggestedName) {
    try {
        picked = await window.showSaveFilePicker({
            suggestedName,
            types: [{ description: 'GME-Datei', accept: { 'application/octet-stream': ['.gme'] } }],
        });
        return true;
    } catch (e) {
        if (e.name === 'AbortError')
            return false;
        throw e;
    }
}

/** Merkt sich die Daten für writePickedFile. */
export function setPendingFile(data) {
    pending = data.slice();
}

/** Schreibt die gemerkten Daten in die gewählte Datei und liefert ihren Namen. */
export async function writePickedFile() {
    const file = picked, data = pending;
    picked = pending = null;
    if (!file || !data)
        throw new Error('Keine Datei gewählt.');
    const writable = await file.createWritable();
    await writable.write(data);
    await writable.close();
    return file.name;
}

// ---------- Automatisch gesicherter Bearbeitungsstand (IndexedDB) ----------

let database = null;
let pendingMedia = null;

function autosaveStore(mode) {
    database ??= new Promise((resolve, reject) => {
        const request = indexedDB.open('tupfer', 1);
        request.onupgradeneeded = () => request.result.createObjectStore('autosave');
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
    return database.then(db => db.transaction('autosave', mode));
}

async function autosavePut(key, value) {
    const transaction = await autosaveStore('readwrite');
    transaction.objectStore('autosave').put(value, key);
    return new Promise((resolve, reject) => {
        transaction.oncomplete = () => resolve();
        transaction.onerror = () => reject(transaction.error);
    });
}

async function autosaveGet(key) {
    const request = (await autosaveStore('readonly')).objectStore('autosave').get(key);
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

/** Sichert den Bearbeitungsstand (JSON). */
export function autosaveSave(json) {
    return autosavePut('project', json);
}

/** Merkt sich die Bilder und Audiodateien (ZIP) für autosaveSaveMedia. */
export function autosaveSetMedia(zip) {
    pendingMedia = zip.slice();
}

/** Sichert die gemerkten Bilder und Audiodateien. */
export async function autosaveSaveMedia() {
    const data = pendingMedia;
    pendingMedia = null;
    await autosavePut('media', data ?? new Uint8Array(0));
}

/** Liefert den gesicherten Bearbeitungsstand oder null. */
export async function autosaveLoad() {
    return (await autosaveGet('project')) ?? null;
}

/** Liefert die gesicherten Bilder und Audiodateien (ZIP) Base64-kodiert, leer wenn keine. */
export async function autosaveLoadMedia() {
    const bytes = (await autosaveGet('media')) ?? new Uint8Array(0);
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000)
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(binary);
}

/** Setzt den Titel des Browser-Tabs. */
export function setTitle(title) {
    document.title = title;
    // In der Nextcloud-App auch den Titel des umgebenden Fensters
    try {
        if (window.parent !== window && window.parent.document.getElementById('tupfer-app'))
            window.parent.document.title = title;
    } catch { }
}

/** Öffnet die Adresse in einem neuen Tab. */
export function openUrl(url) {
    window.open(url, '_blank', 'noopener');
}

// ---------- Sprachausgabe mit meSpeak (eSpeak als JavaScript) ----------

let sources = null;
const voices = {};
let engine = null;
let calls = 0;

async function fetchText(url) {
    const response = await fetch(url);
    if (!response.ok)
        throw new Error(`${url}: ${response.status} ${response.statusText}`);
    return response.text();
}

/** Führt ein CommonJS-Modul aus und liefert seine Exporte. */
function runModule(code, require) {
    const module = { exports: {} };
    new Function('module', 'exports', 'require', code)(module, module.exports, require);
    return module.exports;
}

/** Lädt meSpeak (beim ersten Mal) und die Stimme für die Sprache. */
export async function loadSpeech(language) {
    if (!sources) {
        const [espeak, mespeak, config] = await Promise.all([
            fetchText('mespeak/ESpeak.js'),
            fetchText('mespeak/mespeak.js'),
            fetchText('mespeak/mespeak_config.json'),
        ]);
        sources = { espeak, mespeak, config: JSON.parse(config) };
    }
    if (!voices[language]) {
        try {
            voices[language] = JSON.parse(await fetchText(`mespeak/voices/${language}.json`));
        } catch (e) {
            throw new Error(`Keine Stimme für die Sprache „${language}“ (verfügbar: de, en, es, fr, it, nl).`);
        }
        engine = null;
    }
}

/** Spricht den Text und liefert die WAV-Datei als Zahlen-Array. */
export function speak(language, text) {
    const voice = voices[language];
    if (!sources || !voice)
        throw new Error('Die Sprachausgabe wurde noch nicht geladen.');
    // eSpeak (Emscripten) stürzt nach vielen Aufrufen ab, daher regelmäßig neu starten
    if (!engine || calls >= 40) {
        const ESpeak = runModule(sources.espeak, () => { throw new Error('require'); });
        engine = runModule(sources.mespeak, () => ESpeak);
        engine.loadConfig(sources.config);
        for (const v of Object.values(voices))
            engine.loadVoice(v);
        calls = 0;
    }
    calls++;
    const wav = engine.speak(text, { rawdata: 'array', voice: voice.voice_id, speed: 140 });
    if (!wav || wav.length <= 44)
        throw new Error(`Sprachausgabe für „${text}“ fehlgeschlagen.`);
    return wav;
}

// ---------- Natürliche Sprachausgabe mit Piper (neuronale Stimmen, läuft im Browser) ----------
// Laufzeit, Lautschrift und Stimmen werden beim ersten Gebrauch geladen (zusammen etwa 90 MB)
// und vom Browser zwischengespeichert, die Stimmen dauerhaft im Cache-Speicher.

const PIPER_ORT = 'https://cdnjs.cloudflare.com/ajax/libs/onnxruntime-web/1.18.0/';
const PIPER_PHONEMIZE = 'https://cdn.jsdelivr.net/npm/@diffusionstudio/piper-wasm@1.0.0/build/piper_phonemize';
const PIPER_VOICES = 'https://huggingface.co/rhasspy/piper-voices/resolve/main/';

/** Geladene Stimmen (Modell → Promise mit Konfiguration und Sitzung), zuletzt benutzte am Ende. */
const piperVoices = new Map();
const PIPER_MAX_VOICES = 3;
let piperPhonemizer = null;

function loadScript(url) {
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = url;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`${new URL(url).host} ist nicht erreichbar`));
        document.head.appendChild(script);
    });
}

/** Lädt eine Datei, beim ersten Mal aus dem Netz, danach aus dem Cache-Speicher des Browsers. */
async function fetchCached(url) {
    const cache = 'caches' in window ? await caches.open('tupfer-piper') : null;
    let response = cache && await cache.match(url);
    if (!response) {
        try {
            response = await fetch(url);
        } catch {
            throw new Error(`${new URL(url).host} ist nicht erreichbar`);
        }
        if (!response.ok)
            throw new Error(`${url}: ${response.status} ${response.statusText}`);
        if (cache)
            await cache.put(url, response.clone()).catch(() => { });
    }
    return response;
}

/** Wandelt Text in Lautschrift-IDs (espeak-ng); eine Instanz wird wiederverwendet, solange sie läuft. */
let phonemizeOut = null, phonemizeErr = null, phonemizeScript = null, ortReady = null;
async function phonemize(espeakVoice, text) {
    // Nur einmal laden, auch wenn mehrere Texte gleichzeitig gesprochen werden
    await (phonemizeScript ??= loadScript(PIPER_PHONEMIZE + '.js').catch(e => { phonemizeScript = null; throw e; }));
    for (let attempt = 0; ; attempt++) {
        piperPhonemizer ??= window.createPiperPhonemize({
            print: line => { phonemizeOut = line; },
            printErr: line => { phonemizeErr = line; },
            locateFile: file => file.endsWith('.wasm') ? PIPER_PHONEMIZE + '.wasm' : file.endsWith('.data') ? PIPER_PHONEMIZE + '.data' : file,
        });
        let instance;
        try {
            instance = await piperPhonemizer;
        } catch (e) {
            piperPhonemizer = null;
            throw e;
        }
        phonemizeOut = phonemizeErr = null;
        try {
            instance.callMain(['-l', espeakVoice, '--input', JSON.stringify([{ text }]), '--espeak_data', '/espeak-ng-data']);
        } catch (e) {
            phonemizeErr ??= String(e);
        }
        if (phonemizeOut)
            return JSON.parse(phonemizeOut).phoneme_ids;
        piperPhonemizer = null; // neu starten und noch einmal versuchen
        if (attempt === 1)
            throw new Error('Lautschrift fehlgeschlagen: ' + (phonemizeErr ?? 'keine Ausgabe'));
    }
}

/** Pfad in piper-voices ohne Endung, z. B. de_DE-kerstin-low → de/de_DE/kerstin/low/de_DE-kerstin-low. */
function voicePath(model) {
    const [locale, name, quality] = model.split('-');
    if (!locale || !name || !quality)
        throw new Error(`Unbekannte Stimme „${model}“.`);
    return `${locale.split('_')[0]}/${locale}/${name}/${quality}/${model}`;
}

async function loadPiperVoice(model) {
    const path = voicePath(model);
    await (ortReady ??= loadScript(PIPER_ORT + 'ort.min.js').then(() => {
        ort.env.wasm.wasmPaths = PIPER_ORT;
        // Mehrere Threads gehen nur auf Seiten mit Cross-Origin-Isolation
        ort.env.wasm.numThreads = self.crossOriginIsolated ? Math.min(4, navigator.hardwareConcurrency || 1) : 1;
    }, e => { ortReady = null; throw e; }));
    const [config, data] = await Promise.all([
        fetchCached(PIPER_VOICES + path + '.onnx.json').then(r => r.json()),
        fetchCached(PIPER_VOICES + path + '.onnx').then(r => r.arrayBuffer()),
    ]);
    const session = await ort.InferenceSession.create(data);
    return { config, session };
}

/** Lädt eine Stimme (beim ersten Mal aus dem Netz); behält nur die zuletzt benutzten im Speicher. */
async function useVoice(model) {
    let voice = piperVoices.get(model);
    piperVoices.delete(model);
    voice ??= loadPiperVoice(model);
    piperVoices.set(model, voice);
    while (piperVoices.size > PIPER_MAX_VOICES) {
        const [oldest, old] = piperVoices.entries().next().value;
        piperVoices.delete(oldest);
        old.then(v => v.session.release()).catch(() => { });
    }
    try {
        return await voice;
    } catch (e) {
        piperVoices.delete(model);
        throw e;
    }
}

/**
 * Spricht den Text mit einer natürlichen Stimme, liefert WAV (mono, 16 Bit) Base64-kodiert.
 * Stimme wie in piper-voices, z. B. „de_DE-kerstin-low“; bei mehreren Sprechern „…#sprecher“.
 */
export async function speakNatural(voiceId, text) {
    const [model, speaker] = voiceId.split('#');
    const { config, session } = await useVoice(model);
    const ids = await phonemize(config.espeak.voice, text.trim());
    const inference = config.inference ?? {};
    const feeds = {
        input: new ort.Tensor('int64', BigInt64Array.from(ids, BigInt), [1, ids.length]),
        input_lengths: new ort.Tensor('int64', BigInt64Array.from([BigInt(ids.length)]), [1]),
        scales: new ort.Tensor('float32', Float32Array.from([inference.noise_scale ?? 0.667, inference.length_scale ?? 1, inference.noise_w ?? 0.8]), [3]),
    };
    if (config.num_speakers > 1)
        feeds.sid = new ort.Tensor('int64', BigInt64Array.from([BigInt(config.speaker_id_map?.[speaker] ?? 0)]), [1]);
    const samples = (await session.run(feeds)).output.data;
    return btoa(toBinary(wavFromSamples(samples, config.audio.sample_rate)));
}

function wavFromSamples(samples, sampleRate) {
    const wav = new DataView(new ArrayBuffer(44 + samples.length * 2));
    const text = (pos, s) => [...s].forEach((ch, i) => wav.setUint8(pos + i, ch.charCodeAt(0)));
    text(0, 'RIFF'); wav.setUint32(4, 36 + samples.length * 2, true); text(8, 'WAVEfmt ');
    wav.setUint32(16, 16, true); wav.setUint16(20, 1, true); wav.setUint16(22, 1, true);
    wav.setUint32(24, sampleRate, true); wav.setUint32(28, sampleRate * 2, true);
    wav.setUint16(32, 2, true); wav.setUint16(34, 16, true);
    text(36, 'data'); wav.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++)
        wav.setInt16(44 + i * 2, Math.max(-1, Math.min(1, samples[i])) * 0x7fff, true);
    return new Uint8Array(wav.buffer);
}

function toBinary(bytes) {
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000)
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return binary;
}

// ---------- Wiedergabe und Aufnahme ----------

let player = null;
let playing = Promise.resolve();

/** Startet die Wiedergabe von Audiodaten; eine laufende wird beendet. */
export function playAudio(data, mimeType) {
    stopAudio();
    const audio = player = new Audio(URL.createObjectURL(new Blob([data.slice()], { type: mimeType })));
    // Fertig am Ende, bei Fehlern und wenn die Wiedergabe abgebrochen wird (pause)
    playing = new Promise(resolve => {
        const done = () => {
            URL.revokeObjectURL(audio.src);
            resolve();
        };
        audio.addEventListener('ended', done);
        audio.addEventListener('pause', done);
        audio.addEventListener('error', done);
        audio.play().catch(e => {
            console.warn('Wiedergabe nicht möglich:', e);
            done();
        });
    });
}

/** Ist erfüllt, wenn die zuletzt gestartete Wiedergabe zu Ende ist oder abgebrochen wurde. */
export function waitAudio() {
    return playing;
}

/** Beendet eine laufende Wiedergabe. */
export function stopAudio() {
    if (player) {
        player.pause();
        player = null;
    }
}

let recording = null;

/** Startet eine Aufnahme vom Mikrofon (rohe Samples über die Web-Audio-API). */
export async function startRecording() {
    if (!navigator.mediaDevices?.getUserMedia)
        throw new Error('Der Browser erlaubt hier keine Aufnahme (nur über https oder localhost).');
    const stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
    });
    const context = new AudioContext();
    const source = context.createMediaStreamSource(stream);
    const processor = context.createScriptProcessor(4096, 1, 1);
    const chunks = [];
    processor.onaudioprocess = e => chunks.push(new Float32Array(e.inputBuffer.getChannelData(0)));
    source.connect(processor);
    processor.connect(context.destination);
    recording = { stream, context, source, processor, chunks };
}

/** Beendet die Aufnahme und liefert sie als WAV (mono, 16 Bit) in der gewünschten Abtastrate, Base64-kodiert. */
export async function stopRecording(sampleRate) {
    if (!recording)
        throw new Error('Es läuft keine Aufnahme.');
    const { stream, context, source, processor, chunks } = recording;
    recording = null;
    processor.disconnect();
    source.disconnect();
    stream.getTracks().forEach(t => t.stop());
    const inputRate = context.sampleRate;
    await context.close();

    const input = new Float32Array(chunks.reduce((n, c) => n + c.length, 0));
    let offset = 0;
    for (const c of chunks) {
        input.set(c, offset);
        offset += c.length;
    }
    // Auf die Zielrate umrechnen (lineare Interpolation) und in 16 Bit wandeln
    const count = Math.floor(input.length * sampleRate / inputRate);
    const wav = new DataView(new ArrayBuffer(44 + count * 2));
    const text = (pos, s) => [...s].forEach((ch, i) => wav.setUint8(pos + i, ch.charCodeAt(0)));
    text(0, 'RIFF'); wav.setUint32(4, 36 + count * 2, true); text(8, 'WAVEfmt ');
    wav.setUint32(16, 16, true); wav.setUint16(20, 1, true); wav.setUint16(22, 1, true);
    wav.setUint32(24, sampleRate, true); wav.setUint32(28, sampleRate * 2, true);
    wav.setUint16(32, 2, true); wav.setUint16(34, 16, true);
    text(36, 'data'); wav.setUint32(40, count * 2, true);
    for (let i = 0; i < count; i++) {
        const pos = i * inputRate / sampleRate;
        const j = Math.floor(pos), f = pos - j;
        const v = (input[j] ?? 0) * (1 - f) + (input[j + 1] ?? input[j] ?? 0) * f;
        wav.setInt16(44 + i * 2, Math.max(-1, Math.min(1, v)) * 0x7fff, true);
    }

    const bytes = new Uint8Array(wav.buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000)
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(binary);
}
