# meSpeak

Sprachausgabe für die Browser-Version des TipToy Designers: [meSpeak](https://www.masswerk.at/mespeak/)
2.0.2 (npm-Paket `mespeak`), eine Emscripten-Portierung von eSpeak. Lizenz: GNU GPL 3 (siehe `COPYING`),
Quelltext im npm-Paket [`mespeak`](https://www.npmjs.com/package/mespeak).

- `ESpeak.js`, `mespeak_config.json` – unverändert aus `src/`
- `mespeak.js` – unverändert `src/index.js`
- `voices/*.json` – Stimmen aus `voices/` (`en.json` stammt aus `voices/en/`)

Die Dateien sind CommonJS-Module; `../tiptoy.js` lädt sie erst beim ersten Bauen einer GME-Datei.
