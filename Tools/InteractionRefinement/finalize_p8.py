from common_p8 import *
import sys,concurrent.futures
sys.stdout.reconfigure(encoding='utf-8');assert not(P8/'finalization.json').exists();a=json.loads((P8/'audit-01.json').read_text(encoding='utf-8'));p=json.loads((P8/'publication.json').read_text(encoding='utf-8'));c=json.loads((P8/'local-cleanup.json').read_text(encoding='utf-8'));assert a['passed'] and p['passed'] and c['safeToDeleteLocalCopies'];assert sha(P8/'WORKSPACE-INDEX.md')==c['indexSha256'];assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
for r in p['files']+a['evidence']:assert sha(ROOT/r['path'])==r['sha256'],r['path']
for path,r in a['approvedChanges'].items():assert sha(ROOT/path)==r['expectedSha256']
older=[]
for pub in [LOG/'P4/publication.json',LOG/'P5/publication.json',LOG/'P6/publication.json',LOG/'P6/performance-followup-01/publication.json',LOG/'P6/scenario-followup-01/publication.json',LOG/'P7/publication.json']:
 old=json.loads(pub.read_text(encoding='utf-8'))
 for r in old['files']:
  if Path(r['path']).name not in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:
   assert sha(ROOT/r['path'])==r['sha256'],r['path'];older.append(r['path'])
prior=json.loads((LOG/'P7/publication.json').read_text(encoding='utf-8'))
for r in prior['files']:
 if Path(r['path']).name in ['IMPLEMENTATION.md','P3-QUALIFICATION-STATE.md']:assert sha(P8/'publication-docs-before'/Path(r['path']).name)==r['sha256']
arc=ROOT/'Logs/WorkspaceArchive-20261007-01';assert sha(arc/'manifest.json')==a['archive']['manifestSha256'];assert sha(arc/'extra-files.zip')=='49d58bf32bd6b89cd044481c6786118cc1b02f8fcf7a471dfe269a6df15605d0'
keep=ROOT/'Builds/CaptureDefault-20261006-37';before={x['path']:x['sha256'] for x in json.loads((BASE/'current37.sha256.json').read_text(encoding='utf-8'))}
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:now=dict(pool.map(lambda q:(q.relative_to(keep).as_posix(),sha(q)),[q for q in keep.rglob('*') if q.is_file()]))
assert now==before
for label,args in [('git-head',['rev-parse','HEAD']),('git-staged-names',['diff','--cached','--name-status'])]:assert subprocess.check_output(['git','-c','core.quotepath=false']+args,cwd=ROOT,encoding='utf-8',timeout=60)==(BASE/(label+'.txt')).read_text(encoding='utf-8')
for role in ['male','knight']:
 path='Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/'+role+'_Flocking.asset';assert sha(ROOT/path)==json.loads((P8/'baseline.json').read_text(encoding='utf-8'))['files'][path];assert b'separationStrength: 48' in(ROOT/path).read_bytes()
r={'status':p['status'],'humanInputAccepted':False,'universalPerformanceAccepted':False,'publicSelectionEntryEnabled':True,'p9Started':False,'newBuildProduced':False,'sourceMetaVerified':30,'evidenceVerified':len(a['evidence']),'documentsVerified':len(p['files']),'olderReportsAndImagesVerified':older,'previousPlanAndStateBackupsVerified':True,'current37FilesVerified':len(now),'archiveVerified':True,'gitUnchanged':True,'official48Unchanged':True,'indexSha256':c['indexSha256'],'project':project_check(full=True),'userData':user_check(),'activeProcesses':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'finishedAt':datetime.datetime.now().isoformat()};r['passed']=r['project']['passed'] and r['userData']['passed'] and not r['activeProcesses'] and not r['lock'];save(P8/'finalization.json',r);assert r['passed'];print(json.dumps(r,ensure_ascii=False,indent=2))

