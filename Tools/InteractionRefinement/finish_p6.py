from common_p6 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');r=json.loads((P6/'local-cleanup.json').read_text(encoding='utf-8'));assert sha(P6/'WORKSPACE-INDEX.md')==r['indexSha256'];assert json.loads((P6/'finalization.json').read_text(encoding='utf-8'))['passed'];assert project_check()['passed'] and user_check()['passed'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists();print('INDEX_AND_CHECKPOINT_VERIFIED',r['count'],r['bytes'],flush=True)
