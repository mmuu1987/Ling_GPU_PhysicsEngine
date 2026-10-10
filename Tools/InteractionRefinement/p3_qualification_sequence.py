from common_p3 import *
import sys,subprocess
jobs=[['run_p3_qualification.py','playmode','qualification-motion-01','holdmotion'],['run_p3_rendered.py','move','qualification-move-01'],['run_p3_rendered.py','hold','qualification-hold-01'],['run_p3_rendered.py','retreat','qualification-retreat-01'],['run_p3_rendered.py','performance','qualification-perf24-a','gui','24'],['run_p3_rendered.py','performance','qualification-perf48-a','gui','48'],['run_p3_rendered.py','performance','qualification-perf48-b','gui','48'],['run_p3_rendered.py','performance','qualification-perf24-b','gui','24'],['run_p3_qualification.py','editmode','qualification-editmode-01']]
for args in jobs:
 print('STEP',args,flush=True)
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
