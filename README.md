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
| `Tupfer.Designer.Tests/` | Tests (OID/GME, Stift-Simulation, Bearbeiten) |
| `examples/tic-tac-toi/` | Beispiel: Tic-Tac-Toe als Designer-Projekt mit Spielfeld-Grafik (auch in der Web-App) |
| `examples/meine-leute/` | Vorlage „Meine Leute“: Familienseite für die Kleinsten mit eigenen Fotos und Stimmen (auch in der Web-App) |
| `examples/wo-ist/` | Suchspiel „Wo ist …?“ mit acht Tieren für Vorschulkinder (auch in der Web-App) |
| `examples/weihnachten/` | Beispiel: Die Weihnachtsgeschichte als Comic-Broschüre (4 Seiten A5, Seiten als SVG-Grafiken; auch in der Web-App) |
| `examples/druckertest/` | Drucker-Testseite (PDF + GME), um die passenden Druckeinstellungen zu finden |
| `deploy/setup/` | Setups für Windows (Inno Setup), Linux (.deb, AppImage) und macOS (.dmg) |
| `nextcloud/` | App für Nextcloud: die Web-App mit Dateien aus Nextcloud (siehe „Tupfer Designer in Nextcloud“) |

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

## Beispiel: Die Weihnachtsgeschichte

Die Weihnachtsgeschichte nach Lukas und Matthäus als Comic für Kinder: vier Seiten A5, die als
Broschüre auf ein A4-Blatt passen. Vorschau: `examples/weihnachten/vorschau.png`.

- **Titelseite:** Stern, Maria, Jesuskind, Josef und die Schafe lassen sich antippen.
- **Seiten 2 und 3:** Jedes der acht Comic-Bilder erzählt seine Szene – erst der Erzähler, dann die
  Figuren mit den Sätzen aus den Sprechblasen, mit eigenen Stimmen für Maria, Engel, Josef, Kaiser,
  Wirt, Hirten und Könige.
- **Seite 4:** „Wusstest du schon?“ (Christmette, Krippe, Sternsinger, C+M+B) und ein Weihnachtsgebet.

Die Zeichnungen sind SVG-Grafiken (`seite1.svg` … `seite4.svg`) und bleiben im Druck scharf; die
Texte in Sprechblasen und Kästen sind normale Texte und lassen sich im Designer ändern. Auf dem Stift
zuerst den Titel antippen (Produkt-Code `903`). Drucken mit *Broschüre drucken*: beidseitig, an der
kurzen Kante wenden, ohne Skalierung. Die Bilder sind ohne Schwarzanteil eingestellt, damit der Stift
die Codes auch auf dem dunklen Nachthimmel liest.

## Herunterladen

