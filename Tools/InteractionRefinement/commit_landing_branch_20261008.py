import os,subprocess,hashlib,sys,json
ROOT=r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
P3=os.path.join(ROOT,'Logs','InteractionRefinement-20261007','P3')
FILES=os.path.join(P3,'burst-landing-20261008-01','files')
FINAL=os.path.join(P3,'burst-optin-finalshape-20261008-01','candidate')
BRANCH='feat/p3-burst-reserve-default-off'
def git(*a,env=None,inp=None):
    r=subprocess.run(['git',*a],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace',env=env,input=inp)
    if r.returncode!=0: raise SystemExit('git %s failed: %s'%(' '.join(a),r.stderr))
    return r.stdout.strip()
report={}
# Sources: landing Grid/Runtime (tested), candidate asmdef (tested), adopted lane file and its tests as currently in the working tree.
srcs={
 'Assets/MassEngine/Terrain/TerrainNavigationGrid.cs':os.path.join(FILES,'Assets','MassEngine','Terrain','TerrainNavigationGrid.cs'),
 'Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs':os.path.join(FILES,'Assets','MassEngine','Terrain','TerrainNavigationRuntime.cs'),
 'Assets/MassEngine/MassEngine.asmdef':os.path.join(FINAL,'Assets','MassEngine','MassEngine.asmdef'),
 'Assets/MassEngine/Terrain/TerrainLaneApproach33.cs':os.path.join(ROOT,'Assets','MassEngine','Terrain','TerrainLaneApproach33.cs'),
}
for k,v in srcs.items(): report[k]=hashlib.sha256(open(v,'rb').read()).hexdigest()
head=git('rev-parse','HEAD')
env=os.environ.copy(); tmp=os.path.join(P3,'burst-landing-20261008-01','landing.index'); env['GIT_INDEX_FILE']=tmp
if os.path.exists(tmp): os.remove(tmp)
git('read-tree',head,env=env)
for rel,src in srcs.items():
    blob=git('hash-object','-w','--',src)
    git('update-index','--add','--cacheinfo','100644,%s,%s'%(blob,rel),env=env)
tree=git('write-tree',env=env)
ident=subprocess.run(['git','config','user.name'],cwd=ROOT,capture_output=True,text=True).stdout.strip()
msg='''feat(terrain): add default-off Burst reserve path for navigation solve

- Burst solve is used only when the Editor is started with --war-sandbox-nav-burst.
  Without the flag, or with --war-sandbox-nav-managed, the managed flow field stays in use.
- Burst job and workspace are compiled only under UNITY_EDITOR; Player code keeps the managed backend.
- Preparation gate, native-disposal audit and cold-start diagnostics are validated in Editor only.
- Includes the already-adopted Lane approach file required by TerrainNavigationRuntime.

Verification: EditMode density-runtime regression 150/150 with default-off flags (temporary Editor runs).
Not verified: Player/AOT build, manual UI, formal ABBA for this commit (default-off path only).
'''
commit=git('commit-tree',tree,'-p',head,'-m',msg,env=env)
git('update-ref','refs/heads/'+BRANCH,commit)
report.update({'head':head,'tree':tree,'commit':commit,'branch':BRANCH,'userNameConfigured':bool(ident)})
open(os.path.join(P3,'burst-landing-20261008-01','commit-result.json'),'w',encoding='utf-8').write(json.dumps(report,indent=1,ensure_ascii=False))
print(json.dumps(report,indent=1,ensure_ascii=False))
