from common_p3 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');d=P3/'perf-review-01';dest=d/'finalization.json';assert not dest.exists();pub=json.loads((d/'publication.json').read_text(encoding='utf-8'));analysis=json.loads((d/'analysis.json').read_text(encoding='utf-8'));notes=json.loads((d/'source-inspection.json').read_text(encoding='utf-8'))
for r in pub['files']+analysis['sourceFiles']:
 p=ROOT/r['path'];assert p.stat().st_size==r['bytes'] and sha(p)==r['sha256']
for r in pub['replacedDocuments']:assert sha(ROOT/r['backup'])==r['oldSha256']
for r in notes['sourceFiles']:assert sha(ROOT/r['path'])==r['sha256']
assert sha(ROOT/'Logs/WorkspaceArchive-20261007-01/manifest.json')==pub['archiveManifestSha256']
r={'status':'P3_PERFORMANCE_REVIEW_PUBLISHED_PENDING_CAUSAL_DIAGNOSIS_AND_HUMAN','publishedFilesVerified':len(pub['files']),'rawEvidenceFilesVerified':len(analysis['sourceFiles']),'sourceInspectionFilesVerified':len(notes['sourceFiles']),'preservedBeforeDocsVerified':2,'archiveFilesVerifiedAtPublication':462,'needsReview':True,'p3Accepted':False,'p4Started':False,'newBuildProduced':False,'project':project_check(),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'checkedAt':datetime.datetime.now().isoformat()};assert r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(dest,r);print(json.dumps(r,ensure_ascii=False,indent=2))
