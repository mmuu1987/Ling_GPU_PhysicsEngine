from common_p6 import *
import sys
commands=[['audit_p6_followup.py','perf-before-02','perf-before-03','perf-original-repeat-01','perf-original-repeat-02','local-05','editmode-04'],['publish_p6_followup.py']]
for args in commands:
 print('STEP',args,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
