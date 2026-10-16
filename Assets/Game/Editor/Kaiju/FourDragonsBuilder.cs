using System;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Editor;
using MassEngine.Game.Editor;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// 四龙 (2026-10-15): the Asset Store pack "Four Evil Dragons HP" as 巨兽级 units on 2048 maps.
    /// Three flyers (CombatConfig.flightAltitude 20 m, fireballs, only ranged units and flyers hit them) and the
    /// Nightmare as a ground melee giant (the pack has no flight clips for it).
    ///
    /// Licence: Unity Asset Store EULA, the repository is public. Nothing derived from the pack is committed:
    /// the pack, the baked VAT (CharacterPipeline/Generated/FED_*) and the materials (ThirdParty/_Derived) stay
    /// local and git-ignored. Only these small configs (stats, GUID-stable) and this builder are committed.
    /// After a clean clone: import the free pack into Assets/FourEvilDragonsHP and run this menu.
    /// </summary>
    public static class FourDragonsBuilder
    {
        const string Pack = "Assets/FourEvilDragonsHP";
        const string Dir = "Assets/Game/Content/Characters/Kaiju/Dragons";
        const string MaterialDir = "Assets/ThirdParty/_Derived/FourEvilDragons";
        const string TemplateUnit = "Assets/Game/Content/Characters/Kaiju/Dino/kaiju-dino.asset";
        const string SourceTemplateId = "roster-kaiju-dino";
        const string MainCatalogPath = "Assets/Game/Scenes/Catalog.asset";
        const string CatalogPath = "Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset";
        public const float FlightAltitude = 20f;
        static readonly string[] Parts = { "", "_Spawn", "_Movement", "_Flocking", "_Animation", "_Combat", "_Render" };
        static readonly string[] Refs = { null, "spawnConfig", "movementConfig", "flockingConfig", "animationConfig", "combatConfig", "renderConfig" };
        static readonly StringBuilder Log = new StringBuilder();

        sealed class Def
        {
            public string key, title, mesh, prefab, animDir, idle, move, attack, death;
            public bool flying;
            public float length, radius, speed, priceScale;
            public int hp, damage, count;
            public float interval, splash, range;
            public Color fx; // fireball tint (CombatConfig.projectileFxColor), alpha 0 = legacy tracer
        }

        // First functional values, not a balance claim (flyers are immune to melee, priced above the 4.5 kaiju).
        static readonly Def[] Defs =
        {
            new Def { key = "usurper", title = "篡位者·红龙（飞行巨兽）", mesh = "DragonUsurperMesh", prefab = "DragonUsurper/Red", animDir = "DragonUsurper",
                idle = "FlyIdle", move = "FlyForward", attack = "FlyFlame", death = "Die", flying = true,
                length = 18f, radius = 8f, speed = 10f, hp = 7000, damage = 300, interval = 3f, splash = 7f, range = 110f, count = 6, priceScale = 6f, fx = new Color(1f, .35f, .05f, 1f) },
            new Def { key = "terrorbringer", title = "恐惧使者·绿龙（飞行巨兽）", mesh = "DragonTerrorBringerMesh", prefab = "DragonTerrorBringer/Green", animDir = "DragonTerrorBringer",
                idle = "FlyIdle", move = "FlyForward", attack = "FlyAttack", death = "die", flying = true,
                length = 17f, radius = 7.5f, speed = 11f, hp = 6000, damage = 260, interval = 3.3f, splash = 7f, range = 110f, count = 6, priceScale = 6f, fx = new Color(.45f, 1f, .15f, 1f) },
            new Def { key = "souleater", title = "噬魂者·黑龙（飞行巨兽）", mesh = "DragonSoulEaterMesh", prefab = "DragonSoulEater/Grey", animDir = "DragonSoulEater",
                idle = "Fly Float", move = "Fly Forward", attack = "Fly Fireball Shoot", death = "Die", flying = true,
                length = 15f, radius = 6.5f, speed = 12f, hp = 4500, damage = 120, interval = 1.2f, splash = 5f, range = 110f, count = 6, priceScale = 6f, fx = new Color(.6f, .2f, 1f, 1f) },
            new Def { key = "nightmare", title = "梦魇·蓝龙（地面巨兽）", mesh = "DragonTheNightmareMesh", prefab = "DragonNightmare/Blue", animDir = "DragonNightMare",
                idle = "idle01", move = "walk", attack = "Basic Attack", death = "die", flying = false,
                length = 15f, radius = 6.5f, speed = 8f, hp = 8000, damage = 140, interval = 1.2f, splash = 4.5f, range = 0f, count = 6, priceScale = 4.5f },
        };

        public static string TemplateIdOf(string key) => "roster-dragon-" + key;

        [MenuItem("Tools/巨兽/生成四龙（飞行巨兽，需要本地资源包）")]
        public static void BuildMissing() => BuildAll(false);

        [MenuItem("Tools/巨兽/重新烘焙四龙（全部重建）")]
        public static void RebuildAll() => BuildAll(true);

        [MenuItem("Tools/巨兽/四龙：只刷新数值（不重新烘焙）")]
        public static void RefreshValues()
        {
            Log.Clear();
            try
            {
                foreach (var d in Defs)
                {
                    var unit = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(Dir + "/dragon-" + d.key + ".asset");
                    if (unit == null || unit.combatConfig == null) { Log.AppendLine(d.key + ": not built, skipped"); continue; }
                    var clip = AssetDatabase.IsValidFolder(Pack) ? LoadClip(Pack + "/Animations/" + d.animDir + "/" + d.attack + ".fbx") : null;
                    ApplyValues(d, unit, unit.combatConfig.projectileTargetHeight * 2f, clip != null ? clip.length : 0f);
                    foreach (var o in new UnityEngine.Object[] { unit, unit.spawnConfig, unit.movementConfig, unit.flockingConfig, unit.animationConfig, unit.combatConfig, unit.renderConfig })
                        EditorUtility.SetDirty(o);
                }
                EnsureImpactFx();
                AssetDatabase.SaveAssets();
                Log.AppendLine("DONE");
            }
            catch (Exception e)
            {
                Log.AppendLine("FAILED: " + e);
                Debug.LogException(e);
            }
            Directory.CreateDirectory("Logs/FourDragons");
            File.WriteAllText("Logs/FourDragons/refresh.txt", Log.ToString(), new UTF8Encoding(false));
            Debug.Log("[FourDragons] " + Log);
        }

        const string ImpactMaterialPath = Dir + "/FireballImpact.mat";
        const string SharedProjectileRender = "Assets/Game/Settings/ProjectileRenderConfig.asset";

        /// <summary>
        /// Fireball look (2026-10-16): the shared projectile render config (all 2048 maps) gets an impact material so
        /// splash shots burst on impact. Types without projectileFxColor keep the legacy ring + flash.
        /// </summary>
        static void EnsureImpactFx()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ImpactMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/MassEngine/ProjectileImpact") ?? throw new InvalidOperationException("ProjectileImpact shader missing");
                mat = new Material(shader) { name = "FireballImpact" };
                AssetDatabase.CreateAsset(mat, ImpactMaterialPath);
                Log.AppendLine("impact material created " + ImpactMaterialPath);
            }
            var render = AssetDatabase.LoadAssetAtPath<MassEngine.Projectiles.ProjectileRenderConfig>(SharedProjectileRender);
            if (render == null) { Log.AppendLine("WARN: " + SharedProjectileRender + " missing"); return; }
            if (render.impactMaterial == null)
            {
                render.impactMaterial = mat;
                EditorUtility.SetDirty(render);
                Log.AppendLine("impact material assigned to " + SharedProjectileRender);
            }
            else Log.AppendLine("shared impact material kept: " + AssetDatabase.GetAssetPath(render.impactMaterial));
        }

        static void BuildAll(bool force)
        {
            Log.Clear();
            try
            {
                if (!AssetDatabase.IsValidFolder(Pack)) throw new InvalidOperationException("资源包不在 " + Pack + "：请先从 Asset Store 导入 Four Evil Dragons HP。");
                EnsureFolder(Dir);
                EnsureFolder(MaterialDir);
                foreach (var d in Defs)
                {
                    if (!force && File.Exists(Dir + "/dragon-" + d.key + ".asset") && AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(Dir + "/dragon-" + d.key + ".asset") != null)
                    {
                        Log.AppendLine(d.key + ": already built (use 重新烘焙 to rebuild)");
                        continue;
                    }
                    try { BuildOne(d); }
                    catch (Exception e) { Log.AppendLine("FAILED " + d.key + ": " + e); Debug.LogException(e); }
                }
                EnsureImpactFx();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Log.AppendLine("DONE");
            }
            catch (Exception e)
            {
                Log.AppendLine("FAILED: " + e);
                Debug.LogException(e);
            }
            Directory.CreateDirectory("Logs/FourDragons");
            File.WriteAllText("Logs/FourDragons/build.txt", Log.ToString(), new UTF8Encoding(false));
            Debug.Log("[FourDragons] " + Log);
        }

        static void BuildOne(Def d)
        {
            string meshPath = Pack + "/Mesh/" + d.mesh + ".fbx";
            ConfigureModel(meshPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath) ?? throw new InvalidOperationException("missing " + meshPath);
            var clips = new[] { d.idle, d.move, d.attack, d.death }.Select(n => LoadClip(Pack + "/Animations/" + d.animDir + "/" + n + ".fbx")).ToArray();

            // Materials (local only): the kaiju VAT materials with the pack's albedo.
            var template = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(TemplateUnit) ?? throw new InvalidOperationException("missing " + TemplateUnit);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "/Prefab/" + d.prefab + ".prefab");
            var albedo = prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null).Select(m => m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null)
                .FirstOrDefault(t => t != null) : null;
            if (albedo == null) throw new InvalidOperationException("no albedo texture on " + d.prefab);
            // The pack keeps other data in the albedo alpha; the VAT shaders output it as coverage. Import it opaque (local pack only).
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(albedo)) is TextureImporter ti && ti.alphaSource != TextureImporterAlphaSource.None)
            {
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.SaveAndReimport();
                Log.AppendLine(d.key + ": albedo alpha ignored " + ti.assetPath);
            }
            string matDir = MaterialDir + "/" + d.key;
            EnsureFolder(matDir);
            var render = template.renderConfig;
            var near = UpsertMaterial(render.nearMaterial, matDir + "/" + d.key + "_Near.mat", albedo);
            var mid = UpsertMaterial(render.midMaterial, matDir + "/" + d.key + "_Mid.mat", albedo);
            var far = render.farMaterial == render.midMaterial ? mid : UpsertMaterial(render.farMaterial, matDir + "/" + d.key + "_Far.mat", albedo);

            // Pipeline template (local): kaiju configs + these materials, ground values (no flight in the bake capture).
            // (Asset paths are never reused: deleted paths keep resolving to GUIDs, which the pipeline refuses.)
            string outputName = NextOutputName("FED_" + Capital(d.key));
            foreach (var old in AssetDatabase.GetSubFolders(matDir))
                if (Path.GetFileName(old).StartsWith("Template") || Path.GetFileName(old).StartsWith("Prepared")) AssetDatabase.DeleteAsset(old);
            string stamp = DateTime.Now.ToString("MMddHHmmss");
            string tplDir = matDir + "/Template_" + stamp;
            EnsureFolder(tplDir);
            var tpl = CharacterPipeline.CloneUnit(template, tplDir, "Template");
            tpl.renderConfig.nearMaterial = near; tpl.renderConfig.midMaterial = mid; tpl.renderConfig.farMaterial = far;
            CharacterPipeline.Save(tpl.renderConfig);

            // The pack's FBX carry unit-conversion scales on the mesh node and the root bone; the pipeline wants a unit-scale
            // hierarchy under one declared size root. Canonical copy (local only), same adapter as DragonBuilder.PrepareOne.
            string prepDir = matDir + "/Prepared_" + stamp;
            EnsureFolder(prepDir);
            var prepared = PrepareModel(model, clips, prepDir, near, d.flying, out var mapped);

            // Size: scale so the bind pose is d.length long (measured on the baked bind mesh, ratio only).
            var probe = (GameObject)UnityEngine.Object.Instantiate(model);
            Bounds bind;
            try
            {
                var skin = probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                var baked = new Mesh(); skin.BakeMesh(baked, true);
                var m = probe.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                var pts = baked.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
                bind = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) bind.Encapsulate(p);
                UnityEngine.Object.DestroyImmediate(baked);
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            float length = Mathf.Max(bind.size.x, bind.size.z);
            float height = d.length * bind.size.y / Mathf.Max(0.001f, length);

            var recipe = ScriptableObject.CreateInstance<CharacterRecipe>();
            recipe.name = "FED_" + d.key;
            recipe.model = prepared; recipe.sizeRootPath = "ScalePivot";
            recipe.idle = mapped[0]; recipe.move = mapped[1]; recipe.attack = mapped[2]; recipe.death = mapped[3];
            recipe.frameRate = 30;
            recipe.targetBodyHeight = height;
            recipe.groundBindFeet = true;
            recipe.agentRadius = d.radius;
            recipe.lowPolicy = CharacterLowLodPolicy.StandardClustering;
            recipe.lowVertexBudget = 500;
            recipe.fullVertexBudget = 20000;
            recipe.maxTextureMiB = 64;
            // Half VAT rounding grows with coordinate size: these 15-18 m bodies flap 25-35 m wingspans, measured 2.5-3.4 cm.
            // The gate is 5 cm (0.15 % of the wingspan, invisible at this size); the baked data is the same either way,
            // the only alternative would be smaller dragons.
            recipe.maximumPositionError = .05f;
            recipe.unitTemplate = tpl;
            recipe.outputName = outputName;
            Log.AppendLine(d.key + ": sizeRoot '" + recipe.sizeRootPath + "' bind " + bind.size.ToString("F2") + " -> height " + height.ToString("F2") + " output " + recipe.outputName);

            var report = CharacterPipeline.Run(recipe);
            Log.AppendLine(d.key + ": pipeline " + report.status + " full " + report.fullVertices + " low " + report.lowVertices +
                " frames " + report.totalFrames + " err " + (report.maxPositionError * 1000f).ToString("F2") + " mm tex " + (report.textureBytes / 1048576f).ToString("F1") + " MiB");
            string output = CharacterPipeline.Output(recipe);
            var final = AssetDatabase.LoadAssetAtPath<ScriptableObject>(output + "/FinalVAT.asset") ?? AssetDatabase.LoadAssetAtPath<ScriptableObject>(output + "/Full.asset");
            var generated = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(output + "/Unit.asset") ?? throw new InvalidOperationException("pipeline unit missing");

            // Committed configs: created once from the pipeline unit, afterwards only rewritten in place (stable GUIDs).
            var unit = UpsertConfigs(d.key, generated);
            unit.renderConfig.nearMaterial = near; unit.renderConfig.midMaterial = mid; unit.renderConfig.farMaterial = far;
            var bound = UnitTypeBinder.Bind(unit, final);
            if (!bound.IsValid) throw new InvalidOperationException(string.Join("\n", bound.Errors));
            ApplyValues(d, unit, height, clips[2].length);
            foreach (var o in new UnityEngine.Object[] { unit, unit.spawnConfig, unit.movementConfig, unit.flockingConfig, unit.animationConfig, unit.combatConfig, unit.renderConfig })
                EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            KaijuBuilder.RegisterTemplate(unit, MainCatalogPath, null, TemplateIdOf(d.key), SourceTemplateId, d.priceScale, false, Log);
            KaijuBuilder.RegisterTemplate(unit, CatalogPath, null, TemplateIdOf(d.key), SourceTemplateId, d.priceScale, false, Log);
            KaijuBuilder.Log.Clear();
            KaijuBuilder.BuildPolicy2048(unit);
            Log.Append(KaijuBuilder.Log);
            Log.AppendLine(d.key + ": OK");
        }

        static void ApplyValues(Def d, UnitTypeConfig u, float height, float attackClip)
        {
            u.unitTypeName = d.title;
            var c = u.combatConfig;
            c.maxHp = d.hp;
            c.attackDamage = d.damage;
            c.attackInterval = Mathf.Max(d.interval, attackClip * .9f);
            c.projectileTargetHeight = height * .5f;
            c.flightAltitude = d.flying ? FlightAltitude : 0f;
            c.projectileFxColor = d.flying ? d.fx : new Color(0f, 0f, 0f, 0f);
            if (d.flying)
            {
                // Fireballs from the air: area fire (range >= 100 m), splash on impact, straight line.
                c.projectileRange = d.range;
                c.targetAcquireRadius = d.range + 10f;
                c.attackRange = d.radius + 3f;
                c.projectileSpeed = 45f;
                c.projectileGravity = 0f; // straight fire bolts from the air, no lob (2026-10-16)
                c.projectileHitRadius = 1f;
                c.projectileMaxLifetime = 6f;
                c.projectileTrailLength = 6f;
                c.projectileOriginHeight = height * .55f;
                c.projectileSplashRadius = d.splash;
                c.attackReleasePhase = .5f;
                c.meleeSplashRadius = 0f;
                c.chargeDamageMultiplier = 1f;
            }
            else
            {
                c.projectileRange = 0f;
                c.projectileSplashRadius = 0f;
                c.attackRange = d.radius + 3f;
                c.targetAcquireRadius = Mathf.Max(c.targetAcquireRadius, d.radius * 3f);
                c.meleeSplashRadius = d.splash;
                c.projectileOriginHeight = height * .55f;
            }
            u.flockingConfig.agentRadius = d.radius;
            u.movementConfig.maxSpeed = d.speed;
            // Flight clips have no stride: play them at 1x at cruise speed. The Nightmare walks.
            var a = new SerializedObject(u.animationConfig);
            float reference = d.flying ? d.speed : d.speed * .75f;
            a.FindProperty("moveReferenceSpeed").floatValue = reference;
            var start = a.FindProperty("moveStartSpeed"); start.floatValue = Mathf.Min(start.floatValue, reference * .3f);
            var stop = a.FindProperty("moveStopSpeed"); stop.floatValue = Mathf.Min(stop.floatValue, start.floatValue * .8f);
            a.ApplyModifiedPropertiesWithoutUndo();
            var s = u.spawnConfig;
            s.unitCount = d.count;
            float side = 2f * d.radius * 1.2f;
            s.spawnSize = new Vector3(side * 3f, 0f, side * 2f); // >= count * 4 r^2 (policy packing rule)
            Log.AppendLine(d.key + ": values hp " + c.maxHp + " dmg " + c.attackDamage + " interval " + c.attackInterval.ToString("F2") +
                " range " + c.projectileRange + " flight " + c.flightAltitude + " radius " + d.radius + " height " + height.ToString("F2"));
        }

        static UnitTypeConfig UpsertConfigs(string key, UnitTypeConfig generated)
        {
            var sources = new ScriptableObject[] { generated, generated.spawnConfig, generated.movementConfig, generated.flockingConfig, generated.animationConfig, generated.combatConfig, generated.renderConfig };
            var result = new ScriptableObject[Parts.Length];
            string name = "dragon-" + key;
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
                else if (i == Parts.Length - 1)
                {
                    // Render config: keep the GUID, take the freshly bound values.
                    EditorUtility.CopySerialized(sources[i], existing);
                }
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

        static void ConfigureModel(string path)
        {
            var im = (ModelImporter)AssetImporter.GetAtPath(path) ?? throw new InvalidOperationException("no importer " + path);
            bool dirty = false;
            if (!im.isReadable) { im.isReadable = true; dirty = true; }
            if (im.animationType != ModelImporterAnimationType.Generic) { im.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (im.importBlendShapes) { im.importBlendShapes = false; dirty = true; }
            if (dirty) im.SaveAndReimport();
        }

        static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"))
            ?? throw new InvalidOperationException("no clip in " + path);

        static string NextOutputName(string stem)
        {
            for (int i = 1; i < 100; i++)
            {
                string name = stem + i.ToString("00");
                string path = CharacterPipeline.Root + "/Generated/" + name;
                // Deleted (rolled back) paths keep their GUID mapping, which the pipeline treats as taken.
                if (!Directory.Exists(path) && !File.Exists(path + ".meta") && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) return name;
            }
            throw new InvalidOperationException("no free output name for " + stem);
        }

        static GameObject PrepareModel(GameObject source, AnimationClip[] clips, string folder, Material material, bool flying, out AnimationClip[] mapped)
        {
            var stage = new GameObject("Inactive dragon preparation"); stage.SetActive(false);
            try
            {
                var raw = UnityEngine.Object.Instantiate(source, stage.transform, false); raw.name = "Raw";
                var root = new GameObject(source.name + "Prepared"); root.transform.SetParent(stage.transform, false);
                var pivot = new GameObject("ScalePivot").transform; pivot.SetParent(root.transform, false);
                var body = UnityEngine.Object.Instantiate(source, pivot); body.name = "Body";
                foreach (var a in raw.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                foreach (var a in body.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);

                var byPath = raw.GetComponentsInChildren<Transform>(true).ToDictionary(t => AnimationUtility.CalculateTransformPath(t, raw.transform));
                var restScales = byPath.ToDictionary(p => p.Key, p => p.Value.localScale);
                var transforms = body.GetComponentsInChildren<Transform>(true);
                var oldWorld = transforms.ToDictionary(t => t, t => t.localToWorldMatrix);
                var worldPos = transforms.ToDictionary(t => t, t => t.position);
                var worldRot = transforms.ToDictionary(t => t, t => t.rotation);
                foreach (var t in transforms)
                {
                    Vector3 sc = t.localScale; float mean = (sc.x + sc.y + sc.z) / 3f;
                    if (!(sc.x > 0 && sc.y > 0 && sc.z > 0 && (sc - Vector3.one * mean).magnitude / mean < .0002f)) throw new InvalidOperationException("non-uniform scale: " + t.name);
                    t.localScale = Vector3.one; t.SetPositionAndRotation(worldPos[t], worldRot[t]);
                }
                // The SoulEater's mesh node sits 226.656 m below its parent; skinning ignores the node, so park every leaf
                // skin node at its parent's origin (bindposes absorb it) and keep it there in every clip.
                var parked = new System.Collections.Generic.HashSet<Transform>();
                foreach (var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin.transform != body.transform && skin.transform.childCount == 0 && skin.transform.localPosition.sqrMagnitude > 1e-8f)
                    {
                        Log.AppendLine("  skin node " + skin.name + " parked from " + skin.transform.localPosition.ToString("F3"));
                        skin.transform.localPosition = Vector3.zero;
                        parked.Add(skin.transform);
                    }
                foreach (var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var src = skin.sharedMesh;
                    var mesh = UnityEngine.Object.Instantiate(src); mesh.name = skin.name + "Canonical";
                    // Rest vertices in the bind pose (any invertible matrix keeps the deformation exact, the bindposes absorb it);
                    // bone[0] * bindpose[0] is the skin's bind-time placement, so the static mesh (low LOD, bounds) is where the
                    // skinned body is, not where the FBX node parked the raw vertices.
                    int anchor = skin.rootBone != null ? Array.IndexOf(skin.bones, skin.rootBone) : -1;
                    if (anchor < 0) anchor = 0;
                    Matrix4x4 vertex = skin.transform.worldToLocalMatrix * oldWorld[skin.bones[anchor]] * src.bindposes[anchor];
                    Matrix4x4 normal = vertex.inverse.transpose;
                    mesh.vertices = src.vertices.Select(v => vertex.MultiplyPoint3x4(v)).ToArray();
                    mesh.normals = src.normals.Select(n => normal.MultiplyVector(n).normalized).ToArray();
                    Log.AppendLine("  rest mesh centre " + vertex.MultiplyPoint3x4(src.bounds.center).ToString("F2"));
                    if (src.tangents.Length == src.vertexCount)
                        mesh.tangents = src.tangents.Select(tg => { var v3 = vertex.MultiplyVector(new Vector3(tg.x, tg.y, tg.z)).normalized; return new Vector4(v3.x, v3.y, v3.z, tg.w); }).ToArray();
                    if (src.colors32.Length == src.vertexCount)
                    {
                        // Some pack meshes carry unused vertex colours (alpha 0); the VAT shaders multiply them in. Neutral white.
                        Log.AppendLine("  vertex colours replaced by white (" + src.name + ")");
                        mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), src.vertexCount).ToArray();
                    }
                    var binds = src.bindposes;
                    mesh.bindposes = skin.bones.Select((bone, j) => bone.worldToLocalMatrix * oldWorld[bone] * binds[j] * vertex.inverse).ToArray();
                    // Winding check: some pack meshes are authored inside-out (double-sided source material); the VAT shaders cull back faces.
                    {
                        var vs = mesh.vertices; var ns = mesh.normals; int inverted = 0, total = 0;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            var tri = mesh.GetTriangles(sub);
                            for (int i = 0; i + 2 < tri.Length; i += 3)
                            {
                                var face = Vector3.Cross(vs[tri[i + 1]] - vs[tri[i]], vs[tri[i + 2]] - vs[tri[i]]);
                                if (face.sqrMagnitude < 1e-12f) continue;
                                total++; if (Vector3.Dot(face, ns[tri[i]] + ns[tri[i + 1]] + ns[tri[i + 2]]) < 0f) inverted++;
                            }
                        }
                        Log.AppendLine("  winding: " + inverted + "/" + total + " inverted, det " + vertex.determinant.ToString("F4") + ", submeshes " + mesh.subMeshCount);
                        if (inverted * 2 > total)
                        {
                            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                            {
                                var tri = mesh.GetTriangles(sub);
                                for (int i = 0; i + 2 < tri.Length; i += 3) { int t0 = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t0; }
                                mesh.SetTriangles(tri, sub);
                            }
                            Log.AppendLine("  winding flipped to outward");
                        }
                    }
                    mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, folder + "/" + mesh.name + ".asset");
                    skin.sharedMesh = mesh; skin.localBounds = mesh.bounds; skin.sharedMaterial = material;
                }
                if (body.GetComponentsInChildren<MeshRenderer>(true).Length != 0) throw new InvalidOperationException("unexpected rigid renderer");

                var bindLocalPos = transforms.Select(t => t.localPosition).ToArray();
                var bindLocalRot = transforms.Select(t => t.localRotation).ToArray();
                var bodySkin = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                var bakeTmp = new Mesh();
                Bounds Pose()
                {
                    bodySkin.BakeMesh(bakeTmp, false);
                    var m = bodySkin.transform.localToWorldMatrix;
                    var v = bakeTmp.vertices; var b = new Bounds(m.MultiplyPoint3x4(v[0]), Vector3.zero);
                    for (int i = 1; i < v.Length; i++) b.Encapsulate(m.MultiplyPoint3x4(v[i]));
                    return b;
                }
                Bounds bindPose = Pose();
                Log.AppendLine("  bind pose " + bindPose.min.ToString("F2") + " .. " + bindPose.max.ToString("F2"));

                string[] actions = { "Idle", "Move", "Attack", "Death" };
                string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                var preparedPaths = transforms.ToDictionary(t => AnimationUtility.CalculateTransformPath(t, body.transform));
                var allKeys = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Keyframe>[]>[4];
                var frameMinY = new System.Collections.Generic.List<float>[4];
                for (int action = 0; action < 4; action++)
                {
                    var sourceClip = clips[action];
                    foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
                    {
                        if (binding.type != typeof(Transform) || !byPath.ContainsKey(binding.path)) continue; // unmapped bones: nothing to drive
                        if (!binding.propertyName.StartsWith("m_LocalScale.")) continue;
                        int axis = binding.propertyName.EndsWith(".x") ? 0 : binding.propertyName.EndsWith(".y") ? 1 : 2;
                        float expected = restScales[binding.path][axis];
                        foreach (var key in AnimationUtility.GetEditorCurve(sourceClip, binding).keys)
                            if (Mathf.Abs(key.value / expected - 1f) >= .0002f) throw new InvalidOperationException("animated scale on " + binding.path + " in " + sourceClip.name);
                    }
                    int frames = Mathf.Max(1, Mathf.CeilToInt(sourceClip.length * 30f));
                    var keys = preparedPaths.ToDictionary(p => p.Key, p => Enumerable.Range(0, 7).Select(_ => new System.Collections.Generic.List<Keyframe>()).ToArray());
                    frameMinY[action] = new System.Collections.Generic.List<float>();
                    Bounds clipBounds = default;
                    for (int frame = 0; frame <= frames; frame++)
                    {
                        float time = Mathf.Min(frame / 30f, sourceClip.length);
                        sourceClip.SampleAnimation(raw, time);
                        foreach (var pair in preparedPaths)
                        {
                            var target = pair.Value;
                            if (byPath.TryGetValue(pair.Key, out var original)) target.SetPositionAndRotation(original.position, original.rotation);
                            target.localScale = Vector3.one;
                            if (parked.Contains(target)) target.localPosition = Vector3.zero;
                            Vector3 p = target.localPosition; Quaternion q = target.localRotation;
                            float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                            for (int c = 0; c < 7; c++) keys[pair.Key][c].Add(new Keyframe(time, values[c]));
                        }
                        var pose = Pose();
                        frameMinY[action].Add(pose.min.y);
                        if (frame == 0) clipBounds = pose; else clipBounds.Encapsulate(pose);
                    }
                    allKeys[action] = keys;
                    Log.AppendLine("  " + actions[action] + " (" + sourceClip.name + ") " + clipBounds.min.ToString("F2") + " .. " + clipBounds.max.ToString("F2"));
                }
                UnityEngine.Object.DestroyImmediate(bakeTmp);

                // Flyers: the pack's flight clips hover metres above the model origin. Altitude is the engine's job
                // (flightAltitude), so bring the lowest flying point (idle/move/attack together, no pops between them)
                // down to the bind ground: smaller VAT coordinates (Half precision) and a framed capture.
                // Death keeps its own fall but never goes below the ground.
                float flightShift = 0f;
                if (flying)
                {
                    flightShift = Mathf.Max(0f, Enumerable.Range(0, 3).Min(i => frameMinY[i].Min()) - bindPose.min.y);
                    Log.AppendLine("  flight shift " + flightShift.ToString("F2") + " m (source units)");
                }

                mapped = new AnimationClip[4];
                for (int action = 0; action < 4; action++)
                {
                    var keys = allKeys[action];
                    if (flightShift > 0f)
                    {
                        var ys = keys[""][1];
                        for (int k = 0; k < ys.Count; k++)
                        {
                            float shift = action < 3 ? flightShift : Mathf.Clamp(frameMinY[3][k] - bindPose.min.y, 0f, flightShift);
                            var key = ys[k]; key.value -= shift; ys[k] = key;
                        }
                    }
                    var clip = new AnimationClip { name = actions[action], frameRate = 30f };
                    foreach (var pair in keys)
                        for (int c = 0; c < 7; c++)
                        {
                            var curve = new AnimationCurve(pair.Value[c].ToArray());
                            for (int k = 0; k < curve.length; k++)
                            {
                                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                            }
                            string path = "ScalePivot/Body" + (pair.Key.Length == 0 ? "" : "/" + pair.Key);
                            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), props[c]), curve);
                        }
                    clip.EnsureQuaternionContinuity();
                    var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = action != 3; AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AssetDatabase.CreateAsset(clip, folder + "/" + actions[action] + ".anim");
                    mapped[action] = clip;
                }
                // Back to the bind pose before saving the prefab (sampling left the last death frame on it).
                for (int i = 0; i < transforms.Length; i++) { transforms[i].localPosition = bindLocalPos[i]; transforms[i].localRotation = bindLocalRot[i]; }
                root.transform.SetParent(null, false);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + root.name + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(stage); }
        }

        static string Capital(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
