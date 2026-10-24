// Browser-Funktionen für den TipToy Designer (Aufruf aus BrowserInterop.cs)

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

/** Setzt den Titel des Browser-Tabs. */
export function setTitle(title) {
    document.title = title;
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
