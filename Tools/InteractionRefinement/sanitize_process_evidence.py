"""Remove command-line credential material from P0 process evidence. No project assets touched."""
from pathlib import Path
import json,time
R=Path(__file__).resolve().parents[2];P=R/'Logs/InteractionRefinement-20261007/P0'
def clean(path):
 if not path.exists():return
 d=json.loads(path.read_text(encoding='utf-8'))
 for proc in d.get('activeProcesses',[]):
  cmd=proc.pop('CommandLine','')
  if cmd:
   proc['role']='AssetImportWorker' if 'AssetImportWorker' in cmd else 'EditorOrPlayer'
   proc['matchesProject']=str(R).replace('\\','/').lower() in cmd.replace('\\','/').lower()
   proc['commandLineOmitted']=True
 path.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
clean(P/'preflight.json')
for i in range(200):
 if (P/'preparation.json').exists():
  clean(P/'preparation.json');print('Process evidence sanitized; command lines not retained',flush=True);break
 time.sleep(3)
else:print('Preflight sanitized; preparation not yet produced',flush=True)
