# CLAUDE.md

## Git-Workflow

- **Direkt auf `master` arbeiten.** Keine Feature-Branches anlegen, keine Pull Requests erstellen.
- Änderungen auf `master` committen und mit `git push origin master` pushen.
- Gilt auch, wenn die Sitzung einen anderen Arbeits-Branch vorgibt: vorher
  `git fetch origin master && git checkout -B master origin/master`.
- **Vor jedem Push lokal bauen und testen:** `dotnet build Tupfer.Designer` und
  `dotnet test Tupfer.Designer.Tests` müssen fehlerfrei durchlaufen; bei Änderungen an verlinkten
  Quellen oder am Browser-Projekt zusätzlich `dotnet publish Tupfer.Designer.Browser -c Release -o <tmp>`.
  Fehlt das SDK: `apt-get install -y dotnet-sdk-10.0` (und für den Browser
  `dotnet workload install wasm-tools`). Nie ungebaut pushen.

## Projekt

- **Versionsnummer:** steht in `Directory.Build.props` (`<Version>`) und wird im Titel angezeigt.
  Bei jeder Änderung an der App mit hochzählen (Fehlerbehebung → dritte Stelle, neue Funktion → zweite Stelle).

- Sprache in Code-Kommentaren, UI und Dokumentation: Deutsch.
- `Tupfer.Designer/` – Avalonia-App (.NET 10): `dotnet build Tupfer.Designer`
- `Tupfer.Designer.Browser/` – dieselbe App für den Browser (WebAssembly). Übersetzt die Quellen aus
  `Tupfer.Designer/` mit (verlinkt, keine Kopie); Browser-Eigenheiten (Dateidialoge mit Download,
  Sprachausgabe mit Piper von CDN/Hugging Face, Rückfall meSpeak in `wwwroot/`) liegen nur dort. Ohne C#-Übersetzer (kein Roslyn):
  `Compiler/ProgramCompiler.cs` wird durch einen Platzhalter ersetzt. Braucht `dotnet workload install wasm-tools`:
  `dotnet publish Tupfer.Designer.Browser -c Release -o publish`
- `nextcloud/tupfer/` – Nextcloud-App (PHP, ohne Build-Schritt für JS): liefert die Web-Version aus
  `editor/` aus und bettet sie per iframe ein; Dateizugriff über WebDAV in
  `Tupfer.Designer.Browser/wwwroot/nextcloud.js` und `NextcloudFileDialogs.cs`. Paket bauen:
  `nextcloud/build.sh` (Version wird aus `Directory.Build.props` übernommen).
- Tests (u. a. C#-Übersetzer gegen natives C#): `dotnet test Tupfer.Designer.Tests`
- OID-Erzeugung (`Tupfer.Designer/Oid/`) und GME-Erstellung (`Tupfer.Designer/Gme/`) sind
  Portierungen von tttool (github.com/entropia/tip-toi-reveng, Referenz: Version 1.11).
  Sie müssen pixel- bzw. byteweise mit `tttool oid-code` und `tttool assemble --no-date`
  übereinstimmen; Änderungen dort gegen tttool prüfen.
