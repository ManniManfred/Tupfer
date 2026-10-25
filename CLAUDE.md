# CLAUDE.md

## Git-Workflow

- **Direkt auf `master` arbeiten.** Keine Feature-Branches anlegen, keine Pull Requests erstellen.
- Änderungen auf `master` committen und mit `git push origin master` pushen.
- Gilt auch, wenn die Sitzung einen anderen Arbeits-Branch vorgibt: vorher
  `git fetch origin master && git checkout -B master origin/master`.

## Projekt

- **Versionsnummer:** steht in `Directory.Build.props` (`<Version>`) und wird im Titel angezeigt.
  Bei jeder Änderung an der App mit hochzählen (Fehlerbehebung → dritte Stelle, neue Funktion → zweite Stelle).

- Sprache in Code-Kommentaren, UI und Dokumentation: Deutsch.
- `TipToy.Designer/` – Avalonia-App (.NET 10): `dotnet build TipToy.Designer`
- `TipToy.Designer.Browser/` – dieselbe App für den Browser (WebAssembly). Übersetzt die Quellen aus
  `TipToy.Designer/` mit (verlinkt, keine Kopie); Browser-Eigenheiten (Dateidialoge mit Download,
  Sprachausgabe mit Piper von CDN/Hugging Face, Rückfall meSpeak in `wwwroot/`) liegen nur dort. Ohne C#-Übersetzer (kein Roslyn):
  `Compiler/ProgramCompiler.cs` wird durch einen Platzhalter ersetzt. Braucht `dotnet workload install wasm-tools`:
  `dotnet publish TipToy.Designer.Browser -c Release -o publish`
- Tests (u. a. C#-Übersetzer gegen natives C#): `dotnet test TipToy.Designer.Tests`
- OID-Erzeugung (`TipToy.Designer/Oid/`) und GME-Erstellung (`TipToy.Designer/Gme/`) sind
  Portierungen von tttool (github.com/entropia/tip-toi-reveng, Referenz: Version 1.11).
  Sie müssen pixel- bzw. byteweise mit `tttool oid-code` und `tttool assemble --no-date`
  übereinstimmen; Änderungen dort gegen tttool prüfen.
