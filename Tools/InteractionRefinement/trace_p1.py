from common_p1 import *
import re,collections,sys
sys.stdout.reconfigure(encoding='utf-8')
results={'unitClasses':{},'radiusConsumers':[],'renderScale':[],'spawnScale':[]}
classes=collections.Counter()
for p in (ROOT/'Assets').rglob('*.asset'):
 if p.stat().st_size>50000:continue
 s=p.read_text(encoding='utf-8',errors='replace')
 if 'unitTypeClassName:' in s:
  m=re.search(r'^\s*unitTypeClassName:\s*(.*)$',s,re.M);classes[m[1].strip() if m else '<missing>']+=1
results['unitClasses']=dict(classes)
for p in (ROOT/'Assets/MassEngine').rglob('*'):
 if not p.is_file() or p.suffix not in ['.cs','.hlsl','.shader','.compute'] or 'Tests' in p.parts:continue
 lines=p.read_text(encoding='utf-8',errors='replace').splitlines();rel=p.relative_to(ROOT).as_posix()
 for i,line in enumerate(lines):
  if ('agentRadius' in line or 'GetAgentRadius(' in line) and not re.search(r'public float|float agentRadius;',line):results['radiusConsumers'].append({'path':rel,'line':i+1,'code':'\n'.join(lines[max(0,i-1):min(len(lines),i+3)])})
  if p.suffix in ['.hlsl','.shader'] and re.search(r'\bscale\b',line) and ('Render' in rel or 'VAT' in rel or 'Vat' in rel or 'LitInstanced' in rel):results['renderScale'].append({'path':rel,'line':i+1,'code':'\n'.join(lines[max(0,i-2):min(len(lines),i+4)])})
  if p.name=='DefaultSpawnModule.cs' and 'scale' in line:results['spawnScale'].append({'path':rel,'line':i+1,'code':line})
save(P1/'source-trace.json',results);print(json.dumps(results,ensure_ascii=False,indent=2),flush=True)
