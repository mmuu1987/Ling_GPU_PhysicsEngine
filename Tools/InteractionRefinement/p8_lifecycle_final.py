from common_p8 import *
import sys
for tag in ['scoped-scene-04','scoped-kernels-03','selection-01','local-01','legacy-kernels-01','deployment-01','scenarios-01','editmode-02']:assert json.loads((P8/tag/'process.json').read_text(encoding='utf-8'))['passed']
for args in [['apply_p8.py','06-redeploy-unload-test'],['run_p8.py','playmode','scoped-scene-05','scoped-scene','gui'],['make_p8_media.py','scoped-scene-05']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
