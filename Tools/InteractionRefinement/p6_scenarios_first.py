from common_p6 import *
import sys
for args in [['apply_p6.py','10-four-groups-and-roster'],['run_p6.py','playmode','scenarios-01','local-scenarios']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
