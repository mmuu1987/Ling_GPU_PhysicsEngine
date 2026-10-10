import os,re,json
ROOT=r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
P3=os.path.join(ROOT,'Logs','InteractionRefinement-20261007','P3')
SRC=os.path.join(P3,'burst-optin-finalshape-20261008-01','candidate','Assets','MassEngine','Terrain')
DST=os.path.join(P3,'burst-landing-20261008-01','files','Assets','MassEngine','Terrain')
os.makedirs(DST,exist_ok=True)
def rd(p): return open(p,encoding='utf-8',newline='').read()
def rep(s,old,new,count=1):
    n=s.count(old); assert n==count,(n,old[:120]); return s.replace(old,new)
# ---- Grid: guard the Burst factory (Editor-only) ----
g=rd(os.path.join(SRC,'TerrainNavigationGrid.cs'))
line='new TerrainNavigationBurstWorkspace(this, walkable, neighbours, edgeCosts, uniformOutputDirections);'
g=rep(g,'        internal TerrainNavigationBurstWorkspace CreateBurstWorkspace() =>','#if UNITY_EDITOR\n        internal TerrainNavigationBurstWorkspace CreateBurstWorkspace() =>')
g=rep(g,line,line+'\n#endif')
# ---- Runtime: keep every Burst reference Editor-only ----
r=rd(os.path.join(SRC,'TerrainNavigationRuntime.cs'))
r=rep(r,'        private TerrainNavigationBurstWorkspace burstWorkspace;\n        private bool burstUnavailable;\n',
      '#if UNITY_EDITOR\n        private TerrainNavigationBurstWorkspace burstWorkspace;\n        private bool burstUnavailable;\n#endif\n')
r=rep(r,'            burstWorkspace?.Dispose(); burstWorkspace = null;\n            // Requests use polling',
      '#if UNITY_EDITOR\n            burstWorkspace?.Dispose(); burstWorkspace = null;\n#endif\n            // Requests use polling')
r=rep(r,'namespace MassEngine {\n[Unity.Burst.BurstCompile(','#if UNITY_EDITOR\nnamespace MassEngine {\n[Unity.Burst.BurstCompile(')
r=rep(r,'\n#if UNITY_EDITOR\nnamespace MassEngine {\n // Temporary independent','\n#endif\n#if UNITY_EDITOR\nnamespace MassEngine {\n // Temporary independent')
open(os.path.join(DST,'TerrainNavigationGrid.cs'),'w',encoding='utf-8',newline='').write(g)
open(os.path.join(DST,'TerrainNavigationRuntime.cs'),'w',encoding='utf-8',newline='').write(r)
# ---- static check: non-editor preprocessed view must not mention Burst/preparation identifiers ----
def nonEditor(text):
    stack=[];out=[]
    for l in text.split('\n'):
        t=l.strip()
        if t.startswith('#if UNITY_EDITOR'): stack.append('editor'); continue
        if t.startswith('#if'): stack.append('other'); out.append(l); continue
        if t.startswith('#else') and stack and stack[-1]=='editor': stack[-1]='else'; out.append(l) if False else None; continue
        if t.startswith('#endif'):
            if stack: stack.pop()
            else: out.append(l)
            continue
        if all(s in('other','else') for s in stack): out.append(l)
    return '\n'.join(out)
bad=['TerrainNavigationBurstWorkspace','burstWorkspace','CreateBurstWorkspace','P3PreparationGate','BurstSolveCount','TerrainNavigationSolveJob','Unity.Burst','burstUnavailable','preparation','BurstReserveRequested']
report=[]
for name,text in [('grid',g),('runtime',r)]:
    v=nonEditor(text)
    hits=[(b,v.count(b)) for b in bad if b in v]
    report.append('%s nonEditorBurstHits=%s lines=%d'%(name,hits,len(v.split('\n'))))
    open(os.path.join(DST,name+'-nonEditor.txt'),'w',encoding='utf-8').write(v)
open(os.path.join(P3,'burst-landing-20261008-01','static-check.txt'),'w',encoding='utf-8').write('\n'.join(report))
print('\n'.join(report))
