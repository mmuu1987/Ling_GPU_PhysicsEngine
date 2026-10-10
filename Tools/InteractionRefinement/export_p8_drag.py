from common_p8 import *
import base64
p=P8/'scoped-scene-02/p8-frames/00000628-drag.png'
(P8/'scoped-scene-02/drag-preview.b64').write_text(base64.b64encode(p.read_bytes()).decode('ascii'))
