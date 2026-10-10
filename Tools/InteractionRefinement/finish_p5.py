from common_p5 import *
import sys
for tool in ['publish_p5.py','finalize_p5.py']:
 print('STEP',tool,flush=True);r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/tool)],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
