from common_p6 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');folder=P6/'performance-followup-01';assert not(folder/'finalization.json').exists();a=json.loads((folder/'audit.json').read_text(encoding='utf-8'));p=json.loads((folder/'publication.json').read_text(encoding='utf-8'));c=json.loads((folder/'local-cleanup.json').read_text(encoding='utf-8'));assert a['passed'] and p['passed'];assert sha(folder/'WORKSPACE-INDEX.md')==c['indexSha256']
for r in p['files']+a['evidence']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
for path,r in a['approvedChanges'].items():assert sha(ROOT/path)==r['expectedSha256']
assert sha(ROOT/'Logs/WorkspaceArchive-20261007-01/manifest.json')==a['archive']['manifestSha256']
r={'status':p['status'],'performanceAccepted':False,'productionChanged':False,'sourceMetaVerified':24,'evidenceVerified':len(a['evidence']),'documentsVerified':len(p['files']),'indexSha256':c['indexSha256'],'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'finishedAt':datetime.datetime.now().isoformat()};r['passed']=r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(folder/'finalization.json',r);assert r['passed'];print(json.dumps(r,ensure_ascii=False,indent=2))
