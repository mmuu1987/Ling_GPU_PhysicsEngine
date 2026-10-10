from common_p3 import *
import sys,subprocess
for args in [['recover_p3_lock.py'],['apply_p3.py','04-qualified-compression']]:
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
