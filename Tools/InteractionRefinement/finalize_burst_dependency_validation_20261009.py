from pathlib import Path
import subprocess,sys
R=Path(__file__).resolve().parents[2]
for name in ['portable_burst_dependency_tests_20261009.py','validate_burst_dependency_repair_20261009_v3.py','audit_burst_dependency_repair_20261009.py']:
 print('STAGE',name,flush=True)
 subprocess.run([sys.executable,str(R/'Tools/InteractionRefinement'/name)],cwd=R,check=True)
print('FINAL_REPAIR_VERIFIED',flush=True)
