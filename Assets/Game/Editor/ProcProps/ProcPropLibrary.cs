using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Procedural nature props (pilot, 2026-10-13): flat-shaded low poly, colour in vertex colour (rgb) and baked
    /// AO in vertex alpha, rendered by "MassEngine/Battlefield Props 1024" with _VertexColor = 1.
    /// Every prefab carries a LODGroup whose level 1 = near mesh and level 2 = far mesh, which is exactly what
    /// <see cref="Decorator.Place"/> keeps. <see cref="CreateRemap"/> maps third-party pool prefabs to these by category.
    /// Assets are overwritten in place (GUIDs stay stable).
    /// </summary>
    public static class ProcPropLibrary
    {
        public const string Root = "Assets/Game/Content/ProcProps";
        public const string MaterialPath = Root + "/Materials/ProcProp_VertexColour.mat";
        public const string ReportPath = "outputs/ProcProps/library.json";
        public const string DumpPath = "outputs/ProcProps/library_dump.txt";

        sealed class Kind
        {
            public string category, prefix; public int count; public Func<int, bool, FlatMesh> build; public float aoRadius;
            public string remapFor; public float weight = 1f;   // which third-party category this kind may stand in for
        }

        static Kind K(string cat, string prefix, int count, Func<int, bool, FlatMesh> build, float ao, string remapFor = null, float weight = 1f) =>
            new Kind { category = cat, prefix = prefix, count = count, build = build, aoRadius = ao, remapFor = remapFor, weight = weight };

        static readonly Kind[] Kinds =
        {
            K("Broadleaf", "ProcTree_Broadleaf", 4, ProcPropMeshes.Broadleaf, .3f, "Broadleaf", .8f),
            K("Autumn",    "ProcTree_Autumn",    3, ProcPropMeshes.BroadleafAutumn, .3f, "Broadleaf", .2f),
            K("Birch",     "ProcTree_Birch",     3, ProcPropMeshes.Birch,     .3f, "Birch"),
            K("Pine",      "ProcTree_Pine",      4, ProcPropMeshes.Pine,      .25f, "Pine", .86f),
            K("Cypress",   "ProcTree_Cypress",   2, ProcPropMeshes.Cypress,   .25f, "Pine", .14f),
            K("Dead",      "ProcTree_Dead",      2, ProcPropMeshes.DeadTree,  .2f),
            K("Bush",      "ProcBush",           4, ProcPropMeshes.Bush,      .45f, "Bush"),
            K("Fern",      "ProcFern",           3, ProcPropMeshes.Fern,      .5f),
            K("Rock",      "ProcRock_Large",     6, ProcPropMeshes.RockLarge, .45f, "Rock"),
            K("RockTall",  "ProcRock_Tall",      3, ProcPropMeshes.RockTall,  .3f, "RockTall"),
            K("RockPile",  "ProcRock_Pile",      6, ProcPropMeshes.RockPile,  .5f, "RockPile"),
            K("Pebbles",   "ProcPebbles",        3, ProcPropMeshes.Pebbles,   .5f),
            K("Grass",     "ProcGrass",          3, ProcPropMeshes.Grass,     .5f, "Grass"),
            K("Flower",    "ProcFlowers",        3, ProcPropMeshes.Flowers,   .5f, "Flower"),
            K("FlowerPatch","ProcFlowerPatch",   3, ProcPropMeshes.FlowerPatch, .5f),
            K("Mushroom",  "ProcMushrooms",      3, ProcPropMeshes.Mushrooms, .5f),
            K("Stump",     "ProcStump",          2, ProcPropMeshes.Stump,     .45f, "Stump"),
            K("Logs",      "ProcLogs",           2, ProcPropMeshes.Logs,      .45f, "Logs"),
            K("FallenLog", "ProcLog_Fallen",     2, ProcPropMeshes.FallenLog, .45f),
            K("Mountain",  "ProcMountain",       3, ProcPropMeshes.Mountain,  0f, "Mountain"),
            // themed variants (round 3: snow / dry maps)
            K("PineSnow",  "ProcTree_PineSnow",  4, ProcPropMeshes.PineSnow,  .25f),
            K("RockSnow",  "ProcRock_Snow",      4, ProcPropMeshes.RockSnow,  .45f),
            K("BushSnow",  "ProcBush_Snow",      2, ProcPropMeshes.BushSnowKind, .45f),
            K("MountainSnow", "ProcMountain_Snow", 3, ProcPropMeshes.MountainSnow, 0f),
            K("BushDry",   "ProcBush_Dry",       3, ProcPropMeshes.BushDryKind, .45f),
            K("GrassDry",  "ProcGrass_Dry",      3, ProcPropMeshes.GrassDryKind, .5f),
            // 远景林冠团 (round 4: backdrop canopy clumps, far LOD only)
            K("ClumpGreen",    "ProcClump_Green",    3, ProcPropMeshes.ClumpGreen,    .3f),
            K("ClumpAutumn",   "ProcClump_Autumn",   3, ProcPropMeshes.ClumpAutumn,   .3f),
            K("ClumpPine",     "ProcClump_Pine",     3, ProcPropMeshes.ClumpPine,     .25f),
            K("ClumpPineSnow", "ProcClump_PineSnow", 3, ProcPropMeshes.ClumpPineSnow, .25f),
            K("ClumpDry",      "ProcClump_Dry",      3, ProcPropMeshes.ClumpDry,      .3f),
        };

        /// <summary>All variant prefab paths of one kind (e.g. "Mushroom"), for extra scatter passes.</summary>
        public static string[] Pool(string category)
        {
            var k = Kinds.First(x => x.category == category);
            return Enumerable.Range(0, k.count).Select(i => PrefabPath(k, i)).ToArray();
        }

        [Serializable] public sealed class Entry { public string name, category, prefab; public int nearTriangles, farTriangles; public float nearAoMean, nearAoMin; }
        [Serializable] public sealed class LibraryReport { public string time; public float seconds; public List<Entry> entries = new List<Entry>(); }

        static string PrefabPath(Kind k, int i) => Root + "/Prefabs/" + k.prefix + "_" + (i + 1).ToString("00") + ".prefab";

        [MenuItem("Tools/程序化道具/生成道具库（自然类试点）", false, 1)]
        static void MenuBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcProps] 请先退出 Play 模式。"); return; }
            Build();
        }

        /// <summary>(Re)builds meshes, material and prefabs; writes outputs/ProcProps/library.json + a near-LOD dump.</summary>
        public static void Build()
        {
            var t0 = DateTime.Now;
            Battlefield1024Generator.EnsureFolder(Root + "/Meshes");
            Battlefield1024Generator.EnsureFolder(Root + "/Prefabs");
            Battlefield1024Generator.EnsureFolder(Root + "/Materials");
            var mat = SaveMaterial();
            BuildGround();
            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            var report = new LibraryReport { time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            var dump = new StringBuilder();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                int n = 0, total = Kinds.Sum(k => k.count);
                foreach (var k in Kinds)
                    for (int i = 0; i < k.count; i++)
                    {
                        string name = k.prefix + "_" + (i + 1).ToString("00");
                        EditorUtility.DisplayProgressBar("程序化道具", name, n++ / (float)total);
                        int seed = 1000 + i * 7 + k.category.Length * 101;
                        var near = k.build(seed, false); var far = k.build(seed, true);
                        var nearAo = Ao(near, k.aoRadius); var farAo = Ao(far, k.aoRadius);
                        var nearMesh = SaveMesh(Root + "/Meshes/" + name + "_Near.asset", ToMesh(near, nearAo, linear, name + "_Near"));
                        var farMesh = SaveMesh(Root + "/Meshes/" + name + "_Far.asset", ToMesh(far, farAo, linear, name + "_Far"));
                        SavePrefab(PrefabPath(k, i), name, nearMesh, farMesh, mat, preview);
                        report.entries.Add(new Entry
                        {
                            name = name, category = k.category, prefab = PrefabPath(k, i), nearTriangles = near.Triangles, farTriangles = far.Triangles,
                            nearAoMean = nearAo.Length > 0 ? nearAo.Average() : 1, nearAoMin = nearAo.Length > 0 ? nearAo.Min() : 1,
                        });
                        if (i == 0) Dump(dump, name, near, nearAo);
                    }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); EditorUtility.ClearProgressBar(); }
            AssetDatabase.SaveAssets();
            report.seconds = (float)(DateTime.Now - t0).TotalSeconds;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            File.WriteAllText(DumpPath, dump.ToString());
            Debug.Log("[ProcProps] 道具库生成完成：" + report.entries.Count + " 个预制体，用时 " + report.seconds.ToString("F1") + " s → " + Root);
        }

        // ------------------------------------------------------------ remap (third-party pool path -> procedural prefab)
        /// <summary>Category of a third-party pool prefab by file name; null = keep the original.</summary>
        public static string Category(string path)
        {
            string f = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool rocky = f.Contains("rock") || f.Contains("stone");
            if (f.Contains("mountain")) return "Mountain";
            if (f.Contains("stump")) return "Stump";
            if (f.Contains("logs")) return "Logs";
            if (f.Contains("birch")) return "Birch";
            if (f.Contains("pine") || f.Contains("fir") || f.Contains("conifer")) return "Pine";
            if (f.Contains("menhir") || (rocky && f.Contains("tall"))) return "RockTall";
            if (f.Contains("pile") || (rocky && (f.Contains("small") || f.Contains("medium")))) return "RockPile";
            if (rocky) return "Rock";
            if (f.Contains("shrub") || f.Contains("leafs") || f.Contains("bush")) return "Bush";
            if (f.Contains("poppy") || f.Contains("flower")) return "Flower";
            if (f.Contains("grass") || f.Contains("foliage")) return "Grass";
            if (f.Contains("tree")) return "Broadleaf";
            return null;
        }

        /// <summary>
        /// Fresh remap for one generation run. Variant choice uses its own per-category counter (never the decorator
        /// RNG), so positions, rotations and heights stay identical to the original map.
        /// </summary>
        public static Func<string, bool, string> CreateRemap()
        {
            var counters = new Dictionary<string, int>();
            return (path, far) =>
            {
                if (path == null || path.StartsWith(Root + "/")) return null;   // already procedural
                string cat = Category(path);
                var kinds = cat == null ? null : Kinds.Where(x => x.remapFor == cat).ToArray();
                if (kinds == null || kinds.Length == 0) return null;
                counters.TryGetValue(cat, out int c); counters[cat] = c + 1;
                uint h = unchecked((uint)(c * 0x9E3779B1u) ^ (uint)(cat.Length * 0x85EBCA77u));
                h ^= h >> 15; h = unchecked(h * 0x2C1B3C6Du); h ^= h >> 12;
                float u = (h & 0xffff) / 65536f * kinds.Sum(x => x.weight);
                var k = kinds[kinds.Length - 1];
                foreach (var x in kinds) { if (u < x.weight) { k = x; break; } u -= x.weight; }
                return PrefabPath(k, (int)((h >> 16) % (uint)k.count));
            };
        }

        // ------------------------------------------------------------ helpers
        internal static float[] Ao(FlatMesh m, float radius)
        {
            int faces = m.Triangles;
            if (radius <= 0) return Enumerable.Repeat(1f, faces).ToArray();
            var s = new VertexAoBaker.Settings { rays = 64, radiusFraction = radius, biasFraction = .002f, groundPlane = true, groundWeight = .8f, contrast = 1f, minAo = .4f };
            return VertexAoBaker.ComputeFaceAo(m.V.ToArray(), m.T.ToArray(), s);
        }

        internal static Mesh ToMesh(FlatMesh m, float[] faceAo, bool linear, string name)
        {
            // Memory option 甲 (2026-10-13): 8-bit vertex colour (Color32, 4 B instead of 16 B per vertex).
            var cols = new List<Color32>(m.C.Count);
            for (int i = 0; i < m.C.Count; i++)
            {
                var c = m.C[i]; if (linear) c = c.linear;
                c.a = faceAo[i / 3];
                cols.Add((Color32)c);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(m.V); mesh.SetColors(cols); mesh.SetTriangles(m.T, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh SaveMesh(string path, Mesh mesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing); existing.name = mesh.name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Material SaveMaterial()
        {
            var m = new Material(Battlefield1024PropHaze.FindShader()) { name = "ProcProp_VertexColour", enableInstancing = true };
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_VertexColor", 1f);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing == null) { AssetDatabase.CreateAsset(m, MaterialPath); return m; }
            EditorUtility.CopySerialized(m, existing); existing.name = "ProcProp_VertexColour";
            Object.DestroyImmediate(m); EditorUtility.SetDirty(existing);
            return existing;
        }

        internal static void SavePrefab(string path, string name, Mesh near, Mesh far, Material mat, Scene preview)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, preview);
            Renderer Child(string n, Mesh mesh)
            {
                var go = new GameObject(n); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; return r;
            }
            var rn = Child("LOD_Near", near); var rf = Child("LOD_Far", far);
            var lod = root.AddComponent<LODGroup>();
            // Level 0 and 1 = near, level 2 = far (Decorator.Place keeps level 1 near / level 2 far).
            lod.SetLODs(new[] { new LOD(.5f, new[] { rn }), new LOD(.2f, new[] { rn }), new LOD(.02f, new[] { rf }) });
            lod.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        static void Dump(StringBuilder sb, string name, FlatMesh m, float[] ao)
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Append("o ").Append(name).Append('\n');
            for (int i = 0; i < m.V.Count; i++)
            {
                var v = m.V[i]; var c = m.C[i];
                sb.AppendFormat(inv, "v {0:F3} {1:F3} {2:F3} {3:F3} {4:F3} {5:F3} {6:F3}\n", v.x, v.y, v.z, c.r, c.g, c.b, ao[i / 3]);
            }
        }

        // ------------------------------------------------------------ procedural ground textures (512², seamless)
        public const string GroundDir = Root + "/Ground";
        /// <summary>Theme "" = green (森林丘陵·程序化道具); "Dry", "Autumn", "Snow" live in sub folders.</summary>
        public static string GroundPath(string name, string theme = "") =>
            (string.IsNullOrEmpty(theme) ? GroundDir : GroundDir + "/" + theme) + "/" + name + ".png";

        sealed class GroundPal
        {
            public string theme, grassLo, grassHi, fleck, meadowLo, meadowHi, dirtLo, dirtHi, pebLight, pebDark, rock, rockTop, forestLo, forestHi, litter;
            public string[] specks; public float speckChance, rockTopAmount = .35f;
        }

        static readonly GroundPal[] GroundThemes =
        {
            new GroundPal { theme = "", grassLo = "#62A743", grassHi = "#80C057", fleck = "#96CF68", meadowLo = "#7EBB52", meadowHi = "#9BCB62",
                specks = new[] { "#FFFFFF", "#FFD34E", "#FF9FB8" }, speckChance = .28f, dirtLo = "#A97C50", dirtHi = "#C29364", pebLight = "#D4B28A", pebDark = "#8E6A45",
                rock = "#9EA096", rockTop = "#7FA65A", forestLo = "#4B7E37", forestHi = "#5F9445", litter = "#7A6A3E" },
            new GroundPal { theme = "Dry", grassLo = "#B3A85C", grassHi = "#C9BC70", fleck = "#D8CD86", meadowLo = "#A9A55A", meadowHi = "#C2B868",
                specks = new[] { "#E8D9A0", "#C98F4E", "#F2E3B0" }, speckChance = .16f, dirtLo = "#B58A5A", dirtHi = "#C99B68", pebLight = "#DCC096", pebDark = "#94704A",
                rock = "#A89F8C", rockTop = "#9A9A5A", forestLo = "#8C8848", forestHi = "#A09A55", litter = "#7A5E36" },
            new GroundPal { theme = "Autumn", grassLo = "#8DAF4F", grassHi = "#A6BB5C", fleck = "#C3C46A", meadowLo = "#9DB255", meadowHi = "#B5BE62",
                specks = new[] { "#E8743B", "#F2C94C", "#C9452F" }, speckChance = .32f, dirtLo = "#A97C50", dirtHi = "#C29364", pebLight = "#D4B28A", pebDark = "#8E6A45",
                rock = "#9EA096", rockTop = "#A08A50", forestLo = "#8E6A3A", forestHi = "#A87A40", litter = "#C4542E" },
            new GroundPal { theme = "Snow", grassLo = "#E2E9EE", grassHi = "#F6F9FB", fleck = "#FFFFFF", meadowLo = "#E6ECF0", meadowHi = "#F7FAFC",
                specks = new[] { "#9DB28A", "#8A9E7C", "#B7C4AE" }, speckChance = .22f, dirtLo = "#A99F94", dirtHi = "#BDB4AA", pebLight = "#E8ECEF", pebDark = "#7C766F",
                rock = "#8E949B", rockTop = "#E8EEF2", rockTopAmount = .7f, forestLo = "#D8E1E6", forestHi = "#ECF1F4", litter = "#8A9480" },
        };

        static void BuildGround()
        {
            foreach (var g in GroundThemes) BuildGround(g);
        }

        static void BuildGround(GroundPal g)
        {
            Battlefield1024Generator.EnsureFolder(Path.GetDirectoryName(GroundPath("x", g.theme)).Replace('\\', '/'));
            Color H(string h) => ProcPropMeshes.Hex(h);
            int salt = g.theme.Length * 97;
            SaveGround(GroundPath("GrassA", g.theme), (u, v) =>
            {
                float n = PFbm(u, v, 4, 11 + salt, 4), f = PNoise(u, v, 48, 12 + salt);
                var c = Color.Lerp(H(g.grassLo), H(g.grassHi), n);
                return f > .74f ? Color.Lerp(c, H(g.fleck), .5f) : c;
            });
            SaveGround(GroundPath("GrassB", g.theme), (u, v) =>
            {
                float n = PFbm(u, v, 4, 21 + salt, 4);
                var c = Color.Lerp(H(g.meadowLo), H(g.meadowHi), n);
                var (d1, d2, id) = Voronoi(u, v, 22, 23 + salt);
                if (d1 < .09f && Hash01(id) < g.speckChance) return H(g.specks[(int)(Hash01(id * 7 + 1) * g.specks.Length) % g.specks.Length]);
                return c;
            });
            SaveGround(GroundPath("Dirt", g.theme), (u, v) =>
            {
                float n = PFbm(u, v, 4, 31 + salt, 4);
                var c = Color.Lerp(H(g.dirtLo), H(g.dirtHi), n);
                var (d1, d2, id) = Voronoi(u, v, 18, 33 + salt);
                if (d1 < .2f && Hash01(id) < .35f) return Hash01(id * 3 + 2) < .5f ? H(g.pebLight) : H(g.pebDark);
                return c;
            });
            SaveGround(GroundPath("Rock", g.theme), (u, v) =>
            {
                var (d1, d2, id) = Voronoi(u, v, 6, 41 + salt);
                var c = H(g.rock) * (.88f + .2f * Hash01(id));
                if (d2 - d1 < .05f) c *= .8f;
                float top = PFbm(u, v, 3, 43 + salt, 3);
                if (top > .6f) c = Color.Lerp(c, H(g.rockTop), g.rockTopAmount);
                c.a = 1; return c;
            });
            SaveGround(GroundPath("Forest", g.theme), (u, v) =>
            {
                float n = PFbm(u, v, 4, 51 + salt, 4), l = PNoise(u, v, 16, 52 + salt), sp = PNoise(u, v, 64, 53 + salt);
                var c = Color.Lerp(H(g.forestLo), H(g.forestHi), n);
                if (l > .64f) c = Color.Lerp(c, H(g.litter), .5f);
                if (sp > .8f) c *= .85f;
                c.a = 1; return c;
            });
        }

        static void SaveGround(string path, Func<float, float, Color> f)
        {
            const int N = 512;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) px[y * N + x] = f(x / (float)N, y / (float)N);
            var t = new Texture2D(N, N, TextureFormat.RGB24, false);
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        static float Hash01(int i)
        {
            uint x = unchecked((uint)i * 747796405u + 2891336453u);
            x = unchecked(((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u);
            return ((x >> 22) ^ x) / 4294967296f;
        }

        static float PNoise(float u, float v, int f, int s)
        {
            float x = u * f, y = v * f; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int X0 = ((x0 % f) + f) % f, Y0 = ((y0 % f) + f) % f, X1 = (X0 + 1) % f, Y1 = (Y0 + 1) % f;
            float G(int a, int b) => Hash01(a * 7919 + b * 104729 + s * 15485863);
            return Mathf.Lerp(Mathf.Lerp(G(X0, Y0), G(X1, Y0), fx), Mathf.Lerp(G(X0, Y1), G(X1, Y1), fx), fy);
        }

        static float PFbm(float u, float v, int f, int s, int oct)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < oct; i++) { sum += PNoise(u, v, f, s + i * 13) * amp; norm += amp; amp *= .5f; f *= 2; }
            return sum / norm;
        }

        /// <summary>Periodic Voronoi: nearest / second distance (cell units) and nearest cell id.</summary>
        static (float d1, float d2, int id) Voronoi(float u, float v, int f, int s)
        {
            float x = u * f, y = v * f; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float d1 = 9, d2 = 9; int id = 0;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j, wx = ((gx % f) + f) % f, wy = ((gy % f) + f) % f, cid = wx * 977 + wy * 131 + s * 7;
                float px = gx + Hash01(cid), py = gy + Hash01(cid + 5);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < d1) { d2 = d1; d1 = d; id = cid; } else if (d < d2) d2 = d;
            }
            return (d1, d2, id);
        }

        // ------------------------------------------------------------ comparison map
        // 2026-10-13: menu removed - 森林丘陵 itself is procedural now (ProcSkinMap); the duplicate map was deleted.
        static void MenuProcForest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcProps] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty) { Debug.LogError("[ProcProps] 场景 " + s.name + " 有未保存的修改，为避免丢失已取消生成。请先保存再运行。"); return; }
            }
            string restore = SceneManager.GetActiveScene().path;
            try
            {
                Build();
                var report = Battlefield1024Generator.Generate(new ForestHillsProcMap());
                Debug.Log("[ProcProps] 对比地图生成完成\n" + report.Summary());
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                EditorUtility.ClearProgressBar();
                Battlefield1024Generator.RepairCatalogAndBuildSettings();
                if (!string.IsNullOrEmpty(restore) && File.Exists(restore) && SceneManager.GetActiveScene().path != restore)
                    EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
            }
        }
    }
}
