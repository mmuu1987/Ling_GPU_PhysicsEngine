from common_p4 import *
import sys
for args in [['editmode','editmode-02'],['playmode','playmode-01','translation']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/run_p4.py')]+args,cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
