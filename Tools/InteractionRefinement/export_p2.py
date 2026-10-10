from common_p2 import *
import sys,re,io,zipfile,base64,textwrap
name=sys.argv[1];assert re.fullmatch('[a-z0-9-]+',name)
folder=P2/name;assert folder.exists();memory=io.BytesIO()
with zipfile.ZipFile(memory,'w',zipfile.ZIP_DEFLATED) as z:
 for sub in ['radius-evidence','p2-evidence']:
  for p in (folder/sub).rglob('*'):
   if p.is_file():z.write(p,p.relative_to(folder).as_posix())
text='\n'.join(textwrap.wrap(base64.b64encode(memory.getvalue()).decode(),120))
out=P2/(name+'-evidence.b64');assert not out.exists();out.write_text(text,encoding='ascii');print('EXPORTED',len(memory.getvalue()),'bytes')
