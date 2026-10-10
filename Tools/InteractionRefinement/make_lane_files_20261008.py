import os
ROOT=r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
P3=os.path.join(ROOT,'Logs','InteractionRefinement-20261007','P3')
SRC=os.path.join(P3,'burst-landing-20261008-01','files','Assets','MassEngine','Terrain','TerrainNavigationGrid.cs')
DSTDIR=os.path.join(P3,'burst-lane-20261008-01','files','Assets','MassEngine','Terrain')
os.makedirs(DSTDIR,exist_ok=True)
g=open(SRC,encoding='utf-8',newline='').read()
a=g.index('        public bool HasClearCardinalRoute33(int from,int to)')
b=g.index('        public uint[] CopyWalkable()')
new='''        // Straight-run blockers depend only on immutable neighbour masks. The first route query builds them;
        // each answer is the same as walking the segment cell by cell.
        private int[] clearRightFrom, clearLeftFrom, clearUpFrom, clearDownFrom;
        private void EnsureStraightRuns()
        {
            if (clearRightFrom != null) return;
            int n = CellCount, w = ResolutionX;
            var right = new int[n]; var left = new int[n]; var up = new int[n]; var down = new int[n];
            for (int c = n - 1; c >= 0; c--)
            {
                right[c] = (neighbours[c] & 1) == 0 ? c : (c + 1 < n ? right[c + 1] : n);
                up[c] = (neighbours[c] & 2) == 0 ? c : (c + w < n ? up[c + w] : n);
            }
            for (int c = 0; c < n; c++)
            {
                left[c] = (neighbours[c] & 16) == 0 ? c : (c - 1 >= 0 ? left[c - 1] : -1);
                down[c] = (neighbours[c] & 32) == 0 ? c : (c - w >= 0 ? down[c - w] : -1);
            }
            clearRightFrom = right; clearLeftFrom = left; clearUpFrom = up; clearDownFrom = down;
        }

        public bool HasClearCardinalRoute33(int from,int to)
        {
            if(from<0||to<0||from>=CellCount||to>=CellCount||walkable[from]==0||walkable[to]==0)return false;
            if(from==to)return true;
            EnsureStraightRuns();
            if(from/ResolutionX==to/ResolutionX) return to>from ? clearRightFrom[from]>=to : clearLeftFrom[from]<=to;
            if(from%ResolutionX==to%ResolutionX) return to>from ? clearUpFrom[from]>=to : clearDownFrom[from]<=to;
            return false;
        }

'''
g=g[:a]+new+g[b:]
open(os.path.join(DSTDIR,'TerrainNavigationGrid.cs'),'w',encoding='utf-8',newline='').write(g)
print('written',len(g))
