import os,re
ROOT=r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
W=os.path.join(ROOT,'Logs','InteractionRefinement-20261007','P3','burst-optin-finalshape-20261008-01','candidate')
out=[]
for rel in ['Assets/MassEngine/Terrain/TerrainNavigationGrid.cs','Assets/MassEngine/Terrain/TerrainNavigationRuntime.cs']:
    s=open(os.path.join(W,rel),encoding='utf-8').read().splitlines()
    out.append('=== '+rel+' lines=%d'%len(s))
    for i,l in enumerate(s):
        if re.search(r'#if|#else|#endif|namespace|class |struct |Burst|CreateBurstWorkspace|HasClearCardinalRoute33|public uint\[\]|private readonly|neighbours\s*=|neighbours;',l):
            out.append('%d: %s'%(i+1,l[:150]))
open(os.path.join(ROOT,'Logs','InteractionRefinement-20261007','P3','outline_candidate_20261008.txt'),'w',encoding='utf-8').write('\n'.join(out))
