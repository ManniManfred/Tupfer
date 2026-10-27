# Tupfer Designer

Mit dem **Tupfer Designer** gestaltet man eigene Seiten für den Ravensburger **tiptoi**-Stift:
Bilder und Texte setzen, OID-Flächen aufziehen, Skripte und Sprachausgaben festlegen, die
GME-Datei für den Stift bauen und die Seiten mit OID-Codes drucken. Er läuft als Desktop-Anwendung
(Windows, Linux, macOS) und im Browser. OID-Codes und GME-Dateien entsprechen
[tttool](https://github.com/entropia/tip-toi-reveng), das selbst aber nicht benötigt wird.

Ideal, um **tiptoi selber zu machen**: eigene tiptoi-Bücher, Spielpläne, Fotoseiten und Lernspiele
für Kinder – kostenlos und quelloffen (MIT-Lizenz).

- **Im Browser ausprobieren:** https://mannimanfred.github.io/Tupfer/
- **Herunterladen (Windows, Linux, macOS):** https://mannimanfred.github.io/Tupfer/download.html

Bis Version 1.10 hieß das Projekt „TipToy Designer“.

## Inhalt

| Ordner | Beschreibung |
| --- | --- |
| `Tupfer.Designer/` | Die Anwendung (Avalonia, .NET 10) |
| `Tupfer.Designer.Browser/` | Dieselbe Anwendung als Web-App (WebAssembly, siehe „Tupfer Designer im Browser“) |
| `Tupfer.Designer.Tests/` | Tests (OID/GME, Stift-Simulation, C#-Übersetzer, Bearbeiten) |
| `examples/tic-tac-toi/` | Beispiel: Tic-Tac-Toe als Designer-Projekt mit Spielfeld-Grafik (auch in der Web-App) |
| `examples/meine-leute/` | Vorlage „Meine Leute“: Familienseite für die Kleinsten mit eigenen Fotos und Stimmen (auch in der Web-App) |
| `examples/wo-ist/` | Suchspiel „Wo ist …?“ mit acht Tieren für Vorschulkinder (auch in der Web-App) |
| `examples/tic-tac-toi-csharp/` | Dasselbe Spiel als C#-Programm (siehe „Programmieren in C#“) |
| `examples/druckertest/` | Drucker-Testseite (PDF + GME), um die passenden Druckeinstellungen zu finden |
| `deploy/`, `captain-definition` | Web-App im Container mit nginx (z. B. für CapRover) |
| `deploy/setup/` | Setups für Windows (Inno Setup), Linux (.deb, AppImage) und macOS (.dmg) |

## Beispiel: Tic-Tac-Toe

Zwei Spieler – **Kreuz** und **Kreis** – tippen abwechselnd auf die neun Felder. Der Stift sagt
an, wer an der Reihe ist und ob ein Feld schon belegt ist, und erkennt Sieg (Reihe, Spalte,
Diagonale) sowie Unentschieden. Danach wartet das Spiel, bis *Start* angetippt wird.

Ausprobieren: `examples/tic-tac-toi/tic-tac-toi.tupfer.json` öffnen (in der Web-App unter
*Datei → Beispiele*), mit *Testen* (F8) ohne Stift durchspielen, dann *Auf den Stift speichern*
und *Seiten drucken*. Zum Starten auf dem Stift zuerst *Start* (Produkt-Code `900`) antippen.

**Druckhinweis:** OID-Codes mit 1200 dpi und ohne Skalierung („Tatsächliche Größe“) drucken,
sonst erkennt der Stift sie nicht zuverlässig (siehe „Drucker testen“).

## Vorlage: Meine Leute

Eine Seite für die Kleinsten (ab etwa 2 Jahren): sechs große Fotos – Mama, Papa, Oma, Opa,
Geschwister und das Kind selbst. Tippt man ein Bild an, sagt die Person „Hallo!“. Keine Spielregeln,
große Flächen, damit jeder Tipper etwas auslöst. Vorschau: `examples/meine-leute/vorschau.png`.

So wird sie zu eurer Seite:

1. `examples/meine-leute/meine-leute.tupfer.json` öffnen (in der Web-App unter *Datei → Beispiele*)
   und unter neuem Namen speichern.
2. Platzhalterbild auswählen, löschen und das eigene Foto an derselben Stelle einfügen – am besten
   quadratisch zuschneiden. Namen unter den Bildern anpassen (z. B. den Vornamen statt „Geschwister“).
3. Unter *Sprachausgabe* zu jedem Text eine **eigene Aufnahme** machen – die echte Stimme von Mama,
   Oma oder dem großen Geschwisterkind ist das Schönste daran. Die Texte lassen sich nach Belieben ändern.
4. Mit *Testen* (F8) ausprobieren, dann *Auf den Stift speichern* und die Seite drucken.

Auf dem Stift zuerst *Start* antippen (Produkt-Code `901`, verträgt sich also mit dem Tic-Tac-Toe-Beispiel).
Für kleine Hände lohnt dickes Papier; ob eine Laminierfolie die Erkennung stört, vorher testen.

## Beispiel: Wo ist …?

Ein Suchspiel für Kinder ab etwa 4 Jahren mit acht Tieren (Katze, Hund, Kuh, Ente, Pferd, Schwein,
Schaf, Huhn). Vorschau: `examples/wo-ist/vorschau.png`.

- **Frei antippen:** Jedes Tier sagt, wer es ist („Das ist die Katze. Miau!“).
- **Suchspiel (Lupe in der Mitte):** Der Stift fragt „Wo ist die Kuh?“. Richtig → ein Lob und die
  nächste Frage; ein anderes Tier → „Das ist der Hund. Wau wau!“ und die Frage noch einmal – ohne
  „Falsch!“. Nach acht Tieren gibt es ein „Hurra!“. Die Reihenfolge wechselt von Runde zu Runde,
  damit sie sich nicht auswendig lernen lässt.
- **Nochmal fragen** wiederholt die aktuelle Frage.

Auf dem Stift zuerst *Start* antippen (Produkt-Code `902`). Die Texte lassen sich durch eigene
Aufnahmen ersetzen, zum Beispiel echte Tiergeräusche. Die Tierbilder stammen aus
[Twemoji](https://github.com/jdecked/twemoji) (CC-BY 4.0, siehe `THIRD-PARTY-NOTICES.md`).

## Herunterladen

Fertige Setups gibt es auf der [Download-Seite](https://mannimanfred.github.io/Tupfer/download.html)
(in der Web-App unter *Datei → Desktop-Version herunterladen*) und unter
[Releases](https://github.com/ManniManfred/Tupfer/releases):

| System | Datei |
| --- | --- |
| Windows 10/11 | `Tupfer-Designer-Setup-Windows-x64.exe` |
| Linux | `Tupfer-Designer-Linux-x64.deb` (Debian, Ubuntu, Mint …) oder `Tupfer-Designer-Linux-x64.AppImage` (alle Distributionen, ohne Installation) |
| macOS (Apple-Chip / Intel) | `Tupfer-Designer-macOS-arm64.dmg` / `Tupfer-Designer-macOS-x64.dmg` |

Die Setups baut der Workflow `.github/workflows/setups.yml`, sobald sich die Versionsnummer in
`Directory.Build.props` ändert (oder von Hand über *Actions → Setups → Run workflow*), und
veröffentlicht sie als Release `v<Version>`. Sie sind nicht signiert: Windows fragt beim ersten Start
nach („Weitere Informationen → Trotzdem ausführen“), unter macOS die App beim ersten Mal per
Rechtsklick → *Öffnen* starten.

## Starten (Desktop)

```bash
dotnet run --project Tupfer.Designer
# oder direkt mit einem Projekt bzw. einer tttool-YAML-Datei:
dotnet run --project Tupfer.Designer -- examples/tic-tac-toi/tic-tac-toi.tupfer.json
```

Voraussetzung: .NET 10 SDK. **tttool wird nicht benötigt** – der Designer erzeugt OID-Codes und
GME-Dateien selbst. Für die Sprachausgabe nutzt er unter Windows die eingebaute Stimme, unter
Linux/macOS `pico2wave`, `espeak` oder `say`. Ist `oggenc` (vorbis-tools) installiert, werden
die Texte als OGG eingebettet, sonst als WAV (größere Datei, der Stift spielt beides).

## Funktionen

- **Seiten:** Fotos und Bilder einfügen – auch per Drag&Drop aus dem Explorer – (drehen, zuschneiden, skalieren) und Texte setzen
  (Schriftart, Größe, Farbe, Ausrichtung), farbige Flächen (Rechteck, Kreis/Ellipse oder Polygon mit Bögen, auch mit den Zeichenwerkzeugen aufgezogen) als Hintergrund. Die Zeichenreihenfolge lässt sich ändern – eine Ebene
  oder ganz nach vorne/hinten (Strg+Bild↑/↓, mit Umschalt ganz); die Liste *Bilder, Texte und Farbflächen*
  zeigt die Reihenfolge, wählt auch verdeckte Elemente aus und sortiert per Drag & Drop um (mit der Maus am Eintrag, per Finger am Griff links). OID-Flächen liegen immer über
  Bildern und Texten. Mit der Maus OID-Flächen aufziehen,
  verschieben und in der Größe ändern – als Rechteck, Kreis/Ellipse oder beliebiges Polygon, dessen
  Kanten sich zu Kreisbögen biegen lassen. Jede Fläche wird einem Skript oder einem Sondercode
  (Produkt/Anmelden, Wiederholen, Stopp) zugeordnet.
- **Mehrere Objekte anordnen:** Mit Strg- oder Umschalt-Klick (auf dem Handy: *Mehrere wählen*)
  bzw. Strg+A mehrere Flächen, Bilder und Texte wählen. Sie lassen sich gemeinsam verschieben (Ziehen,
  Pfeiltasten) und löschen, links/rechts/oben/unten bündig oder mittig **ausrichten** und ab drei
  Objekten mit gleichen Abständen **verteilen** (rechte Spalte oder *Bearbeiten → Ausrichten*).
  Ein einzelnes Objekt wird an der Seite ausgerichtet, z. B. waagerecht zentriert.
- **Skripte:** tttool-Skripte bearbeiten, mit Kurzreferenz der Befehle.
- **Programm (C#):** Spiellogik in C# schreiben statt in der Skriptsprache (siehe unten).
- **Sprachausgabe:** Texte, die beim Bauen per Text-to-Speech vertont werden, mit Knopf zum Anhören.
  Jeder Text kann stattdessen eine eigene Aufnahme bekommen – direkt mit dem Mikrofon aufgenommen
  oder als Audiodatei (`.wav`, `.ogg`, `.mp3`) übernommen; sie liegt im Projektordner unter
  `aufnahmen/`. Auf dem Desktop nutzt das unter Windows die eingebaute Audio-Schnittstelle, unter
  Linux/macOS `paplay`/`aplay`/`ffplay`/`afplay` zum Abspielen und `arecord`, `sox` oder `ffmpeg`
  zum Aufnehmen. Alternativ eigene Audiodateien im Medienverzeichnis.
- **Testen (F8):** Ohne Drucken und Stift ausprobieren – im Testmodus führt Antippen einer Fläche ihr
  Skript aus wie der Stift: Register zählen mit, Sprünge werden verfolgt, Texte, Aufnahmen und
  Audiodateien werden nacheinander abgespielt. Rechts stehen die Registerwerte und der Ablauf
  (welche Zeile, welche Befehle). *Neu starten* entspricht dem Antippen des Produkt-Codes.
  Spiele (`G`), Timer und Soundprofile werden nicht simuliert.
- **Automatisch gesichert:** Der Bearbeitungsstand wird laufend (jede Sekunde, wenn sich etwas
  geändert hat) gesichert und beim nächsten Start wiederhergestellt – auch ein noch nicht gespeichertes
  Projekt. Im Browser übersteht er so ein Neuladen der Seite (gespeichert im Browser mit Bildern und
  Aufnahmen), auf dem Desktop einen Absturz (`%AppData%\TupferDesigner` bzw. `~/.config/TupferDesigner`).
  Das ersetzt nicht das Speichern: Die Projektdatei selbst wird nur mit *Speichern* geändert.
  Rückgängig/Wiederholen gilt nach dem Neustart erst für neue Änderungen.
- **Projekt:** Produkt-ID, Begrüßung, Initialisierung, Druckeinstellungen usw.
- **Drucken (Strg+P):** Seiten direkt drucken. Auf dem Desktop geht es an den Standarddrucker
  (Windows: über das PDF-Programm, falls es Drucken anbietet; Linux/macOS: über `lp`), sonst öffnet
  sich das PDF im PDF-Programm. Im Browser erscheint der Druckdialog; auf dem Handy öffnet sich das
  PDF in einem neuen Tab. Immer ohne Skalierung („Tatsächliche Größe“ / 100 %) drucken.
- **Export:**
  - Seiten als druckfertige PDF (Bilder und Texte + OID-Muster in den Flächen)
  - OID-Tabelle aller Codes als PDF, einzelne Flächen als PNG
  - tttool-YAML inkl. `*.codes.yaml`, Import vorhandener YAML-Dateien
  - GME-Datei bauen (Taste F5) – eingebaut, optional über das externe tttool
  - **Auf den Stift speichern (Umschalt+F5):** baut die GME und speichert sie direkt auf den Stift.
    Auf dem Desktop wird das Laufwerk „tiptoi“ automatisch gefunden (sonst Speichern-Dialog). Im
    Browser öffnet sich ein Speichern-Dialog, in dem man das Laufwerk des Stifts wählt – nur in
    Chrome und Edge am PC. Auf dem Handy: GME herunterladen und mit der App *Dateien* auf den Stift
    kopieren (Stift per OTG-Adapter anschließen).

OID-Muster und GME-Dateien sind direkte Portierungen von tttool: Die Muster sind pixelgenau
gleich, die GME-Dateien bei gleichen Audiodateien byteweise identisch mit `tttool assemble`.
Auch die Codes für Skripte werden nach demselben Verfahren vergeben, sodass YAML-Export und
tttool zusammenpassen. Nur Spiele (`games`) und Timer kann der eingebaute Builder nicht – dafür
unter *Projekt* „GME mit externem tttool bauen“ aktivieren.

## Tupfer Designer im Browser

Der Designer läuft auch ohne Installation im Browser (WebAssembly). Lokal starten:

```bash
dotnet workload install wasm-tools   # einmalig
dotnet run --project Tupfer.Designer.Browser
# veröffentlichen (statische Dateien, z. B. für einen Webserver):
dotnet publish Tupfer.Designer.Browser -c Release -o publish   # Ergebnis: publish/wwwroot
```

Unter *Datei → Beispiele* lassen sich die Beispiele aus `examples/` (Tic-Tac-Toe, Meine Leute, Wo ist …?) direkt öffnen. Per
Adresse geht es auch gleich beim Start: `…/index.html?beispiel=tic-tac-toi`. Weitere Beispiele werden in
`Tupfer.Designer.Browser/Examples.cs` und der `.csproj` eingetragen.

Der Workflow `.github/workflows/web.yml` baut die Web-Version bei jedem Push auf `master` und
veröffentlicht sie auf GitHub Pages unter https://mannimanfred.github.io/Tupfer/ (einmalig unter
*Settings → Pages → Source* „GitHub Actions“ wählen).

**Auf eigenem Server (z. B. CapRover):** `captain-definition` und `deploy/Dockerfile` bauen die
Web-Version in einem Container (.NET-SDK mit `wasm-tools`) und liefern sie mit nginx aus. In
CapRover eine App anlegen, HTTPS aktivieren und unter *Deployment* das GitHub-Repository
(Branch `master`) eintragen – oder lokal `caprover deploy` ausführen. Der Bau braucht einige
Minuten und etwa 2–3 GB Arbeitsspeicher. Lokal testen:
`docker build -f deploy/Dockerfile -t tupfer-web . && docker run -p 8080:80 tupfer-web`

**Auf dem Handy:** Bei schmalen Bildschirmen (unter etwa 760 Pixel) schaltet die Oberfläche auf ein
kompaktes Layout um: Im Tab *Seiten* wechselt man oben zwischen *Seite* (Zeichenfläche), *Seiten*
und *Auswahl* (Eigenschaften), die Spalten der anderen Tabs stehen untereinander. Mit dem Pfeilkreuz
(„Ansicht verschieben“) schiebt ein Finger die Seite und zwei Finger zoomen; der Doppelpfeil passt die
Seite in die Breite ein. Am bequemsten ist trotzdem ein Tablet oder das Handy im Querformat.

Unterschiede zur Desktop-Version:

- **Dateien:** Der Browser darf nicht direkt auf die Festplatte schreiben. Gespeicherte Projekte,
  PDFs, PNGs, YAML- und GME-Dateien werden **heruntergeladen**. Ein Projekt mit Bildern oder
  Audiodateien wird als `.zip` heruntergeladen; zum Öffnen das ZIP wählen – oder die
  `.tupfer.json` zusammen mit ihren Bildern (Mehrfachauswahl).
- **Sprachausgabe:** natürlich klingende Stimmen mit [Piper](https://github.com/rhasspy/piper)
  (neuronale Sprachausgabe, läuft im Browser). Im Tab *Sprachausgabe* wählt man die Stimme des
  Projekts und bei Bedarf für jeden Text eine eigene, z. B. für verschiedene Figuren: Deutsch mit
  mehreren weiblichen und männlichen Stimmen und „Thorsten“ in verschiedenen Stimmungen (fröhlich,
  flüsternd, wütend …), außerdem Englisch, Französisch, Spanisch, Italienisch und Niederländisch.
  Beim ersten Gebrauch werden Laufzeit und Lautschrift (etwa 30 MB) und jede Stimme (20–110 MB) von
  cdnjs, jsDelivr und Hugging Face geladen, danach kommen sie aus dem Browser-Cache. Die Texte
  bleiben dabei im Browser. Ist das Laden nicht möglich, spricht
  [meSpeak](https://www.masswerk.at/mespeak/) (eSpeak als JavaScript, klingt deutlich künstlicher).
  Die Texte werden als WAV eingebettet. In der Desktop-Version gibt es die Auswahl nicht; dort
  spricht die Sprachausgabe des Systems.
- **Aufnahmen** mit dem Mikrofon brauchen eine Seite über `https` (oder `localhost`); der Browser
  fragt beim ersten Mal nach der Erlaubnis. Gespeichert wird als WAV (mono, 22 kHz).
- **tttool** kann im Browser nicht gestartet werden; die Option dafür fehlt.
- **Kein C#-Programm:** Der Übersetzer (Roslyn) würde die Web-App mehr als verdoppeln, deshalb fehlt
  der Tab *Programm (C#)*. Projekte mit C#-Programm lassen sich trotzdem öffnen und bauen – dann
  mit den zuletzt (in der Desktop-Version) übersetzten Skripten.
- Schriftarten: Es steht nur die eingebaute Schrift (Inter) zur Verfügung.

## Typischer Ablauf

1. Projekt anlegen, Produkt-ID wählen (z. B. 900–999).
2. Unter *Seiten* Bilder und Texte einfügen und Flächen aufziehen; eine Fläche als *Produkt (Anmelden)* markieren.
3. Unter *Skripte* und *Sprachausgabe* festlegen, was beim Antippen passiert.
4. Mit *Testen* (F8) die Flächen antippen und das Verhalten prüfen.
5. *Erstellen → Auf den Stift speichern* (oder *GME-Datei bauen* und die `.gme` selbst auf den Stift kopieren).
6. *Erstellen → Seiten drucken* (oder *Seiten als PDF*) – mit 1200 dpi in Originalgröße drucken.

Projekte werden als `*.tupfer.json` gespeichert; Pfade zu Bildern relativ zur Projektdatei.
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
liefert IntelliSense in der IDE. Die Tests in `Tupfer.Designer.Tests` vergleichen das übersetzte
Programm mit dem echten C#-Programm (`dotnet test Tupfer.Designer.Tests`).

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

## Quellen

- [tttool / tip-toi-reveng](https://github.com/entropia/tip-toi-reveng) – Werkzeug zum Erstellen eigener tiptoi-Inhalte
- [tttool-Dokumentation](https://tttool.readthedocs.io/)

## Lizenz

Der Tupfer Designer steht unter der [MIT-Lizenz](LICENSE): Jeder darf ihn nutzen, verändern und
weitergeben, auch kommerziell, solange der Lizenzhinweis erhalten bleibt.

Ausnahmen und Fremdbestandteile (u. a. die Portierung von tttool, MIT; meSpeak, GPL 3; die
Lizenzen der Piper-Stimmen) stehen in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

„tiptoi“ ist eine eingetragene Marke der Ravensburger Verlag GmbH. Dieses Projekt steht in keiner
Verbindung zu Ravensburger.
