using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// Kaiju B (2026-10-15): 巨兽级兵种. Re-uses an existing giant's configs and VAT, scaled up.
    /// Configs (small YAML) are created once and then only rewritten in place, so their GUIDs stay stable
    /// for the catalog. The scaled VAT (meshes/textures) lives under Generated/ (git-ignored) and is
    /// rebuilt and re-wired into the render config by this menu after a clean clone.
    /// </summary>
    public static class KaijuBuilder
    {
        public const string TemplateId = "roster-kaiju-dino";
        const string SrcLib = "Assets/Game/Content/Characters/Giants/Prepared02/Integrated/Library/";
        const string SrcName = "giant-dino";
        const string Root = "Assets/Game/Content/Characters/Kaiju";
        const string Dir = Root + "/Dino";
        const string GenDir = Dir + "/Generated";
        const string Name = "kaiju-dino";
        const string CatalogPath = "Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset";
        // The battlefield catalog the 1024/2048 maps actually use (prices are read from here).
        const string MainCatalogPath = "Assets/Game/Scenes/Catalog.asset";
        const string SrcTemplateId = "roster-giant-dino";
        const string FeaturedEntryId = "giants-dino";
        public const float Scale = 3f;
        const float PriceScale = 4.5f; // 2026-10-15: 5.0 tested weak (6 units at 61500); back to 4.5 (7 units)
        static readonly string[] Parts = { "", "_Spawn", "_Movement", "_Flocking", "_Animation", "_Combat", "_Render" };
        static readonly string[] Refs = { null, "spawnConfig", "movementConfig", "flockingConfig", "animationConfig", "combatConfig", "renderConfig" };
        internal static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Tools/巨兽/生成巨兽暴龙（配置与放大模型）")]
        public static void BuildDino()
        {
            Log.Clear();
            try
            {
                EnsureFolder("Assets/Game/Content/Characters", "Kaiju");
                EnsureFolder(Root, "Dino");
                EnsureFolder(Dir, "Generated");
                var configs = CopyConfigs();
                ApplyValues(configs);
                var vat = BuildVat(configs[6]);
                var render = new SerializedObject(configs[6]);
                render.FindProperty("vatProfile").objectReferenceValue = vat;
                render.ApplyModifiedPropertiesWithoutUndo();
                foreach (var c in configs) EditorUtility.SetDirty(c);
                RegisterInCatalog(configs[0], MainCatalogPath, null);
                RegisterInCatalog(configs[0], CatalogPath, FeaturedEntryId);
                BuildPolicy2048(configs[0]);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Log.AppendLine("OK");
            }
            catch (Exception e)
            {
                Log.AppendLine("FAILED: " + e);
                Debug.LogException(e);
            }
            Directory.CreateDirectory("Logs/Kaiju");
            File.WriteAllText("Logs/Kaiju/build.txt", Log.ToString(), new UTF8Encoding(false));
            Debug.Log("[Kaiju] " + Log);
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static ScriptableObject[] CopyConfigs()
        {
            var result = new ScriptableObject[Parts.Length];
            for (int i = 0; i < Parts.Length; i++)
            {
                string src = SrcLib + SrcName + Parts[i] + ".asset", dst = Dir + "/" + Name + Parts[i] + ".asset";
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(dst) == null)
                {
                    if (!AssetDatabase.CopyAsset(src, dst)) throw new InvalidOperationException("copy failed " + src);
                    Log.AppendLine("copied " + dst);
                }
                result[i] = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dst);
                if (result[i] == null) throw new InvalidOperationException("missing " + dst);
                result[i].name = Name + Parts[i];
            }
            var main = new SerializedObject(result[0]);
            for (int i = 1; i < Parts.Length; i++) main.FindProperty(Refs[i]).objectReferenceValue = result[i];
            main.ApplyModifiedPropertiesWithoutUndo();
            return result;
        }

        static void Set(SerializedObject so, string prop, float value)
        {
            var p = so.FindProperty(prop) ?? throw new InvalidOperationException("no property " + prop + " on " + so.targetObject.name);
            if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(value); else p.floatValue = value;
        }

        static void ApplyValues(ScriptableObject[] c)
        {
            var src = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(SrcLib + SrcName + "_Flocking.asset"));
            float baseRadius = src.FindProperty("agentRadius").floatValue;
            var srcCombat = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(SrcLib + SrcName + "_Combat.asset"));
            var srcAnim = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(SrcLib + SrcName + "_Animation.asset"));

            var main = new SerializedObject(c[0]);
            main.FindProperty("unitTypeName").stringValue = "巨兽暴龙（巨兽级）";
            main.ApplyModifiedPropertiesWithoutUndo();

            float radius = baseRadius * Scale; // 3.9 -> 11.7 m (about 17 m tall)
            var flock = new SerializedObject(c[3]);
            Set(flock, "agentRadius", radius);
            flock.ApplyModifiedPropertiesWithoutUndo();

            var combat = new SerializedObject(c[5]);
            Set(combat, "attackRange", srcCombat.FindProperty("attackRange").floatValue * Scale);
            Set(combat, "targetAcquireRadius", srcCombat.FindProperty("targetAcquireRadius").floatValue * Scale);
            Set(combat, "projectileOriginHeight", srcCombat.FindProperty("projectileOriginHeight").floatValue * Scale);
            Set(combat, "projectileTargetHeight", srcCombat.FindProperty("projectileTargetHeight").floatValue * Scale);
            Set(combat, "maxHp", 9000);
            Set(combat, "attackDamage", 150);
            Set(combat, "attackInterval", 1.3f);
            Set(combat, "meleeSplashRadius", 5f);
            combat.ApplyModifiedPropertiesWithoutUndo();

            var move = new SerializedObject(c[2]);
            Set(move, "maxSpeed", 7f);
            move.ApplyModifiedPropertiesWithoutUndo();

            // Bigger strides: the move clip's reference speed scales with the body so feet do not slide.
            var anim = new SerializedObject(c[4]);
            Set(anim, "moveReferenceSpeed", srcAnim.FindProperty("moveReferenceSpeed").floatValue * Scale);
            Set(anim, "moveStopSpeed", srcAnim.FindProperty("moveStopSpeed").floatValue * Scale);
            Set(anim, "moveStartSpeed", srcAnim.FindProperty("moveStartSpeed").floatValue * Scale);
            anim.ApplyModifiedPropertiesWithoutUndo();

            // 6 bodies of r 11.7 need >= 6 * 4r^2 = 3285 m^2 of footprint (policy packing rule).
            var spawn = new SerializedObject(c[1]);
            Set(spawn, "unitCount", 6);
            spawn.FindProperty("spawnSize").vector3Value = new Vector3(90f, 0f, 45f);
            spawn.ApplyModifiedPropertiesWithoutUndo();
            Log.AppendLine("values: radius " + radius + " range " + combat.FindProperty("attackRange").floatValue);
        }

        static UnityEngine.Object BuildVat(ScriptableObject render)
        {
            var srcRender = new SerializedObject(AssetDatabase.LoadAssetAtPath<ScriptableObject>(SrcLib + SrcName + "_Render.asset"));
            var srcVat = srcRender.FindProperty("vatProfile").objectReferenceValue as ScriptableObject;
            if (srcVat == null) throw new InvalidOperationException("source VAT profile missing");
            string path = GenDir + "/KaijuDino_VAT.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var vat = UnityEngine.Object.Instantiate(srcVat);
            vat.name = "KaijuDino_VAT";
            AssetDatabase.CreateAsset(vat, path);
            var so = new SerializedObject(vat);
            foreach (var m in new[] { "cleanMesh", "midLodMesh", "lowLodMesh" })
            {
                var p = so.FindProperty(m);
                if (p?.objectReferenceValue is Mesh mesh)
                {
                    var scaled = ScaleMesh(mesh);
                    AssetDatabase.AddObjectToAsset(scaled, vat);
                    p.objectReferenceValue = scaled;
                }
                else Log.AppendLine("no mesh " + m);
            }
            foreach (var t in new[] { "positionTexture", "midLodPositionTexture", "lowLodPositionTexture" })
            {
                var p = so.FindProperty(t);
                if (p?.objectReferenceValue is Texture2D tex)
                {
                    var scaled = ScaleTexture(tex);
                    AssetDatabase.AddObjectToAsset(scaled, vat);
                    p.objectReferenceValue = scaled;
                }
                else Log.AppendLine("no texture " + t);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vat);
            AssetDatabase.SaveAssets();
            Log.AppendLine("vat " + path + " from " + AssetDatabase.GetAssetPath(srcVat));
            return AssetDatabase.LoadMainAssetAtPath(path);
        }

        static Mesh ScaleMesh(Mesh src)
        {
            var m = UnityEngine.Object.Instantiate(src);
            m.name = "Kaiju_" + src.name;
            var v = m.vertices;
            if (v != null && v.Length > 0)
            {
                for (int i = 0; i < v.Length; i++) v[i] *= Scale;
                m.vertices = v;
                m.RecalculateBounds();
            }
            var b = src.bounds;
            m.bounds = new Bounds(b.center * Scale, b.size * Scale);
            Log.AppendLine("mesh " + src.name + " verts " + (v?.Length ?? 0) + " bounds " + m.bounds.size);
            return m;
        }

        static Color[] ReadPixels(Texture2D src, out string how)
        {
            try { how = "cpu"; return src.GetPixels(); }
            catch (Exception) { }
            how = "gpu";
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            rt.filterMode = FilterMode.Point;
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(src.width, src.height, TextureFormat.RGBAHalf, false, true);
            tmp.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tmp.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tmp.GetPixels();
            UnityEngine.Object.DestroyImmediate(tmp);
            return px;
        }

        static Texture2D ScaleTexture(Texture2D src)
        {
            var px = ReadPixels(src, out string how);
            // Self-check of the GPU path on a readable texture: both reads must agree.
            if (how == "cpu")
            {
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tmp = new Texture2D(src.width, src.height, TextureFormat.RGBAHalf, false, true);
                tmp.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); tmp.Apply(false, false);
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var g = tmp.GetPixels(); UnityEngine.Object.DestroyImmediate(tmp);
                float diff = 0; for (int i = 0; i < px.Length; i++) diff = Mathf.Max(diff, Mathf.Abs(px[i].r - g[i].r) + Mathf.Abs(px[i].g - g[i].g) + Mathf.Abs(px[i].b - g[i].b));
                Log.AppendLine("  gpu-vs-cpu max diff " + diff);
            }
            float maxAbs = 0;
            for (int i = 0; i < px.Length; i++)
            {
                px[i].r *= Scale; px[i].g *= Scale; px[i].b *= Scale;
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(px[i].r), Mathf.Abs(px[i].g), Mathf.Abs(px[i].b));
            }
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBAHalf, false, true)
            {
                name = "Kaiju_" + src.name, filterMode = src.filterMode, wrapMode = src.wrapMode, anisoLevel = src.anisoLevel
            };
            dst.SetPixels(px);
            dst.Apply(false, false);
            Log.AppendLine("tex " + src.name + " " + src.width + "x" + src.height + " fmt " + src.format + " read " + how + " max|xyz| " + maxAbs);
            if (src.format != TextureFormat.RGBAHalf) Log.AppendLine("  WARNING source format " + src.format);
            return dst;
        }

        public const string Policy1024 = "Assets/Game/Content/Battlefields/Map1024/RosterPolicy1024.asset";
        public const string Policy2048 = "Assets/Game/Content/Battlefields/Map2048/RosterPolicy2048.asset";
        const string Policy1024Guid = "a9c9d5598b518dc48a39ef6e1360e080";

        /// <summary>
        /// 2048 maps only (plan: 1024 maps keep their roster): the 1024 roster plus the kaiju, with a larger radius cap.
        /// Re-synced from the 1024 policy on every build. Battlefield2048Setup assigns it to the template scene;
        /// the template and already generated 2048 scenes are re-pointed here (only that one field).
        /// </summary>
        internal static void BuildPolicy2048(ScriptableObject kaiju)
        {
            var src = AssetDatabase.LoadAssetAtPath<WarSandboxRosterPolicy>(Policy1024) ?? throw new InvalidOperationException("1024 policy missing");
            var dst = AssetDatabase.LoadAssetAtPath<WarSandboxRosterPolicy>(Policy2048);
            if (dst == null)
            {
                if (!AssetDatabase.CopyAsset(Policy1024, Policy2048)) throw new InvalidOperationException("copy policy failed");
                dst = AssetDatabase.LoadAssetAtPath<WarSandboxRosterPolicy>(Policy2048);
                Log.AppendLine("created " + Policy2048);
            }
            var unit = (UnitTypeConfig)kaiju;
            var list = new System.Collections.Generic.List<UnitTypeConfig>(src.templates);
            // Keep every 2048-only template already registered (巨兽暴龙, the four dragons, ...), then this one.
            if (dst.templates != null)
                foreach (var extra in dst.templates)
                    if (extra != null && !list.Contains(extra)) list.Add(extra);
            if (!list.Contains(unit)) list.Add(unit);
            dst.templates = list.ToArray();
            dst.maximumUnits = src.maximumUnits; dst.maximumLargeUnits = src.maximumLargeUnits;
            dst.useTemplateSpawnDefaults = src.useTemplateSpawnDefaults; dst.restrictLargeToTeamZero = src.restrictLargeToTeamZero;
            float radius = src.maximumRadius;
            foreach (var t in dst.templates)
                if (t != null && t.flockingConfig != null) radius = Mathf.Max(radius, t.flockingConfig.agentRadius + .1f);
            dst.maximumRadius = radius;
            dst.explanation = "2048×2048 战场：1024 角色库 + 巨兽级（巨兽暴龙、四龙）。人数上限不是性能保证。";
            dst.name = "RosterPolicy2048";
            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssets();
            string guid = AssetDatabase.AssetPathToGUID(Policy2048);
            Log.AppendLine("policy2048 templates " + dst.templates.Length + " maxRadius " + dst.maximumRadius);

            var scenes = new System.Collections.Generic.List<string> { "Assets/Game/Content/Battlefields/Map2048/Green2048.unity" };
            if (Directory.Exists("Assets/Game/Content/Battlefields/Maps2048"))
                scenes.AddRange(Directory.GetFiles("Assets/Game/Content/Battlefields/Maps2048", "*.unity", SearchOption.AllDirectories));
            var open = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            foreach (var raw in scenes)
            {
                string path = raw.Replace('\\', '/');
                if (!File.Exists(path)) continue;
                for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
                {
                    var loaded = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                    if (loaded.path != path) continue;
                    foreach (var root in loaded.GetRootGameObjects())
                        foreach (var d in root.GetComponentsInChildren<WarSandboxRuntimeDeployment>(true))
                        { var so = new SerializedObject(d); so.FindProperty("rosterPolicy").objectReferenceValue = dst; so.ApplyModifiedPropertiesWithoutUndo(); }
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(loaded);
                    Log.AppendLine("policy -> " + path + " (open scene)");
                    path = null; break;
                }
                if (path == null) continue;
                string text = File.ReadAllText(path);
                string from = "rosterPolicy: {fileID: 11400000, guid: " + Policy1024Guid + ", type: 2}";
                string to = "rosterPolicy: {fileID: 11400000, guid: " + guid + ", type: 2}";
                int n = 0, at = -1; while ((at = text.IndexOf(from, at + 1, StringComparison.Ordinal)) >= 0) n++;
                if (n == 1) { File.WriteAllText(path, text.Replace(from, to)); Log.AppendLine("policy -> " + path); }
                else Log.AppendLine("policy skip " + path + " (" + (text.Contains(to) ? "already" : "matches " + n) + ")");
            }
            AssetDatabase.Refresh();
        }

        static void RegisterInCatalog(ScriptableObject config, string catalogPath, string featuredEntryId) =>
            RegisterTemplate(config, catalogPath, featuredEntryId, TemplateId, SrcTemplateId, PriceScale, true, Log);

        /// <summary>
        /// Adds (or refreshes) one catalog template entry by duplicating an existing source entry.
        /// copyPreview: reuse the source entry's unitPreview (same model) or clear it (new model).
        /// </summary>
        internal static void RegisterTemplate(ScriptableObject config, string catalogPath, string featuredEntryId,
            string templateId, string sourceTemplateId, float priceScale, bool copyPreview, StringBuilder log)
        {
            string TemplateId = templateId, SrcTemplateId = sourceTemplateId;
            float PriceScale = priceScale;
            var Log = log;
            var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>(catalogPath) ?? throw new InvalidOperationException("catalog missing " + catalogPath);
            var so = new SerializedObject(catalog);
            var templates = so.FindProperty("templates");
            int existing = -1, source = -1;
            for (int i = 0; i < templates.arraySize; i++)
            {
                string id = templates.GetArrayElementAtIndex(i).FindPropertyRelative("templateId").stringValue;
                if (id == TemplateId) existing = i;
                if (id == SrcTemplateId) source = i;
            }
            if (source < 0) throw new InvalidOperationException("source template missing in catalog");
            if (existing < 0)
            {
                templates.InsertArrayElementAtIndex(source); // duplicates the source entry at source+1
                existing = source + 1;
                Log.AppendLine("catalog: added " + TemplateId + " to " + catalogPath);
            }
            var e = templates.GetArrayElementAtIndex(existing);
            var s = templates.GetArrayElementAtIndex(existing == source + 1 ? source : source);
            e.FindPropertyRelative("templateId").stringValue = TemplateId;
            e.FindPropertyRelative("revision").intValue = 1;
            e.FindPropertyRelative("config").objectReferenceValue = config;
            e.FindPropertyRelative("hiddenFromSelection").boolValue = false;
            e.FindPropertyRelative("selectionNote").stringValue = "";
            e.FindPropertyRelative("unitPreview").objectReferenceValue = copyPreview ? s.FindPropertyRelative("unitPreview").objectReferenceValue : null;
            e.FindPropertyRelative("priceScale").floatValue = PriceScale;

            var entries = so.FindProperty("entries");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                if (featuredEntryId == null || entry.FindPropertyRelative("id").stringValue != featuredEntryId) continue;
                var featured = entry.FindPropertyRelative("featuredTemplateIds");
                bool has = false;
                for (int j = 0; j < featured.arraySize; j++) has |= featured.GetArrayElementAtIndex(j).stringValue == TemplateId;
                if (!has)
                {
                    featured.InsertArrayElementAtIndex(0);
                    featured.GetArrayElementAtIndex(0).stringValue = TemplateId;
                    Log.AppendLine("catalog: featured in " + featuredEntryId);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }
    }
}

