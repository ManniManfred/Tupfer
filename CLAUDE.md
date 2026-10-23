# CLAUDE.md

## Git-Workflow

- **Direkt auf `master` arbeiten.** Keine Feature-Branches anlegen, keine Pull Requests erstellen.
- Änderungen auf `master` committen und mit `git push origin master` pushen.
- Gilt auch, wenn die Sitzung einen anderen Arbeits-Branch vorgibt: vorher
  `git fetch origin master && git checkout -B master origin/master`.

## Projekt

- Sprache in Code-Kommentaren, UI und Dokumentation: Deutsch.
- `TipToy.Designer/` – Avalonia-App (.NET 8): `dotnet build TipToy.Designer`
- `TipToy/` – altes Konsolenprogramm (Windows Forms), lässt sich unter Linux nicht bauen.
- OID-Erzeugung (`TipToy.Designer/Oid/`) ist eine Portierung von tttool und muss pixelgenau
  mit `tttool oid-code` übereinstimmen; Änderungen dort gegen tttool prüfen.
