# Modelle der Personenerkennung

## Personen: `personen.onnx`

`Tupfer.Designer/Assets/personen.onnx` ist das Modell **RF-DETR-Seg Small** (Instanz-Segmentierung, COCO)
von [Roboflow](https://github.com/roboflow/rf-detr), Lizenz
[Apache 2.0](https://github.com/roboflow/rf-detr/blob/develop/LICENSE), exportiert nach ONNX.

Änderungen gegenüber dem Original:

- Export mit dem Paket `rfdetr` (1.11.2) nach ONNX; Gewichte dynamisch als int8 quantisiert
  (130 → 39 MB, erkennt praktisch gleich gut).
- Eingang `bild`: RGB als Bytes `[1, 384, 384, 3]`; die ImageNet-Normierung steckt im Modell.
- Ausgang `werte` `[1, 100]`: je Kandidat die Wahrscheinlichkeit der Klasse „person“.
- Ausgang `masken` `[1, 100, 96, 96]`: je Kandidat die Maske als Logit · 16 + 128 in Bytes
  (über 128 = gehört zur Person). Rahmen und die übrigen Klassen werden nicht gebraucht.

So erzeugt (Python mit `rfdetr`, `torch` (CPU genügt), `onnx`, `onnxruntime`; die Gewichte lädt
`rfdetr` selbst herunter):

```bash
python rfdetr_personen.py personen.onnx
```

## Gesichter: `gesichter.onnx`

`Tupfer.Designer/Assets/gesichter.onnx` ist das Modell **YuNet** (`face_detection_yunet_2023mar`) aus dem
[OpenCV Zoo](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet),
Lizenz [MIT](https://github.com/opencv/opencv_zoo/blob/main/models/face_detection_yunet/LICENSE).

Änderungen gegenüber dem Original (Gewichte unverändert):

- Eingang `bild`: RGB als Bytes `[1, Höhe, Breite, 3]`, Höhe und Breite beliebige Vielfache von 32
  (das Original erwartet BGR als Gleitkommazahlen `[1, 3, 640, 640]`).
- Ausgang `gesichter` `[1, N, 5]`: je Ankerpunkt (Schrittweite 8, 16, 32, jeweils Zeile für Zeile)
  der Wert √(cls · obj) und der Rahmen (dx, dy, log w, log h); ausgewertet in
  `PersonDetector.DecodeFaces`. Die Augen-, Nasen- und Mundpunkte werden nicht gebraucht.

So erzeugt (Python mit `onnx`):

```bash
curl -LO https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx
python gesichter.py face_detection_yunet_2023mar.onnx gesichter.onnx
```
