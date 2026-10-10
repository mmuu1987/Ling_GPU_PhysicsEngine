from common_p6 import *
import sys
for args in [['apply_p6.py','09-probe-time-budget'],['run_p6.py','playmode','perf-before-02','local-perf'],['run_p6.py','playmode','perf-before-03','local-perf','reverse']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
