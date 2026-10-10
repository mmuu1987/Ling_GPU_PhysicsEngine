from common_p6 import *
import sys
commands=[['apply_p6.py','04-observer-lifecycle'],['run_p6.py','playmode','local-02','local'],['run_p6.py','playmode','scene-01','local-scene'],['run_p6.py','playmode','legacy-kernels-01','legacy-kernels']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
