# TipToy – Tic-Tac-Toe für den tiptoi-Stift

Ein selbstgebautes Tic-Tac-Toe-Spiel für den Ravensburger **tiptoi**-Stift, erstellt mit
[tttool](https://github.com/entropia/tip-toi-reveng), sowie der **TipToy Designer** – eine
Avalonia-Anwendung, mit der man eigene tiptoi-Seiten gestalten und drucken kann.

## Inhalt

| Datei / Ordner | Beschreibung |
| --- | --- |
| `tic-tac-toi.yaml` | Spiellogik und Sprachausgaben (tttool-Quelldatei, Produkt-ID `900`) |
| `tic-tac-toi.codes.yaml` | Feste Zuordnung von Skriptnamen zu OID-Codes (von tttool erzeugt) |
| `tic-tac-toi.gme` | Fertig gebaute Spieldatei für den tiptoi-Stift |
| `tic-tac-toi.pdf` | Druckvorlage mit Spielfeld und OID-Codes |
| `TipToy.Designer/` | Avalonia-Anwendung zum Gestalten von tiptoi-Seiten (siehe unten) |
| `examples/tic-tac-toi/` | Das Tic-Tac-Toe-Spiel als Designer-Projekt mit Spielfeld-Grafik |
| `examples/tic-tac-toi-csharp/` | Das Tic-Tac-Toe-Spiel als C#-Programm (siehe „Programmieren in C#“) |
| `TipToy.Designer.Tests/` | Tests für den C#-Übersetzer |
| `examples/druckertest/` | Drucker-Testseite (PDF + GME), um die passenden Druckeinstellungen zu finden |
| `TipToy/` | Experimentelles C#-Konsolenprogramm (erzeugt keine gültigen Codes, siehe unten) |
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

## TipToy Designer

Plattformübergreifende Desktop-Anwendung (Windows, Linux, macOS) zum Erstellen eigener tiptoi-Seiten.

```bash
dotnet run --project TipToy.Designer
# oder direkt mit einem Projekt bzw. einer tttool-YAML-Datei:
dotnet run --project TipToy.Designer -- examples/tic-tac-toi/tic-tac-toi.tiptoy.json
```

Voraussetzung: .NET 10 SDK. **tttool wird nicht benötigt** – der Designer erzeugt OID-Codes und
GME-Dateien selbst. Für die Sprachausgabe nutzt er unter Windows die eingebaute Stimme, unter
Linux/macOS `pico2wave`, `espeak` oder `say`. Ist `oggenc` (vorbis-tools) installiert, werden
die Texte als OGG eingebettet, sonst als WAV (größere Datei, der Stift spielt beides).

### Funktionen

- **Seiten:** Fotos und Bilder einfügen (drehen, zuschneiden, skalieren) und Texte setzen
  (Schriftart, Größe, Farbe, Ausrichtung). Mit der Maus OID-Flächen aufziehen,
  verschieben und in der Größe ändern. Jede Fläche wird einem Skript oder einem Sondercode
  (Produkt/Anmelden, Wiederholen, Stopp) zugeordnet.
- **Skripte:** tttool-Skripte bearbeiten, mit Kurzreferenz der Befehle.
- **Programm (C#):** Spiellogik in C# schreiben statt in der Skriptsprache (siehe unten).
- **Sprachausgabe:** Texte, die beim Bauen per Text-to-Speech vertont werden; alternativ eigene
  Audiodateien (`.ogg`, `.mp3`, `.wav`) im Medienverzeichnis.
- **Projekt:** Produkt-ID, Begrüßung, Initialisierung, Druckeinstellungen usw.
- **Export:**
  - Seiten als druckfertige PDF (Bilder und Texte + OID-Muster in den Flächen)
  - OID-Tabelle aller Codes als PDF, einzelne Flächen als PNG
  - tttool-YAML inkl. `*.codes.yaml`, Import vorhandener YAML-Dateien
  - GME-Datei bauen (Taste F5) – eingebaut, optional über das externe tttool

OID-Muster und GME-Dateien sind direkte Portierungen von tttool: Die Muster sind pixelgenau
gleich, die GME-Dateien bei gleichen Audiodateien byteweise identisch mit `tttool assemble`.
Auch die Codes für Skripte werden nach demselben Verfahren vergeben, sodass YAML-Export und
tttool zusammenpassen. Nur Spiele (`games`) und Timer kann der eingebaute Builder nicht – dafür
unter *Projekt* „GME mit externem tttool bauen“ aktivieren.

### Typischer Ablauf

1. Projekt anlegen, Produkt-ID wählen (z. B. 900–999).
2. Unter *Seiten* Bilder und Texte einfügen und Flächen aufziehen; eine Fläche als *Produkt (Anmelden)* markieren.
3. Unter *Skripte* und *Sprachausgabe* festlegen, was beim Antippen passiert.
4. *Erstellen → GME-Datei bauen* und die `.gme` auf den Stift kopieren.
5. *Erstellen → Seiten als PDF* und mit 1200 dpi in Originalgröße drucken.

Projekte werden als `*.tiptoy.json` gespeichert; Pfade zu Bildern relativ zur Projektdatei.
Hintergrundbilder älterer Projekte werden beim Öffnen zu einem seitenfüllenden Bild.

## Programmieren in C#

Statt die Skriptsprache von tttool zu verwenden, kann die Spiellogik im Tab *Programm (C#)* als
C#-Klasse geschrieben werden. Der Designer übersetzt sie in Skripte für den Stift (F6, oder
automatisch beim Bauen und Exportieren):

```csharp
class Zaehlspiel : TipToi
{
    int punkte;

    [Oid] void Apfel()
    {
        if (punkte < 5) { punkte++; Play("richtig"); }
        else            { punkte = 0; Play("fertig"); }
    }
}
```

wird zu

```yaml
Apfel:
- $punkte < 5? $punkte += 1 P(richtig)
- $punkte := 0 P(fertig)
```

| C# | Stift |
| --- | --- |
| Felder (`int`, `bool` …), Initialwerte | Register (0 bis 65535), `init` |
| `[Oid] void Name()` | Skript zum Antippen; in *Seiten* einer Fläche zuordnen |
| andere Methoden (auch mit Parametern und `return`) | werden beim Aufruf eingesetzt |
| `if`/`else`/`switch`, `&&`, `\|\|`, `!` | Zeilen mit Bedingungen (die erste passende wird ausgeführt) |
| `+ - * / % & \| ^ << >>`, `++`, `+=` … | Rechenbefehle, Zwischenergebnisse in Hilfsregistern |
| `Play("a")`, `PlayAll("a", "b")`, `PlayRandom("a", "b")` | `P(a)`, `PA(a,b)`, `P(a,b)` |
| `x = Random(6);`, `Timer(n)`, `Cancel()` | Pseudo-Zufallszahl 0–5, `T(…)`, `C` |
| `for (int i = 0; i < 5; i++)`, `foreach (var x in new[] { 2, 4 })` | wird ausgerollt (feste Anzahl, höchstens 100 Durchläufe) |

Schleifen mit fester Anzahl werden beim Übersetzen ausgerollt: Jeder Durchlauf wird eine Kopie
des Rumpfs, in der die Schleifenvariable eine Konstante ist. Bedingungen, die nur von ihr abhängen
(`if (i == 2)`), sind damit schon beim Übersetzen entschieden und kosten nichts. Ein `if` auf
Registern in jedem Durchlauf erzeugt dagegen pro Durchlauf einen Sprung.

Nicht möglich, weil der Stift es nicht kann: `while`/`do`-Schleifen, `break`/`continue` in
Schleifen, Rekursion, Rückgabewerte, Texte, Arrays und Objekte – der Übersetzer meldet das mit
Zeilennummer. Code nach einem `if`,
Bedingungen auf berechneten Werten und Zeilen mit mehr als 8 Befehlen werden auf Hilfsskripte
verteilt, die per Sprung (`J`) folgen; der Stift springt erst, wenn die laufende Ausgabe zu Ende ist.

`examples/tic-tac-toi-csharp/` enthält Tic-Tac-Toe als C#-Programm. Die Datei `TipToi.cs` dort
liefert IntelliSense in der IDE. Die Tests in `TipToy.Designer.Tests` vergleichen das übersetzte
Programm mit dem echten C#-Programm und mit dem Original-Spiel (`dotnet test TipToy.Designer.Tests`).

## Drucker testen

Nicht jeder Drucker druckt die feinen OID-Punkte so, dass der Stift sie erkennt. Mit der
Testseite in `examples/druckertest/` findest du die passende Einstellung:

1. `druckertest.gme` in das Hauptverzeichnis des Stifts kopieren.
2. `druckertest.pdf` in Originalgröße drucken (keine Skalierung / „Tatsächliche Größe“, höchste
   Qualität, keine Tonersparfunktion).
3. In jeder Zeile zuerst „Anmelden“, dann „Test“ antippen. Der Stift sagt die erkannte Variante an:

   | Variante | Auflösung | Punktgröße |
   | --- | --- | --- |
   | A | 1200 dpi | 1 Pixel |
   | B | 1200 dpi | 2 Pixel (Standard) |
   | C | 1200 dpi | 3 Pixel |
   | D | 1200 dpi | 4 Pixel |
   | E | 600 dpi | 1 Pixel |
   | F | 600 dpi | 2 Pixel |

4. Die Variante, die zuverlässig erkannt wird, im Designer unter *Projekt → Druckauflösung /
   Punktgröße* eintragen.

Die Testseite lässt sich im Designer jederzeit neu erzeugen: *Erstellen → Drucker-Testseite …*
(baut die GME-Datei gleich mit).

## C#-Programm `TipToy`

Das Programm liest OIDs aus `oids.txt` (eine pro Zeile), rendert für jede OID ein PNG nach
`oid_temp/` und fasst alle Bilder in einem A4-PDF (`OIDs.pdf`, 4×5 Raster) zusammen. Fehlt
`oids.txt`, wird eine Beispieldatei angelegt.

```bash
cd TipToy
dotnet run
```

Voraussetzungen: .NET 10 SDK und Windows (das Programm nutzt `System.Drawing` und Windows Forms).
Abhängigkeit: [PDFsharp](https://www.pdfsharp.net/) 6.2.2.

> **Achtung:** Die Codierung im C#-Programm ist eine vereinfachte, experimentelle Nachbildung und
> erzeugt **keine** echten tiptoi-kompatiblen OID-Muster. Für funktionierende Codes zum Ausdrucken
> `tttool oid-code` bzw. `tttool oid-table` verwenden.

## Quellen

- [tttool / tip-toi-reveng](https://github.com/entropia/tip-toi-reveng) – Werkzeug zum Erstellen eigener tiptoi-Inhalte
- [tttool-Dokumentation](https://tttool.readthedocs.io/)
