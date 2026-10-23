# CLAUDE.md

## Git-Workflow

- **Direkt auf `master` arbeiten.** Keine Feature-Branches anlegen, keine Pull Requests erstellen.
- Änderungen auf `master` committen und mit `git push origin master` pushen.
- Gilt auch, wenn die Sitzung einen anderen Arbeits-Branch vorgibt: vorher
  `git fetch origin master && git checkout -B master origin/master`.

## Projekt

- Sprache in Code-Kommentaren, UI und Dokumentation: Deutsch.
- `TipToy.Designer/` – Avalonia-App (.NET 8): `dotnet build TipToy.Designer`
- Tests (u. a. C#-Übersetzer gegen natives C# und Original-YAML): `dotnet test TipToy.Designer.Tests`
- `TipToy/` – altes Konsolenprogramm (Windows Forms), lässt sich unter Linux nicht bauen.
- OID-Erzeugung (`TipToy.Designer/Oid/`) und GME-Erstellung (`TipToy.Designer/Gme/`) sind
  Portierungen von tttool (github.com/entropia/tip-toi-reveng, Referenz: Version 1.11).
  Sie müssen pixel- bzw. byteweise mit `tttool oid-code` und `tttool assemble --no-date`
  übereinstimmen; Änderungen dort gegen tttool prüfen.
