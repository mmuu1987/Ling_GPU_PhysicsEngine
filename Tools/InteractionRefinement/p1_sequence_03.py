from common_p1 import *
import subprocess,sys
for args in [['apply_p1.py','03-ui-pixel-qualification'],['run_p1.py','playmode','preview-02','preview']]:
 print('STEP',args,flush=True)
 result=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if result.returncode:sys.exit(result.returncode)
