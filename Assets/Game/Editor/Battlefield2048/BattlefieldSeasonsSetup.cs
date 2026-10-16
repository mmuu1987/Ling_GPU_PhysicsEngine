using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Seasons (2026-10-16, plan B): installs <see cref="BattlefieldSeasonSwitcher"/> into the 2048 scenes.
    /// Ground textures come from the procedural library (ProcProps/Ground/&lt;theme&gt;), props swap to the same variant index of
    /// the season's category (same position / height / yaw). Props lose the batching-static flag; the switcher batches at
    /// runtime after the swap. Terrain, obstacles and combat are untouched.
    /// </summary>
    public static class BattlefieldSeasonsSetup
    {
        const string Menu = Battlefield2048Setup.MenuRoot + "季节切换/";
        const string GroundName = "Plan B - Shared Render And Navigation Surface";
        const string BackdropName = "Visual Only Backdrop - No navigation or colliders";
        const string LogPath = "Logs/Seasons/setup.txt";
        static readonly string[] Themes = { "", "Autumn", "Snow", "Dry" };
        static readonly Color[] Hazes = { new Color(.38f, .57f, .62f, 1), new Color(.5f, .54f, .56f, 1), new Color(.66f, .73f, .78f, 1), new Color(.6f, .56f, .47f, 1) };
        const string Hide = "-";

        /// <summary>Folder → native season (Hills green, RidgePass autumn, CliffPlateau snow, CanyonMesa dry).</summary>
        public static readonly Dictionary<string, int> Native = new Dictionary<string, int>
        {
            { "Hills2048", 0 }, { "RidgePass2048", 1 }, { "CliffPlateau2048", 2 }, { "CanyonMesa2048", 3 },
        };

        // category → category per season (missing = keep; "-" = hide)
        static readonly Dictionary<string, string>[] Swaps =
        {
            new Dictionary<string, string> // 绿地
            {
                { "Autumn", "Broadleaf" }, { "PineSnow", "Pine" }, { "BushSnow", "Bush" }, { "BushDry", "Bush" }, { "GrassDry", "Grass" },
                { "RockSnow", "Rock" }, { "MountainSnow", "Mountain" }, { "Dead", "Broadleaf" }, { "Cypress", "Pine" },
                { "ClumpAutumn", "ClumpGreen" }, { "ClumpDry", "ClumpGreen" }, { "ClumpPineSnow", "ClumpPine" },
            },
            new Dictionary<string, string> // 秋天
            {
                { "Broadleaf", "Autumn" }, { "PineSnow", "Pine" }, { "BushSnow", "Bush" }, { "BushDry", "Bush" }, { "GrassDry", "Grass" },
                { "RockSnow", "Rock" }, { "MountainSnow", "Mountain" }, { "Cypress", "Pine" },
                { "ClumpGreen", "ClumpAutumn" }, { "ClumpDry", "ClumpAutumn" }, { "ClumpPineSnow", "ClumpPine" },
            },
            new Dictionary<string, string> // 雪地
            {
                { "Broadleaf", "PineSnow" }, { "Birch", "PineSnow" }, { "Autumn", "PineSnow" }, { "Pine", "PineSnow" }, { "Cypress", "PineSnow" },
                { "Bush", "BushSnow" }, { "BushDry", "BushSnow" }, { "Fern", "BushSnow" },
                { "Grass", Hide }, { "GrassDry", Hide }, { "Flower", Hide }, { "FlowerPatch", Hide }, { "Mushroom", Hide },
                { "Rock", "RockSnow" }, { "Mountain", "MountainSnow" },
                { "ClumpGreen", "ClumpPineSnow" }, { "ClumpAutumn", "ClumpPineSnow" }, { "ClumpPine", "ClumpPineSnow" }, { "ClumpDry", "ClumpPineSnow" },
            },
            new Dictionary<string, string> // 荒漠
            {
                { "Broadleaf", "Cypress|Dead" }, { "Birch", "Dead" }, { "Autumn", "Dead" }, { "PineSnow", "Pine" },
                { "Bush", "BushDry" }, { "BushSnow", "BushDry" }, { "Fern", "BushDry" },
                { "Grass", "GrassDry" }, { "Flower", "GrassDry" }, { "FlowerPatch", "GrassDry" }, { "Mushroom", Hide },
                { "RockSnow", "Rock" }, { "MountainSnow", "Mountain" },
                { "ClumpGreen", "ClumpDry" }, { "ClumpAutumn", "ClumpDry" }, { "ClumpPineSnow", "ClumpPine" },
            },
        };

        static readonly string[] Categories =
        {
            "Broadleaf", "Autumn", "Birch", "Pine", "Cypress", "Dead", "Bush", "Fern", "Rock", "RockTall", "RockPile", "Pebbles", "Grass", "Flower",
            "FlowerPatch", "Mushroom", "Stump", "Logs", "FallenLog", "Mountain", "PineSnow", "RockSnow", "BushSnow", "MountainSnow", "BushDry", "GrassDry",
            "ClumpGreen", "ClumpAutumn", "ClumpPine", "ClumpPineSnow", "ClumpDry",
        };

        struct Key { public string cat; public int variant, level, slot; }

        [MenuItem(Menu + "装到 丘陵会战（试点）", false, 50)]
        static void MenuHills() => InstallFromMenu("Hills2048");

        [MenuItem(Menu + "装到全部 4 张 2048 地图", false, 51)]
        static void MenuAll() => InstallFromMenu(Native.Keys.ToArray());

        static void InstallFromMenu(params string[] folders)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Seasons] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[Seasons] 有未保存的场景，已取消。"); return; }
            string restore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var log = new StringBuilder();
            foreach (var f in folders)
            {
                try { log.AppendLine(Install(f)); }
                catch (System.Exception e) { log.AppendLine("FAIL " + f + "：" + e.Message); Debug.LogException(e); }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.WriteAllText(LogPath, log.ToString());
            Debug.Log("[Seasons] " + log);
            if (!string.IsNullOrEmpty(restore) && File.Exists(restore)) EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
        }

        /// <summary>Installs / refreshes the switcher in one 2048 scene (idempotent). Returns a one-line summary.</summary>
        public static string Install(string folder)
        {
            int native = Native.TryGetValue(folder, out var n) ? n : 0;
            string scenePath = "Assets/Game/Content/Battlefields/Maps2048/" + folder + "/" + folder + ".unity";
            if (!File.Exists(scenePath)) throw new System.Exception("缺少场景 " + scenePath);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var scenery = roots.FirstOrDefault(g => g.name == Battlefield1024Generator.SceneryName);
            if (scenery == null) throw new System.Exception("场景里缺少景物根节点");
            var grounds = roots.Where(g => g.name == GroundName || g.name == BackdropName).Select(g => g.GetComponent<Renderer>()).Where(r => r != null).ToArray();
            if (grounds.Length == 0) throw new System.Exception("场景里缺少地面");

            // library meshes
            var keyOf = new Dictionary<Mesh, Key>();
            var meshes = new Dictionary<string, List<Mesh>[]>();   // "cat#variant" → per level
            var variants = new Dictionary<string, int>();
            foreach (var cat in Categories)
            {
                string[] pool;
                try { pool = ProcPropLibrary.Pool(cat); } catch { continue; }
                variants[cat] = pool.Length;
                for (int v = 0; v < pool.Length; v++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pool[v]);
                    if (prefab == null) continue;
                    var levels = Levels(prefab);
                    meshes[cat + "#" + v] = levels;
                    for (int l = 0; l < levels.Length; l++)
                        for (int s = 0; s < levels[l].Count; s++)
                            if (!keyOf.ContainsKey(levels[l][s])) keyOf[levels[l][s]] = new Key { cat = cat, variant = v, level = l, slot = s };
                }
            }

            // building meshes per palette (same kind / variant / LOD level / slot)
            var palOfSeason = new[] { ProcBuildingMeshes.PaletteFor(""), ProcBuildingMeshes.PaletteFor(""), ProcBuildingMeshes.PaletteFor("Snow"), ProcBuildingMeshes.PaletteFor("Dry") };
            var bKey = new Dictionary<Mesh, (BK k, int v, int level, int slot, BPal pal)>();
            var bMeshes = new Dictionary<(BK, int, BPal), List<Mesh>[]>();
            foreach (var pal in ProcBuildingMeshes.Palettes)
                foreach (BK k in System.Enum.GetValues(typeof(BK)))
                    for (int v = 0; v < ProcBuildingMeshes.Variants(k); v++)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProcBuildingLibrary.PrefabPath(k, v, pal));
                        if (prefab == null) continue;
                        var levels = BuildingLevels(prefab);
                        bMeshes[(k, v, pal)] = levels;
                        for (int l = 0; l < levels.Length; l++)
                            for (int s2 = 0; s2 < levels[l].Count; s2++)
                                if (!bKey.ContainsKey(levels[l][s2])) bKey[levels[l][s2]] = (k, v, l, s2, pal);
                    }

            // meshes used in the scene; drop batching-static so they can be swapped at runtime
            var used = new HashSet<Mesh>(); var usedBuildings = new HashSet<Mesh>();
            int unknown = 0, flags = 0;
            foreach (var f in scenery.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                if (keyOf.ContainsKey(f.sharedMesh)) used.Add(f.sharedMesh);
                else if (bKey.ContainsKey(f.sharedMesh)) usedBuildings.Add(f.sharedMesh);
                else unknown++;
            }
            foreach (var r in scenery.GetComponentsInChildren<Renderer>(true))
            {
                var fl = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                if ((fl & StaticEditorFlags.BatchingStatic) != 0) { GameObjectUtility.SetStaticEditorFlags(r.gameObject, fl & ~StaticEditorFlags.BatchingStatic); flags++; }
            }

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(scenery);   // older installs (switcher class lived in a mismatched file)
            // always a fresh component: an early install referenced a scene-local script object (class lived in a mismatched file)
            foreach (var old in scenery.GetComponents<BattlefieldSeasonSwitcher>()) Object.DestroyImmediate(old);
            var switcher = scenery.AddComponent<BattlefieldSeasonSwitcher>();
            switcher.nativeSeason = native;
            switcher.decorationRoot = scenery.transform;
            switcher.groundRenderers = grounds;
            switcher.seasons = new BattlefieldSeasonSwitcher.SeasonLook[BattlefieldSeasons.Count];
            var summary = new StringBuilder(folder + "：原样=" + BattlefieldSeasons.NameOf(native) + "，景物模型 " + used.Count + " 种（库外 " + unknown + " 个物体不换），去掉静态合批标记 " + flags);
            for (int s = 0; s < BattlefieldSeasons.Count; s++)
            {
                var look = new BattlefieldSeasonSwitcher.SeasonLook
                {
                    grassA = Tex("GrassA", s), grassB = Tex("GrassB", s), dirt = Tex("Dirt", s), rock = Tex("Rock", s), forest = Tex("Forest", s), haze = Hazes[s],
                };
                var from = new List<Mesh>(); var to = new List<Mesh>(); int hidden = 0;
                if (s != native)
                    foreach (var m in used)
                    {
                        var k = keyOf[m];
                        if (!Swaps[s].TryGetValue(k.cat, out var target) || target == k.cat) continue;
                        if (target == Hide) { from.Add(m); to.Add(null); hidden++; continue; }
                        if (target.Contains("|")) { var options = target.Split('|'); target = options[k.variant % options.Length]; }   // mixed (e.g. 荒漠 broadleaf → cypress / dead)
                        if (!variants.TryGetValue(target, out int count) || count == 0) continue;
                        if (!meshes.TryGetValue(target + "#" + (k.variant % count), out var levels) || levels.Length == 0) continue;
                        var lv = levels[Mathf.Min(k.level, levels.Length - 1)];
                        if (lv.Count == 0) continue;
                        from.Add(m); to.Add(lv[k.slot % lv.Count]);
                    }
                look.from = from.ToArray(); look.to = to.ToArray();
                var bf = new List<Mesh>(); var bt = new List<Mesh>();
                foreach (var m in usedBuildings)
                {
                    var bk = bKey[m];
                    if (bk.pal == palOfSeason[s]) continue;
                    if (!bMeshes.TryGetValue((bk.k, bk.v, palOfSeason[s]), out var lv) || bk.level >= lv.Length || lv[bk.level].Count == 0) continue;
                    bf.Add(m); bt.Add(lv[bk.level][bk.slot % lv[bk.level].Count]);
                }
                look.buildingFrom = bf.ToArray(); look.buildingTo = bt.ToArray();
                summary.Append("；" + WarSandboxSeasonsShort(s) + "房 " + bf.Count);
                switcher.seasons[s] = look;
                summary.Append("；" + BattlefieldSeasons.Names[s] + " 换 " + (from.Count - hidden) + " 隐 " + hidden);
            }
            EditorUtility.SetDirty(switcher);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("保存场景失败");
            return summary.ToString();
        }

        static string WarSandboxSeasonsShort(int s) => BattlefieldSeasons.Names[s];

        /// <summary>Building prefab meshes per child name level (LOD_HD / LOD_Near / LOD_Far, else all filters).</summary>
        static List<Mesh>[] BuildingLevels(GameObject prefab)
        {
            var names = new[] { "LOD_HD", "LOD_Near", "LOD_Far" };
            var result = new List<Mesh>[names.Length + 1];
            for (int i = 0; i < names.Length; i++)
            {
                var t = prefab.transform.Find(names[i]);
                result[i] = t != null ? t.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null).ToList() : new List<Mesh>();
            }
            result[names.Length] = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => !names.Contains(f.name) && (f.transform.parent == null || !names.Contains(f.transform.parent.name)))
                .Select(f => f.sharedMesh).Where(m => m != null).ToList();
            return result;
        }

        static Texture Tex(string name, int season) => AssetDatabase.LoadAssetAtPath<Texture>(ProcPropLibrary.GroundPath(name, Themes[season]));

        /// <summary>Meshes per authored LOD level (level 0 = everything when the prefab has no LODGroup).</summary>
        static List<Mesh>[] Levels(GameObject prefab)
        {
            var group = prefab.GetComponentInChildren<LODGroup>(true);
            if (group == null)
                return new[] { prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Where(m => m != null).ToList() };
            return group.GetLODs().Select(l => l.renderers.Where(r => r != null).Select(r => r.GetComponent<MeshFilter>())
                .Where(f => f != null && f.sharedMesh != null).Select(f => f.sharedMesh).ToList()).ToArray();
        }
    }
}
