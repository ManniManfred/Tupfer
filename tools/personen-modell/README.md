# Modelle der Personenerkennung

## Personen: `personen.onnx`

`Tupfer.Designer/Assets/personen.onnx` ist das Modell **DeepLab v3** (MobileNetV2, Pascal VOC, 21 Klassen)
von [MediaPipe](https://ai.google.dev/edge/mediapipe/solutions/vision/image_segmenter#deeplab-v3),
Lizenz [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0), umgewandelt nach ONNX.

Änderungen gegenüber dem Original:

- Format TFLite → ONNX (tf2onnx, Opset 17), Gewichte unverändert.
- Eingang `bild`: RGB als Bytes `[1, 257, 257, 3]`; die Normierung auf −1 … 1 steckt im Modell.
- Ausgang `person`: Wahrscheinlichkeit der Klasse „person“ (Index 15, Softmax über alle Klassen)
  `[1, 257, 257]` als Bytes 0 … 255. Der Designer mittelt sie über überlappende Kacheln.

So erzeugt (Python mit `tensorflow-cpu`, `tf2onnx`, `onnx`):

```bash
curl -O https://storage.googleapis.com/mediapipe-models/image_segmenter/deeplab_v3/float32/1/deeplab_v3.tflite
python -m tf2onnx.convert --tflite deeplab_v3.tflite --output deeplab_raw.onnx --opset 17
python umwandeln.py deeplab_raw.onnx personen.onnx
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
