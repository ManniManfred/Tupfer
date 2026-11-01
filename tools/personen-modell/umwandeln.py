# Fügt dem DeepLab-v3-Modell (MediaPipe, nach ONNX umgewandelt) Vor- und Nachbearbeitung hinzu:
# Eingang RGB-Bytes, Ausgang Maske der Klasse „person“. Aufruf: python umwandeln.py ein.onnx aus.onnx
import onnx, sys
from onnx import helper, TensorProto as T, numpy_helper
import numpy as np
m = onnx.load(sys.argv[1])
g = m.graph
inner_in = g.input[0].name; inner_out = g.output[0].name
# Neuer Eingang: uint8 RGB [1,257,257,3]
new_in = helper.make_tensor_value_info('bild', T.UINT8, [1, 257, 257, 3])
nodes = [
    helper.make_node('Cast', ['bild'], ['bild_f'], to=T.FLOAT),
    helper.make_node('Mul', ['bild_f', 'skala'], ['bild_s']),
    helper.make_node('Sub', ['bild_s', 'eins'], [inner_in]),
]
g.initializer.extend([numpy_helper.from_array(np.array(1/127.5, np.float32), 'skala'),
                      numpy_helper.from_array(np.array(1.0, np.float32), 'eins'),
                      numpy_helper.from_array(np.array(15, np.int64), 'person_klasse')])
post = [
    helper.make_node('ArgMax', [inner_out], ['klasse'], axis=3, keepdims=0),
    helper.make_node('Equal', ['klasse', 'person_klasse'], ['ist_person']),
    helper.make_node('Cast', ['ist_person'], ['person'], to=T.UINT8),
]
old_nodes = list(g.node)
del g.node[:]
g.node.extend(nodes + old_nodes + post)
del g.input[:]; g.input.append(new_in)
del g.output[:]; g.output.append(helper.make_tensor_value_info('person', T.UINT8, [1, 257, 257]))
m.doc_string = 'DeepLab v3 (MediaPipe, Apache 2.0), Pascal VOC; Ausgabe: 1 = Person'
onnx.checker.check_model(m)
onnx.save(m, sys.argv[2])
