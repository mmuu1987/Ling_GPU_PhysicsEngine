from common_p3 import *
import io,zipfile,base64,textwrap,sys,re
name=sys.argv[1];assert re.fullmatch('[a-z0-9-]+',name);folder=P3/name/'crowding-evidence';assert folder.exists()
evidence=json.loads((folder/'evidence.json').read_text(encoding='utf-8'));assert evidence['captureComplete']
selected={s['image'] for s in evidence['samples'] if s.get('image') and (s['phase']=='initial' or (s['phase']=='move' and s['tick']==1800) or (s['phase']=='contact' and s['tick']==2400))}
memory=io.BytesIO();manifest=[]
with zipfile.ZipFile(memory,'w',zipfile.ZIP_DEFLATED) as z:
 for p in folder.iterdir():
  if p.is_file() and (p.suffix!='.png' or p.name in selected):z.write(p,p.name);manifest.append({'name':p.name,'bytes':p.stat().st_size,'sha256':sha(p)})
 z.writestr('export-manifest.json',json.dumps(manifest,indent=2))
encoded=base64.b64encode(memory.getvalue()).decode();destination=P3/(name+'-export');assert not destination.exists();destination.mkdir();parts=[]
for n,start in enumerate(range(0,len(encoded),1800000)):
 part=encoded[start:start+1800000];p=destination/('part-%02d.txt'%n);p.write_text('\n'.join(part[i:i+120] for i in range(0,len(part),120)),encoding='ascii');parts.append(p.relative_to(ROOT).as_posix())
save(destination/'index.json',{'parts':parts,'zipBytes':len(memory.getvalue()),'zipSha256':__import__('hashlib').sha256(memory.getvalue()).hexdigest()});print('EXPORTED',name,len(manifest),len(memory.getvalue()),len(parts),'parts',flush=True)
