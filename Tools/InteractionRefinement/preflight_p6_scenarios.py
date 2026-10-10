from common_p6 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');dest=P6/'scenario-followup-01';assert not dest.exists();r={'active':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'project':project_check(),'userData':user_check()};print(json.dumps(r,ensure_ascii=False,indent=2),flush=True);assert not r['active'] and not r['lock'] and r['project']['passed'] and r['userData']['passed'];dest.mkdir();save(dest/'preflight.json',r);save(dest/'approved-before.json',json.loads((P6/'approved-changes.json').read_text(encoding='utf-8')))
