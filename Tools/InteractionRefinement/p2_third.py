from common_p2 import *
import sys,subprocess
for args in [['apply_p2.py','03-fixture-boundary'],['run_p2.py','playmode','preview-02','preview'],['run_p2.py','editmode','editmode-02']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
