import re
ROOT=r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
s=open(ROOT+r'\Assets\MassEngine\Terrain\TerrainNavigationGrid.cs',encoding='utf-8',errors='replace').read().splitlines()
out=[]
for i,l in enumerate(s):
    if 'HasClearCardinalRoute33' in l or 'TryGetCell' in l or 'public Vector2 CellCenter' in l or 'public bool IsWalkable' in l or 'CellCenter(' in l and 'public' in l:
        out.append('%d: %s'%(i+1,l))
start=[i for i,l in enumerate(s) if 'bool HasClearCardinalRoute33' in l]
if start:
    a=start[0]
    out.append('--- body')
    out+=['%d: %s'%(j+1,s[j]) for j in range(a,min(a+40,len(s)))]
open(ROOT+r'\Logs\InteractionRefinement-20261007\P3\grid_lane_read_20261008.txt','w',encoding='utf-8').write('\n'.join(out))
print(len(s))
