"""Integrate only the verified repair into a new local worktree branch. No push/merge/default change."""
from pathlib import Path
import subprocess,json,hashlib,datetime,sys,os
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2]
W=R/'outputs/BurstDependencyRepair-20261009-01'
E=R/'outputs/BurstBranchIntegration-20261009-01'
T=R/'outputs/B10'
BASE='085957ba7e838d23379cbc8fe6249540c86f31d9'
BRANCH='p3/burst-nav-dependency-fix-20261009'
ENV=os.environ.copy();ENV['GIT_OPTIONAL_LOCKS']='0'
def git(*args,cwd=R,check=True):
 p=subprocess.run(['git','-c','core.autocrlf=false',*args],cwd=cwd,env=ENV,capture_output=True,check=check)
 if check:return p.stdout
 return p
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def save(path,d):path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
def protection():
 old=json.loads((W/'mother-before.json').read_text(encoding='utf-8'))
 return {'head':git('rev-parse','HEAD').decode().strip(),'branch':git('branch','--show-current').decode().strip(),'index':sha(R/'.git/index'),'files':{p:sha(R/p) for p in old['files']},'trackedStatusSha256':hashlib.sha256(git('status','--porcelain=v1','--untracked-files=no')).hexdigest()}
assert not E.exists() and not T.exists(),'Unique integration path already exists'
assert git('show-ref','--verify','--quiet','refs/heads/'+BRANCH,check=False).returncode==1,'Branch already exists or ref check failed'
assert json.loads((W/'validation-03/summary.json').read_text())['status']=='all_six_editor_runs_passed'
assert json.loads((W/'source-provenance.json').read_text())['sourceAndPackageProvenancePassed']
assert json.loads((W/'patch-apply-check.json').read_text())['passed']
m=json.loads((W/'repair-manifest.json').read_text());assert m['baseCommit']==BASE
paths=sorted(m['changes']);assert len(paths)==11
for p in paths:assert sha(W/'repair-files'/p)==m['changes'][p]['newSha']
assert git('rev-parse',BASE).decode().strip()==BASE
E.mkdir();state={'status':'running','branch':BRANCH,'base':BASE,'worktree':str(T),'steps':[],'authorization':'User explicitly continued the proposed independent branch integration; local commit only, no push/merge/default enabling.'};save(E/'result.json',state)
print('PREFLIGHT mother protection',flush=True)
before=protection();save(E/'mother-before.json',before)
assert before['head']=='aea369904b87aca537a2d2ba32c2d449167df6ac' and before['branch']=='feat/mother-version08-sync'
try:
 print('CREATE ISOLATED WORKTREE',BRANCH,flush=True)
 p=git('worktree','add','-b',BRANCH,str(T),BASE)
 state['steps'].append('worktree_created');save(E/'result.json',state)
 assert git('rev-parse','HEAD',cwd=T).decode().strip()==BASE
 assert not git('status','--porcelain=v1',cwd=T).strip(),'New worktree is not clean'
 print('APPLY VERIFIED PATCH',flush=True)
 git('apply','--check',str(W/'dependency-repair.patch'),cwd=T)
 git('apply',str(W/'dependency-repair.patch'),cwd=T)
 for path in paths:assert sha(T/path)==m['changes'][path]['newSha'],path
 state['steps'].append('patch_applied_bytes_verified');save(E/'result.json',state)
 git('add','--',*paths,cwd=T)
 staged=git('diff','--cached','--name-only','-z',cwd=T).decode().strip('\0').split('\0')
 assert sorted(staged)==paths,staged
 expected=set(paths)
 status=git('status','--porcelain=v1','-z',cwd=T).decode().split('\0')
 assert all(s[3:] in expected for s in status if s),status
 print('COMMIT exact 11-file set',flush=True)
 message='fix(nav): include Burst fallback dependencies and portable regression tests\n\nComplete the default-off Editor-only fallback from 085957ba. Add adopted lane implementation and explicit Burst/Collections references, include lane/gate/actual Runtime GPU tests, and correct stale preparation comment. No algorithm, default flag, package version, scene, or player behavior change.\n\nVerified against full committed Unity project plus these exact files: default 79/79, opt-in 79/79, managed override/disabled/forced-sync/no-output four runs 1/1 each (162 executions, 79 distinct tests). GPU upload exact bits and workspace balance checked. Source bytes match final repair manifest. Player/AOT, performance ABBA and P9/manual acceptance not newly qualified.'
 git('commit','-m',message,cwd=T)
 commit=git('rev-parse','HEAD',cwd=T).decode().strip()
 assert git('rev-parse','HEAD^',cwd=T).decode().strip()==BASE
 files=git('diff-tree','--no-commit-id','--name-only','-r','-z',commit,cwd=T).decode().strip('\0').split('\0')
 assert sorted(files)==paths,files
 for path in paths:
  assert hashlib.sha256(git('show',commit+':'+path,cwd=T)).hexdigest()==m['changes'][path]['newSha'],'Committed blob mismatch '+path
 assert not git('status','--porcelain=v1',cwd=T).strip(),'Integration worktree has unstaged changes'
 stat=git('show','--stat','--oneline','HEAD',cwd=T).decode('utf-8')
 state.update(status='local_code_commit_verified',commit=commit,files=paths,diffStat=stat,allCommittedRepairBlobsMatchValidatedBytes=True,worktreeClean=True,pushed=False,merged=False)
 print('COMMIT VERIFIED',commit,flush=True)
except Exception as e:
 state.update(status='blocked',error=str(e));print('BLOCKED',repr(e),flush=True)
finally:
 after=protection();save(E/'mother-after.json',after)
 state['motherProtectionPassed']=before==after
 state['motherChangedFiles']=[p for p,h in before['files'].items() if after['files'].get(p)!=h]
 state['motherHeadUnchanged']=before['head']==after['head'];state['motherIndexUnchanged']=before['index']==after['index']
 save(E/'result.json',state)
 print(json.dumps(state,ensure_ascii=False,indent=2),flush=True)
if state['status']!='local_code_commit_verified' or not state['motherProtectionPassed']:sys.exit(1)
