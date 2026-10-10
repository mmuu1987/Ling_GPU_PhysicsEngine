from common_p7 import *
import sys,base64
f=P7/sys.argv[1];assert f.is_dir()
for p in f.glob('p7-*.png'):
 q=p.with_suffix('.b64.txt');assert not q.exists();q.write_text(base64.b64encode(p.read_bytes()).decode(),encoding='ascii');print(q.relative_to(ROOT))
