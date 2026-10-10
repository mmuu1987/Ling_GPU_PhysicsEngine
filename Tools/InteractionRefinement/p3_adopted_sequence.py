from common_p3 import *
import subprocess,sys
for args in [['apply_p3.py','05-adopted-separation-fixture'],['adopt_separation.py'],['run_p3.py','editmode','editmode-adopted-01'],['run_p3.py','playmode','adopted-01','crowding'],['export_p3_v3.py','adopted-01']]:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
