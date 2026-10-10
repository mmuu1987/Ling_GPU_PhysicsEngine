# -*- coding: utf-8 -*-
"""Build p3-solve-detail payload: temporary UNITY_EDITOR probes for CreateFlowField and Lane Apply."""
import json, os
ROOT = r'E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine'
GRID = 'Assets/MassEngine/Terrain/TerrainNavigationGrid.cs'
LANE = 'Assets/MassEngine/Terrain/TerrainLaneApproach33.cs'
OBS  = 'Assets/Game/Editor/InteractionRefinementP3Qualification.cs'

def text(rel):
    return open(os.path.join(ROOT, rel.replace('/', os.sep)), 'rb').read().decode('utf-8').replace('\r\n', '\n')

CLS = (
"#if UNITY_EDITOR\n"
"        // Temporary diagnostic-only probe store; removed after this run.\n"
"        public static class P3Detail\n"
"        {\n"
"            public struct SolveRow { public int frame, targets, seeds, pops, relaxes, cells; public long s0, s1, s2, s3, s4; }\n"
"            public struct LaneRow { public int frame, goals, total, nonZero, rowCol, routeCalls, overrides; public long l0, l1, l2, routeTicks; }\n"
"            public static readonly System.Collections.Generic.List<SolveRow> solveRows = new System.Collections.Generic.List<SolveRow>(32768);\n"
"            public static readonly System.Collections.Generic.List<LaneRow> laneRows = new System.Collections.Generic.List<LaneRow>(32768);\n"
"            public static int laneNonZero, laneRowCol, routeCalls, overrides;\n"
"            public static long routeTicks;\n"
"            public static long Clock() { return System.Diagnostics.Stopwatch.GetTimestamp(); }\n"
"            public static void Write(string folder)\n"
"            {\n"
"                var inv = System.Globalization.CultureInfo.InvariantCulture;\n"
"                string freq = System.Diagnostics.Stopwatch.Frequency.ToString(inv);\n"
"                using (var w = new System.IO.StreamWriter(System.IO.Path.Combine(folder, \"solve-detail.csv\")))\n"
"                {\n"
"                    w.WriteLine(\"frame,targets,seeds,pops,relaxes,cells,s0,s1,s2,s3,s4,frequency\");\n"
"                    foreach (var r in solveRows) w.WriteLine(string.Join(\",\", r.frame.ToString(inv), r.targets.ToString(inv), r.seeds.ToString(inv), r.pops.ToString(inv), r.relaxes.ToString(inv), r.cells.ToString(inv), r.s0.ToString(inv), r.s1.ToString(inv), r.s2.ToString(inv), r.s3.ToString(inv), r.s4.ToString(inv), freq));\n"
"                }\n"
"                using (var w = new System.IO.StreamWriter(System.IO.Path.Combine(folder, \"lane-detail.csv\")))\n"
"                {\n"
"                    w.WriteLine(\"frame,goals,total,nonZero,rowCol,routeCalls,overrides,l0,l1,l2,routeTicks,frequency\");\n"
"                    foreach (var r in laneRows) w.WriteLine(string.Join(\",\", r.frame.ToString(inv), r.goals.ToString(inv), r.total.ToString(inv), r.nonZero.ToString(inv), r.rowCol.ToString(inv), r.routeCalls.ToString(inv), r.overrides.ToString(inv), r.l0.ToString(inv), r.l1.ToString(inv), r.l2.ToString(inv), r.routeTicks.ToString(inv), freq));\n"
"                }\n"
"            }\n"
"        }\n"
"#endif\n")

grid_pairs = [
 ["            ValidatePadding(stopRadius, nameof(stopRadius));\n            var result = new Vector2[CellCount];",
  "            ValidatePadding(stopRadius, nameof(stopRadius));\n#if UNITY_EDITOR\n            long s0 = P3Detail.Clock(), s1 = 0, s2 = 0, s3 = 0; int p3Seeds = 0, p3Pops = 0, p3Relaxes = 0;\n#endif\n            var result = new Vector2[CellCount];"],
 ["            if (targets == null) return result;",
  "#if UNITY_EDITOR\n            s1 = P3Detail.Clock();\n#endif\n            if (targets == null) return result;"],
 ["                PushOrDecrease(target);",
  "#if UNITY_EDITOR\n                p3Seeds++;\n#endif\n                PushOrDecrease(target);"],
 ["            while (heapCount > 0)",
  "#if UNITY_EDITOR\n            s2 = P3Detail.Clock();\n#endif\n            while (heapCount > 0)"],
 ["                int cell = PopMinimum();",
  "                int cell = PopMinimum();\n#if UNITY_EDITOR\n                p3Pops++;\n#endif"],
 ["                    PushOrDecrease(adjacent);",
  "#if UNITY_EDITOR\n                    p3Relaxes++;\n#endif\n                    PushOrDecrease(adjacent);"],
 ["            for (int cell = 0; cell < CellCount; cell++)\n            {\n                int next = nextCells[cell];",
  "#if UNITY_EDITOR\n            s3 = P3Detail.Clock();\n#endif\n            for (int cell = 0; cell < CellCount; cell++)\n            {\n                int next = nextCells[cell];"],
 ["            return result;\n        }\n\n        // Cell centers are rounded to float.",
  "#if UNITY_EDITOR\n            P3Detail.solveRows.Add(new P3Detail.SolveRow { frame = Time.frameCount, targets = targets.Count, seeds = p3Seeds, pops = p3Pops, relaxes = p3Relaxes, cells = CellCount, s0 = s0, s1 = s1, s2 = s2, s3 = s3, s4 = P3Detail.Clock() });\n#endif\n            return result;\n        }\n\n" + CLS + "\n        // Cell centers are rounded to float."],
]

