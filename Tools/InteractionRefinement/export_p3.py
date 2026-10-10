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
out=P3/(name+'-evidence.b64');assert not out.exists();out.write_text('\n'.join(textwrap.wrap(base64.b64encode(memory.getvalue()).decode(),120)),encoding='ascii');print('EXPORTED',name,len(manifest),len(memory.getvalue()),flush=True)
