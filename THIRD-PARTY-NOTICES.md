# Fremdbestandteile und Lizenzen

Der Tupfer Designer selbst steht unter der [MIT-Lizenz](LICENSE). Er enthält bzw. nutzt die
folgenden Bestandteile Dritter.

## Portierter Code

### tttool (tip-toi-reveng)

`Tupfer.Designer/Oid/` (OID-Codes) und `Tupfer.Designer/Gme/` (GME-Dateien, Skriptsprache) sind
Portierungen von [tttool](https://github.com/entropia/tip-toi-reveng) nach C#.

```
Copyright (c) 2015 Joachim Breitner

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Im Repository enthalten

| Bestandteil | Ort | Lizenz |
|---|---|---|
| [meSpeak](https://www.masswerk.at/mespeak/) 2.0.2 (Norbert Landsteiner) mit eSpeak (Jonathan Duddington) über [speak.js](https://github.com/kripken/speak.js) | `Tupfer.Designer.Browser/wwwroot/mespeak/` | GNU GPL 3, Text in [`COPYING`](Tupfer.Designer.Browser/wwwroot/mespeak/COPYING); Quelltext: npm-Paket [`mespeak`](https://www.npmjs.com/package/mespeak) |

Die meSpeak-Dateien sind unverändert und werden von der Browser-Version nur geladen (eigene
Dateien, Aufruf über JavaScript). Sie behalten ihre GPL-Lizenz.

## NuGet-Pakete (beim Bauen geladen)

| Paket | Lizenz |
|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (inkl. Desktop, Browser, Themes.Fluent) | MIT |
| Avalonia.Fonts.Inter – Schrift [Inter](https://github.com/rsms/inter) | MIT (Paket), SIL Open Font License 1.1 (Schrift) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT |
| [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) (Roslyn) | MIT |
| [System.Speech](https://github.com/dotnet/runtime) | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT |
| xunit, Microsoft.NET.Test.Sdk (nur Tests) | Apache 2.0 bzw. MIT |

## Zur Laufzeit aus dem Netz geladen (nur Browser-Version)

Diese Dateien werden nicht mit dem Designer verteilt. Der Browser lädt sie beim ersten Gebrauch
direkt von den genannten Servern.

| Bestandteil | Quelle | Lizenz |
|---|---|---|
| [onnxruntime-web](https://github.com/microsoft/onnxruntime) 1.18.0 | cdnjs | MIT |
| piper_phonemize aus [@diffusionstudio/piper-wasm](https://www.npmjs.com/package/@diffusionstudio/piper-wasm) 1.0.0 (mit espeak-ng) | jsDelivr | GNU GPL 3 |
| [Piper](https://github.com/rhasspy/piper)-Stimmen | [Hugging Face, rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices) | je Stimme verschieden, siehe unten |

### Lizenzen der Piper-Stimmen

Jede Stimme hat eine eigene Lizenz; maßgeblich ist die Datei `MODEL_CARD` im Ordner der Stimme
auf Hugging Face. Das betrifft auch die Texte, die der Designer damit in GME-Dateien einbettet.
Die meisten Stimmen (z. B. Thorsten, Kerstin; Eva K., Karlsson und Ramona aus dem M-AILABS-Datensatz)
dürfen frei genutzt werden. **Pavoque** stammt aus einem Datensatz unter CC BY-NC-SA 4.0 und
ist daher nur für nicht-kommerzielle Zwecke gedacht. Vor einer kommerziellen Nutzung erzeugter
GME-Dateien die Lizenz der verwendeten Stimme prüfen.

## Marken

„tiptoi“ ist eine eingetragene Marke der Ravensburger Verlag GmbH. Dieses Projekt steht in keiner
Verbindung zu Ravensburger und wird von Ravensburger weder unterstützt noch geprüft.
