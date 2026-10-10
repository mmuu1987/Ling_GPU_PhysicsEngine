"""Burst default-off fallback formal landing: new branch + targeted 2-file commit + push attempt.
User-authorized. Never 'git add .'; aborts before any mutation if preconditions fail."""
import subprocess, hashlib, json, sys
from pathlib import Path
ROOT=Path(r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine')
EV=ROOT/'Logs/InteractionRefinement-20261007/P3/burst-landing-20261008-01/files'
OUT=ROOT/'Logs/InteractionRefinement-20261007/P3/burst-landing-20261008-02'
GRID='Assets/MassEngine/Terrain/TerrainNavigationGrid.cs'
RUNTIME='Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs'
BRANCH='p3/burst-nav-fallback-20261008'
MOTHER='feat/mother-version08-sync'
HEAD_EXPECT='aea369904b87aca537a2d2ba32c2d449167df6ac'
sha=lambda b:hashlib.sha256(b).hexdigest()
report={'steps':[]}
def step(name,**kw):report['steps'].append(dict(name=name,**kw));print('STEP',name,json.dumps(kw,ensure_ascii=False)[:400],flush=True)

def git(*a,check=True,timeout=120,env=None):
    r=subprocess.run(['git']+list(a),cwd=ROOT,capture_output=True,timeout=timeout,env=env)
    out=r.stdout.decode('utf-8',errors='replace');err=r.stderr.decode('utf-8',errors='replace')
    if check and r.returncode!=0:raise RuntimeError('git '+' '.join(a)+' rc='+str(r.returncode)+' :: '+out[-400:]+err[-400:])
    return r.returncode,out,err

def non_editor_burst(text):
    hits=[];stack=[]
    for line in text.replace('\r\n','\n').split('\n'):
        s=line.strip()
        if s.startswith('#if'):stack.append('UNITY_EDITOR' in s);continue
        if s.startswith('#elif'):
            if stack:stack[-1]='UNITY_EDITOR' in s
            continue
        if s.startswith('#else'):
            if stack:stack[-1]=not stack[-1]
            continue
        if s.startswith('#endif'):
            if stack:stack.pop()
            continue
        if not any(stack) and 'Burst' in line:hits.append(s[:80])
    return hits

MSG='''P3: Burst navigation fallback (default-off, opt-in via --war-sandbox-nav-burst)

TerrainNavigationGrid/TerrainNavigationRuntime: Editor-only Burst sync IJob.Run
fallback on top of the adopted indexed-heap/output and lane early-exit baseline.
Default (no flag) keeps the original managed solve; --war-sandbox-nav-managed wins
when both flags are present; compiler-disabled/forced-sync/compile-failure paths
fall back to managed with bitwise-identical output. No parallel navigation; solve
algorithm, strict float, target order, refresh and round-robin unchanged.

Verified: 150/150 regression, exhaustive equivalence, 160 runtime/scene/rendering
case executions, formal ABBA passed (P99 ratios 0.670/0.738; >33.33ms frames 2/6
vs 86/85). Zero Burst references outside UNITY_EDITOR.

Evidence: Logs/InteractionRefinement-20261007/P3/burst-landing-20261008-01,
Docs/InteractionRefinement-20261007/P3-BURST-RUNTIME-QUALIFICATION-20261008.md,
Docs/InteractionRefinement-20261007/P3-BURST-OPTIN-AND-TAIL-20261008.md'''

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    assert not OUT.exists(),'output dir exists'
    t=subprocess.run(['tasklist'],capture_output=True).stdout.decode('utf-8',errors='replace').lower()
    assert 'unity.exe' not in t,'Unity running'
    assert not (ROOT/'Temp/UnityLockfile').exists(),'lockfile'
    _,b,_=git('rev-parse','--abbrev-ref','HEAD');assert b.strip()==MOTHER,b
    _,h,_=git('rev-parse','HEAD');assert h.strip()==HEAD_EXPECT,h
    _,staged,_=git('diff','--cached','--name-only');assert staged.strip()=='','staged entries: '+staged
    rc,_,_=git('rev-parse','--verify','refs/heads/'+BRANCH,check=False);assert rc!=0,'branch already exists'
    rc,uname,_=git('config','user.name',check=False)
    assert rc==0 and uname.strip(),'git user.name unset - abort before any mutation'
    landing={p:(EV/p).read_bytes() for p in (GRID,RUNTIME)}
    pre={p:(ROOT/p).read_bytes() for p in (GRID,RUNTIME)}
    gd=landing[GRID].decode('utf-8');rt=landing[RUNTIME].decode('utf-8')
    assert 'war-sandbox-nav-burst' in rt and 'war-sandbox-nav-managed' in rt,'opt-in flags missing'
    assert 'PopMinimum' in gd,'adopted heap baseline missing in landing grid'
    assert ('TerrainNavigationSolveJob' in gd) or ('TerrainNavigationSolveJob' in rt),'kernel missing'
    for p in (GRID,RUNTIME):
        hits=non_editor_burst(landing[p].decode('utf-8'));assert not hits,(p,hits)
        assert landing[p]!=pre[p],'landing identical to working tree: '+p
    OUT.mkdir(parents=True)
    for p,data in pre.items():
        q=OUT/'pre'/p;q.parent.mkdir(parents=True,exist_ok=True);q.write_bytes(data)
    step('prechecks_ok',head=h.strip(),gitUser=uname.strip(),
         landingSha={p:sha(x) for p,x in landing.items()},preSha={p:sha(x) for p,x in pre.items()})
    committed=None;pushed=None;pushOut=''
    try:
        git('checkout','-b',BRANCH);step('branch_created',branch=BRANCH)
        for p,data in landing.items():(ROOT/p).write_bytes(data)
        git('add','--',GRID,RUNTIME)
        _,sN,_=git('diff','--cached','--name-only')
        files=sorted(x for x in sN.strip().replace('\r','').split('\n') if x)
        assert files==sorted([GRID,RUNTIME]),'staged set wrong: '+repr(files)
        _,dstat,_=git('diff','--cached','--stat')
        git('commit','-m',MSG)
        _,c,_=git('rev-parse','HEAD');committed=c.strip()
        _,par,_=git('rev-parse','HEAD~1');assert par.strip()==HEAD_EXPECT,'parent wrong'
        _,names,_=git('show','--name-only','--pretty=format:','HEAD')
        shown=sorted(x for x in names.strip().replace('\r','').split('\n') if x)
        assert shown==sorted([GRID,RUNTIME]),'commit touches extra files: '+repr(shown)
        step('committed',commit=committed,diffStat=dstat.strip()[-300:])
        import os
        env=os.environ.copy();env['GIT_TERMINAL_PROMPT']='0'
        try:
            rc,po,pe=git('push','-u','origin',BRANCH,check=False,timeout=180,env=env)
            pushed=(rc==0);pushOut=(po+pe)[-500:]
        except subprocess.TimeoutExpired:
            pushed=False;pushOut='push timed out (credentials prompt suppressed)'
        step('push_attempt',ok=pushed,out=pushOut)
    finally:
        git('checkout',MOTHER)
        for p,data in pre.items():(ROOT/p).write_bytes(data)
        _,h2,_=git('rev-parse','HEAD')
        _,b2,_=git('rev-parse','--abbrev-ref','HEAD')
        _,s2,_=git('diff','--cached','--name-only')
        report['backOnMother']=(h2.strip()==HEAD_EXPECT and b2.strip()==MOTHER)
        report['preBytesRestored']=all((ROOT/p).read_bytes()==data for p,data in pre.items())
        report['stagedAfter']=s2.strip()
        report['commit']=committed;report['pushed']=pushed;report['pushOut']=pushOut
        report['lockfile']=(ROOT/'Temp/UnityLockfile').exists()
        (OUT/'landing-executed.json').write_text(json.dumps(report,ensure_ascii=False,indent=1),encoding='utf-8')
        print('FINAL',json.dumps(report,ensure_ascii=False)[:1800],flush=True)
    ok=bool(report['backOnMother'] and report['preBytesRestored'] and report['stagedAfter']=='' and committed)
    sys.exit(0 if ok else 1)
if __name__=='__main__':main()