lane_pairs = [
 ["   var rows=new List<int>[nav.ResolutionZ];",
  "#if UNITY_EDITOR\n   long l0=TerrainNavigationGrid.P3Detail.Clock();\n#endif\n   var rows=new List<int>[nav.ResolutionZ];"],
 ["   float near=Mathf.Max(nav.CellSize*4,stopRadius+nav.CellSize);",
  "#if UNITY_EDITOR\n   long l1=TerrainNavigationGrid.P3Detail.Clock();\n#endif\n   float near=Mathf.Max(nav.CellSize*4,stopRadius+nav.CellSize);"],
 ["    Vector2 original=directions[cell];if(original.sqrMagnitude<.0001f)continue; // Never override stop/unreachable.",
  "    Vector2 original=directions[cell];if(original.sqrMagnitude<.0001f)continue; // Never override stop/unreachable.\n#if UNITY_EDITOR\n    TerrainNavigationGrid.P3Detail.laneNonZero++;\n#endif"],
 ["    Vector2 here=nav.CellCenter(cell),toward=mean-here;",
  "#if UNITY_EDITOR\n    TerrainNavigationGrid.P3Detail.laneRowCol++;\n#endif\n    Vector2 here=nav.CellCenter(cell),toward=mean-here;"],
 ["    if(Vector2.Distance(here,nav.CellCenter(target))<near||!nav.HasClearCardinalRoute33(cell,target))continue;",
  "#if UNITY_EDITOR\n    if(Vector2.Distance(here,nav.CellCenter(target))<near)continue;\n    long p3rt=TerrainNavigationGrid.P3Detail.Clock();bool p3ok=nav.HasClearCardinalRoute33(cell,target);TerrainNavigationGrid.P3Detail.routeTicks+=TerrainNavigationGrid.P3Detail.Clock()-p3rt;TerrainNavigationGrid.P3Detail.routeCalls++;\n    if(!p3ok)continue;\n#else\n    if(Vector2.Distance(here,nav.CellCenter(target))<near||!nav.HasClearCardinalRoute33(cell,target))continue;\n#endif"],
 ["    directions[cell]=proposed;\n   }\n  }",
  "    directions[cell]=proposed;\n#if UNITY_EDITOR\n    TerrainNavigationGrid.P3Detail.overrides++;\n#endif\n   }\n#if UNITY_EDITOR\n   TerrainNavigationGrid.P3Detail.laneRows.Add(new TerrainNavigationGrid.P3Detail.LaneRow { frame = Time.frameCount, goals = goals.Count, total = directions.Length, nonZero = TerrainNavigationGrid.P3Detail.laneNonZero, rowCol = TerrainNavigationGrid.P3Detail.laneRowCol, routeCalls = TerrainNavigationGrid.P3Detail.routeCalls, overrides = TerrainNavigationGrid.P3Detail.overrides, l0 = l0, l1 = l1, l2 = TerrainNavigationGrid.P3Detail.Clock(), routeTicks = TerrainNavigationGrid.P3Detail.routeTicks });\n   TerrainNavigationGrid.P3Detail.laneNonZero = 0; TerrainNavigationGrid.P3Detail.laneRowCol = 0; TerrainNavigationGrid.P3Detail.routeCalls = 0; TerrainNavigationGrid.P3Detail.overrides = 0; TerrainNavigationGrid.P3Detail.routeTicks = 0;\n#endif\n  }"],
]

obs_anchor = "                using (var writer = new StreamWriter(Path.Combine(output, \"frames.csv\")))"
obs_pairs = [[obs_anchor, "                TerrainNavigationGrid.P3Detail.Write(output);\n" + obs_anchor]]

payload = {GRID: grid_pairs, LANE: lane_pairs, OBS: obs_pairs}
for rel, pairs in payload.items():
    t = text(rel)
    for a, b in pairs:
        n = t.count(a)
        assert n == 1, 'anchor x%d in %s: %r' % (n, rel, a[:70])
out = os.path.join(ROOT, 'Tools', 'InteractionRefinement', 'p3-solve-detail-20261008-payload.json')
assert not os.path.exists(out)
with open(out, 'w', encoding='utf-8') as f:
    json.dump(payload, f, ensure_ascii=False)
print('PAYLOAD OK', sum(len(v) for v in payload.values()), 'replacements')
