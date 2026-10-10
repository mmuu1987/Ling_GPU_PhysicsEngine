from common_p0 import *
import sys
sys.stdout.reconfigure(encoding='utf-8')
r=subprocess.run(['git','ls-tree','-r','--name-only','HEAD'],cwd=ROOT,capture_output=True,encoding='utf-8',errors='replace',check=True,timeout=90)
paths=[p.strip('"') for p in r.stdout.splitlines() if any(s.lower() in p.lower() for s in ['MaleCharacterPBR','SwordAndShiled','SwordAndShield','RPG Tiny Hero Duo'])]
result={'headOnly':True,'paths':paths};save(P0/'legacy-source-history.json',result);print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
