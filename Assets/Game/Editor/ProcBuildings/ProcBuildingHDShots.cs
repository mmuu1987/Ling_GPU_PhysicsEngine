using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>2026-10-15 review renders of the detailed (HD) sample buildings: no assets written, preview scene only.</summary>
    public static class ProcBuildingHDShots
    {
        const string Out = ProcBuildingLibrary.OutDir + "/hd";

        sealed class Item { public FlatMesh m; public Vector3 pos; public float yaw; }
        static readonly BK[][] Grids =
        {
            new[] { BK.Cottage, BK.Longhouse, BK.Barn, BK.Windmill, BK.Inn },
            new[] { BK.WallSeg, BK.Tower, BK.Watchtower, BK.Tent },
            new[] { BK.RuinHouse, BK.RuinTower, BK.RuinWall },
        };
        static readonly string[] GridNames = { "village", "fort", "ruins" };

        [MenuItem("Tools/程序化建筑/精细样板图", false, 30)]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcBuildings] 请先退出 Play 模式。"); return; }
            Run();
        }

        public static void Run()
        {
            Directory.CreateDirectory(Out);
            var log = new StringBuilder();
            FlatMesh HD(BK k, int v, BPal p)
            {
                var m = ProcBuildingMeshes.BuildHD(k, v, p);
                log.AppendLine(p.Name + " " + k + " " + v + "  HD " + m.Triangles + "  old " + ProcBuildingMeshes.Build(k, v, p, false).Triangles);
                return m;
            }
            foreach (var p in ProcBuildingMeshes.Palettes)
            {
                var row = new List<Item>(); float x = 0f;
                for (int v = 0; v < 4; v++)
                {
                    var fp = ProcBuildingMeshes.Footprint(BK.TownHouse, v);
                    row.Add(new Item { m = HD(BK.TownHouse, v, p), pos = new Vector3(x + fp.x * .5f, 0, 0) });
                    x += fp.x + (v == 2 ? 3f : 1.2f);
                }
                foreach (var it in row) it.pos.x -= x * .5f;
                Render(row, p, Out + "/hd_townhouses_" + p.Name + ".jpg", new Vector3(-.3f, .32f, -1f), .52f);
                if (p.Name != "Green") { Render(new List<Item> { new Item { m = HD(BK.Church, 0, p) } }, p, Out + "/hd_church_" + p.Name + ".jpg", new Vector3(-.85f, .45f, -1f), .8f); continue; }
                Render(new List<Item> { new Item { m = HD(BK.Church, 0, p) } }, p, Out + "/hd_church_Green.jpg", new Vector3(-.85f, .4f, -1f), .62f);
                Render(new List<Item> { new Item { m = HD(BK.Church, 1, p), yaw = 180f } }, p, Out + "/hd_church_back_Green.jpg", new Vector3(-.85f, .45f, -1f), .8f);
                Render(new List<Item> { new Item { m = HD(BK.Keep, 0, p), pos = new Vector3(-9.5f, 0, 0) }, new Item { m = HD(BK.Keep, 1, p), pos = new Vector3(10f, 0, 0) } },
                    p, Out + "/hd_keeps_Green.jpg", new Vector3(-.4f, .4f, -1f), .62f);
                Render(new List<Item> { new Item { m = HD(BK.Gatehouse, 0, p) } }, p, Out + "/hd_gatehouse_Green.jpg", new Vector3(-.55f, .3f, -1f), .82f);
                // before / after
                Render(new List<Item>
                {
                    new Item { m = ProcBuildingMeshes.Build(BK.TownHouse, 1, p, false), pos = new Vector3(-5f, 0, 0) },
                    new Item { m = ProcBuildingMeshes.BuildHD(BK.TownHouse, 1, p), pos = new Vector3(5f, 0, 0) },
                }, p, Out + "/hd_compare_house_Green.jpg", new Vector3(-.2f, .3f, -1f), .8f);
            }
            foreach (var p in ProcBuildingMeshes.Palettes)
                for (int gi = 0; gi < Grids.Length; gi++)
                {
                    var items = new List<Item>(); float x = 0f, z = 0f, rowD = 0f; int col = 0;
                    foreach (var k in Grids[gi])
                        for (int v = 0; v < ProcBuildingMeshes.Variants(k); v++)
                        {
                            var fp = ProcBuildingMeshes.Footprint(k, v);
                            if (col == 6) { col = 0; x = 0f; z += rowD + 3f; rowD = 0f; }
                            items.Add(new Item { m = HD(k, v, p), pos = new Vector3(x + fp.x * .5f, 0, z + fp.y * .5f) });
                            x += fp.x + 3f; rowD = Mathf.Max(rowD, fp.y); col++;
                        }
                    float maxX = items.Max(i => i.pos.x), maxZ = items.Max(i => i.pos.z);
                    foreach (var it in items) { it.pos.x -= maxX * .5f; it.pos.z = maxZ - it.pos.z - maxZ * .5f; }
                    Render(items, p, Out + "/hd_" + GridNames[gi] + "_" + p.Name + ".jpg", new Vector3(-.3f, .55f, -1f), .62f);
                }
            File.WriteAllText(Out + "/hd_triangles.txt", log.ToString());
            Debug.Log("[ProcBuildings] 精细样板图完成 → " + Out);
        }

        static void Render(List<Item> items, BPal p, string file, Vector3 viewDir, float tight)
        {
            const int W = 1600, Hh = 1000;
            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ProcPropLibrary.MaterialPath);
            var pru = new PreviewRenderUtility();
            var made = new List<Object>();
            try
            {
                var bounds = new Bounds(Vector3.zero, Vector3.zero); bool first = true;
                foreach (var it in items)
                {
                    var mesh = ProcPropLibrary.ToMesh(it.m, ProcPropLibrary.Ao(it.m, .25f), linear, "hd");
                    var go = EditorUtility.CreateGameObjectWithHideFlags("hd", HideFlags.HideAndDontSave);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    go.transform.SetPositionAndRotation(it.pos, Quaternion.Euler(0, it.yaw, 0));
                    pru.AddSingleGO(go); made.Add(go); made.Add(mesh);
                    var wb = new Bounds(go.transform.TransformPoint(mesh.bounds.center), Vector3.zero);
                    foreach (var c in Corners(mesh.bounds)) wb.Encapsulate(go.transform.TransformPoint(c));
                    if (first) { bounds = wb; first = false; } else bounds.Encapsulate(wb);
                }
                // ground
                var ground = EditorUtility.CreateGameObjectWithHideFlags("ground", HideFlags.HideAndDontSave);
                var gm = new Mesh(); float g = 400f;
                gm.SetVertices(new List<Vector3> { new Vector3(-g, -.02f, -g), new Vector3(g, -.02f, -g), new Vector3(g, -.02f, g), new Vector3(-g, -.02f, g) });
                Color gc = p.IsSnow ? new Color(.86f, .9f, .93f) : p.IsDry ? new Color(.78f, .68f, .5f) : new Color(.45f, .64f, .33f);
                if (linear) gc = gc.linear;
                gm.SetColors(new List<Color32> { gc, gc, gc, gc }); gm.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); gm.RecalculateNormals();
                ground.AddComponent<MeshFilter>().sharedMesh = gm;
                ground.AddComponent<MeshRenderer>().sharedMaterial = mat;
                pru.AddSingleGO(ground); made.Add(ground); made.Add(gm);

                var cam = pru.camera;
                cam.fieldOfView = 30f; cam.nearClipPlane = .5f; cam.farClipPlane = 2000f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.62f, .74f, .84f);
                float radius = bounds.extents.magnitude;
                float dist = radius / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * tight;
                Vector3 target = bounds.center - Vector3.up * bounds.extents.y * .12f;
                cam.transform.position = target + viewDir.normalized * dist;
                cam.transform.LookAt(target);
                pru.lights[0].intensity = 1.25f; pru.lights[0].transform.rotation = Quaternion.Euler(42f, 25f, 0f); pru.lights[0].color = new Color(1f, .97f, .9f);
                pru.lights[1].intensity = .35f;
                pru.ambientColor = new Color(.42f, .45f, .5f);
                pru.BeginStaticPreview(new Rect(0, 0, W, Hh));
                pru.Render(true);
                var tex = pru.EndStaticPreview();
                File.WriteAllBytes(file, tex.EncodeToJPG(90));
                Object.DestroyImmediate(tex);
            }
            finally
            {
                foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
                pru.Cleanup();
            }
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }
    }
}
