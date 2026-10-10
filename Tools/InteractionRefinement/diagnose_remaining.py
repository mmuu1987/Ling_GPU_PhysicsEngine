from common_p0 import *
import re,sys
sys.stdout.reconfigure(encoding='utf-8')
manifest=json.loads((BASE/'project-files.sha256.json').read_text(encoding='utf-8'));paths=[r['path'] for r in manifest];guid=json.loads((BASE/'guid-paths.json').read_text(encoding='utf-8'))
menu=(ROOT/'Assets/Game/Scenes/MainMenu.unity').read_text(encoding='utf-8');links=[{'line':l.strip(),'resolved':guid.get(g)} for l in menu.splitlines() for g in re.findall(r'guid: ([a-f0-9]{32})',l) if 'catalog' in l.lower() or 'm_Script' in l]
raw=[p for p in paths if Path(p).suffix.lower() in ['.fbx','.prefab','.anim'] and any(t in p.lower() for t in ['male','hero','sword','shield'])]
archives=[]
for folder,dirs,files in os.walk(ROOT/'Logs'):
 if any(f.startswith('gpu-Idle-') and f.endswith('.png') for f in files):archives.append(str(Path(folder).relative_to(ROOT)))
result={'menuLinks':links,'humanoidSourceCandidates':raw,'realGpuCaptureDirectories':archives}
save(P0/'remaining-failure-diagnosis.json',result);print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
