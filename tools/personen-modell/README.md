# Modell der Personenerkennung

`Tupfer.Designer/Assets/personen.onnx` ist das Modell **DeepLab v3** (MobileNetV2, Pascal VOC, 21 Klassen)
von [MediaPipe](https://ai.google.dev/edge/mediapipe/solutions/vision/image_segmenter#deeplab-v3),
Lizenz [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0), umgewandelt nach ONNX.

Änderungen gegenüber dem Original:

- Format TFLite → ONNX (tf2onnx, Opset 17), Gewichte unverändert.
- Eingang `bild`: RGB als Bytes `[1, 257, 257, 3]`; die Normierung auf −1 … 1 steckt im Modell.
- Ausgang `person`: Maske `[1, 257, 257]` als Bytes, 1 = Klasse „person“ (Index 15, größter Wert
  aller Klassen), sonst 0.

So erzeugt (Python mit `tensorflow-cpu`, `tf2onnx`, `onnx`):

```bash
curl -O https://storage.googleapis.com/mediapipe-models/image_segmenter/deeplab_v3/float32/1/deeplab_v3.tflite
python -m tf2onnx.convert --tflite deeplab_v3.tflite --output deeplab_raw.onnx --opset 17
python umwandeln.py deeplab_raw.onnx personen.onnx
```
