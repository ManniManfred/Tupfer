# Erzeugt personen.onnx aus RF-DETR-Seg Small (Roboflow, Apache 2.0): Export nach ONNX, Gewichte als
# int8 (dynamische Quantisierung, 130 → 39 MB), dazu Vor- und Nachbearbeitung:
# Eingang RGB-Bytes, Ausgang je Kandidat Wert „Person“ und Maske als Bytes.
# Aufruf: python rfdetr_personen.py personen.onnx   (braucht rfdetr, torch, onnx, onnxruntime)
import os, sys, tempfile
import numpy as np, onnx
from onnx import helper, TensorProto as T, numpy_helper
from onnxruntime.quantization import quantize_dynamic, QuantType
import rfdetr

work = tempfile.mkdtemp()
model = rfdetr.RFDETRSegSmall()
n = model.model.resolution  # 384
model.export(output_dir=work)
raw = os.path.join(work, [f for f in os.listdir(work) if f.endswith('.onnx')][0])
quantized = os.path.join(work, 'int8.onnx')
quantize_dynamic(raw, quantized, weight_type=QuantType.QUInt8)

m = onnx.load(quantized)
g = m.graph
inner_in = g.input[0].name
# Neuer Eingang: uint8 RGB [1,n,n,3]; das Modell erwartet ImageNet-normiertes RGB [1,3,n,n]
new_in = helper.make_tensor_value_info('bild', T.UINT8, [1, n, n, 3])
mean = np.array([0.485, 0.456, 0.406], np.float32) * 255
std = np.array([0.229, 0.224, 0.225], np.float32) * 255
g.initializer.extend([
    numpy_helper.from_array(mean.reshape(1, 1, 1, 3), 'mittel'),
    numpy_helper.from_array((1 / std).reshape(1, 1, 1, 3), 'skala'),
    numpy_helper.from_array(np.array([1], np.int64), 'person_klasse'),
    numpy_helper.from_array(np.array([2], np.int64), 'achse2'),
    numpy_helper.from_array(np.array(16.0, np.float32), 'stufen'),
    numpy_helper.from_array(np.array(128.5, np.float32), 'mitte'),
    numpy_helper.from_array(np.array(0.0, np.float32), 'null'),
    numpy_helper.from_array(np.array(255.0, np.float32), 'f255'),
])
pre = [
    helper.make_node('Cast', ['bild'], ['bild_f'], to=T.FLOAT),
    helper.make_node('Sub', ['bild_f', 'mittel'], ['bild_z']),
    helper.make_node('Mul', ['bild_z', 'skala'], ['bild_n']),
    helper.make_node('Transpose', ['bild_n'], [inner_in], perm=[0, 3, 1, 2]),
]
# Ausgänge: „werte“ [1,100] = Sigmoid der Klasse „person“ (COCO-Index 1);
# „masken“ [1,100,n/4,n/4] = Logit · 16 + 128 als Byte (0 … 255, > 128 = gehört zur Person)
post = [
    helper.make_node('Gather', ['labels', 'person_klasse'], ['p_logit'], axis=2),
    helper.make_node('Squeeze', ['p_logit', 'achse2'], ['p_logit2']),
    helper.make_node('Sigmoid', ['p_logit2'], ['werte']),
    helper.make_node('Mul', ['masks', 'stufen'], ['m16']),
    helper.make_node('Add', ['m16', 'mitte'], ['m128']),
    helper.make_node('Clip', ['m128', 'null', 'f255'], ['m_clip']),
    helper.make_node('Cast', ['m_clip'], ['masken'], to=T.UINT8),
]
old_nodes = list(g.node)
del g.node[:]
g.node.extend(pre + old_nodes + post)
del g.input[:]; g.input.append(new_in)
del g.output[:]
g.output.extend([helper.make_tensor_value_info('werte', T.FLOAT, [1, 100]),
                 helper.make_tensor_value_info('masken', T.UINT8, [1, 100, n // 4, n // 4])])
m.doc_string = 'RF-DETR-Seg Small (Roboflow, Apache 2.0), int8; Ausgabe: Wert Person, Masken (Logit·16+128)'
onnx.checker.check_model(m)
onnx.save(m, sys.argv[1])
