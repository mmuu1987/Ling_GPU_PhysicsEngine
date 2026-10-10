from common_p5 import *
import zipfile,hashlib,sys
sys.stdout.reconfigure(encoding='utf-8');assert not(P5/'finalization.json').exists();a=json.loads((P5/'audit-01.json').read_text(encoding='utf-8'));p=json.loads((P5/'publication.json').read_text(encoding='utf-8'));assert a['passed'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
for r in p['files']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
for r in a['evidence']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
for path,r in a['approvedChanges'].items():assert sha(ROOT/path)==r['expectedSha256'],path
known={'P3-PERFORMANCE-REVIEW.md':'4e565d6e3e606366c878209d10b9a7a5a95ce3596fd746b46b1e5fe67a7f38fd','P3-PERFORMANCE-REVIEW.html':'47b8fbc606ccfe3a73349fd8fd9c34a910d5e0540038f9b1197ab78bee08a626'}
for n,s in known.items():assert sha(ROOT/'Docs/InteractionRefinement-20261007'/n)==s
handoff=json.loads((P5/'handoff.json').read_text(encoding='utf-8'))
for old in handoff['p4Publication']['files']:
 n=Path(old['path']).name;q=P5/'handoff-docs-before'/n if n in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md'] else ROOT/old['path'];assert sha(q)==old['sha256'],old['path']
arc=ROOT/'Logs/WorkspaceArchive-20261007-01';assert sha(arc/'manifest.json')==a['archive']['manifestSha256'];assert sha(arc/'extra-files.zip')=='49d58bf32bd6b89cd044481c6786118cc1b02f8fcf7a471dfe269a6df15605d0';manifest=json.loads((arc/'manifest.json').read_text(encoding='utf-8'))
with zipfile.ZipFile(arc/'extra-files.zip') as z:
 for item in manifest['files']:
  s=item['storage'];data=(ROOT/s['path']).read_bytes() if s['kind']=='existing-file' else z.read(s['entry']);assert len(data)==item['bytes'] and hashlib.sha256(data).hexdigest()==item['sha256'],item['path']
r={'status':'P5_AUTOMATED_CHECKPOINT_PENDING_MANUAL','documentsAndScreenshotsVerified':len(p['files']),'evidenceVerified':len(a['evidence']),'sourceMetaVerified':len(a['approvedChanges']),'oldP3ReportsUnchanged':True,'archiveItemsVerified':len(manifest['files']),'archiveManifestSha256':a['archive']['manifestSha256'],'zipUnchanged':True,'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'finishedAt':datetime.datetime.now().isoformat()};r['passed']=r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(P5/'finalization.json',r);assert r['passed'];print(json.dumps(r,ensure_ascii=False,indent=2))

