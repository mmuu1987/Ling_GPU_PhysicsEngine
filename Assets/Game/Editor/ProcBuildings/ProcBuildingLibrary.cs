using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Procedural building library (2026-10-15, user-approved 档1: decoration only, same style as the procedural props,
    /// village / fort / ruin themes, 2048 maps). Same pipeline as <see cref="ProcPropLibrary"/>: flat-shaded vertex-colour
    /// meshes with per-face AO in vertex alpha, the shared ProcProp material, prefabs with a LODGroup (level 1 near,
    /// level 2 far). One set per palette (Green / Dry / Snow). Assets are overwritten in place (GUIDs stay stable).
    /// </summary>
    public static class ProcBuildingLibrary
    {
        public const string Root = "Assets/Game/Content/ProcBuildings";
        public const string OutDir = "outputs/ProcBuildings";
        const float AoRadius = .25f;

        public static string Name(BK k, int v, BPal p) => "Bld_" + p.Name + "_" + k + "_" + (v + 1).ToString("00");
        public static string PrefabPath(BK k, int v, BPal p) => Root + "/Prefabs/" + Name(k, v, p) + ".prefab";

        static IEnumerable<BK> Kinds => Enum.GetValues(typeof(BK)).Cast<BK>();

        /// <summary>True when every prefab of every palette exists.</summary>
        public static bool Ready => ProcBuildingMeshes.Palettes.All(p => Kinds.All(k => Enumerable.Range(0, ProcBuildingMeshes.Variants(k)).All(v => File.Exists(PrefabPath(k, v, p)))));

        [Serializable] public sealed class Entry { public string name; public int hdTriangles, nearTriangles, farTriangles; public float width, depth, height, aoMean; }
        [Serializable] public sealed class Report { public string time; public float seconds; public int count, maxNearTriangles, maxHdTriangles; public List<Entry> entries = new List<Entry>(); }

        [MenuItem("Tools/程序化建筑/生成建筑库", false, 1)]
        static void MenuBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcBuildings] 请先退出 Play 模式。"); return; }
            Build();
        }

        public static void Build()
        {
            var t0 = DateTime.Now;
            Battlefield1024Generator.EnsureFolder(Root + "/Meshes");
            Battlefield1024Generator.EnsureFolder(Root + "/Prefabs");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ProcPropLibrary.MaterialPath);
            if (mat == null) { ProcPropLibrary.Build(); mat = AssetDatabase.LoadAssetAtPath<Material>(ProcPropLibrary.MaterialPath); }
            if (mat == null) throw new InvalidOperationException("缺少道具材质 " + ProcPropLibrary.MaterialPath);
            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            var report = new Report { time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                int n = 0, total = ProcBuildingMeshes.Palettes.Length * Kinds.Sum(k => ProcBuildingMeshes.Variants(k));
                foreach (var p in ProcBuildingMeshes.Palettes)
                    foreach (var k in Kinds)
                        for (int v = 0; v < ProcBuildingMeshes.Variants(k); v++)
                        {
                            string name = Name(k, v, p);
                            EditorUtility.DisplayProgressBar("程序化建筑", name, n++ / (float)total);
                            var near = ProcBuildingMeshes.Build(k, v, p, false);
                            var far = ProcBuildingMeshes.Build(k, v, p, true);
                            var nearAo = ProcPropLibrary.Ao(near, AoRadius);
                            var farAo = Enumerable.Repeat(1f, far.Triangles).ToArray();
                            var nearMesh = ProcPropLibrary.SaveMesh(Root + "/Meshes/" + name + "_Near.asset", ProcPropLibrary.ToMesh(near, nearAo, linear, name + "_Near"));
                            var farMesh = ProcPropLibrary.SaveMesh(Root + "/Meshes/" + name + "_Far.asset", ProcPropLibrary.ToMesh(far, farAo, linear, name + "_Far"));
                            int hdTris = 0;
                            if (ProcBuildingMeshes.HasHD(k))
                            {
                                var hd = ProcBuildingMeshes.BuildHD(k, v, p); hdTris = hd.Triangles;
                                var hdMesh = ProcPropLibrary.SaveMesh(Root + "/Meshes/" + name + "_HD.asset", ProcPropLibrary.ToMesh(hd, ProcPropLibrary.Ao(hd, AoRadius), linear, name + "_HD"));
                                SavePrefab3(PrefabPath(k, v, p), name, hdMesh, nearMesh, farMesh, mat, preview);
                            }
                            else ProcPropLibrary.SavePrefab(PrefabPath(k, v, p), name, nearMesh, farMesh, mat, preview);
                            var fp = ProcBuildingMeshes.Footprint(k, v);
                            report.entries.Add(new Entry
                            {
                                name = name, hdTriangles = hdTris, nearTriangles = near.Triangles, farTriangles = far.Triangles, width = fp.x, depth = fp.y,
                                height = nearMesh.bounds.max.y, aoMean = nearAo.Length > 0 ? nearAo.Average() : 1f,
                            });
                        }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            report.seconds = (float)(DateTime.Now - t0).TotalSeconds;
            report.count = report.entries.Count;
            report.maxNearTriangles = report.entries.Max(e => e.nearTriangles);
            report.maxHdTriangles = report.entries.Max(e => e.hdTriangles);
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(OutDir + "/library.json", JsonUtility.ToJson(report, true));
            Debug.Log("[ProcBuildings] 建筑库生成完成：" + report.count + " 个预制体，最多 " + report.maxNearTriangles + " 面，用时 " + report.seconds.ToString("F1") + " s → " + Root);
        }

        /// <summary>2026-10-15 detailed buildings: LOD_HD (close), LOD_Near (old detailed mesh, mid distance), LOD_Far.
        /// Battlefield1024Decorator.PlaceAt keeps HD + Near as a two-level LODGroup and drops Far.</summary>
        static void SavePrefab3(string path, string name, Mesh hd, Mesh near, Mesh far, Material mat, UnityEngine.SceneManagement.Scene preview)
        {
            var root = new GameObject(name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            Renderer Child(string n, Mesh mesh)
            {
                var go = new GameObject(n); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; return r;
            }
            var rh = Child("LOD_HD", hd); var rn = Child("LOD_Near", near); var rf = Child("LOD_Far", far);
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.25f, new[] { rh }), new LOD(.05f, new[] { rn }), new LOD(.01f, new[] { rf }) });
            lod.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ showcase renders (for review)
        [MenuItem("Tools/程序化建筑/建筑库展示图", false, 20)]
        static void MenuShowcase()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcBuildings] 请先退出 Play 模式。"); return; }
            if (!Ready) Build();
            Showcase();
        }

        static readonly BK[][] Groups =
        {
            new[] { BK.Cottage, BK.TownHouse, BK.Longhouse, BK.Barn, BK.Windmill, BK.Church, BK.Inn },
            new[] { BK.WallSeg, BK.Tower, BK.Gatehouse, BK.Keep, BK.Tent, BK.Watchtower },
            new[] { BK.Well, BK.Stall, BK.Haystack, BK.Cart, BK.Crates, BK.Woodpile, BK.Fence, BK.StoneFence, BK.RuinHouse, BK.RuinTower, BK.RuinWall, BK.Rubble },
        };
        static readonly string[] GroupNames = { "houses", "fort", "props_ruins" };

        public static void Showcase()
        {
            Directory.CreateDirectory(OutDir);
            foreach (var p in ProcBuildingMeshes.Palettes)
                for (int g = 0; g < Groups.Length; g++)
                {
                    var items = Groups[g].SelectMany(k => Enumerable.Range(0, ProcBuildingMeshes.Variants(k)).Select(v => (k, v))).ToList();
                    RenderGroup(p, items, OutDir + "/showcase_" + p.Name + "_" + GroupNames[g] + ".jpg", g == 2 ? 6 : 5);
                }
            Debug.Log("[ProcBuildings] 展示图完成 → " + OutDir);
        }

        static void RenderGroup(BPal p, List<(BK k, int v)> items, string file, int columns)
        {
            const int W = 1920, Hh = 1080;
            var pru = new PreviewRenderUtility();
            var made = new List<GameObject>();
            try
            {
                float cell = items.Max(it => Mathf.Max(ProcBuildingMeshes.Footprint(it.k, it.v).x, ProcBuildingMeshes.Footprint(it.k, it.v).y)) + 4f;
                int rows = Mathf.CeilToInt(items.Count / (float)columns);
                var ground = EditorUtility.CreateGameObjectWithHideFlags("ground", HideFlags.HideAndDontSave);
                var gm = new Mesh();
                float gx = columns * cell * .5f + cell, gz = rows * cell * .5f + cell;
                gm.SetVertices(new List<Vector3> { new Vector3(-gx, -.02f, -gz), new Vector3(gx, -.02f, -gz), new Vector3(gx, -.02f, gz), new Vector3(-gx, -.02f, gz) });
                Color gc = p.IsSnow ? new Color(.86f, .9f, .93f) : p.IsDry ? new Color(.78f, .68f, .5f) : new Color(.45f, .64f, .33f);
                if (PlayerSettings.colorSpace == ColorSpace.Linear) gc = gc.linear;
                gm.SetColors(new List<Color32> { gc, gc, gc, gc }); gm.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); gm.RecalculateNormals();
                ground.AddComponent<MeshFilter>().sharedMesh = gm;
                ground.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ProcPropLibrary.MaterialPath);
                pru.AddSingleGO(ground); made.Add(ground);
                for (int i = 0; i < items.Count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(items[i].k, items[i].v, p));
                    if (prefab == null) continue;
                    var go = pru.InstantiatePrefabInScene(prefab);
                    foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                    int cx = i % columns, cz = i / columns;
                    go.transform.position = new Vector3((cx - (columns - 1) * .5f) * cell, 0f, ((rows - 1) * .5f - cz) * cell);
                    
                }
                float span = Mathf.Max(columns, rows * 1.6f) * cell;
                var cam = pru.camera;
                cam.fieldOfView = 30f; cam.nearClipPlane = 1f; cam.farClipPlane = 3000f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.62f, .74f, .84f);
                Vector3 dir = new Vector3(-.35f, .75f, -1f).normalized;
                cam.transform.position = dir * span * 1.75f + new Vector3(0, 0, -cell * .3f);
                cam.transform.LookAt(new Vector3(0, 2f, -cell * .15f));
                pru.lights[0].intensity = 1.25f; pru.lights[0].transform.rotation = Quaternion.Euler(48f, -35f, 0f); pru.lights[0].color = new Color(1f, .97f, .9f);
                pru.lights[1].intensity = .35f;
                pru.ambientColor = new Color(.42f, .45f, .5f);
                pru.BeginStaticPreview(new Rect(0, 0, W, Hh));
                pru.Render(true);
                var tex = pru.EndStaticPreview();
                File.WriteAllBytes(file, tex.EncodeToJPG(88));
                Object.DestroyImmediate(tex);
            }
            finally
            {
                if (made.Count > 0 && made[0] != null) Object.DestroyImmediate(made[0]);
                pru.Cleanup();
            }
        }
    }
}
