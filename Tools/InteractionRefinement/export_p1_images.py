from common_p1 import *
import sys,re,io,zipfile,base64,textwrap
name=sys.argv[1];assert re.fullmatch('[a-z0-9-]+',name)
folder=P1/name/'radius-evidence';assert folder.exists();memory=io.BytesIO()
with zipfile.ZipFile(memory,'w',zipfile.ZIP_DEFLATED) as z:
 for p in folder.glob('*.png'):z.write(p,p.name)
text='\n'.join(textwrap.wrap(base64.b64encode(memory.getvalue()).decode(),120))
out=P1/(name+'-images.b64');assert not out.exists();out.write_text(text,encoding='ascii');print('EXPORTED',len(memory.getvalue()),'bytes')