Fertige Setups gibt es auf der [Download-Seite](https://mannimanfred.github.io/Tupfer/download.html)
(in der Web-App unter *Datei → Desktop-Version herunterladen*) und unter
[Releases](https://github.com/ManniManfred/Tupfer/releases):

| System | Datei |
| --- | --- |
| Windows 10/11 | `Tupfer-Designer-Setup-Windows-x64.exe` |
| Linux | `Tupfer-Designer-Linux-x64.deb` (Debian, Ubuntu, Mint …) oder `Tupfer-Designer-Linux-x64.AppImage` (alle Distributionen, ohne Installation) |
| macOS (Apple-Chip / Intel) | `Tupfer-Designer-macOS-arm64.dmg` / `Tupfer-Designer-macOS-x64.dmg` |
| Nextcloud 28–35 | `Tupfer-Designer-Nextcloud.tar.gz` (siehe „Tupfer Designer in Nextcloud“) |

Die Setups baut der Workflow `.github/workflows/setups.yml`, sobald ein Tag `v<Version>` gepusht
wird, und veröffentlicht sie als Release unter diesem Tag. Die Versionsnummer kommt aus dem Tag
(`v1.27.0` → `1.27.0`), `Directory.Build.props` gilt nur für lokale Builds. Neue Version:
`git tag v1.27.0 && git push origin v1.27.0`. Sie sind nicht signiert: Windows fragt beim ersten Start
nach („Weitere Informationen → Trotzdem ausführen“), unter macOS die App beim ersten Mal per
Rechtsklick → *Öffnen* starten.

## Starten (Desktop)

```bash
dotnet run --project Tupfer.Designer
# oder direkt mit einem Projekt bzw. einer tttool-YAML-Datei:
dotnet run --project Tupfer.Designer -- examples/tic-tac-toi/tic-tac-toi.tupfer.json
```

Voraussetzung: .NET 10 SDK. **tttool wird nicht benötigt** – der Designer erzeugt OID-Codes und
GME-Dateien selbst. Die Sprachausgabe ist dieselbe wie im Browser: natürlich klingende
[Piper](https://github.com/rhasspy/piper)-Stimmen, im Tab *Sprachausgabe* wählbar (siehe
[im Browser](#tupfer-designer-im-browser)). Beim ersten Gebrauch lädt der Designer die Lautschrift
[piper-phonemize](https://github.com/rhasspy/piper-phonemize) (mit espeak-ng, 25–60 MB) von GitHub
und jede Stimme (20–110 MB) von Hugging Face und legt sie im Benutzerverzeichnis ab
(Windows: `%LOCALAPPDATA%\Tupfer\piper`, Linux: `~/.local/share/Tupfer/piper`, macOS:
`~/Library/Application Support/Tupfer/piper`); gesprochen wird danach ohne Netz, die Stimmen laufen
mit ONNX Runtime auf dem eigenen Rechner. Ohne Netz beim ersten Mal spricht die Sprachausgabe des
Systems: unter Windows die eingebaute Stimme, unter Linux/macOS `pico2wave`, `espeak` oder `say`.
Diese Texte werden als Ogg Vorbis eingebettet – mit `oggenc` (vorbis-tools), falls installiert, sonst mit
dem eingebauten Encoder; die Piper-Stimmen werden als WAV eingebettet.

## Funktionen

- **Seiten:** Fotos und Bilder einfügen – auch per Drag&Drop aus dem Explorer – (drehen, skalieren, zuschneiden – rechteckig, als Kreis/Ellipse oder als Polygon mit Bögen) und Texte setzen
  - Bilder: PNG, JPEG, BMP, GIF, WebP und **SVG**. Eine SVG-Grafik bleibt eine Vektorgrafik und
    wird auf dem Bildschirm, im PDF und beim Drucken in der jeweiligen Auflösung gezeichnet – in jeder
    Größe scharf, ohne sie vorher zu rastern (mit [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia),
    auch Verläufe, Transparenz und Texte). Sie wird in ihrer eigenen Größe eingefügt. Wer die Grafik
    dagegen in einzelne bearbeitbare Formen und Texte zerlegen will, nimmt *SVG importieren* (unten).
  (Schriftart, Größe, Farbe, Ausrichtung), farbige Flächen (Rechteck, Kreis/Ellipse oder Polygon mit Bögen, mit oder ohne Rand und Füllung, auch mit den Zeichenwerkzeugen aufgezogen) als Hintergrund. Die Zeichenreihenfolge lässt sich ändern – eine Ebene
  oder ganz nach vorne/hinten (Strg+Bild↑/↓, mit Umschalt ganz); die Liste *Bilder, Texte und Farbflächen*
  zeigt die Reihenfolge, wählt auch verdeckte Elemente aus und sortiert per Drag & Drop um (mit der Maus am Eintrag, per Finger am Griff links). OID-Flächen liegen immer über
  Bildern und Texten. Mit der Maus OID-Flächen aufziehen,
  verschieben und in der Größe ändern – als Rechteck, Kreis/Ellipse oder beliebiges Polygon, dessen
  Kanten sich zu Kreisbögen biegen lassen. Jede Fläche wird einem Skript, einer Sprachausgabe oder
  einem Sondercode (Produkt/Anmelden, Wiederholen, Stopp) zugeordnet. Soll eine Fläche nur etwas
  sagen, wählt man als Ziel *Sprachausgabe* und dann einen Text oder legt mit **+** gleich einen
  neuen an und tippt ihn dort ein (mit Knopf zum Anhören); das Skript `P(name)` legt der Designer selbst an.
- **Symbole für Start, Wiederholen und Stopp:** *Bearbeiten → Symbol einfügen* legt ein rundes Symbol
  (grün mit Abspielpfeil, blau mit Kreispfeil, rot mit Quadrat, je 15 mm) samt passender OID-Fläche an:
  *Start* meldet das Produkt am Stift an, *Wiederholen* spielt das Letzte noch einmal, *Stopp* beendet
  die Wiedergabe. Die Symbole landen nebeneinander am unteren Rand und sind mit ihrer Fläche zusammen
  gewählt, sodass man sie gemeinsam verschiebt. Die Grafiken liegen als SVG in `dateien/` des Projekts.
- **Bild speichern:** Bild wählen und *Bild speichern …* klicken – die Datei des Bildes landet
  unverändert (ohne Zuschnitt und Drehung, in voller Auflösung) auf der Festplatte, z. B. ein Foto aus
  Immich oder ein Bild aus einem geöffneten `*.tupfer`. Im Browser wird sie heruntergeladen, in
  Nextcloud neben das Projekt gelegt.
- **Personen erkennen:** Bild wählen und *Personen erkennen* klicken – für jede Person auf dem Foto
  entsteht eine OID-Fläche als Polygon entlang ihres Umrisses, mit eigenem Text, den man danach ändert
  oder aufnimmt. Nach dem Erkennen fragt der Designer mit einem Ausschnitt des Kopfs nach dem Namen
  jeder Person (von links nach rechts); Fläche, Text und Skript heißen dann gleich richtig, z. B.
  „Änni“ → Skript und Text `aenni` (klein geschrieben, Umlaute ersetzt, Leerzeichen als `_`). Ist der
  Name schon als Skript oder Text vergeben, wird nummeriert (`anni2`), vorhandene Skripte bleiben
  unberührt. Ohne Namen gilt der Platzhalter `person1`/„Person 1“ …; *Abbrechen* legt nichts an. Jede Person bekommt ihre eigene Fläche,
  auch wenn sich alle berühren, jemand ein Kind auf dem Arm hat oder von hinten zu sehen ist
  (Instanz-Segmentierung RF-DETR-Seg von Roboflow). Bei großen Gruppen wird zusätzlich in
  Ausschnitten passend zur Gesichtsgröße gesucht (Gesichter mit YuNet). Verdeckt etwas eine Person
  (z. B. ein Schild vor dem Bauch), bekommt jeder sichtbare Teil eine Fläche mit demselben Text. Die
  Erkennung läuft auf dem eigenen Rechner bzw. im Browser (Modelle zusammen 39 MB, werden
  mitgeliefert); das Foto verlässt ihn nicht. Sie dauert am Desktop wenige Sekunden, im Browser bei
  großen Gruppen bis etwa eine halbe Minute. Die Ecken lassen sich wie bei jedem Polygon nachbessern. Ist das Bild auf der Seite gedreht (z. B. ein Handyfoto, das laut EXIF auf
  dem Kopf gespeichert ist), wird es für die Erkennung aufgerichtet – auf dem Kopf stehende Personen
  findet sie sonst schlecht.
  Liegen auf dem Bild schon Flächen (z. B. aus einem früheren Durchgang), bietet die Namensabfrage an,
  sie zu ersetzen: Die alten Flächen verschwinden, Personen mit demselben Namen behalten Text, Aufnahme,
  Skript und Code, und nicht mehr benutzte Skripte und Texte werden entfernt (Texte mit Aufnahme bleiben).
  Ohne Haken kommen die neuen Flächen dazu (`anni2` …).
- **Aufräumen:** *Bearbeiten → Unbenutzte Skripte und Texte entfernen …* löscht nach Rückfrage Skripte,
  die keine Fläche, kein anderes benutztes Skript und weder Init noch Begrüßung oder Zusatz-YAML aufrufen,
  und Texte, die nirgends mehr abgespielt werden. Das lässt sich nicht rückgängig machen.
- **Fotos aus Immich:** *Bearbeiten → Bild aus Immich …* (oder der Knopf *Immich*) holt Fotos vom
  eigenen [Immich](https://immich.app)-Server. Beim ersten Mal fragt der Designer nach der Adresse
  (z. B. `https://fotos.example.org`, mit oder ohne `/api`) und einem API-Key (in Immich unter
  *Kontoeinstellungen → API-Schlüssel*; nötige Rechte: `user.read` (für die Prüfung der Verbindung), `album.read`, `person.read`, `asset.read`,
  `asset.view`, `asset.download`, `face.read`). Danach zeigt er die Alben bzw. die Personen mit Namen,
  jeweils mit Vorschaubildern; das Suchfeld filtert sie nach dem Namen, und *Fotos suchen* (oder
  Eingabetaste) sucht Fotos nach ihrem Inhalt mit der Smart-Suche von Immich (z. B. „Strand“,
  „Geburtstag“; das Standardmodell von Immich versteht Englisch am besten, für Deutsch in Immich ein
  mehrsprachiges Modell wählen). Darin wählt man ein oder mehrere Fotos und fügt sie ein – als große
  Vorschau (meist 1440 Pixel an der langen Seite, reicht zum Drucken) oder mit *Original statt großer
  Vorschau* in voller Auflösung. Originale, die sich nicht anzeigen lassen (HEIC, RAW), werden durch die
  Vorschau ersetzt; in Immich bearbeitete Fotos kommen in der bearbeiteten Fassung. Die Fotos werden
  wie eine normale Bilddatei Teil des Projekts (im `*.tupfer` unter `dateien/`). Laut EXIF gedreht
  gespeicherte Fotos stehen richtig herum, weil das Bild-Element entsprechend gedreht wird.
  Adresse und API-Key stehen in den Einstellungen des Programms (am Desktop
  `einstellungen.json` neben `zuletzt.json` im Benutzerprofil, unter Linux/macOS nur für den Benutzer
  lesbar; im Browser im Speicher der Seite), **nie im Projekt**; im Projekt steht nur die Kennung des Fotos.
  *Zugang …* ändert sie. Verständliche Meldungen gibt es, wenn der Server nicht erreichbar ist, der Key
  abgelehnt wird oder ihm Rechte fehlen.
- **Personen erkennen bei Fotos aus Immich:** Immich kennt die Gesichter auf seinen Fotos und meist auch
  die Namen. Bei einem Foto aus Immich erkennt *Personen erkennen* die Personen wie sonst mit ihrem
  ganzen Umriss als OID-Fläche und holt die Namen von Immich: Jede Person bekommt den Namen des Gesichts,
  das in ihrem Umriss liegt, und die Abfrage der Namen erscheint damit vorausgefüllt (Gesichter ohne
  Namen bleiben leer → „Person 1“), sodass man nur noch korrigiert. Findet die eigene Erkennung zu einem
  Gesicht keine Person, bekommt es eine rechteckige Fläche um das Gesicht. Die Rahmen von Immich gelten
  für das richtig gedrehte Bild in Immichs Größe und werden auf das eingefügte Bild umgerechnet – mit
  Auflösung, EXIF-Drehung, Zuschnitt, Skalierung und Drehung auf der Seite; Gesichter außerhalb des
  Zuschnitts entfallen. Kennt Immich auf dem Foto keine Gesichter oder ist der Server nicht erreichbar,
  sagt der Designer das und erkennt die Personen ohne Namen. Verwendete Endpunkte (geprüft gegen die API-Beschreibung von
  Immich 3.4, sie gehen auch mit älteren Versionen): `GET /api/users/me`, `GET /api/albums`,
  `GET /api/people`, `POST /api/search/metadata` (mit `albumIds`/`personIds`), `POST /api/search/smart`,
  `GET /api/assets/{id}/thumbnail`, `GET /api/assets/{id}/original`, `GET /api/faces?id={id}` und
  `GET /api/people/{id}/thumbnail`, jeweils mit dem Header `x-api-key`.
- **Mehrere Objekte anordnen:** Mit Strg- oder Umschalt-Klick (auf dem Handy: *Mehrere wählen*)
  bzw. Strg+A mehrere Flächen, Bilder und Texte wählen. Sie lassen sich gemeinsam verschieben (Ziehen,
  Pfeiltasten) und löschen, links/rechts/oben/unten bündig oder mittig **ausrichten** und ab drei
  Objekten mit gleichen Abständen **verteilen** (rechte Spalte oder *Bearbeiten → Ausrichten*).
  Ein einzelnes Objekt wird an der Seite ausgerichtet, z. B. waagerecht zentriert.
- **Skripte:** tttool-Skripte bearbeiten, mit Kurzreferenz der Befehle.
- **Sprachausgabe:** Texte, die beim Bauen per Text-to-Speech vertont werden, mit Knopf zum Anhören.
  Jeder Text kann stattdessen eine eigene Aufnahme bekommen – direkt mit dem Mikrofon aufgenommen
  oder als Audiodatei (`.wav`, `.ogg`, `.mp3`) übernommen; sie liegt im Projektordner unter
  `aufnahmen/`. Aufnahmen und übernommene WAV-Dateien werden als Ogg Vorbis (mono, 22 kHz) gespeichert,
  dem Format des Stifts – etwa ein Zehntel der WAV-Größe. Auf dem Desktop nutzt das unter Windows die eingebaute Audio-Schnittstelle, unter
  Linux/macOS `paplay`/`aplay`/`ffplay`/`afplay` zum Abspielen und `arecord`, `sox` oder `ffmpeg`
  zum Aufnehmen. Alternativ eigene Audiodateien im Medienverzeichnis.
  Die Schere an einer Aufnahme (WAV oder Ogg) öffnet einen Editor: Anfang und Ende in der Wellenform
  ziehen, Lautstärke einstellen, anhören (eine orange Linie zeigt die Stelle, die gerade erklingt) und übernehmen. Die bearbeitete Aufnahme wird als neue Datei
  gespeichert. *Lautstärke angleichen* bringt alle Aufnahmen auf einmal auf den gleichen Sprachpegel wie die
  Sprachausgabe (-22 dBFS, Pausen zählen nicht mit; einzelne Spitzen werden weich begrenzt); dasselbe macht
  „Automatisch“ im Editor für eine Aufnahme.
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
  - SVG: Seite als SVG exportieren (Bilder eingebettet, Texte, Farbflächen; OID-Flächen auf einer
    ausgeblendeten Ebene) und SVG-Grafiken importieren – Formen, Pfade (auch Kurven und Bögen),
    Linien, Texte und Bilder werden zu bearbeitbaren Farbflächen, Texten und Bildern, die OID-Ebene
    wieder zu OID-Flächen. Verläufe werden zur ersten Farbe, Löcher in Pfaden werden mitgefüllt.
  - GME-Datei bauen (Taste F5) – eingebaut, optional über das externe tttool
  - **Auf den Stift speichern (Umschalt+F5):** baut die GME und speichert sie direkt auf den Stift.
    Auf dem Desktop wird das Laufwerk „tiptoi“ automatisch gefunden (sonst Speichern-Dialog). Im
    Browser öffnet sich ein Speichern-Dialog, in dem man das Laufwerk des Stifts wählt – nur in
    Chrome und Edge am PC. Auf dem Handy: GME herunterladen und mit der App *Dateien* auf den Stift
    kopieren (Stift per OTG-Adapter anschließen).
- **Stift-Manager (*Erstellen → Stift-Manager …*):** am Desktop ein eigenes Fenster neben dem Designer
  (erneutes Aufrufen holt es nach vorne), im Browser eine Ebene über der Ansicht.
  - *Auf dem Stift:* alle GME-Dateien (auch in Unterordnern) mit Produkt-ID aus dem GME-Kopf, Titel,
    Größe und Datum; eigene Projekte und Ravensburger-Produkte (Kommentar „CHOMPTECH …“) sind
    gekennzeichnet. Dazu der freie Speicher, Warnungen bei doppelten Produkt-IDs (auch mit dem
    geöffneten Projekt) und *Löschen* mit Rückfrage. Der Stift wird als Laufwerk „tiptoi“ gefunden,
    sonst mit *Ordner wählen …*.
  - *Ravensburger-Katalog:* die offiziellen Produkte als Kacheln mit großem Bild, Titel, Produkt-ID, Größe
    und Stand. *Installieren* lädt die GME von Ravensburger und legt sie auf den Stift (erst in eine
    temporäre Datei, vorher wird der freie Speicher geprüft). Ist die Datei auf dem Stift älter oder
    anders groß als die aktuelle, heißt es *Update verfügbar* bzw. *Aktualisieren*.
  - Quelle des Katalogs sind die öffentlichen Service-Seiten
    [tiptoi® Audiodateien](https://service.ravensburger.de/tiptoi%C2%AE/tiptoi%C2%AE_Audiodateien),
    auf denen Ravensburger jede GME zum freien Download anbietet. Produkt-ID, Datum und Größe kommen
    aus einer HTTP-Range-Anfrage über die ersten 128 Bytes jeder Datei. Der Abruf dauert etwa eine
    Minute; der Katalog wird zwischengespeichert (`ravensburger-katalog.json` neben den
    Einstellungen) und nach einer Woche neu geladen. Die Schnittstelle des tiptoi Managers
    (`ttapiv2.ravensburger.com`) wird nicht genutzt, weil sie ohne die dort eingebauten Zugangsdaten
    nur einen leeren Katalog liefert.
  - *Web-Version:* Die Ravensburger-Server erlauben keine Zugriffe von Web-Seiten (CORS), daher gibt
    es Katalog und Download nur am Desktop; im Browser führt ein Knopf zur Ravensburger-Seite. In
    Chrome und Edge lässt sich das Laufwerk des Stifts mit *Ordner wählen …* freigeben, dann gehen
    Auflisten und Löschen (ohne Angabe des freien Speichers). In anderen Browsern ist der Stift-Teil
    ausgeblendet.
- **Darstellung hell oder dunkel:** Über das Sonne/Mond-Symbol in der Kopfzeile oder unter
  *Datei → Darstellung* „Wie System“ (Standard), „Hell“ oder „Dunkel“. „Wie System“ folgt dem Betriebssystem, im Browser der Einstellung `prefers-color-scheme`.
  Die Wahl wirkt sofort und wird mit den übrigen Einstellungen gespeichert (am Desktop in
  `einstellungen.json` im Benutzerprofil, im Browser im Speicher der Seite). Nur die Oberfläche wird
  dunkel – die Seiten bleiben weiß wie auf dem Papier, damit man sieht, was gedruckt wird.

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

Der Workflow `.github/workflows/web.yml` baut die Web-Version bei jedem gepushten Tag `v<Version>`
und veröffentlicht sie auf GitHub Pages unter https://mannimanfred.github.io/Tupfer/. Einmalig
einrichten: unter *Settings → Pages → Source* „GitHub Actions“ wählen und unter *Settings →
Environments → github-pages → Deployment branches and tags* eine Regel für Tags `v*` hinzufügen
(sonst lässt GitHub nur Deploys vom Branch `master` zu).

**Auf dem Handy:** Bei schmalen Bildschirmen (unter etwa 760 Pixel) schaltet die Oberfläche auf ein
kompaktes Layout um: Im Tab *Seiten* wechselt man oben zwischen *Seite* (Zeichenfläche), *Seiten*
und *Auswahl* (Eigenschaften), die Spalten der anderen Tabs stehen untereinander. Mit dem Pfeilkreuz
(„Ansicht verschieben“) schiebt ein Finger die Seite und zwei Finger zoomen (20 bis 1000 %, der Wert lässt sich auch direkt
eintippen); der Doppelpfeil passt die
Seite in die Breite ein. Am bequemsten ist trotzdem ein Tablet oder das Handy im Querformat.

Unterschiede zur Desktop-Version:

- **Dateien:** Der Browser darf nicht direkt auf die Festplatte schreiben. Gespeicherte Projekte,
  PDFs, PNGs, YAML- und GME-Dateien werden **heruntergeladen**. Das Projekt (`.tupfer`) enthält
  Bilder und Aufnahmen und lässt sich so wieder öffnen. Ältere Projekte öffnet man als `.zip`
  oder als `.tupfer.json` zusammen mit ihren Bildern (Mehrfachauswahl).
- **Sprachausgabe:** natürlich klingende Stimmen mit [Piper](https://github.com/rhasspy/piper)
  (neuronale Sprachausgabe, läuft im Browser). Im Tab *Sprachausgabe* wählt man die Stimme des
  Projekts und bei Bedarf für jeden Text eine eigene, z. B. für verschiedene Figuren: Deutsch mit
  mehreren weiblichen und männlichen Stimmen und „Thorsten“ in verschiedenen Stimmungen (fröhlich,
  flüsternd, wütend …), außerdem Englisch, Französisch, Spanisch, Italienisch und Niederländisch.
  Beim ersten Gebrauch werden Laufzeit und Lautschrift (etwa 30 MB) und jede Stimme (20–110 MB) von
  cdnjs, jsDelivr und Hugging Face geladen, danach kommen sie aus dem Browser-Cache. Die Texte
  bleiben dabei im Browser. Ohne Internetverbindung gibt es im Browser keine Sprachausgabe (dann
  Texte selbst aufnehmen oder Audiodateien verwenden). Die Texte werden als WAV eingebettet. Die Desktop-Version hat dieselben Stimmen.
- **Personen erkennen** lädt beim ersten Gebrauch die Laufzeit onnxruntime-web von cdnjs (wie die
  Sprachausgabe); die Modelle kommen von der eigenen Seite.
- **Aufnahmen** mit dem Mikrofon brauchen eine Seite über `https` (oder `localhost`); der Browser
  fragt beim ersten Mal nach der Erlaubnis. Gespeichert wird als Ogg Vorbis (mono, 22 kHz).
- **Fotos aus Immich** gehen im Browser nur, wenn der Immich-Server Zugriffe von der Seite des
  Designers erlaubt (CORS). Immich selbst schaltet CORS nur im Entwicklungsmodus ein; ohne Freigabe
  meldet der Designer „nicht erreichbar … CORS“. Abhilfe: die Desktop-Version oder die Nextcloud-App nehmen (dort ohne CORS) oder im
  Webserver vor Immich die Freigabe ergänzen, z. B. bei nginx (Adresse des Designers anpassen):

  ```nginx
  location /api/ {
      if ($request_method = OPTIONS) {
          add_header Access-Control-Allow-Origin "https://mannimanfred.github.io" always;
          add_header Access-Control-Allow-Headers "x-api-key, content-type, accept" always;
          add_header Access-Control-Allow-Methods "GET, POST, OPTIONS" always;
          return 204;
      }
      add_header Access-Control-Allow-Origin "https://mannimanfred.github.io" always;
      proxy_pass http://immich-server:2283;
  }
  ```

  Läuft der Designer über `https` (z. B. GitHub Pages), muss auch Immich über `https` erreichbar sein;
  `http://`-Adressen blockiert der Browser.
- **tttool** kann im Browser nicht gestartet werden; die Option dafür fehlt.
- Schriftarten: Es steht nur die eingebaute Schrift (Inter) zur Verfügung.

## Tupfer Designer in Nextcloud

Die App `nextcloud/tupfer` bringt die Web-Version in die eigene Nextcloud (28 bis 35): Der Designer
läuft wie im Browser, liest und speichert seine Dateien aber direkt in Nextcloud. Nichts wird
heruntergeladen oder hochgeladen, die Dateien bleiben auf dem eigenen Server.

**Installieren** (als Administrator auf dem Server):

```bash
cd /var/www/nextcloud/apps        # oder custom_apps, je nach Einrichtung
tar xzf Tupfer-Designer-Nextcloud.tar.gz
chown -R www-data:www-data tupfer
sudo -u www-data php ../occ app:enable tupfer
```

Das Paket `Tupfer-Designer-Nextcloud.tar.gz` liegt bei jedem
[Release](https://github.com/ManniManfred/Tupfer/releases); selbst bauen mit `nextcloud/build.sh`
(.NET-SDK mit `wasm-tools`, Ergebnis in `nextcloud/dist/`). Es enthält die fertige Web-Version im
Ordner `editor/`, die Nextcloud über PHP ausliefert. So braucht der Webserver keine eigenen Regeln.

**Benutzen:** Die App bearbeitet nur Projekte im Format `*.tupfer` (mit allen Bildern und
Aufnahmen in einer Datei). Ältere `*.tupfer.json` und tttool-YAML öffnet man in der Desktop- oder
Web-Version; einmal gespeichert, sind sie ein `*.tupfer`.

- **Anlegen:** Über **+ Neu → Tupfer-Projekt** in der Dateien-App entsteht im aktuellen Ordner ein
  neues Projekt (Inhalt wie *Datei → Neu*), das sich gleich im Designer öffnet.
- **Bearbeiten:** Ein Klick auf eine `*.tupfer` öffnet sie im Designer, ebenso der Eintrag
  *Mit Tupfer Designer öffnen* im Menü der Datei. Im App-Menü startet **Tupfer** mit dem zuletzt
  bearbeiteten Stand; *Datei → Öffnen* zeigt die Dateiauswahl von Nextcloud.
- **Speichern** schreibt das Projekt zurück in seine Datei in Nextcloud. Wurde sie inzwischen
  anderswo geändert (z. B. auf einem anderen Gerät), fragt der Designer vor dem Überschreiben.
  Ein neues Projekt oder ein Beispiel fragt beim ersten Speichern nach Ordner und Namen.
- **Speichern unter** fragt nach Ordner und Namen in Nextcloud (mit Rückfrage, wenn es die Datei
  schon gibt); danach speichert *Speichern* in die neue Datei.
- **Einfügen:** *Bild einfügen*, *SVG importieren* und *Aufnahme laden* zeigen die Dateiauswahl
  von Nextcloud; die Dateien werden beim Speichern Teil des Projekts. Bilder vom eigenen Rechner
  lassen sich weiter auf die Seite ziehen.
- **Exporte** (GME, PDF, SVG, YAML) landen neben dem Projekt in Nextcloud. Bei einem noch nicht
  gespeicherten Projekt werden sie wie im Browser heruntergeladen. *Drucken* und *Auf den Stift*
  funktionieren wie im Browser.
- **Darstellung:** Ist in Nextcloud fest ein helles oder dunkles Design gewählt, übernimmt der Designer
  es bei „Wie System“; beim Design „Standard“ folgt er wie Nextcloud dem Browser.

**Fotos aus Immich** in Nextcloud: Die Zugriffe auf Immich laufen über den Nextcloud-Server; Immich
braucht deshalb keine CORS-Freigabe, und Nextcloud muss Immich nur selbst erreichen (auch im eigenen
Netz). Der Administrator trägt den Server ein:

```bash
sudo -u www-data php occ config:app:set tupfer immich_url --value=https://fotos.example.org
```

Weitergeleitet wird nur dorthin und nur für die Zugriffe, die der Designer braucht (Anmeldung prüfen,
Alben, Personen, Suche, Vorschauen, Originale, Gesichter), und nur für angemeldete Benutzer. Den
API-Key gibt jeder Benutzer im Designer selbst ein; die Adresse ist dort fest. Der Key liegt im
Speicher des Browsers für die Nextcloud-Adresse, nicht in Nextcloud und nicht im Projekt; Nextcloud
reicht ihn nur durch. Ohne eingetragenen Server sagt der Designer, was zu tun ist.

Die natürliche Sprachausgabe lädt ihre Stimmen wie im Browser von cdnjs, jsDelivr und Hugging Face.
Die App erlaubt dafür diese Adressen in ihren Sicherheitsregeln (Content-Security-Policy). Ist der
Server vom Internet abgeschnitten, gibt es keine Sprachausgabe.

## Typischer Ablauf

1. Projekt anlegen, Produkt-ID wählen (z. B. 900–999).
2. Unter *Seiten* Bilder und Texte einfügen und Flächen aufziehen; eine Fläche als *Produkt (Anmelden)* markieren.
3. Unter *Skripte* und *Sprachausgabe* festlegen, was beim Antippen passiert.
4. Mit *Testen* (F8) die Flächen antippen und das Verhalten prüfen.
5. *Erstellen → Auf den Stift speichern* (oder *GME-Datei bauen* und die `.gme` selbst auf den Stift kopieren).
6. *Erstellen → Seiten drucken* (oder *Seiten als PDF*) – mit 1200 dpi in Originalgröße drucken.

Projekte werden als `*.tupfer` gespeichert: eine ZIP-Datei mit dem Projekt (`projekt.json`) und
allen Bildern, Aufnahmen und Audiodateien, auf die es verweist – man kann sie also einfach
weitergeben. Zum Bearbeiten entpackt der Designer sie in ein Arbeitsverzeichnis im Benutzerprofil
(Windows: `%LocalAppData%\TupferDesigner\projekte`, Linux: `~/.local/share/TupferDesigner/projekte`);
die GME-Datei entsteht neben der Projektdatei. Ältere Projekte (`*.tupfer.json`, Pfade relativ zur
Projektdatei) lassen sich weiter öffnen und werden beim Speichern als `*.tupfer` daneben abgelegt.
Hintergrundbilder älterer Projekte werden beim Öffnen zu einem seitenfüllenden Bild.
*Datei → Schließen* (am Desktop auch Strg+W) schließt das Projekt – bei ungespeicherten Änderungen
mit Rückfrage – und zeigt den Startbildschirm mit *Neues Projekt*, *Projekt öffnen* und am Desktop
den zuletzt geöffneten Dateien.

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

Die Testseite lässt sich im Designer jederzeit neu erzeugen: *Erstellen → Drucker-Testseite → PDF und
GME speichern …* (baut die GME-Datei gleich mit). Unter Windows druckt *Erstellen → Drucker-Testseite →
Direkt drucken …* sie ohne PDF-Programm in Originalgröße; das ist sicherer, weil manche PDF-Programme (z. B. der
Acrobat Reader) die feinen Punkte beim Drucken so verändern, dass der Stift sie nicht erkennt. Auch *Seiten
drucken* (Strg+P) druckt unter Windows direkt.

## Quellen

- [tttool / tip-toi-reveng](https://github.com/entropia/tip-toi-reveng) – Werkzeug zum Erstellen eigener tiptoi-Inhalte
- [tttool-Dokumentation](https://tttool.readthedocs.io/)

## Lizenz

Der Tupfer Designer steht unter der [MIT-Lizenz](LICENSE): Jeder darf ihn nutzen, verändern und
weitergeben, auch kommerziell, solange der Lizenzhinweis erhalten bleibt.

Ausnahmen und Fremdbestandteile (u. a. die Portierung von tttool, MIT; die
Lizenzen der Piper-Stimmen) stehen in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

„tiptoi“ ist eine eingetragene Marke der Ravensburger Verlag GmbH. Dieses Projekt steht in keiner
Verbindung zu Ravensburger.
