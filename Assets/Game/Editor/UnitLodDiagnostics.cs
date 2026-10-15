using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Read-only LOD preview helper (used by FarLodFixups / MaleWarriorRebuild): renders the shipped VAT LOD paths of a unit type
    /// (actual runtime mesh/material/MPB via the M5.3 GPU capture fixture) and writes
    /// mesh/texture facts. Never writes to Assets. The one-off male screenshot menu was archived 2026-10-15 to
    /// outputs/Cleanup-20261015-01.
    /// </summary>
    public static class UnitLodDiagnostics
    {
        private const int Tile = 256;

        /// <summary>Renders one (possibly in-memory) unit config. Atlas name = file name of label.</summary>
        public static void RunUnitConfig(UnitTypeConfig unit, string path, string output, StringBuilder report)
        {
            var render = unit.renderConfig;
            var profile = render != null ? render.vatProfile as VATProfile : null;
            report.AppendLine();
            report.AppendLine("== " + path + "  render=" + AssetDatabase.GetAssetPath(render) + "  profile=" + AssetDatabase.GetAssetPath(profile));
            if (profile == null) { report.AppendLine("   no VATProfile"); return; }
            report.AppendLine("   authored meshes near/mid/far = " + Name(render.nearMesh) + " / " + Name(render.midMesh) + " / " + Name(render.farMesh));
            report.AppendLine("   materials near/mid/far = " + Mat(render.nearMaterial) + " / " + Mat(render.midMaterial) + " / " + Mat(render.farMaterial));
            report.AppendLine("   profile: clean=" + Name(profile.cleanMesh) + " tex " + profile.textureWidth + "x" + profile.textureHeight + " rpf " + profile.rowsPerFrame +
                " | mid=" + Name(profile.midLodMesh) + " tex " + profile.midLodTextureWidth + "x" + profile.midLodTextureHeight + " rpf " + profile.midLodRowsPerFrame +
                " | low=" + Name(profile.lowLodMesh) + " tex " + profile.lowLodTextureWidth + "x" + profile.lowLodTextureHeight + " rpf " + profile.lowLodRowsPerFrame +
                " | frames " + profile.totalFrameCount + "@" + profile.frameRate);

            var runtime = ResolvedUnitTypeRuntime.Resolve(unit, 1f);
            string[] lodNames = { "near", "mid", "far" };
            for (int lod = 0; lod < 3; lod++)
            {
                Mesh m = runtime.GetMesh(lod);
                var block = runtime.GetBlock(lod);
                Texture pos = block != null ? block.GetTexture("_VATPosTex") : null;
                report.AppendLine("   [" + lodNames[lod] + "] mesh " + MeshFacts(m) + " | posTex " + (pos != null ? pos.name + " " + pos.width + "x" + pos.height : "null") +
                    " | texWidth " + (block != null ? block.GetFloat("_VATTexWidth").ToString() : "?") +
                    " | mat " + Mat(runtime.GetMaterial(lod)));
            }

            // Hybrid A: mid mesh + mid VAT block drawn with the near material (isolates mesh/VAT vs. far shader).
            var hybridMeshOnNear = ResolvedUnitTypeRuntime.Resolve(unit, 1f);
            hybridMeshOnNear.nearMesh = hybridMeshOnNear.midMesh;
            hybridMeshOnNear.nearBlock = hybridMeshOnNear.midBlock;
            // Hybrid B: near mesh + near VAT block drawn with the mid material (isolates the mid shader).
            var hybridShaderOnNear = ResolvedUnitTypeRuntime.Resolve(unit, 1f);
            hybridShaderOnNear.nearMaterial = hybridShaderOnNear.midMaterial;

            var rows = new List<(string label, ResolvedUnitTypeRuntime rt, int lod)>
            {
                ("near", runtime, 0), ("mid", runtime, 1), ("far", runtime, 2),
                ("midMesh+nearMat", hybridMeshOnNear, 0), ("nearMesh+midMat", hybridShaderOnNear, 0)
            };
            var cols = new List<(AgentState state, VATProfile.VATClipWindow w, float frac)>
            {
                (AgentState.Idle, profile.idle, .3f), (AgentState.Move, profile.move, .25f), (AgentState.Move, profile.move, .75f),
                (AgentState.Attack, profile.attack, .35f), (AgentState.Attack, profile.attack, .6f)
            };
            Vector3[] views = { new Vector3(3f, 1.8f, -5f), new Vector3(-3f, 1.2f, 5f) };

            Bounds bounds = MassEngine.Editor.VatAppearanceRegression.CombinedBounds(profile);
            int columns = cols.Count * views.Length;
            var atlas = new Texture2D(columns * Tile, rows.Count * Tile, TextureFormat.RGBA32, false);
            try
            {
                for (int v = 0; v < views.Length; v++)
                using (var capture = new MassEngine.Editor.VatAppearanceRegressionCapture(bounds, Tile, views[v]))
                {
                    for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < cols.Count; c++)
                    {
                        var col = cols[c];
                        float time = Mathf.Max(0f, col.w.frameCount * col.frac) / Mathf.Max(1, col.w.frameRate);
                        Color32[] px;
                        try { px = capture.Capture(rows[r].rt, rows[r].lod, col.state, time, col.state); }
                        catch (Exception e)
                        {
                            report.AppendLine("   capture fail " + rows[r].label + " " + col.state + ": " + e.Message);
                            px = Enumerable.Repeat(new Color32(255, 0, 255, 255), Tile * Tile).ToArray();
                        }
                        // rows top-to-bottom in the PNG: Texture2D origin is bottom-left.
                        atlas.SetPixels32((v * cols.Count + c) * Tile, (rows.Count - 1 - r) * Tile, Tile, Tile, px);
                    }
                }
                atlas.Apply(false, false);
                string file = Path.GetFileNameWithoutExtension(path) + "-lods.png";
                File.WriteAllBytes(Path.Combine(output, file), atlas.EncodeToPNG());
                report.AppendLine("   atlas " + file + ": rows = " + string.Join(", ", rows.Select(x => x.label)) +
                    "; cols = [view A: idle, move .25, move .75, attack .35, attack .6] then same for view B");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }

        private static string Name(UnityEngine.Object o) => o == null ? "null" : o.name;
        private static string Mat(Material m) => m == null ? "null" : m.name + "(" + (m.shader != null ? m.shader.name : "?") + ")";

        private static string MeshFacts(Mesh m)
        {
            if (m == null) return "null";
            long idx = 0;
            for (int s = 0; s < m.subMeshCount; s++) idx += m.GetIndexCount(s);
            int[] tris = m.GetTriangles(0);
            Vector3[] v = m.vertices;
            // Weld by position so UV seams do not count as holes.
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 10000f), Mathf.RoundToInt(v[i].y * 10000f), Mathf.RoundToInt(v[i].z * 10000f));
                if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld.Add(k, w); }
                id[i] = w;
            }
            var edges = new Dictionary<long, int>();
            int degenerate = 0;
            var used = new bool[v.Length];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = id[tris[t]], b = id[tris[t + 1]], c = id[tris[t + 2]];
                used[tris[t]] = used[tris[t + 1]] = used[tris[t + 2]] = true;
                if (a == b || b == c || a == c) { degenerate++; continue; }
                foreach (var (x, y) in new[] { (a, b), (b, c), (c, a) })
                {
                    long key = ((long)Mathf.Min(x, y) << 32) | (uint)Mathf.Max(x, y);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            int boundary = edges.Values.Count(n => n == 1), nonManifold = edges.Values.Count(n => n > 2);
            int unused = used.Count(u => !u);
            return m.name + " (" + AssetDatabase.GetAssetPath(m) + ") v" + m.vertexCount + " sub" + m.subMeshCount + " idx" + idx +
                " sub0tris " + tris.Length / 3 + " welded " + weld.Count + " boundaryEdges " + boundary + " nonManifold " + nonManifold +
                " degenerate " + degenerate + " unusedVerts " + unused + " bounds " + m.bounds.size.ToString("F2");
        }
    }
}

