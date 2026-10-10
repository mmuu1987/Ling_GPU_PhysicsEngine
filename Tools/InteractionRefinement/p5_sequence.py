from common_p5 import *
import sys
for args in [['editmode','editmode-01'],['playmode','playmode-01','resize']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/run_p5.py')]+args,cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
