from common_p6 import *
import sys,xml.etree.ElementTree as ET
root=ET.parse(P6/'editmode-03/results.xml').getroot();counts={name:sum(1 for n in root.iter('test-case') if name in n.get('fullname','') and n.get('result')=='Passed') for name in ['ControlPointDefaultOrderTests','CommandInputSafetyTests','BattleCycleStabilityTests']};print('EDITMODE_CRITICAL_COUNTS',counts,flush=True);assert all(counts.values());save(P6/'critical-editmode-coverage.json',counts)
for name in ['publish_p6.py','finalize_p6.py']:
 r=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement'/name)],cwd=ROOT)
 if r.returncode:sys.exit(r.returncode)
