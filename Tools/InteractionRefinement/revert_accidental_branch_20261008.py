import subprocess,os,json
ROOT=r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
P3=os.path.join(ROOT,'Logs','InteractionRefinement-20261007','P3','burst-landing-20261008-01')
def g(*a): return subprocess.run(['git',*a],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace').stdout.strip()
ref='refs/heads/feat/p3-burst-reserve-default-off'
before=g('rev-parse','--verify','-q',ref)
if before: g('update-ref','-d',ref)
out={'deletedBranchRef':bool(before),'deletedCommit':before,'headNow':g('rev-parse','HEAD'),'branchStillExists':bool(g('rev-parse','--verify','-q',ref)),'status':g('status','--porcelain')[:0] or 'not_printed','stagedIndexCheck':g('diff','--cached','--name-only')[:300]}
os.remove(os.path.join(P3,'commit-result.json')) if os.path.exists(os.path.join(P3,'commit-result.json')) else None
json.dump(out,open(os.path.join(P3,'accidental-commit-revert.json'),'w'),indent=1)
print(json.dumps(out,indent=1))
