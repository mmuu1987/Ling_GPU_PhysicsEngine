using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 原版地图换程序化素材 (approved 2026-10-13 "原来的地图换程序化素材"): wraps an original map (翠绿平原 / 森林丘陵) with the
    /// SAME Id / Folder / name, terrain, obstacles, splat and seed, but every prop is procedural (remapped before the
    /// third-party checks, same as 森林丘陵·程序化道具), the ground uses the procedural textures, and the extra variety
    /// passes are added. Battlefield1024Generator's "生成 翠绿平原 / 森林丘陵" menus build these, so the originals no
    /// longer need the third-party prop / ground files. Backups of the third-party versions: outputs/ProcProps/orig_maps_bak.
    /// </summary>
    public sealed class ProcSkinMap : MapSpec
    {
        readonly MapSpec inner;
        GroundLook look;

        public ProcSkinMap(MapSpec inner) { this.inner = inner; }

        public override string Id => inner.Id;
        public override string Folder => inner.Folder;
        public override string DisplayName => inner.DisplayName;
        public override string Description => inner.Description;
        public override string Briefing => inner.Briefing;
        public override int Seed => inner.Seed;
        public override int TerrainVersion => inner.TerrainVersion;
        public override Vector2 ControlPoint => inner.ControlPoint;
        public override float ControlPointRadius => inner.ControlPointRadius;
        public override Vector3 PreviewCamera => inner.PreviewCamera;
        public override Vector3 PreviewTarget => inner.PreviewTarget;
        public override Vector2[] MustConnect => inner.MustConnect;

        public override GroundLook Look
        {
            get
            {
                if (look != null) return look;
                var o = inner.Look;
                look = new GroundLook
                {
                    GrassA = ProcPropLibrary.GroundPath("GrassA"), GrassB = ProcPropLibrary.GroundPath("GrassB"),
                    Dirt = ProcPropLibrary.GroundPath("Dirt"), Rock = ProcPropLibrary.GroundPath("Rock"), Forest = ProcPropLibrary.GroundPath("Forest"),
                    Tiling = o.Tiling, ForestTiling = o.ForestTiling,
                    PaletteDark = new Vector4(.076f, .275f, .036f, 1), PaletteLight = new Vector4(.262f, .571f, .102f, 1), Stylize = .2f,
                    SlopeRock = o.SlopeRock, OutsideMix = o.OutsideMix, Haze = o.Haze, HazeStart = o.HazeStart, HazeEnd = o.HazeEnd,
                    RidgeBase = o.RidgeBase, FarMountains = o.FarMountains,
                };
                return look;
            }
        }

        public override float Height(float x, float z) => inner.Height(x, z);
        public override bool Blocked(float x, float z) => inner.Blocked(x, z);
        public override Color32 Splat(float x, float z, float height, float slope) => inner.Splat(x, z, height, slope);

        public override void Decorate(Decorator d)
        {
            d.Remap = ProcPropLibrary.CreateRemap();
            d.RemapEarly = true;
            inner.Decorate(d);
            Variety(d);
        }

        /// <summary>Same extra passes as 森林丘陵·程序化道具; they run after the shared rules, so the layout is unchanged.</summary>
        void Variety(Decorator d)
        {
            string[] fern = ProcPropLibrary.Pool("Fern"), mush = ProcPropLibrary.Pool("Mushroom"), fallen = ProcPropLibrary.Pool("FallenLog");
            string[] dead = ProcPropLibrary.Pool("Dead"), patch = ProcPropLibrary.Pool("FlowerPatch"), pebbles = ProcPropLibrary.Pool("Pebbles");
            d.Scatter("林缘蕨类", -470f, -470f, 470f, 470f, 7f, 700,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 5f) && d.Chance(.35f),
                (x, z) => new Decorator.Choice(d.Pick(fern, false), d.Rand(.8f, 1.4f), false, false, .1f));
            d.Scatter("林下蘑菇", -470f, -470f, 470f, 470f, 9f, 450,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 4f) && d.Chance(.22f),
                (x, z) => new Decorator.Choice(d.Pick(mush, false), d.Rand(.6f, 1.1f), false, false, .05f));
            d.Scatter("林中倒木", -470f, -470f, 470f, 470f, 28f, 140,
                (x, z) => d.DeepBlocked(x, z, 6f) && d.Chance(.3f),
                (x, z) => new Decorator.Choice(d.Pick(fallen, false), d.Rand(.8f, 1.2f), false, false, .15f));
            d.Scatter("枯树", -490f, -490f, 490f, 490f, 40f, 60,
                (x, z) => d.Blocked(x, z) && d.Chance(.18f),
                (x, z) => new Decorator.Choice(d.Pick(dead, false), d.Rand(8f, 13f), false, false, .3f));
            d.Scatter("花丛", -460f, -460f, 460f, 460f, 14f, 500,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > 260f && d.Chance(.6f * Smooth(.45f, .7f, Noise(x, z, .009f, 41f))),
                (x, z) => new Decorator.Choice(d.Pick(patch, false), d.Rand(.5f, .9f), false, false, .05f));
            d.Scatter("碎石", -460f, -460f, 460f, 460f, 24f, 300,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > 260f && d.Chance(.14f),
                (x, z) => new Decorator.Choice(d.Pick(pebbles, false), d.Rand(.3f, .6f), false, false, .05f));
        }
    }

    public static class ProcSkinMenus
    {
        [MenuItem(Battlefield1024Generator.MenuRoot + "程序化地图/替换原版：翠绿平原", false, 70)]
        static void Plains() => ProcMapMenus.Run(new ProcSkinMap(new PlainsMap()));
        [MenuItem(Battlefield1024Generator.MenuRoot + "程序化地图/替换原版：森林丘陵", false, 71)]
        static void Forest() => ProcMapMenus.Run(new ProcSkinMap(new ForestHillsMap()));

        /// <summary>2026-10-13 ("森林丘陵 删掉"): removes the duplicate 森林丘陵·程序化道具 map (catalog entry, scene folder,
        /// build scene). The folder is copied to outputs/ProcProps/removed_ForestHillsProc first (with .meta), so it can be restored.</summary>
        [MenuItem(Battlefield1024Generator.MenuRoot + "程序化地图/删除 森林丘陵·程序化道具（重复）", false, 90)]
        static void RemoveForestHillsProc()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcSkin] 请先退出 Play 模式。"); return; }
            var spec = new ForestHillsProcMap();
            string backup = Path.GetFullPath("outputs/ProcProps/removed_ForestHillsProc");
            foreach (var dir in new[] { spec.Dir, spec.DerivedDir })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                string dst = Path.Combine(backup, dir.Replace('/', '_'));
                if (!Directory.Exists(dst)) CopyTree(Path.GetFullPath(dir), dst);
                if (File.Exists(Path.GetFullPath(dir) + ".meta")) File.Copy(Path.GetFullPath(dir) + ".meta", dst + ".meta", true);
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.StartsWith(dir + "/"))
                    UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
                Debug.Log("[ProcSkin] 删除 " + dir + (AssetDatabase.DeleteAsset(dir) ? "" : "（失败）") + "，备份 " + dst);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Battlefield1024Generator.CatalogPath);
            if (catalog != null && catalog.entries != null && catalog.entries.Any(e => e.id == spec.Id))
            {
                catalog.entries = catalog.entries.Where(e => e.id != spec.Id).ToArray();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                Debug.Log("[ProcSkin] 地图列表已移除 " + spec.Id);
            }
            Battlefield1024Generator.RepairCatalogAndBuildSettings();
        }

        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
