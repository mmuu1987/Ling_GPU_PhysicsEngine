from common_p3 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');dest=P3/'qualification-finalization-01.json';assert not dest.exists()
pub=json.loads((P3/'qualification-published.json').read_text(encoding='utf-8'))
for r in pub['files']:
 p=ROOT/r['path'];assert p.stat().st_size==r['bytes'] and sha(p)==r['sha256']
a=json.loads((P3/'qualification-analysis-01.json').read_text(encoding='utf-8'))
for r in a['sourceFiles']:
 p=ROOT/r['path'];assert p.stat().st_size==r['bytes'] and sha(p)==r['sha256']
arc=ROOT/'Logs/WorkspaceArchive-20261007-01';v=json.loads((arc/'verification-qualification-01.json').read_text(encoding='utf-8'));assert sha(arc/'manifest.json')==v['newManifestSha256'] and v['preservedFiles']==462
html=(ROOT/'Docs/InteractionRefinement-20261007/P3-QUALIFICATION.html').read_text(encoding='utf-8');assert html.count('<table>')==5 and '31.3%' in html and '652/652' in html
r={'status':'P3_SUPPLEMENTAL_EVIDENCE_PUBLISHED_REVIEW_REQUIRED','publishedFilesVerified':len(pub['files']),'sourceFilesVerified':len(a['sourceFiles']),'archiveManifestSha256':v['newManifestSha256'],'archiveVerifiedFiles':462,'reviewFlags':pub['reviewFlags'],'needsReview':True,'p3StageAccepted':False,'newBuildProduced':False,'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'finishedAt':datetime.datetime.now().isoformat()};assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(dest,r);print(json.dumps(r,ensure_ascii=False,indent=2))
