from common_p0 import *
import sys
sys.stdout.reconfigure(encoding='utf-8')
runs=[]
for tag in ['rendered-performance-02','rendered-performance-03']:
 print('START',tag,flush=True)
 p=subprocess.run([sys.executable,'-X','utf8',str(ROOT/'Tools/InteractionRefinement/run_p0.py'),'performance',tag,'gui'],cwd=ROOT,capture_output=True,encoding='utf-8',errors='replace',timeout=1050)
 (P0/(tag+'-runner.log')).write_text(p.stdout+'\n'+p.stderr,encoding='utf-8');runs.append({'tag':tag,'passed':p.returncode==0});print('FINISH',tag,'exit',p.returncode,flush=True)
 if p.returncode:save(P0/'rendered-suite.json',{'passed':False,'runs':runs});sys.exit(1)
save(P0/'rendered-suite.json',{'passed':True,'runs':runs})
