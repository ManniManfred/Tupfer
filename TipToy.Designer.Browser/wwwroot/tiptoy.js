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
