using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Editor;
using MassEngine.Game.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// 机枪手 / 炮车 (2026-10-17, plan D): free CC0 models (Quaternius) as two new ranged units on 2048 maps.
    ///   机枪手: Ultimate Modular Men "Swat" + Ultimate Gun Pack "AssaultRifle2_1" (M4) with the folded "Bipod",
    ///           gun rigid in the right hand, left hand held on the handguard by a two-bone IK during the bake.
    ///   炮车:   Animated Tanks "Tank4" (self-propelled gun). VAT has no runtime turret aim: the whole vehicle turns
    ///           to the target (like every unit), the attack clip is a recoil (barrel kick + hull rock), death sinks/tilts.
    /// Licence: CC0 (Assets/ThirdParty/CC0/*/License.txt; the gun pack page on OpenGameArt states CC0).
    /// The models, the baked VAT and the derived meshes/materials stay local (git-ignored, like every model);
    /// only this builder and the small configs (stats, stable GUIDs) are committed.
    /// After a clean clone: put the FBX files back under Assets/ThirdParty/CC0 and run 「生成」.
    /// </summary>
    public static class CC0ArmsBuilder
    {
        const string SwatFbx = "Assets/ThirdParty/CC0/QuaterniusModularMen/Swat.fbx";
        const string GunFbx = "Assets/ThirdParty/CC0/QuaterniusGuns/AssaultRifle2_1.fbx";
        const string BipodFbx = "Assets/ThirdParty/CC0/QuaterniusGuns/Bipod.fbx";
        const string TankFbx = "Assets/ThirdParty/CC0/QuaterniusTanks/Tank4.fbx";
        const string Dir = "Assets/Game/Content/Characters/CC0Arms";
        const string MaterialDir = "Assets/ThirdParty/_Derived/CC0Arms";
        const string RangerUnit = "Assets/Game/Content/Characters/UnifiedRoster/Version03/Library/ranger.asset";
        const string TriceratopsUnit = "Assets/Game/Content/Characters/NonhumanBatch2/Prepared01/Integrated/Library/triceratops.asset";
        const string MainCatalogPath = "Assets/Game/Scenes/Catalog.asset";
        const string CatalogPath = "Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset";
        const string ShotDir = "Logs/CC0Arms/preview";
        static readonly string[] Parts = { "", "_Spawn", "_Movement", "_Flocking", "_Animation", "_Combat", "_Render" };
        static readonly string[] Refs = { null, "spawnConfig", "movementConfig", "flockingConfig", "animationConfig", "combatConfig", "renderConfig" };
        static readonly string[] ActionNames = { "Idle", "Move", "Attack", "Death" };
        static readonly StringBuilder Log = new StringBuilder();

        // Gunner geometry (metres, in the 1.8 m bake): rifle length and the hold points as fractions from the stock end.
        const float GunLength = .85f, GripAt = .37f, HandguardAt = .70f, BipodAt = .58f;
        // Tank: hull length after scaling (the barrel overhangs it).
        const float TankHullLength = 7.5f;
        const float TankReleasePhase = .1f;

        sealed class Spec
        {
            public string key, title, templateUnit, sourceTemplateId, templateId;
            public float priceScale, interval, range, splash, speedFactor, radius, height;
            public int hp, damage, count;
            public float projectileSpeed, gravity, lifetime, trail, hitRadius, releasePhase, originHeight;
            public Color fx;
        }

        // Stats approved 2026-10-17 (plan): 机枪手 hp 100, 4 per 0.15 s, range 300; 炮车 hp 600, 120 per 6 s, splash 12 m,
        // range 700, 0.4x infantry speed. priceScale is calibrated with equal-budget duels (target ~90 / ~250 points).
        static readonly Spec Gunner = new Spec
        {
            key = "gunner", title = "机枪手", templateUnit = RangerUnit, sourceTemplateId = "roster-ranger", templateId = "roster-cc0-gunner",
            priceScale = 3.25f, hp = 100, damage = 4, interval = .15f, range = 300f, splash = 0f, speedFactor = 1f, radius = .45f, height = 1.8f, count = 0,
            projectileSpeed = 220f, gravity = 0f, lifetime = 1.6f, trail = 3f, hitRadius = .5f, releasePhase = .4f, originHeight = 1.3f, fx = new Color(0, 0, 0, 0)
        };
        static readonly Spec Artillery = new Spec
        {
            key = "artillery", title = "炮车", templateUnit = TriceratopsUnit, sourceTemplateId = "roster-triceratops", templateId = "roster-cc0-artillery",
            priceScale = .594f, hp = 600, damage = 120, interval = 6f, range = 700f, splash = 6f, speedFactor = .4f, radius = 2.8f, height = 0f, count = 6,
            projectileSpeed = 300f, gravity = 0f, lifetime = 4f, trail = 4f, hitRadius = 1f, releasePhase = TankReleasePhase, originHeight = 0f, fx = new Color(1f, .55f, .15f, 1f)
        };

        [MenuItem("Tools/机枪手炮车/预览（只出图，不生成）")]
        public static void Preview() => RunLogged("preview", () =>
        {
            Directory.CreateDirectory(ShotDir);
            foreach (var f in Directory.GetFiles(ShotDir)) File.Delete(f);
            foreach (var s in new[] { Gunner, Artillery })
            {
                Prep p = null;
                try
                {
                    p = Prepare(s, null, null);
                    p.stage.transform.position = new Vector3(0, -5000, 0);
                    p.stage.SetActive(true);
                    p.raw.SetActive(false);
                    for (int a = 0; a < 4; a++)
                        foreach (float f in a == 3 ? new[] { 0f, .5f, 1f } : a == 2 ? new[] { 0f, TankReleasePhase + .03f, .5f } : new[] { 0f, .5f })
                        {
                            p.clips[a].SampleAnimation(p.root, p.clips[a].length * f);
                            string n = ShotDir + "/" + s.key + "_" + ActionNames[a] + "_" + (int)(f * 100);
                            Shot(p.root, n + "_side.png", new Vector3(1, .12f, 0));
                            Shot(p.root, n + "_34.png", new Vector3(.8f, .45f, 1));
                        }
                    p.clips[0].SampleAnimation(p.root, 0);
                    Shot(p.root, ShotDir + "/" + s.key + "_top.png", new Vector3(.02f, 1, .02f));
                    Shot(p.root, ShotDir + "/" + s.key + "_back.png", new Vector3(-.4f, .3f, -1));
                    var b = Bounds(p.root);
                    Log.AppendLine(s.key + ": prepared bounds " + b.size.ToString("F2"));
                }
                finally { if (p != null) Object.DestroyImmediate(p.stage); }
            }
        });

        [MenuItem("Tools/机枪手炮车/生成（烘焙 + 注册到 2048 角色库）")]
        public static void Build() => BuildAll(false);

        [MenuItem("Tools/机枪手炮车/重新烘焙（全部重建）")]
        public static void Rebuild() => BuildAll(true);

        static void BuildAll(bool force) => RunLogged("build", () =>
        {
            EnsureFolder(Dir); EnsureFolder(MaterialDir);
            foreach (var s in new[] { Gunner, Artillery })
            {
                if (!force && AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(Dir + "/cc0-" + s.key + ".asset") != null) { Log.AppendLine(s.key + ": already built (重新烘焙 rebuilds)"); continue; }
                try { BuildOne(s); }
                catch (Exception e) { Log.AppendLine("FAILED " + s.key + ": " + e); Debug.LogException(e); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        });

        [MenuItem("Tools/机枪手炮车/只刷新数值（不重新烘焙）")]
        public static void RefreshValues() => RunLogged("refresh", () =>
        {
            foreach (var s in new[] { Gunner, Artillery })
            {
                var unit = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(Dir + "/cc0-" + s.key + ".asset");
                if (unit == null) { Log.AppendLine(s.key + ": not built, skipped"); continue; }
                ApplyValues(s, unit, unit.combatConfig.projectileTargetHeight * 2f);
                foreach (var o in Configs(unit)) EditorUtility.SetDirty(o);
                KaijuBuilder.RegisterTemplate(unit, MainCatalogPath, null, s.templateId, s.sourceTemplateId, s.priceScale, false, Log);
                KaijuBuilder.RegisterTemplate(unit, CatalogPath, null, s.templateId, s.sourceTemplateId, s.priceScale, false, Log);
            }
            AssetDatabase.SaveAssets();
        });

        static void RunLogged(string name, Action body)
        {
            Log.Clear();
            try { body(); Log.AppendLine("DONE"); }
            catch (Exception e) { Log.AppendLine("FAILED: " + e); Debug.LogException(e); }
            Directory.CreateDirectory("Logs/CC0Arms");
            File.WriteAllText("Logs/CC0Arms/" + name + ".txt", Log.ToString(), new UTF8Encoding(false));
            Debug.Log("[CC0Arms] " + Log);
        }

        static Object[] Configs(UnitTypeConfig u) => new Object[] { u, u.spawnConfig, u.movementConfig, u.flockingConfig, u.animationConfig, u.combatConfig, u.renderConfig };

        // ------------------------------------------------------------------ build

        static void BuildOne(Spec s)
        {
            var template = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(s.templateUnit) ?? throw new InvalidOperationException("missing " + s.templateUnit);
            string matDir = MaterialDir + "/" + s.key;
            EnsureFolder(matDir);
            foreach (var old in AssetDatabase.GetSubFolders(matDir))
                if (Path.GetFileName(old).StartsWith("Template") || Path.GetFileName(old).StartsWith("Prepared")) AssetDatabase.DeleteAsset(old);
            string stamp = DateTime.Now.ToString("MMddHHmmss");
            string prepDir = matDir + "/Prepared_" + stamp, tplDir = matDir + "/Template_" + stamp;
            EnsureFolder(prepDir); EnsureFolder(tplDir);

            // Palette materials (local): the template's VAT materials with a point-filtered 8x4 colour atlas.
            var palette = PaletteOf(s);
            var atlas = Atlas(palette, s.key);
            AssetDatabase.CreateAsset(atlas, matDir + "/" + s.key + "_Palette_" + stamp + ".asset");
            var render = template.renderConfig;
            var near = UpsertMaterial(render.nearMaterial, matDir + "/" + s.key + "_Near.mat", atlas);
            var mid = UpsertMaterial(render.midMaterial, matDir + "/" + s.key + "_Mid.mat", atlas);
            var far = render.farMaterial == render.midMaterial ? mid : UpsertMaterial(render.farMaterial, matDir + "/" + s.key + "_Far.mat", atlas);
            var tpl = CharacterPipeline.CloneUnit(template, tplDir, "Template");
            tpl.renderConfig.nearMaterial = near; tpl.renderConfig.midMaterial = mid; tpl.renderConfig.farMaterial = far;
            CharacterPipeline.Save(tpl.renderConfig);

            Prep p = null; GameObject prefab; float height;
            try
            {
                p = Prepare(s, prepDir, near);
                height = s.height > 0 ? s.height : TankHeight(p);
                prefab = PrefabUtility.SaveAsPrefabAsset(p.root, prepDir + "/Prepared.prefab") ?? throw new InvalidOperationException("cannot save prepared prefab");
            }
            finally { if (p != null) Object.DestroyImmediate(p.stage); }
            var mapped = ActionNames.Select(n => AssetDatabase.LoadAssetAtPath<AnimationClip>(prepDir + "/" + n + ".anim")).ToArray();

            var recipe = ScriptableObject.CreateInstance<CharacterRecipe>();
            recipe.name = "CC0_" + s.key;
            recipe.model = prefab; recipe.sizeRootPath = "ScalePivot";
            recipe.idle = mapped[0]; recipe.move = mapped[1]; recipe.attack = mapped[2]; recipe.death = mapped[3];
            recipe.frameRate = s == Gunner ? 20 : 15;
            recipe.targetBodyHeight = height;
            recipe.groundBindFeet = true;
            recipe.agentRadius = s.radius;
            recipe.lowPolicy = CharacterLowLodPolicy.PaletteGrid;
            recipe.paletteColumns = 8; recipe.paletteRows = 4;
            recipe.lowVertexBudget = s == Gunner ? 300 : 450; // far-mesh budgets: infantry 300, large 400-500
            recipe.fullVertexBudget = 24000;
            recipe.maxTextureMiB = 64;
            recipe.maximumPositionError = s == Gunner ? .0025f : .02f;
            recipe.unitTemplate = tpl;
            recipe.outputName = NextOutputName("CC0_" + char.ToUpper(s.key[0]) + s.key.Substring(1));
            Log.AppendLine(s.key + ": height " + height.ToString("F2") + " output " + recipe.outputName + " palette " + palette.Length);

            var report = CharacterPipeline.Run(recipe);
            Log.AppendLine(s.key + ": pipeline " + report.status + " full " + report.fullVertices + " low " + report.lowVertices +
                " frames " + report.totalFrames + " err " + (report.maxPositionError * 1000f).ToString("F2") + " mm tex " + (report.textureBytes / 1048576f).ToString("F1") + " MiB");
            string output = CharacterPipeline.Output(recipe);
            var final = AssetDatabase.LoadAssetAtPath<ScriptableObject>(output + "/FinalVAT.asset") ?? AssetDatabase.LoadAssetAtPath<ScriptableObject>(output + "/Full.asset");
            var generated = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(output + "/Unit.asset") ?? throw new InvalidOperationException("pipeline unit missing");

            var unit = UpsertConfigs(s.key, generated);
            unit.renderConfig.nearMaterial = near; unit.renderConfig.midMaterial = mid; unit.renderConfig.farMaterial = far;
            var bound = UnitTypeBinder.Bind(unit, final);
            if (!bound.IsValid) throw new InvalidOperationException(string.Join("\n", bound.Errors));
            ApplyValues(s, unit, height);
            foreach (var o in Configs(unit)) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            KaijuBuilder.RegisterTemplate(unit, MainCatalogPath, null, s.templateId, s.sourceTemplateId, s.priceScale, false, Log);
            KaijuBuilder.RegisterTemplate(unit, CatalogPath, null, s.templateId, s.sourceTemplateId, s.priceScale, false, Log);
            KaijuBuilder.Log.Clear();
            KaijuBuilder.BuildPolicy2048(unit);
            Log.Append(KaijuBuilder.Log);
            Log.AppendLine(s.key + ": OK");
        }

        static float TankHeight(Prep p)
        {
            // Bind pose: hull length (Tank_body renderer) -> TankHullLength; height is what the pipeline scales by.
            var all = MeshBounds(p.body.gameObject);
            var hull = MeshBounds(p.body.GetComponentsInChildren<Renderer>(true).First(r => r.name.StartsWith("Tank_body")));
            float length = Mathf.Max(hull.size.x, hull.size.z);
            return TankHullLength * all.size.y / length;
        }

        static void ApplyValues(Spec s, UnitTypeConfig u, float height)
        {
            var ranger = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(RangerUnit);
            float infantrySpeed = ranger != null ? ranger.movementConfig.maxSpeed : 12f;
            u.unitTypeName = s.title;
            var c = u.combatConfig;
            c.maxHp = s.hp;
            c.attackDamage = s.damage;
            c.attackInterval = s.interval;
            c.projectileRange = s.range;
            c.targetAcquireRadius = s.range + 10f;
            c.attackRange = s.radius + (s == Gunner ? 1.35f : 3f);
            c.projectileSpeed = s.projectileSpeed;
            c.projectileGravity = s.gravity;
            c.projectileHitRadius = s.hitRadius;
            c.projectileMaxLifetime = s.lifetime;
            c.projectileTrailLength = s.trail;
            c.projectileSplashRadius = s.splash;
            // 炸飞 (2026-10-17, user approved 甲, render only): bodies killed by a shell fly up to 7 m.
            c.knockFlyHeight = s.splash > 0f ? 7f : 0f;
            // 2026-10-17: knock range 6 m (user: 炸飞范围再小一半); plan A then cut the splash itself to 6 m, so the whole splash flies.
            c.knockFlyRadius = 0f;
            c.attackReleasePhase = s.releasePhase;
            c.projectileOriginHeight = s.originHeight > 0 ? s.originHeight : height * .85f;
            c.projectileTargetHeight = height * .5f;
            c.projectileFxColor = s.fx;
            // 2026-10-17 (user approved 甲): the shell look is the dragon fireball sized by the 12 m splash - shrink it and thin it out.
            c.projectileFxScale = s.splash > 0f ? 0.6f : 1f; // 6 m splash x 0.6 = the same look as 12 m x 0.3
            c.projectileFxDensity = s.splash > 0f ? 0.55f : 1f;
            c.meleeSplashRadius = 0f; c.chargeDamageMultiplier = 1f; c.trampleDamageMultiplier = 0f; c.flightAltitude = 0f;
            u.flockingConfig.agentRadius = s.radius;
            u.movementConfig.maxSpeed = infantrySpeed * s.speedFactor;
            var a = new SerializedObject(u.animationConfig);
            if (s == Artillery)
            {
                float reference = u.movementConfig.maxSpeed;
                a.FindProperty("moveReferenceSpeed").floatValue = reference;
                var start = a.FindProperty("moveStartSpeed"); start.floatValue = Mathf.Min(start.floatValue, reference * .3f);
                var stop = a.FindProperty("moveStopSpeed"); stop.floatValue = Mathf.Min(stop.floatValue, start.floatValue * .8f);
            }
            a.ApplyModifiedPropertiesWithoutUndo();
            var sp = u.spawnConfig;
            if (s.count > 0)
            {
                sp.unitCount = s.count;
                float side = 2f * s.radius * 1.2f;
                sp.spawnSize = new Vector3(side * 3f, 0f, side * 2f); // >= count * 4 r^2 (policy packing rule)
            }
            Log.AppendLine(s.key + ": values hp " + c.maxHp + " dmg " + c.attackDamage + " interval " + c.attackInterval + " range " + c.projectileRange +
                " splash " + c.projectileSplashRadius + " speed " + u.movementConfig.maxSpeed.ToString("F1") + " radius " + s.radius + " height " + height.ToString("F2") +
                " count " + sp.unitCount + " priceScale " + s.priceScale);
        }

        static UnitTypeConfig UpsertConfigs(string key, UnitTypeConfig generated)
        {
            var sources = new ScriptableObject[] { generated, generated.spawnConfig, generated.movementConfig, generated.flockingConfig, generated.animationConfig, generated.combatConfig, generated.renderConfig };
            var result = new ScriptableObject[Parts.Length];
            string name = "cc0-" + key;
            for (int i = 0; i < Parts.Length; i++)
            {
                string dst = Dir + "/" + name + Parts[i] + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dst);
                if (existing == null)
                {
                    if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sources[i]), dst)) throw new InvalidOperationException("copy failed " + dst);
                    Log.AppendLine("created " + dst);
                    existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dst);
                }
                else if (i == Parts.Length - 1) EditorUtility.CopySerialized(sources[i], existing);
                existing.name = name + Parts[i];
                result[i] = existing;
            }
            var main = new SerializedObject(result[0]);
            for (int i = 1; i < Parts.Length; i++) main.FindProperty(Refs[i]).objectReferenceValue = result[i];
            main.ApplyModifiedPropertiesWithoutUndo();
            return (UnitTypeConfig)result[0];
        }

        static Material UpsertMaterial(Material source, string path, Texture albedo)
        {
            if (source == null) throw new InvalidOperationException("template material missing");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(source);
            m.shader = source.shader;
            m.enableInstancing = true;
            m.SetTexture("_BaseMap", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
            return m;
        }

        static string NextOutputName(string stem)
        {
            for (int i = 1; i < 100; i++)
            {
                string name = stem + i.ToString("00");
                string path = CharacterPipeline.Root + "/Generated/" + name;
                if (!Directory.Exists(path) && !File.Exists(path + ".meta") && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) return name;
            }
            throw new InvalidOperationException("no free output name for " + stem);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        static void ConfigureModel(string path)
        {
            var im = (ModelImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("no importer " + path);
            bool dirty = false;
            if (!im.isReadable) { im.isReadable = true; dirty = true; }
            if (im.animationType != ModelImporterAnimationType.Generic) { im.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (im.importBlendShapes) { im.importBlendShapes = false; dirty = true; }
            if (dirty) { im.SaveAndReimport(); Log.AppendLine("importer configured " + path); }
        }

        static GameObject LoadModel(string path)
        {
            ConfigureModel(path);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidOperationException("missing " + path);
        }

        static AnimationClip Clip(string fbx, string name) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name.EndsWith("|" + name) || c.name == name)
            ?? throw new InvalidOperationException("clip " + name + " missing in " + fbx);

        // ------------------------------------------------------------------ palette

        static Color[] PaletteOf(Spec s)
        {
            var models = s == Gunner ? new[] { SwatFbx, GunFbx, BipodFbx } : new[] { TankFbx };
            var list = new List<Color>();
            foreach (var m in models)
                foreach (var mat in LoadModel(m).GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials))
                    if (mat != null && !list.Contains(mat.color)) list.Add(mat.color);
            if (list.Count > 32) throw new InvalidOperationException("palette > 32 colours");
            return list.ToArray();
        }

        static Texture2D Atlas(Color[] palette, string key)
        {
            var atlas = new Texture2D(128, 64, TextureFormat.RGBA32, false, false) { name = key + "Palette", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[128 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 128; x++) { int tile = y / 16 * 8 + x / 16; pixels[y * 128 + x] = tile < palette.Length ? palette[tile] : Color.white; }
            atlas.SetPixels(pixels); atlas.Apply(false, false);
            return atlas;
        }

        static Vector2 Tile(int index) => new Vector2((index % 8 + .5f) / 8f, (index / 8 + .5f) / 4f);

        // ------------------------------------------------------------------ preparation

        sealed class Prep
        {
            public GameObject stage, raw, root; public Transform body; public AnimationClip[] clips = new AnimationClip[4];
        }

        sealed class Action4
        {
            public AnimationClip source; public float length; public Func<float, float> sourceTime; public Action<float> hook; public bool loop = true;
        }

        /// <summary>
        /// Canonical Generic copy (RobotExpressiveBuilder.Prepare01 method): unit scales everywhere, world rest geometry kept,
        /// rigid parts become one-bone skins, flat material colours become palette UVs, clips resampled from a raw twin.
        /// prepDir null = preview (nothing saved).
        /// </summary>
        static Prep Prepare(Spec s, string prepDir, Material near)
        {
            var palette = PaletteOf(s);
            var p = new Prep { stage = new GameObject("CC0 arms preparation (inactive)") { hideFlags = HideFlags.DontSave } };
            p.stage.SetActive(false);
            var source = LoadModel(s == Gunner ? SwatFbx : TankFbx);
            p.raw = Object.Instantiate(source, p.stage.transform, false); p.raw.name = "Raw";
            p.root = new GameObject("CC0" + s.key + "Prepared"); p.root.transform.SetParent(p.stage.transform, false);
            var pivot = new GameObject("ScalePivot").transform; pivot.SetParent(p.root.transform, false);
            var bodyGo = Object.Instantiate(source, pivot, false); bodyGo.name = "Body"; p.body = bodyGo.transform;
            foreach (var a in p.raw.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var a in bodyGo.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

            var actions = s == Gunner ? GunnerSetup(p) : TankSetup(p);

            // Canonical: unit scales, same world placement.
            var transforms = bodyGo.GetComponentsInChildren<Transform>(true);
            var oldWorld = transforms.ToDictionary(t => t, t => t.localToWorldMatrix);
            var worldPos = transforms.ToDictionary(t => t, t => t.position);
            var worldRot = transforms.ToDictionary(t => t, t => t.rotation);
            foreach (var t in transforms)
            {
                Vector3 sc = t.localScale; float mean = (sc.x + sc.y + sc.z) / 3f;
                if (!(sc.x > 0 && sc.y > 0 && sc.z > 0 && (sc - Vector3.one * mean).magnitude / mean < .0002f)) throw new InvalidOperationException("non-uniform scale: " + t.name);
            }
            foreach (var t in transforms) { t.localScale = Vector3.one; t.SetPositionAndRotation(worldPos[t], worldRot[t]); }

            int vertices = 0, triangles = 0;
            foreach (var renderer in bodyGo.GetComponentsInChildren<Renderer>(true))
            {
                var skin = renderer as SkinnedMeshRenderer; var filter = renderer.GetComponent<MeshFilter>();
                var src = skin != null ? skin.sharedMesh : filter.sharedMesh;
                var vertex = renderer.transform.worldToLocalMatrix * oldWorld[renderer.transform];
                var mats = renderer.sharedMaterials;
                var mesh = SplitByMaterial(src, mats, palette, vertex, renderer.name + "Canonical");
                if (skin != null)
                    mesh.bindposes = skin.bones.Select((bone, j) => bone.worldToLocalMatrix * oldWorld[bone] * src.bindposes[j] * vertex.inverse).ToArray();
                else
                {
                    var bone = new GameObject("RigidPartBone").transform; bone.SetParent(renderer.transform, false);
                    mesh.boneWeights = Enumerable.Range(0, mesh.vertexCount).Select(_ => new BoneWeight { boneIndex0 = 0, weight0 = 1 }).ToArray();
                    mesh.bindposes = new[] { Matrix4x4.identity };
                    var owner = renderer.gameObject; Object.DestroyImmediate(renderer); Object.DestroyImmediate(filter);
                    skin = owner.AddComponent<SkinnedMeshRenderer>(); skin.bones = new[] { bone }; skin.rootBone = bone;
                }
                mesh.RecalculateBounds();
                if (prepDir != null) AssetDatabase.CreateAsset(mesh, prepDir + "/" + mesh.name + ".asset");
                skin.sharedMesh = mesh; skin.localBounds = mesh.bounds; skin.updateWhenOffscreen = true;
                var mat = near != null ? near : PreviewMaterial(palette);
                skin.sharedMaterials = Enumerable.Repeat(mat, mesh.subMeshCount).ToArray();
                vertices += mesh.vertexCount; triangles += mesh.triangles.Length / 3;
            }
            Log.AppendLine(s.key + ": prepared " + vertices + " vertices " + triangles + " triangles, renderers " + bodyGo.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);

            // Resample every clip from the raw twin (world poses copied by path), with the per-unit hooks (IK, recoil).
            var rawByPath = p.raw.GetComponentsInChildren<Transform>(true).ToDictionary(t => AnimationUtility.CalculateTransformPath(t, p.raw.transform));
            var preparedByPath = transforms.ToDictionary(t => AnimationUtility.CalculateTransformPath(t, bodyGo.transform));
            foreach (var key in preparedByPath.Keys) if (!rawByPath.ContainsKey(key)) throw new InvalidOperationException("raw twin lacks " + key);
            var bindPosition = transforms.Select(t => t.localPosition).ToArray(); var bindRotation = transforms.Select(t => t.localRotation).ToArray();
            var rawRest = p.raw.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => (t.localPosition, t.localRotation));
            int rate = s == Gunner ? 20 : 15;
            for (int action = 0; action < 4; action++)
            {
                var spec = actions[action];
                var times = new SortedSet<float> { 0, spec.length };
                for (int f = 0; f <= Mathf.CeilToInt(spec.length * 60); f++) times.Add(Mathf.Min(spec.length, f / 60f));
                var keys = preparedByPath.ToDictionary(kv => kv.Key, kv => Enumerable.Range(0, 7).Select(_ => new List<Keyframe>()).ToArray());
                foreach (float time in times)
                {
                    foreach (var kv in rawRest) { kv.Key.localPosition = kv.Value.localPosition; kv.Key.localRotation = kv.Value.localRotation; }
                    spec.source.SampleAnimation(p.raw, spec.sourceTime(time));
                    spec.hook?.Invoke(time / Mathf.Max(1e-4f, spec.length));
                    foreach (var kv in preparedByPath) kv.Value.SetPositionAndRotation(rawByPath[kv.Key].position, rawByPath[kv.Key].rotation);
                    foreach (var kv in preparedByPath)
                    {
                        var t = kv.Value; float[] v = { t.localPosition.x, t.localPosition.y, t.localPosition.z, t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w };
                        for (int k = 0; k < 7; k++) keys[kv.Key][k].Add(new Keyframe(time, v[k]));
                    }
                }
                var clip = new AnimationClip { name = ActionNames[action], frameRate = rate };
                foreach (var kv in keys)
                {
                    string path = "ScalePivot/Body" + (kv.Key.Length > 0 ? "/" + kv.Key : "");
                    for (int k = 0; k < 7; k++)
                    {
                        var curve = new AnimationCurve(kv.Value[k].ToArray());
                        for (int i = 0; i < curve.length; i++) { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
                        clip.SetCurve(path, typeof(Transform), (k < 3 ? "m_LocalPosition." : "m_LocalRotation.") + "xyzw"[k < 3 ? k : k - 3], curve);
                    }
                }
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = spec.loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
                if (prepDir != null) AssetDatabase.CreateAsset(clip, prepDir + "/" + ActionNames[action] + ".anim");
                p.clips[action] = clip;
                for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = bindPosition[i]; transforms[i].localRotation = bindRotation[i]; }
            }
            CharacterGeometry.CheckHierarchy(p.root, "ScalePivot");
            if (bodyGo.GetComponentsInChildren<MeshRenderer>(true).Length != 0) throw new InvalidOperationException("incomplete rigid conversion");
            return p;
        }

        /// <summary>Unshares vertices between submeshes (one colour per vertex), bakes the node transform, palette UVs, white vertex colours.</summary>
        static Mesh SplitByMaterial(Mesh src, Material[] mats, Color[] palette, Matrix4x4 vertex, string name)
        {
            var normal = vertex.inverse.transpose;
            var sv = src.vertices; var sn = src.normals; var st = src.tangents; var sw = src.boneWeights;
            bool hasN = sn.Length == sv.Length, hasT = st.Length == sv.Length, hasW = sw.Length == sv.Length;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tg = new List<Vector4>(); var w = new List<BoneWeight>(); var uv = new List<Vector2>();
            var subs = new List<int[]>();
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                var color = mats[Mathf.Min(sub, mats.Length - 1)].color;
                int index = Array.IndexOf(palette, color); if (index < 0) throw new InvalidOperationException("colour not in palette");
                var tile = Tile(index);
                var map = new Dictionary<int, int>();
                var tri = src.GetTriangles(sub);
                var outTri = new int[tri.Length];
                for (int i = 0; i < tri.Length; i++)
                {
                    int o = tri[i];
                    if (!map.TryGetValue(o, out int ni))
                    {
                        ni = v.Count; map[o] = ni;
                        v.Add(vertex.MultiplyPoint3x4(sv[o]));
                        n.Add(hasN ? normal.MultiplyVector(sn[o]).normalized : Vector3.up);
                        if (hasT) { var d = vertex.MultiplyVector(new Vector3(st[o].x, st[o].y, st[o].z)).normalized; tg.Add(new Vector4(d.x, d.y, d.z, st[o].w)); }
                        if (hasW) w.Add(sw[o]);
                        uv.Add(tile);
                    }
                    outTri[i] = ni;
                }
                subs.Add(outTri);
            }
            var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(n); if (hasT) mesh.SetTangents(tg); mesh.SetUVs(0, uv);
            mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), v.Count).ToArray();
            if (hasW) mesh.boneWeights = w.ToArray();
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            // Winding check (the VAT shaders cull back faces); a mirrored node transform flips it.
            if (vertex.determinant < 0)
                for (int i = 0; i < subs.Count; i++) { var t = subs[i]; for (int j = 0; j + 2 < t.Length; j += 3) { int x = t[j + 1]; t[j + 1] = t[j + 2]; t[j + 2] = x; } mesh.SetTriangles(t, i); }
            return mesh;
        }

        static Material PreviewMaterial(Color[] palette)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { hideFlags = HideFlags.DontSave };
            var atlas = Atlas(palette, "preview"); atlas.hideFlags = HideFlags.DontSave;
            m.SetTexture("_BaseMap", atlas); m.SetTexture("_MainTex", atlas);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .2f);
            return m;
        }

        // ------------------------------------------------------------------ 机枪手

        sealed class GunFrame { public float m, length; public Vector3 grip, handguard, bipodAnchor; public Quaternion axes; }

        /// <summary>Gun geometry in its prefab space: muzzle sign along the long axis, grip / handguard / bipod points.</summary>
        static GunFrame AnalyseGun(GameObject gunPrefab, Transform stage)
        {
            var g = Object.Instantiate(gunPrefab, stage, false);
            try
            {
                var pts = new List<Vector3>();
                foreach (var mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = stage.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    pts.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
                }
                var b = new Bounds(pts[0], Vector3.zero); foreach (var q in pts) b.Encapsulate(q);
                if (!(b.size.x > b.size.y && b.size.x > b.size.z)) throw new InvalidOperationException("gun long axis is not X: " + b.size);
                float L = b.size.x, x0 = b.min.x, x1 = b.max.x;
                float Extent(float lo, float hi) { var s = pts.Where(q => q.x >= lo && q.x <= hi).ToArray(); return s.Length == 0 ? 0 : s.Max(q => q.y) - s.Min(q => q.y); }
                float m0 = Extent(x0, x0 + .08f * L), m1 = Extent(x1 - .08f * L, x1);
                float sign = m1 < m0 ? 1f : -1f; // the muzzle end is the thin one
                float X(float fromStock) => sign > 0 ? x0 + fromStock * L : x1 - fromStock * L;
                (float lo, float hi) Slice(float f) { var s = pts.Where(q => Mathf.Abs(q.x - X(f)) < .025f * L).ToArray(); return (s.Min(q => q.y), s.Max(q => q.y)); }
                var grip = Slice(GripAt); var guard = Slice(HandguardAt); var bip = Slice(BipodAt);
                var frame = new GunFrame
                {
                    m = sign, length = L,
                    grip = new Vector3(X(GripAt), grip.lo + .35f * (grip.hi - grip.lo), b.center.z),
                    handguard = new Vector3(X(HandguardAt), guard.lo, b.center.z),
                    bipodAnchor = new Vector3(X(BipodAt), bip.lo, b.center.z),
                    axes = Quaternion.LookRotation(new Vector3(sign, 0, 0), Vector3.up),
                };
                Log.AppendLine("gun: bounds " + b.size.ToString("F3") + " ends " + m0.ToString("F3") + "/" + m1.ToString("F3") + " muzzle " + (sign > 0 ? "+X" : "-X") +
                    " grip " + frame.grip.ToString("F3") + " guard " + frame.handguard.ToString("F3"));
                return frame;
            }
            finally { Object.DestroyImmediate(g); }
        }

        static Action4[] GunnerSetup(Prep p)
        {
            var gunPrefab = LoadModel(GunFbx); var bipodPrefab = LoadModel(BipodFbx);
            var gun = AnalyseGun(gunPrefab, p.stage.transform);
            var idle = Clip(SwatFbx, "Idle_Gun_Pointing"); var move = Clip(SwatFbx, "Run_Shoot");
            var attack = Clip(SwatFbx, "Idle_Gun_Shoot"); var death = Clip(SwatFbx, "Death");

            // Mount from the aiming pose: barrel along the right forearm, grip in the palm, upright.
            idle.SampleAnimation(p.raw, 0);
            var wrist = Find(p.raw.transform, "Wrist.R"); var index = Find(p.raw.transform, "Index1.R");
            Vector3 D = wrist.up.normalized; Vector3 U = Vector3.ProjectOnPlane(Vector3.up, D).normalized;
            Quaternion R = Quaternion.LookRotation(D, U) * Quaternion.Inverse(gun.axes);
            float k = GunLength / gun.length;
            Vector3 palm = (wrist.position + index.position) * .5f;
            Vector3 mountPos = palm - R * (k * gun.grip);
            var localPos = wrist.InverseTransformPoint(mountPos);
            var localRot = Quaternion.Inverse(wrist.rotation) * R;
            float localScale = k / wrist.lossyScale.x;
            Log.AppendLine("gunner: forearm " + D.ToString("F3") + " palm " + palm.ToString("F3") + " scale " + k.ToString("F4"));

            Transform Mount(GameObject inst, bool marker)
            {
                var w = Find(inst.transform, "Wrist.R");
                var mount = new GameObject("GunMount").transform; mount.SetParent(w, false);
                mount.localPosition = localPos; mount.localRotation = localRot; mount.localScale = Vector3.one * localScale;
                var g = Object.Instantiate(gunPrefab, mount, false); g.name = "Rifle";
                // Bipod folded forward under the handguard: its legs (-Y) along the barrel, its top block on the anchor.
                var wrap = new GameObject("BipodMount").transform; wrap.SetParent(mount, false);
                wrap.localRotation = Quaternion.Euler(0, 0, 90f * gun.m);
                var bp = Object.Instantiate(bipodPrefab, wrap, false); bp.name = "Bipod";
                var top = BipodTop(bp, wrap);
                wrap.localPosition = gun.bipodAnchor - wrap.localRotation * top;
                if (marker) { var t = new GameObject("IKTarget").transform; t.SetParent(mount, false); t.localPosition = gun.handguard; }
                return mount;
            }
            var rawMount = Mount(p.raw, true);
            Mount(p.body.gameObject, false);
            var ikTarget = rawMount.Find("IKTarget");
            var upper = Find(p.raw.transform, "UpperArm.L"); var lower = Find(p.raw.transform, "LowerArm.L"); var end = Find(p.raw.transform, "Wrist.L");

            void Ik(float weight)
            {
                if (weight <= 0) return;
                Vector3 fwd = rawMount.rotation * gun.axes * Vector3.forward, up = rawMount.rotation * gun.axes * Vector3.up;
                Vector3 target = ikTarget.position - fwd * .03f - up * .035f;
                TwoBoneIk(upper, lower, end, target, new Vector3(-.5f, -1f, -.25f), weight);
            }
            return new[]
            {
                new Action4 { source = idle, length = idle.length, sourceTime = t => t, hook = f => Ik(1) },
                new Action4 { source = move, length = move.length, sourceTime = t => t, hook = f => Ik(1) },
                new Action4 { source = attack, length = attack.length, sourceTime = t => t, hook = f => Ik(1) },
                new Action4 { source = death, length = death.length, sourceTime = t => t, hook = f => Ik(1f - Mathf.SmoothStep(0, 1, f / .3f)), loop = false },
            };
        }

        /// <summary>Top block centre of the bipod in its parent (wrap) frame, before the wrap rotation.</summary>
        static Vector3 BipodTop(GameObject bipod, Transform wrap)
        {
            var pts = new List<Vector3>();
            foreach (var mf in bipod.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = wrap.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                pts.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
            }
            float maxY = pts.Max(q => q.y), minY = pts.Min(q => q.y);
            var top = pts.Where(q => q.y > maxY - .05f * (maxY - minY)).ToArray();
            return new Vector3(top.Average(q => q.x), maxY, top.Average(q => q.z));
        }

        static void TwoBoneIk(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float weight)
        {
            Vector3 S = upper.position, E = lower.position, W = end.position;
            float a = (E - S).magnitude, b = (W - E).magnitude;
            Vector3 T = Vector3.Lerp(W, target, weight);
            Vector3 toT = T - S;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            Vector3 dir = toT.normalized;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1f, 1f), sinA = Mathf.Sqrt(1 - cosA * cosA);
            Vector3 pv = Vector3.ProjectOnPlane(pole, dir);
            if (pv.sqrMagnitude < 1e-8f) pv = Vector3.ProjectOnPlane(Vector3.down, dir);
            pv.Normalize();
            Vector3 E2 = S + a * (dir * cosA + pv * sinA);
            upper.rotation = Quaternion.FromToRotation(E - S, E2 - S) * upper.rotation;
            Vector3 En = lower.position, Wn = end.position;
            lower.rotation = Quaternion.FromToRotation(Wn - En, S + dir * d - En) * lower.rotation;
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) ?? throw new InvalidOperationException("bone " + name + " missing");

        // ------------------------------------------------------------------ 炮车

        static Action4[] TankSetup(Prep p)
        {
            var forward = Clip(TankFbx, "Tank_Forward");
            var gunR = Find(p.raw.transform, "Tank_Gun").GetComponent<Renderer>(); var turretR = Find(p.raw.transform, "Tank_Turret").GetComponent<Renderer>();
            float sign = Mathf.Sign(MeshBounds(gunR).center.x - MeshBounds(turretR).center.x); // the barrel points to the front
            var yaw = Quaternion.Euler(0, sign > 0 ? -90f : 90f, 0); // front -> +Z
            p.raw.transform.localRotation = yaw; p.body.localRotation = yaw;
            Log.AppendLine("artillery: barrel along " + (sign > 0 ? "+X" : "-X") + " -> yaw " + yaw.eulerAngles.y);
            var rawRoot = p.raw.transform; var gun = Find(rawRoot, "Tank_Gun");
            var hullB = MeshBounds(Find(rawRoot, "Tank_body.001").GetComponent<Renderer>()); float hullLength = Mathf.Max(hullB.size.x, hullB.size.z);

            Vector3 Front = yaw * new Vector3(sign, 0, 0); // the barrel direction after the yaw (+Z)
            float Kick(float f)
            {
                float x = f - TankReleasePhase; if (x < 0) return 0;
                if (x < .03f) return x / .03f;
                float r = Mathf.Clamp01((x - .03f) / .42f); return (1 - r) * (1 - r);
            }
            void Recoil(float f)
            {
                float k = Kick(f);
                Vector3 fwd = Front, side = Vector3.Cross(Vector3.up, fwd);
                gun.position -= fwd * (k * .085f * hullLength);
                float lag = Kick(Mathf.Max(0, f - .02f));
                rawRoot.rotation = Quaternion.AngleAxis(-1.8f * lag, side) * yaw; // nose up, rocking on the hull centre
            }
            void Die(float f)
            {
                float e = Mathf.SmoothStep(0, 1, Mathf.Clamp01(f / .7f));
                Vector3 fwd = Front, side = Vector3.Cross(Vector3.up, fwd);
                rawRoot.rotation = Quaternion.AngleAxis(7f * e, fwd) * Quaternion.AngleAxis(3f * e, side) * yaw;
                rawRoot.localPosition = Vector3.down * (.05f * hullLength * e);
                gun.rotation = Quaternion.AngleAxis(9f * e, side) * gun.rotation; // barrel droops about its trunnion
            }
            void Rest(float f) { rawRoot.localRotation = yaw; rawRoot.localPosition = Vector3.zero; }
            // Engine idle: a small hull shudder (the pipeline requires visible idle motion; the tracks stand still).
            void Idle(float f)
            {
                Rest(f);
                float w = Mathf.Sin(f * Mathf.PI * 2f * 3f);
                rawRoot.localPosition = Vector3.up * (.004f * hullLength * w);
                rawRoot.rotation = Quaternion.AngleAxis(.35f * Mathf.Sin(f * Mathf.PI * 2f * 2f), Vector3.Cross(Vector3.up, Front)) * yaw;
            }
            return new[]
            {
                new Action4 { source = forward, length = 1f, sourceTime = t => 0, hook = Idle },
                new Action4 { source = forward, length = forward.length, sourceTime = t => t, hook = Rest },
                new Action4 { source = forward, length = 2f, sourceTime = t => 0, hook = f => { Rest(f); Recoil(f); } },
                new Action4 { source = forward, length = 1.5f, sourceTime = t => 0, hook = f => { Rest(f); Die(f); }, loop = false },
            };
        }

        // ------------------------------------------------------------------ preview renders

        /// <summary>Rest-geometry bounds from the meshes (Renderer.bounds is empty while the staging root is inactive).</summary>
        static Bounds MeshBounds(Renderer r)
        {
            var mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>().sharedMesh;
            var m = r.transform.localToWorldMatrix; var vs = mesh.vertices;
            var b = new Bounds(m.MultiplyPoint3x4(vs[0]), Vector3.zero); foreach (var v in vs) b.Encapsulate(m.MultiplyPoint3x4(v));
            return b;
        }

        static Bounds MeshBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = MeshBounds(rs[0]); foreach (var r in rs) b.Encapsulate(MeshBounds(r));
            return b;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void Shot(GameObject inst, string outPath, Vector3 dir)
        {
            // Edit-mode renders do not re-skin after SampleAnimation: draw CPU-baked copies of the skins instead.
            var skins = inst.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(r => r.enabled).ToArray();
            var baked = new List<GameObject>();
            foreach (var sk in skins)
            {
                var mesh = new Mesh { hideFlags = HideFlags.DontSave }; sk.BakeMesh(mesh, true);
                var go = new GameObject("Baked") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(inst.transform.parent, false);
                go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
                baked.Add(go); sk.enabled = false;
            }
            try { ShotRaw(inst.transform.parent.gameObject, outPath, dir); }
            finally
            {
                foreach (var go in baked) { Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(go); }
                foreach (var sk in skins) sk.enabled = true;
            }
        }

        static void ShotRaw(GameObject inst, string outPath, Vector3 dir)
        {
            var b = Bounds(inst);
            var camGo = new GameObject("ShotCam") { hideFlags = HideFlags.DontSave }; var cam = camGo.AddComponent<Camera>();
            var lightGo = new GameObject("ShotLight") { hideFlags = HideFlags.DontSave }; var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; lightGo.transform.rotation = Quaternion.Euler(40, 140, 0);
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .6f, .65f);
                float r0 = b.extents.magnitude; cam.fieldOfView = 30; cam.nearClipPlane = r0 * .05f; cam.farClipPlane = r0 * 20;
                cam.transform.position = b.center + dir.normalized * r0 * 4f; cam.transform.LookAt(b.center);
                var rt = new RenderTexture(320, 320, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(320, 320, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 320, 320), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            finally { Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); }
        }
    }
}
