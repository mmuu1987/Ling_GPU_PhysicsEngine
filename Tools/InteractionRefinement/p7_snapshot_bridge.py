from common_p7 import *
import sys
assert json.loads((P7/'audit-01.json').read_text(encoding='utf-8'))['passed']
commands=[['apply_p7.py','04-selection-receipt-bridge'],['run_p7.py','playmode','selection-04','selection','gui'],['audit_p7_bridge.py','selection-04','local-01','legacy-kernels-01','deployment-01','scenarios-01','editmode-02']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
