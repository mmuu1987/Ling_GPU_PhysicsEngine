from common_p3 import *
import subprocess,sys
for args in [['apply_p3_qualification.py','07-qualification-tools'],['run_p3_qualification.py','playmode','qualification-kernels-01','kernels']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
