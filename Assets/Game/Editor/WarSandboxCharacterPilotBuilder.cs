using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MassEngine.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>KayKit Knight isolated recipe. Uses the production baker/binder; never edits shipped content.</summary>
    public static class WarSandboxCharacterPilotBuilder
    {
        private const string DefaultRoot = "Assets/Game/Content/Characters/CharacterPilotPlayable";
        private const string SourceScene = "Assets/Game/Experiments/LegacyScenes/WarSandbox.unity";
        public const string Source = "Assets/Art/Source/CharacterPilot/KayKitKnight";
        public const string Evidence = "Logs/AgentCharacterPilot";
        private const string Notice = "Knight pilot | KayKit by Kay Lousberg (CC0) | isolated 64 vs 64 | 30 FPS functional preview";

        [Serializable]
        private sealed class RecipeReport
        {
            public string root, sourceModel, preparedPrefab, profile, trialUnit, scenario, catalog, menuScene, battlefieldScene;
            public string unityVersion, createdUtc;
            public string[] clips, stages;
            public int fullVertices, lowVertices, frames, startingUnits = 128;
            public bool humanAcceptance;
        }

        [MenuItem("MassEngine/Model Trial/KayKit Knight/Create Isolated Trial")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string root = Root();
            EnsureFolder(root);
            EnsureFolder(root + "/Prepared");
            EnsureFolder(root + "/Settings");
            var savedSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Prepared/KayKitKnightPrepared.prefab");
                if (prefab == null) prefab = CreatePreparedPrefab(root + "/Prepared");
                VATProfile profile = AssetDatabase.LoadAssetAtPath<VATProfile>(root + "/KayKitKnightVAT.asset");
                string[] clipPaths = { root + "/Prepared/Idle.anim", root + "/Prepared/Move.anim", root + "/Prepared/Attack.anim", root + "/Prepared/Death.anim" };
                AnimationClip[] clips = clipPaths.Select(Load<AnimationClip>).ToArray();
                if (profile == null)
                {
                    using (var bake = VatBaker.Bake(new VatBakeRequest
                    {
                        model = prefab, idle = clips[0], move = clips[1], attack = clips[2], death = clips[3], frameRate = 30,
                        bakeLowLod = true, lowLodRatio = .25f, lowLodMaxVertices = 1400,
                        extraRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true)
                    })) profile = bake.SaveNew(root + "/KayKitKnightVAT.asset");
                }
                if (!VatProfileValidation.TryValidate(profile, out string profileError)) throw new BuildFailedException(profileError);

                string scenarioPath = root + "/Settings/TrialScenario.asset";
                var scenario = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(scenarioPath);
                UnitTypeConfig trial;
                if (scenario == null)
                {
                    // Reserve this phase's fixed names before creating anything. A retry or a
                    // wrong-type existing asset must not overwrite a user's saved material/config.
                    RequireNewAssetPath(scenarioPath);
                    RequireNewAssetPath(root + "/Settings/KayKitKnightNear.mat");
                    RequireNewAssetPath(root + "/Settings/KayKitKnightMid.mat");
                    scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
                    AssetDatabase.CreateAsset(scenario, scenarioPath);
                    var template = Load<UnitTypeConfig>("Assets/Game/Settings/AttackerUnitConfig.asset");
                    trial = CreateUnit(scenario, template, "KayKitKnightTrial", 0, profile, root + "/Settings");
                    var enemy = CreateUnit(scenario, template, "TrialOpponent", 1, null, root + "/Settings");
                    trial.combatConfig.projectileRange = 0; enemy.combatConfig.projectileRange = 0;
                    Save(trial.combatConfig); Save(enemy.combatConfig);
                    // Source combat/movement settings are unchanged. Only the private deployment is small.
                    SetSpawn(trial, new Vector3(-18, 0, 0));
                    SetSpawn(enemy, new Vector3(18, 0, 0));
                    trial.unitTypeName = "KayKit 剑盾骑士";
                    ConfigurePrivateTuning(trial, root + "/Settings");
                    enemy.unitTypeName = "内置士兵对照";
                    var atlas = Load<Texture2D>(Source + "/knight_texture.png");
                    trial.renderConfig.nearMaterial = CreateMaterial(template.renderConfig.nearMaterial, atlas, root + "/Settings/KayKitKnightNear.mat");
                    trial.renderConfig.midMaterial = CreateMaterial(template.renderConfig.midMaterial, atlas, root + "/Settings/KayKitKnightMid.mat");
                    trial.renderConfig.farMaterial = trial.renderConfig.midMaterial;
                    Save(trial.renderConfig); Save(trial); Save(enemy); Save(scenario);
                }
                else
                {
                    if (scenario.unitTypes == null || scenario.unitTypes.Length != 2) throw new BuildFailedException("Unexpected existing trial roster.");
                    trial = scenario.unitTypes[0];
                }
                foreach (var unit in scenario.unitTypes)
                {
                    var validation = UnitTypeBinder.ValidateBinding(unit);
                    if (!validation.IsValid) throw new BuildFailedException(string.Join("\n", validation.Errors));
                    CheckDiskReference(AssetDatabase.GetAssetPath(unit), "spawnConfig");
                    CheckDiskReference(AssetDatabase.GetAssetPath(unit), "renderConfig");
                }
                CheckDiskReference(AssetDatabase.GetAssetPath(trial.renderConfig), "vatProfile");

                string battlefield = root + "/ModelTrialBattlefield.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(battlefield) == null)
                {
                    CopyNew(SourceScene, battlefield);
                    var scene = EditorSceneManager.OpenScene(battlefield, OpenSceneMode.Single);
                    var manager = Object.FindFirstObjectByType<MassEngineManager>();
                    if (manager == null) throw new BuildFailedException("Copied battlefield has no manager.");
                    manager.scenarioConfig = scenario;
                    manager.battleStarted = false;
                    Camera camera = manager.cullingCamera;
                    if (camera == null) throw new BuildFailedException("Copied battlefield has no camera.");
                    camera.transform.position = new Vector3(5, 23, -32);
                    camera.transform.LookAt(new Vector3(5, 0, 0)); camera.fieldOfView = 65;
                    var controls = camera.GetComponent<MyCameraManager>();
                    controls.MaxZoomDistance = 70; controls.MinZoomDistance = 1.5f;
                    if (manager.lodCenter != null && manager.lodCenter != camera.transform) manager.lodCenter.position = camera.transform.position;
                    EditorSceneManager.SaveScene(scene);
                }
                // Opening a scene unloads unused native assets; managed stack locals are not asset roots.
                // Re-resolve persisted references after the scene boundary, rather than bind stale wrappers.
                scenario = Load<ScenarioConfig>(scenarioPath);
                trial = scenario.unitTypes[0];
                string catalogPath = root + "/Settings/TrialCatalog.asset";
                var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(catalogPath);
                if (catalog == null)
                {
                    RequireNewAssetPath(catalogPath);
                    catalog = ScriptableObject.CreateInstance<WarSandboxBattlefieldCatalog>();
                    catalog.defaultEntryId = "kaykit-knight-pilot";
                    catalog.entries = new[] { new WarSandboxBattlefieldEntry
                    {
                        id = catalog.defaultEntryId, displayName = "KayKit 剑盾骑士 · 独立试点", scenePath = battlefield,
                        rules = Load<WarSandboxBattlefieldConfig>("Assets/Game/Settings/BattlefieldRules_A_Annihilation.asset")
                    } };
                    catalog.templates = new[] {
                        new WarSandboxUnitTemplateEntry { templateId = "kaykit-knight-v1", revision = 2, config = trial },
                        new WarSandboxUnitTemplateEntry { templateId = "kaykit-knight-opponent", revision = 2, config = scenario.unitTypes[1] }
                    };
                    AssetDatabase.CreateAsset(catalog, catalogPath);
                }
                AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                catalog = Load<WarSandboxBattlefieldCatalog>(catalogPath);
                if (!catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out string error) || !catalog.TryValidateTemplates(out error))
                    throw new BuildFailedException(error);
                string menu = root + "/ModelTrialMenu.unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(menu) == null)
                {
                    CopyNew(WarSandboxEntryBuilder.MenuScenePath, menu);
                    var scene = EditorSceneManager.OpenScene(menu, OpenSceneMode.Single);
                    var entry = Object.FindFirstObjectByType<WarSandboxSceneSession>();
                    if (entry == null) throw new BuildFailedException("Copied menu has no session.");
                    catalog = Load<WarSandboxBattlefieldCatalog>(catalogPath);
                    scenario = Load<ScenarioConfig>(scenarioPath);
                    trial = scenario.unitTypes[0];
                    entry.catalog = catalog;
                    entry.enterDefaultOnStart = true;
                    var smoke = new GameObject("M5Trial Automated Verification (opt-in)").AddComponent<WarSandboxModelTrialSmoke>();
                    smoke.session = entry;
                    smoke.expectedCatalog = catalog;
                    smoke.expectedSourceScenario = scenario;
                    smoke.expectedTemplate = trial;
                    smoke.battlefieldId = "kaykit-knight-pilot";
                    smoke.templateId = "kaykit-knight-v1";
                    smoke.templateRevision = 2;
                    smoke.planSlot = "KayKitKnightPilot";
                    new GameObject("Character Pilot Frame Cap").AddComponent<WarSandboxCharacterPilotRuntime>();
                    CreateNotice(entry.transform);
                    EditorSceneManager.SaveScene(scene);
                }
                prefab = Load<GameObject>(root + "/Prepared/KayKitKnightPrepared.prefab");
                profile = Load<VATProfile>(root + "/KayKitKnightVAT.asset");
                scenario = Load<ScenarioConfig>(scenarioPath);
                trial = scenario.unitTypes[0];
                var report = new RecipeReport
                {
                    root = root, sourceModel = Source + "/Knight.fbx", preparedPrefab = AssetDatabase.GetAssetPath(prefab),
                    profile = AssetDatabase.GetAssetPath(profile), trialUnit = AssetDatabase.GetAssetPath(trial), scenario = scenarioPath,
                    catalog = catalogPath, menuScene = menu, battlefieldScene = battlefield, unityVersion = Application.unityVersion,
                    createdUtc = DateTime.UtcNow.ToString("O"), clips = clipPaths, fullVertices = profile.cleanMesh.vertexCount,
                    lowVertices = profile.HasLowLod ? profile.lowLodMesh.vertexCount : 0, frames = profile.totalFrameCount,
                    stages = new[] { "Generic rig paths verified; real clips remapped", "single author atlas; sword/shield attached to hand slots",
                        "production VatBaker.Bake and SaveNew", "production UnitTypeBinder.CreateUnitType and ValidateBinding",
                        "raw YAML persistent references", "isolated Scenario/Catalog/menu/battlefield" }, humanAcceptance = false
                };
                Directory.CreateDirectory("Logs/AgentCharacterPilot");
                File.WriteAllText("Logs/AgentCharacterPilot/recipe.json", JsonUtility.ToJson(report, true));
                CaptureAppearance(root, trial, profile);
                Debug.Log("CHARACTER_PILOT_RECIPE_READY " + root);
            }
            finally
            {
                if (savedSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(savedSetup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [MenuItem("MassEngine/Model Trial/KayKit Knight/Build Windows Trial")]
        public static void BuildWindows()
        {
            string root = Root();
            string menu = root + "/ModelTrialMenu.unity", battle = root + "/ModelTrialBattlefield.unity";
            Load<SceneAsset>(menu); Load<SceneAsset>(battle);
            // A fresh Unity invocation re-resolves all YAML references before the standalone build.
            var catalog = Load<WarSandboxBattlefieldCatalog>(root + "/Settings/TrialCatalog.asset");
            if (!catalog.TryValidate(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null, out string catalogError) ||
                !catalog.TryValidateTemplates(out catalogError)) throw new BuildFailedException(catalogError);
            if (catalog.defaultEntryId != "kaykit-knight-pilot" || catalog.templates[0].templateId != "kaykit-knight-v1")
                throw new BuildFailedException("Persisted pilot IDs changed.");
            if (catalog.templates.Any(t => t.revision != 2 || t.config.combatConfig.projectileRange != 0))
                throw new BuildFailedException("This pilot must be explicit melee, template revision 2.");
            var profile = (VATProfile)Load<ScenarioConfig>(root + "/Settings/TrialScenario.asset").unitTypes[0].renderConfig.vatProfile;
            if (!VatProfileValidation.TryValidate(profile, out string profileError)) throw new BuildFailedException(profileError);
            var scenario = Load<ScenarioConfig>(root + "/Settings/TrialScenario.asset");
            foreach (var unit in scenario.unitTypes)
            {
                var validation = UnitTypeBinder.ValidateBinding(unit);
                if (!validation.IsValid) throw new BuildFailedException(string.Join("\n", validation.Errors));
            }
            string output = Path.GetFullPath("Builds/CharacterPilot-Knight-20260930-02");
            if (Directory.Exists(output)) throw new BuildFailedException("Fresh build directory required.");
            Directory.CreateDirectory(output);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { menu, battle }, locationPathName = Path.Combine(output, "KnightPilot.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Knight pilot build failed: " + result.summary.result);
            CopyLicense(output);
            WritePlayerSupportFiles(output);
            File.WriteAllText(Path.Combine(output, "README.txt"),
                "KayKit Knight isolated production-pipeline trial. Model/animations: Kay Lousberg, CC0.\n" +
                "Launch Start-Knight.cmd. 64 vs 64 default, 30 FPS functional cap; not a performance benchmark.\n" +
                "Existing sandbox content/builds unchanged. Automated verification is not human acceptance.\n" +
                "New Knight CC0 licenses are under KnightLicenses. Other existing game assets keep their original terms.\n" +
                "Internal validation build; not V1 release or legal clearance.\n");
            Debug.Log("CHARACTER_PILOT_BUILD_READY " + output);
        }

        private static void CaptureAppearance(string root, UnitTypeConfig unit, VATProfile profile, string suffix = "")
        {
            string output = "Logs/AgentCharacterPilot/appearance" + suffix;
            Directory.CreateDirectory(output);
            var runtime = ResolvedUnitTypeRuntime.Resolve(unit, 1f);
            var evidence = new List<string>();
            using (var capture = new VatAppearanceRegressionCapture(VatAppearanceRegression.CombinedBounds(profile), 384))
                for (int lod = 0; lod < 3; lod++)
                    foreach (AgentState state in new[] { AgentState.Idle, AgentState.Move, AgentState.Attack, AgentState.Dead })
                    {
                        var clip = VatAppearanceRegression.GetClip(profile, state);
                        var samples = new List<Color32[]>();
                        int sample = 0;
                        foreach (int frame in VatAppearanceRegression.SampleLocalFrames(clip))
                        {
                            Color32[] pixels = capture.Capture(runtime, lod, state, (frame + .25f) / clip.frameRate);
                            if (!VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels), out string error))
                                throw new BuildFailedException("Trial appearance " + lod + "/" + state + ": " + error);
                            samples.Add(pixels);
                            var texture = new Texture2D(capture.Size, capture.Size, TextureFormat.RGBA32, false);
                            try
                            {
                                texture.SetPixels32(pixels); texture.Apply(false, false);
                                File.WriteAllBytes(output + "/lod" + lod + "-" + state + "-" + sample++ + ".png", texture.EncodeToPNG());
                            }
                            finally { Object.DestroyImmediate(texture); }
                        }
                        if (!VatAppearanceRegressionImageChecks.HasMotion(samples, out int changed))
                            throw new BuildFailedException("Trial clip is frozen: " + lod + "/" + state);
                        evidence.Add(lod + "/" + state + ": changed pixels=" + changed);
                    }
            File.WriteAllText(output + "/checks.txt", string.Join("\n", evidence));
        }

        private static void CreateNotice(Transform parent)
        {
            var go = new GameObject("KayKitKnight License Notice", typeof(Canvas));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var label = new GameObject("License", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var rect = label.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(12, 2); rect.sizeDelta = new Vector2(800, 20);
            var text = label.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = Notice; text.fontSize = 12; text.color = Color.white; text.raycastTarget = false;
            var outline = label.AddComponent<Outline>(); outline.effectColor = Color.black;
        }

        private static UnitTypeConfig CreateUnit(ScenarioConfig scenario, UnitTypeConfig template, string name, int team, VATProfile profile, string directory)
        {
            var unit = UnitTypeBinder.CreateUnitType(new UnitTypeCreationRequest
            { scenario = scenario, template = template, unitTypeName = name, teamId = team, profile = profile, directory = directory, exclusiveRender = true }, out string error);
            if (unit == null) throw new BuildFailedException(error);
            return unit;
        }
        private static void SetSpawn(UnitTypeConfig unit, Vector3 center)
        { unit.spawnConfig.unitCount = 64; unit.spawnConfig.spawnCenter = center; unit.spawnConfig.spawnSize = Vector3.zero;
            unit.spawnConfig.formationDensity = .5f; unit.spawnConfig.formationAspect = 2; Save(unit.spawnConfig); }
        private static Material CreateMaterial(Material source, Texture atlas, string path)
        {
            RequireNewAssetPath(path);
            var result = new Material(source) { name = Path.GetFileNameWithoutExtension(path), enableInstancing = true };
            result.SetTexture("_BaseMap", atlas); result.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(result, path); return result;
        }
        private static void RequireNewAssetPath(string path)
        {
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta") ||
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new BuildFailedException("Refusing to overwrite an existing trial asset: " + path);
        }
        private static void WritePlayerSupportFiles(string output)
        {
            Directory.CreateDirectory(output);
            File.Copy("Docs/CharacterPilot-20260930/开始验收.md", Path.Combine(output, "开始验收.md"));
            File.WriteAllText(Path.Combine(output, "Start-Knight.cmd"),
                "@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0KnightPilot.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 --war-sandbox-settings-file=\"%~dp0PilotData\\settings.json\"\r\n",
                new System.Text.UTF8Encoding(false));
        }
        private static void Save(Object value) { EditorUtility.SetDirty(value); AssetDatabase.SaveAssetIfDirty(value); }
        private static void CopyNew(string source, string target)
        { if (File.Exists(target) || !AssetDatabase.CopyAsset(source, target)) throw new BuildFailedException("Refusing failed/overwriting copy: " + target); }
        private static T Load<T>(string path) where T : Object
        { var value = AssetDatabase.LoadAssetAtPath<T>(path); if (value == null) throw new BuildFailedException("Missing " + path); return value; }
        private static void CheckDiskReference(string path, string field)
        { if (File.ReadAllText(path).Contains(field + ": {fileID: 0}")) throw new BuildFailedException("Null disk reference " + path + "/" + field); }
        private static void EnsureFolder(string path)
        { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
        private static string Root() => DefaultRoot;
        private static string Argument(string prefix)
        { foreach (string value in Environment.GetCommandLineArgs()) if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length).Trim('"'); return null; }
        private static void CopyLicense(string output)
        {
            string dest = Path.Combine(output, "KnightLicenses"); Directory.CreateDirectory(dest);
            foreach (string name in new[] { "Characters-License.txt", "Animations-License.txt" })
                File.Copy(Source + "/" + name, Path.Combine(dest, name));
            File.Copy("Docs/CharacterPilot-20260930/selected-source-inventory.json", Path.Combine(dest, "selected-source-inventory.json"));
            File.WriteAllText(Path.Combine(dest, "SOURCE.txt"), "KayKit by Kay Lousberg\nhttps://kaylousberg.itch.io/kaykit-adventurers\nhttps://kaylousberg.itch.io/kaykit-character-animations\nOnly the Knight source/animations carry CC0. Existing engine/game dependencies retain their own license terms.\n");
        }

        public static void FinalizePlayability()
        {
            // Latest attacker template is ranged. A visual sword does not make its copied combat config melee.
            var scenario = Load<ScenarioConfig>(DefaultRoot + "/Settings/TrialScenario.asset");
            foreach (var unit in scenario.unitTypes)
            {
                if (!AssetDatabase.GetAssetPath(unit.combatConfig).StartsWith(DefaultRoot + "/", StringComparison.Ordinal))
                    throw new BuildFailedException("Must not mutate a shared source combat config.");
                unit.combatConfig.projectileRange = 0;
                unit.unitTypeClassName = "MassEngine.DefaultSwordUnit";
                Save(unit.combatConfig); Save(unit);
            }
            var catalog = Load<WarSandboxBattlefieldCatalog>(DefaultRoot + "/Settings/TrialCatalog.asset");
            foreach (var entry in catalog.templates) entry.revision = 2;
            Save(catalog);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(DefaultRoot + "/ModelTrialBattlefield.unity", OpenSceneMode.Single);
                var manager = Object.FindFirstObjectByType<MassEngineManager>();
                var camera = manager.cullingCamera;
                camera.transform.position = new Vector3(5, 23, -32);
                camera.transform.LookAt(new Vector3(5, 0, 0)); camera.fieldOfView = 65;
                var controls = camera.GetComponent<MyCameraManager>();
                if (controls == null) throw new BuildFailedException("Pilot camera controls are missing.");
                controls.MaxZoomDistance = 70; controls.MinZoomDistance = 1.5f;
                EditorSceneManager.SaveScene(scene);
                scene = EditorSceneManager.OpenScene(DefaultRoot + "/ModelTrialMenu.unity", OpenSceneMode.Single);
                var smoke = Object.FindFirstObjectByType<WarSandboxModelTrialSmoke>();
                smoke.templateRevision = 2;
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            File.WriteAllText(Evidence + "/playability-config.txt", "Final pilot: both private combat projectileRange=0, DefaultSwordUnit, template revisions=2; default camera=(5,23,-32), target=(5,0,0), FOV=65, private zoom range=1.5..70. Existing shared configs unchanged. First build/seed/reload are retained diagnostics, not final melee acceptance.\n");
            Debug.Log("CHARACTER_PILOT_MELEE_CONFIG_VERIFIED");
        }

        public static void FinalizeAtlasLod()
        {
            var unit = Load<ScenarioConfig>(DefaultRoot + "/Settings/TrialScenario.asset").unitTypes[0];
            var profile = KnightAtlasLodBuilder.Create((VATProfile)unit.renderConfig.vatProfile,
                DefaultRoot + "/KnightPaletteSafeVAT.asset", 1400);
            var result = UnitTypeBinder.Bind(unit, profile);
            if (!result.IsValid) throw new BuildFailedException(string.Join("\n", result.Errors));
            Save(unit.renderConfig); Save(unit);
            File.Copy(Evidence + "/recipe.json", Evidence + "/recipe-before-atlas.json");
            var report = JsonUtility.FromJson<RecipeReport>(File.ReadAllText(Evidence + "/recipe.json"));
            report.profile = AssetDatabase.GetAssetPath(profile); report.lowVertices = profile.lowLodMesh.vertexCount;
            File.WriteAllText(Evidence + "/recipe.json", JsonUtility.ToJson(report, true));
            CaptureAppearance(DefaultRoot, unit, profile, "-atlas");
            Debug.Log("CHARACTER_PILOT_ATLAS_LOD_VERIFIED");
        }

        public static void FinishPreparation()
        {
            string path = DefaultRoot + "/Prepared/KayKitKnightPrepared.prefab";
            var prefab = Load<GameObject>(path);
            var probe = Object.Instantiate(prefab);
            Vector3 forward;
            try
            {
                Load<AnimationClip>(DefaultRoot + "/Prepared/Idle.anim").SampleAnimation(probe, 0);
                Transform[] bones = probe.GetComponentsInChildren<Transform>(true);
                forward = Vector3.zero;
                foreach (string side in new[] { "l", "r" })
                    forward += bones.Single(t => t.name == "toes." + side).position - bones.Single(t => t.name == "foot." + side).position;
                forward.y = 0; forward.Normalize();
            }
            finally { Object.DestroyImmediate(probe); }
            // Engine yaw = atan2(direction.x, direction.z), hence the animated model must face +Z.
            // Judge sampled foot/toe axes, not the open visor's misleading bind-pose bounds.
            bool correction = Vector3.Dot(forward, Vector3.forward) < -.8f;
            if (!correction && Vector3.Dot(forward, Vector3.forward) < .8f)
                throw new BuildFailedException("Unrecognized animated facing: " + forward);
            if (correction)
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var pivot = contents.transform.Find("ScalePivot");
                    pivot.localRotation = Quaternion.Euler(0, 180, 0) * pivot.localRotation;
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                prefab = Load<GameObject>(path);
                VATProfile profile;
                using (var bake = VatBaker.Bake(new VatBakeRequest
                {
                    model = prefab,
                    idle = Load<AnimationClip>(DefaultRoot + "/Prepared/Idle.anim"),
                    move = Load<AnimationClip>(DefaultRoot + "/Prepared/Move.anim"),
                    attack = Load<AnimationClip>(DefaultRoot + "/Prepared/Attack.anim"),
                    death = Load<AnimationClip>(DefaultRoot + "/Prepared/Death.anim"),
                    frameRate = 30, bakeLowLod = true, lowLodRatio = .25f, lowLodMaxVertices = 1400,
                    extraRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true)
                })) profile = bake.SaveNew(DefaultRoot + "/KayKitKnightVATForward.asset");
                var unit = Load<ScenarioConfig>(DefaultRoot + "/Settings/TrialScenario.asset").unitTypes[0];
                var validation = UnitTypeBinder.Bind(unit, profile);
                if (!validation.IsValid) throw new BuildFailedException(string.Join("\n", validation.Errors));
                Save(unit.renderConfig); Save(unit);
                File.Copy(Evidence + "/recipe.json", Evidence + "/recipe-before-facing.json");
                var report = JsonUtility.FromJson<RecipeReport>(File.ReadAllText(Evidence + "/recipe.json"));
                report.profile = AssetDatabase.GetAssetPath(profile);
                report.fullVertices = profile.cleanMesh.vertexCount; report.lowVertices = profile.lowLodMesh.vertexCount;
                report.frames = profile.totalFrameCount;
                File.WriteAllText(Evidence + "/recipe.json", JsonUtility.ToJson(report, true));
                CaptureAppearance(DefaultRoot, unit, profile, "-facing");
            }
            File.WriteAllText(Evidence + "/orientation.txt", "Sampled Idle foot-to-toe XZ direction before finalization: " + forward +
                "; engine +Z contract; applied private ScalePivot 180-degree correction=" + correction +
                "; final facing=" + (correction ? -forward : forward) + "; old/accepted content unchanged.\n");
            Debug.Log("CHARACTER_PILOT_FACING_VERIFIED");
        }

        public static void Inspect()
        {
            Directory.CreateDirectory(Evidence);
            var sb = new StringBuilder();
            foreach (string path in Directory.GetFiles(Source, "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.isReadable = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importBlendShapes = false;
                importer.SaveAndReimport();
                sb.AppendLine("MODEL " + path + " scale=" + importer.globalScale + " fileScale=" + importer.fileScale);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = Object.Instantiate(asset);
                try
                {
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                        sb.AppendLine("TRANSFORM " + AnimationUtility.CalculateTransformPath(t, go.transform) + " pos=" + t.localPosition + " rot=" + t.localEulerAngles + " scale=" + t.localScale);
                    foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        sb.AppendLine("SKIN " + r.name + " verts=" + r.sharedMesh.vertexCount + " submeshes=" + r.sharedMesh.subMeshCount + " bounds=" + r.bounds);
                    foreach (var f in go.GetComponentsInChildren<MeshFilter>(true))
                        sb.AppendLine("MESH " + f.name + " verts=" + f.sharedMesh.vertexCount + " bounds=" + f.sharedMesh.bounds);
                    foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
                    {
                        var bindings = AnimationUtility.GetCurveBindings(clip);
                        sb.AppendLine("CLIP " + clip.name + " length=" + clip.length + " fps=" + clip.frameRate + " human=" + clip.isHumanMotion + " curves=" + bindings.Length);
                        foreach (string p in bindings.Select(b => b.path).Distinct()) sb.AppendLine("BIND " + p);
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            File.WriteAllText(Evidence + "/import-inspection.txt", sb.ToString());
            Debug.Log("CHARACTER_PILOT_IMPORT_INSPECTION_COMPLETE");
        }
        private static GameObject CreatePreparedPrefab(string directory)
        {
            string prefabPath = directory + "/KayKitKnightPrepared.prefab";
            RequireNewAssetPath(prefabPath);
            var texImporter = (TextureImporter)AssetImporter.GetAtPath(Source + "/knight_texture.png");
            texImporter.textureCompression = TextureImporterCompression.Uncompressed;
            texImporter.mipmapEnabled = true; texImporter.wrapMode = TextureWrapMode.Clamp;
            texImporter.filterMode = FilterMode.Bilinear; texImporter.SaveAndReimport();
            var root = new GameObject("KayKitKnightPrepared");
            var notes = new StringBuilder();
            try
            {
                var pivot = new GameObject("ScalePivot").transform; pivot.SetParent(root.transform, false);
                var body = Object.Instantiate(Load<GameObject>(Source + "/Knight.fbx"), pivot);
                body.name = "Body";
                foreach (var a in body.GetComponentsInChildren<Animator>(true)) { a.applyRootMotion = false; a.enabled = false; }
                var template = Load<UnitTypeConfig>("Assets/Game/Settings/AttackerUnitConfig.asset");
                var material = CreateMaterial(template.renderConfig.nearMaterial, Load<Texture2D>(Source + "/knight_texture.png"), directory + "/PreparedAtlas.mat");
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
                Bounds sourceBounds = ActualSkinnedBounds(body);
                float factor = 1.8f / sourceBounds.size.y;
                if (factor < .1f || factor > 10f) throw new BuildFailedException("Unexpected source body scale: " + sourceBounds);
                pivot.localScale = Vector3.one * factor;
                pivot.localPosition = new Vector3(0, -sourceBounds.min.y * factor, 0);
                // FBX import confirms visor is in +Z and cape is in -Z: engine forward is +Z.
                Attach(body, "handslot.r", "sword_1handed.fbx", "PilotSword", material);
                Attach(body, "handslot.l", "shield_badge.fbx", "PilotShield", material);
                notes.AppendLine("Source baked-vertex bounds: " + sourceBounds + "; uniform child scale=" + factor + "; prepared body height=1.8m; forward=+Z");
                notes.AppendLine("Attachments: author's Unity exports at handslot.r/l; local position=0, rotation=identity, scale=1; both included as explicit VAT MeshRenderers.");
                string[] files = { "General", "MovementBasic", "CombatMelee", "General" };
                string[] names = { "Idle_A", "Running_A", "Melee_1H_Attack_Chop", "Death_A" };
                string[] output = { "Idle", "Move", "Attack", "Death" };
                for (int i = 0; i < names.Length; i++)
                {
                    string sourcePath = Source + "/Rig_Medium_" + files[i] + ".fbx";
                    var original = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().Single(c => c.name == names[i]);
                    var clip = new AnimationClip { name = output[i], frameRate = original.frameRate };
                    var bindings = AnimationUtility.GetCurveBindings(original);
                    foreach (var binding in bindings)
                    {
                        if (binding.type != typeof(Transform) || body.transform.Find(binding.path) == null)
                            throw new BuildFailedException("Unmapped Generic binding: " + names[i] + "/" + binding.path + "/" + binding.type);
                        var mapped = binding;
                        mapped.path = "ScalePivot/Body/" + binding.path;
                        AnimationUtility.SetEditorCurve(clip, mapped, AnimationUtility.GetEditorCurve(original, binding));
                    }
                    clip.EnsureQuaternionContinuity();
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = i < 3; AnimationUtility.SetAnimationClipSettings(clip, settings);
                    string target = directory + "/" + output[i] + ".anim"; RequireNewAssetPath(target); AssetDatabase.CreateAsset(clip, target);
                    if (bindings.Length != 240 || Mathf.Abs(clip.length - original.length) > .04f)
                        throw new BuildFailedException("Clip changed duration or binding coverage unexpectedly: " + names[i]);
                    var probe = Object.Instantiate(root);
                    try
                    {
                        clip.SampleAnimation(probe, 0);
                        Transform sword = probe.GetComponentsInChildren<Transform>(true).Single(t => t.name == "PilotSword");
                        Vector3 first = sword.TransformPoint(Vector3.up);
                        clip.SampleAnimation(probe, clip.length * .45f);
                        Vector3 middle = sword.TransformPoint(Vector3.up);
                        Bounds pose = ActualSkinnedBounds(probe);
                        if (pose.size.magnitude > 6 || float.IsNaN(pose.size.x)) throw new BuildFailedException("Exploded Generic pose " + names[i]);
                        notes.AppendLine(output[i] + " <- " + sourcePath + "#" + names[i] + "; duration=" + clip.length + "; curves=" + bindings.Length + "; sword-tip delta=" + Vector3.Distance(first, middle) + "; mid body bounds=" + pose);
                        if (i == 2 && Vector3.Distance(first, middle) < .08f) throw new BuildFailedException("Attack does not move the sword.");
                    }
                    finally { Object.DestroyImmediate(probe); }
                }
                var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success || saved == null) throw new BuildFailedException("Could not save independent prepared prefab.");
                File.WriteAllText(Evidence + "/preparation.txt", notes.ToString());
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Bounds ActualSkinnedBounds(GameObject root)
        {
            bool first = true; Bounds bounds = default;
            var mesh = new Mesh();
            try
            {
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.BakeMesh(mesh, false);
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 p = root.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                    }
                }
            }
            finally { Object.DestroyImmediate(mesh); }
            if (first || bounds.size.y <= .1f) throw new BuildFailedException("No valid body geometry.");
            return bounds;
        }

        private static void Attach(GameObject body, string bone, string file, string name, Material material)
        {
            Transform slot = body.GetComponentsInChildren<Transform>(true).Single(t => t.name == bone);
            var weapon = Object.Instantiate(Load<GameObject>(Source + "/" + file), slot);
            weapon.name = name;
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
            weapon.transform.localScale = Vector3.one;
            foreach (var r in weapon.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
        }

        private static void ConfigurePrivateTuning(UnitTypeConfig unit, string directory)
        {
            string animPath = directory + "/KnightAnimation.asset";
            RequireNewAssetPath(animPath);
            var anim = unit.animationConfig != null ? Object.Instantiate(unit.animationConfig) : ScriptableObject.CreateInstance<AnimationConfig>();
            anim.name = "KnightAnimation";
            anim.moveReferenceSpeed = unit.movementConfig.maxSpeed;
            AssetDatabase.CreateAsset(anim, animPath); unit.animationConfig = anim;
            string movePath = directory + "/KnightMovement.asset"; RequireNewAssetPath(movePath);
            var move = Object.Instantiate(unit.movementConfig); move.name = "KnightMovement";
            AssetDatabase.CreateAsset(move, movePath); unit.movementConfig = move;
            // Cosmetic per-character tuning only; retain the established damage/HP/range balance.
            unit.combatConfig.attackInterval = Load<AnimationClip>(DefaultRoot + "/Prepared/Attack.anim").length;
            Save(unit.combatConfig); Save(unit);
        }

    }
}
