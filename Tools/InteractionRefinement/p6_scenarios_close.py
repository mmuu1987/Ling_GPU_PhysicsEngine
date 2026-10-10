from common_p6 import *
import sys
commands=[['analyze_p6_scenarios.py'],['audit_p6_scenarios.py','scenarios-02','rendered-load-02','rendered-load-03','local-06','legacy-kernels-03','deployment-02','editmode-05'],['publish_p6_scenarios.py']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
