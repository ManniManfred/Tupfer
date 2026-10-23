# TipToy – Tic-Tac-Toe für den tiptoi-Stift

Ein selbstgebautes Tic-Tac-Toe-Spiel für den Ravensburger **tiptoi**-Stift, erstellt mit
[tttool](https://github.com/entropia/tip-toi-reveng). Zusätzlich enthält das Repository ein
experimentelles C#-Programm, das OID-Codes als PNG-Bilder und PDF erzeugt.

## Inhalt

| Datei / Ordner | Beschreibung |
| --- | --- |
| `tic-tac-toi.yaml` | Spiellogik und Sprachausgaben (tttool-Quelldatei, Produkt-ID `900`) |
| `tic-tac-toi.codes.yaml` | Feste Zuordnung von Skriptnamen zu OID-Codes (von tttool erzeugt) |
| `tic-tac-toi.gme` | Fertig gebaute Spieldatei für den tiptoi-Stift |
| `tic-tac-toi.pdf` | Druckvorlage mit Spielfeld und OID-Codes |
| `TipToy/` | C#-Konsolenprogramm (.NET 8) zum Erzeugen von OID-Bildern und PDF |
| `TipToy.sln` | Visual-Studio-Solution |

## Das Spiel

Zwei Spieler – **Kreuz** und **Kreis** – tippen abwechselnd auf die neun Felder des Spielfelds.
Der Stift sagt an, wer an der Reihe ist, ob ein Feld bereits belegt ist, und erkennt Sieg
(Reihe, Spalte, Diagonale) sowie Unentschieden. Danach wird das Spielfeld automatisch
zurückgesetzt und der Verlierer beginnt die nächste Runde.

Feldbezeichnungen und zugehörige OID-Codes:

|        | links (L) | mitte (M) | rechts (R) |
| --- | --- | --- | --- |
| **oben (O)**  | `feldOL` 3946 | `feldOM` 3952 | `feldOR` 3953 |
| **mitte (M)** | `feldML` 3949 | `feldMM` 3950 | `feldMR` 3951 |
| **unten (U)** | `feldUL` 3954 | `feldUM` 3955 | `feldUR` 3956 |

Weitere Codes: Produkt-ID `900`, Wiederholen (`replay`) `3944`, Stopp `3945`.

## Spiel bauen und auf den Stift kopieren

Voraussetzung: [tttool](https://github.com/entropia/tip-toi-reveng) ist installiert.

```bash
# Spieldatei (.gme) erzeugen – Sprachausgaben werden per Text-to-Speech generiert
tttool assemble tic-tac-toi.yaml

# Druckvorlage mit allen OID-Codes erzeugen
tttool oid-table tic-tac-toi.yaml
```

Anschließend die `tic-tac-toi.gme` in das Hauptverzeichnis des per USB angeschlossenen
tiptoi-Stifts kopieren. Zum Starten zuerst den Produkt-Code (`900`) antippen, danach die Felder.

**Druckhinweis:** OID-Codes sollten mit 1200 dpi und ohne Skalierung („Tatsächliche Größe“)
gedruckt werden, sonst erkennt der Stift sie nicht zuverlässig. Die tttool-Dokumentation enthält
Tipps zu geeigneten Druckern und Einstellungen.

## C#-Programm `TipToy`

Das Programm liest OIDs aus `oids.txt` (eine pro Zeile), rendert für jede OID ein PNG nach
`oid_temp/` und fasst alle Bilder in einem A4-PDF (`OIDs.pdf`, 4×5 Raster) zusammen. Fehlt
`oids.txt`, wird eine Beispieldatei angelegt.

```bash
cd TipToy
dotnet run
```

Voraussetzungen: .NET 8 SDK und Windows (das Programm nutzt `System.Drawing` und Windows Forms).
Abhängigkeit: [PDFsharp](https://www.pdfsharp.net/) 6.2.2.

> **Achtung:** Die Codierung im C#-Programm ist eine vereinfachte, experimentelle Nachbildung und
> erzeugt **keine** echten tiptoi-kompatiblen OID-Muster. Für funktionierende Codes zum Ausdrucken
> `tttool oid-code` bzw. `tttool oid-table` verwenden.

## Quellen

- [tttool / tip-toi-reveng](https://github.com/entropia/tip-toi-reveng) – Werkzeug zum Erstellen eigener tiptoi-Inhalte
- [tttool-Dokumentation](https://tttool.readthedocs.io/)
