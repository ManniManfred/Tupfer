# Fügt dem Gesichtsmodell YuNet (OpenCV Zoo, MIT) Vor- und Nachbearbeitung hinzu:
# Eingang RGB-Bytes beliebiger Größe (Vielfache von 32), Ausgang je Ankerpunkt Wert und Rahmen.
# Aufruf: python gesichter.py face_detection_yunet_2023mar.onnx gesichter.onnx
import onnx, sys
from onnx import helper, TensorProto as T, numpy_helper
import numpy as np
m = onnx.load(sys.argv[1])
g = m.graph
inner_in = g.input[0].name
# Neuer Eingang: uint8 RGB [1,H,W,3]; YuNet erwartet BGR als Gleitkommazahlen 0 … 255 [1,3,H,W]
new_in = helper.make_tensor_value_info('bild', T.UINT8, [1, 'hoehe', 'breite', 3])
g.initializer.extend([numpy_helper.from_array(np.array([2, 1, 0], np.int64), 'bgr'),
                      numpy_helper.from_array(np.array(0.0, np.float32), 'null'),
                      numpy_helper.from_array(np.array(1.0, np.float32), 'eins')])
pre = [
    helper.make_node('Cast', ['bild'], ['bild_f'], to=T.FLOAT),
    helper.make_node('Gather', ['bild_f', 'bgr'], ['bild_bgr'], axis=3),
    helper.make_node('Transpose', ['bild_bgr'], [inner_in], perm=[0, 3, 1, 2]),
]
# Ausgang „gesichter“ [1, N, 5]: Wert √(cls·obj), dann Rahmen (dx, dy, log w, log h) relativ zum
# Ankerpunkt; Reihenfolge: Schrittweite 8, 16, 32, jeweils Zeile für Zeile
post, parts = [], []
for s in (8, 16, 32):
    post += [
        helper.make_node('Clip', [f'cls_{s}', 'null', 'eins'], [f'cls_c{s}']),
        helper.make_node('Clip', [f'obj_{s}', 'null', 'eins'], [f'obj_c{s}']),
        helper.make_node('Mul', [f'cls_c{s}', f'obj_c{s}'], [f'wert2_{s}']),
        helper.make_node('Sqrt', [f'wert2_{s}'], [f'wert_{s}']),
        helper.make_node('Concat', [f'wert_{s}', f'bbox_{s}'], [f'teil_{s}'], axis=2),
    ]
    parts.append(f'teil_{s}')
post.append(helper.make_node('Concat', parts, ['gesichter'], axis=1))
old_nodes = list(g.node)
del g.node[:]
g.node.extend(pre + old_nodes + post)
del g.input[:]; g.input.append(new_in)
del g.output[:]; g.output.append(helper.make_tensor_value_info('gesichter', T.FLOAT, [1, 'n', 5]))
del g.value_info[:]
m.doc_string = 'YuNet (OpenCV Zoo, MIT); Ausgabe je Ankerpunkt: Wert, dx, dy, log w, log h'
onnx.checker.check_model(m)
onnx.save(m, sys.argv[2])
