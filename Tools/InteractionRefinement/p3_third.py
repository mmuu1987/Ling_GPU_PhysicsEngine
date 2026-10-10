from common_p3 import *
import subprocess,sys
for args in [['apply_p3.py','03-wait-window-probe'],['run_p3.py','playmode','wait-window-01','crowding']]:
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
