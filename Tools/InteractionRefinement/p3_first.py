from common_p3 import *
import sys,subprocess
for args in [['apply_p3.py','01-diagnostic-capture'],['run_p3.py','playmode','baseline-01','crowding']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
