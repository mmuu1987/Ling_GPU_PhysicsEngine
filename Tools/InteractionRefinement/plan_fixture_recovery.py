from common_p0 import *
import re,sys
sys.stdout.reconfigure(encoding='utf-8')
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip();prefix='Assets/RPG Tiny Hero Duo/';target='Assets/MassEngine/Tests/Editor/LegacyHumanoid/'
paths=json.loads((P0/'legacy-source-history.json').read_text(encoding='utf-8'))['paths'];paths=set(p for p in paths if p.startswith(prefix));guidmap=json.loads((BASE/'guid-paths.json').read_text(encoding='utf-8'));cache={}
def blob(p):
 if p not in cache:cache[p]=subprocess.check_output(['git','show',head+':'+p],cwd=ROOT,timeout=60)
 assert not cache[p].startswith(b'version https://git-lfs'),'LFS payload unavailable: '+p
 return cache[p]
sourceguid={};assetguids={}
for p in sorted(paths):
 if p.endswith('.meta'):
  m=re.search(rb'^guid:\s*([a-f0-9]{32})',blob(p),re.M)
  if m:sourceguid[m[1].decode()]=p[:-5];assetguids[p[:-5]]=m[1].decode()
seeds=[prefix+'Prefab/MaleCharacterPBR.prefab']+[prefix+'Animation/SwordAndShield/'+n+'.fbx' for n in ['Idle_Normal_SwordAndShield','Die01_SwordAndShield','Attack01_SwordAndShiled']]
queue=seeds[:];recovered=[];existing=[];unresolved=[];seen=set()
while queue:
 p=queue.pop(0)
 if p in seen:continue
 seen.add(p);g=assetguids[p]
 if g in guidmap:
  existing.append({'source':p,'current':guidmap[g],'guid':g});continue
 if p not in paths:raise RuntimeError('Missing dependency blob: '+p)
 assert Path(p).suffix.lower() in {'.prefab','.fbx','.mat','.png','.jpg','.tga','.asset','.controller','.mask'},p
 asset=blob(p);meta=blob(p+'.meta');scan=meta
 if asset.startswith(b'%YAML'):scan+=b'\n'+asset
 for dep in set(re.findall(rb'guid:\s*([a-f0-9]{32})',scan)):
  dep=dep.decode()
  if dep==g or dep.startswith('0000000000000000'):continue
  if dep in guidmap:existing.append({'guid':dep,'current':guidmap[dep]})
  elif dep in sourceguid:queue.append(sourceguid[dep])
  else:unresolved.append({'from':p,'guid':dep})
 recovered.append({'source':p,'destination':target+p[len(prefix):],'guid':g,'bytes':len(asset),'sha256':hashlib.sha256(asset).hexdigest(),'metaSha256':hashlib.sha256(meta).hexdigest()})
result={'readOnlyPlan':True,'sourceCommit':head,'sourcePrefix':prefix,'editorOnlyTarget':target,'files':recovered,'reuseExisting':existing,'unresolved':unresolved,'seedPaths':seeds}
save(P0/'fixture-recovery-plan.json',result);print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
