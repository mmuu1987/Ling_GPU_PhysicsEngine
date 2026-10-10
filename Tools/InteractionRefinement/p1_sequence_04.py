from common_p1 import *
import subprocess,sys
for args in [['apply_p1.py','04-readable-ink'],['run_p1.py','editmode','editmode-03'],['run_p1.py','playmode','preview-03','preview'],['export_p1_images.py','preview-03'],['audit_p1.py','final-audit-02']]:
 print('STEP',args,flush=True)
 result=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/args[0])]+args[1:],cwd=ROOT)
 if result.returncode:sys.exit(result.returncode)
