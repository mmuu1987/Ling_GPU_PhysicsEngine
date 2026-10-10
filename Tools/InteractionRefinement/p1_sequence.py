from common_p1 import *
import subprocess,sys
for args in [['apply_p1.py','02-isolation-and-ui-tests'],['run_p1.py','editmode','editmode-02'],['run_p1.py','playmode','radius-01','radius']]:
 print('STEP',args,flush=True)
 result=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if result.returncode:sys.exit(result.returncode)
