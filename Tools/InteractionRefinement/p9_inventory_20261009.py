from pathlib import Path
import subprocess,json,hashlib,os,sys
sys.stdout.reconfigure(encoding='utf-8')
R=Path(__file__).resolve().parents[2];O=R/'outputs/P9Combined-20261009-01';M='080c5e498f0004c8d54fbe745970fb11e36d3597'
assert not O.exists();O.mkdir()
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0'
def git(*a):return subprocess.check_output(['git','-c','core.quotepath=false',*a],cwd=R,env=env)
def save(n,x):(O/n).write_text(json.dumps(x,ensure_ascii=False,indent=2),encoding='utf-8')
H=git('rev-parse','HEAD').decode().strip()
merge=json.loads((R/'outputs/BurstPublishMerge-20261009-01/merge.json').read_text(encoding='utf-8'));assert merge['mergeCommit']==M
changed=git('diff','--name-only',H,M,'--','Assets','Packages','ProjectSettings').decode().splitlines()
dirty=git('diff','--name-only',H,'--','Assets','Packages','ProjectSettings').decode().splitlines()
untracked=git('ls-files','--others','--exclude-standard','--','Assets','Packages','ProjectSettings').decode().splitlines()
sha=lambda b:hashlib.sha256(b).hexdigest()
overlap={}
for p in changed:
 local=(R/p).read_bytes() if (R/p).is_file() else None
 merged=git('show',M+':'+p)
 try:base=git('show',H+':'+p)
 except subprocess.CalledProcessError:base=None
 norm=lambda b:b.replace(b'\r\n',b'\n') if b is not None else None
 overlap[p]={'localExists':local is not None,'localEqualsMerged':norm(local)==norm(merged),'localEqualsBase':norm(local)==norm(base),'baseExists':base is not None,'localSha':sha(local) if local is not None else None,'mergedSha':sha(merged)}
 if local is not None and norm(local)!=norm(merged) and norm(local)!=norm(base):
  folder=O/'overlaps';folder.mkdir(exist_ok=True);stem=p.replace('/','__');(folder/(stem+'.mother')).write_bytes(local);(folder/(stem+'.merged')).write_bytes(merged)
files=[p.relative_to(R).as_posix() for p in (R/'Assets/Game').rglob('*.cs') if any(x in p.name.lower() for x in ['scoped','radius','translation','resize','plan','isolation','lifecycle','p8','p9','interaction'])]
s={'motherHead':H,'mergedCommit':M,'mergedProjectChanges':changed,'dirtyProjectPaths':dirty,'untrackedProjectPaths':untracked,'mergeLocalOverlap':overlap,'relatedGameSources':files}
save('inventory.json',s)
(O/'dirty-project.diff').write_bytes(git('diff',H,'--','Assets','Packages','ProjectSettings'))
print(json.dumps(s,ensure_ascii=False,indent=2))
