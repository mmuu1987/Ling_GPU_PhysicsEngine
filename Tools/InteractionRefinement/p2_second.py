from common_p2 import *
import sys,subprocess
for args in [['apply_p2.py','02-real-ui-and-gpu-tests'],['run_p2.py','playmode','preview-01','preview']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
