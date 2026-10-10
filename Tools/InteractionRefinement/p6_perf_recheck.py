from common_p6 import *
import sys
for args in [['run_p6.py','playmode','perf-original-repeat-01','original-perf'],['run_p6.py','playmode','perf-original-repeat-02','original-perf'],['run_p6.py','playmode','local-05','local'],['run_p6.py','editmode','editmode-04'],['analyze_p6_followup.py']]:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
