from common_p6 import *
import sys
for args in [['editmode','editmode-01'],['playmode','local-01','local'],['playmode','scene-01','local-scene'],['playmode','legacy-kernels-01','legacy-kernels']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/run_p6.py')]+args,cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
