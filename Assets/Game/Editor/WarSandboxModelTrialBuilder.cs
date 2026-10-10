using System;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>M5.4 fixed trial recipe. Uses the production baker/binder; never edits shipped content.</summary>
    public static class WarSandboxModelTrialBuilder
    {
        private const string DefaultRoot = "Assets/Game/Content/Characters/M54TrialPlayable";
        private const string SourceScene = "Assets/Game/Experiments/LegacyScenes/WarSandbox.unity";
        private const string ClipRoot = "Assets/ThirdParty/RPG Tiny Hero Duo/Animation/SwordAndShield/";
        private const string LicenseSource = "Assets/Art/Source/ModelTrials/UnityChan/License";
        private const string PlayerReadmeSource = "Assets/Documentation/Design/M5.4试玩验收清单.md";
        private const string Notice = "M5.4 model trial (unofficial) | Unity-Chan © UTJ/UCL";

        [Serializable]
        private sealed class RecipeReport
        {
            public string root, sourceModel, preparedPrefab, profile, trialUnit, scenario, catalog, menuScene, battlefieldScene;
            public string unityVersion, createdUtc;
            public string[] clips, stages;
            public int fullVertices, lowVertices, frames, startingUnits = 128;
            public bool humanAcceptance;
        }

        [MenuItem("MassEngine/Model Trial/M5.4 Create Isolated UnityChan Trial")]
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
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Prepared/UnityChanPrepared.prefab");
                if (prefab == null) prefab = UnityChanTrialImporter.CreatePreparedPrefab(root + "/Prepared");
                VATProfile profile = AssetDatabase.LoadAssetAtPath<VATProfile>(root + "/UnityChanVAT.asset");
                // This independent trial uses the short battle-idle loop (as the shipped Female does),
                // not the 4.7-second normal idle. Keep 30 fps without an oversized >100 MiB text VAT asset.
                var clipPaths = new[] { ClipRoot + "Idle_Battle_SwordAndShiled.fbx", ClipRoot + "InPlace/MoveFWD_Battle_InPlace_SwordAndShield.fbx",
                    ClipRoot + "Attack01_SwordAndShiled.fbx", ClipRoot + "Die01_SwordAndShield.fbx" };
                var clips = new AnimationClip[4];
                for (int i = 0; i < clips.Length; i++)
                {
                    clips[i] = Load<AnimationClip>(clipPaths[i]);
                    if (!clips[i].isHumanMotion) throw new BuildFailedException("Trial needs Humanoid clips: " + clipPaths[i]);
                }
                if (profile == null)
                {
                    using (var bake = VatBaker.Bake(new VatBakeRequest
                    {
                        model = prefab, idle = clips[0], move = clips[1], attack = clips[2], death = clips[3], frameRate = 30,
                        bakeLowLod = true, lowLodRatio = .25f, lowLodMaxVertices = 1200,
                        extraRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true)
                    })) profile = bake.SaveNew(root + "/UnityChanVAT.asset");
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
                    RequireNewAssetPath(root + "/Settings/UnityChanNear.mat");
                    RequireNewAssetPath(root + "/Settings/UnityChanMid.mat");
                    scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
                    AssetDatabase.CreateAsset(scenario, scenarioPath);
                    var template = Load<UnitTypeConfig>("Assets/Game/Settings/AttackerUnitConfig.asset");
                    trial = CreateUnit(scenario, template, "UnityChanTrial", 0, profile, root + "/Settings");
                    var enemy = CreateUnit(scenario, template, "TrialOpponent", 1, null, root + "/Settings");
                    // Source combat/movement settings are unchanged. Only the private deployment is small.
                    SetSpawn(trial, new Vector3(-18, 0, 0));
                    SetSpawn(enemy, new Vector3(18, 0, 0));
                    trial.unitTypeName = "UnityChan 试验兵";
                    enemy.unitTypeName = "内置士兵对照";
                    var atlas = Load<Texture2D>(root + "/Prepared/UnityChanAtlas.png");
                    trial.renderConfig.nearMaterial = CreateMaterial(template.renderConfig.nearMaterial, atlas, root + "/Settings/UnityChanNear.mat");
                    trial.renderConfig.midMaterial = CreateMaterial(template.renderConfig.midMaterial, atlas, root + "/Settings/UnityChanMid.mat");
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
                    camera.transform.position = new Vector3(0, 34, -44);
                    camera.transform.LookAt(Vector3.zero);
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
                    catalog.defaultEntryId = "m54-model-trial";
                    catalog.entries = new[] { new WarSandboxBattlefieldEntry
                    {
                        id = catalog.defaultEntryId, displayName = "M5.4 UnityChan 制作验收", scenePath = battlefield,
                        rules = Load<WarSandboxBattlefieldConfig>("Assets/Game/Settings/BattlefieldRules_A_Annihilation.asset")
                    } };
                    catalog.templates = new[] {
                        new WarSandboxUnitTemplateEntry { templateId = "m54-unitychan", revision = 1, config = trial },
                        new WarSandboxUnitTemplateEntry { templateId = "m54-opponent", revision = 1, config = scenario.unitTypes[1] }
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
                    CreateNotice(entry.transform);
                    EditorSceneManager.SaveScene(scene);
                }
                prefab = Load<GameObject>(root + "/Prepared/UnityChanPrepared.prefab");
                profile = Load<VATProfile>(root + "/UnityChanVAT.asset");
                scenario = Load<ScenarioConfig>(scenarioPath);
                trial = scenario.unitTypes[0];
                var report = new RecipeReport
                {
                    root = root, sourceModel = UnityChanTrialImporter.SourceModelPath, preparedPrefab = AssetDatabase.GetAssetPath(prefab),
                    profile = AssetDatabase.GetAssetPath(profile), trialUnit = AssetDatabase.GetAssetPath(trial), scenario = scenarioPath,
                    catalog = catalogPath, menuScene = menu, battlefieldScene = battlefield, unityVersion = Application.unityVersion,
                    createdUtc = DateTime.UtcNow.ToString("O"), clips = clipPaths, fullVertices = profile.cleanMesh.vertexCount,
                    lowVertices = profile.HasLowLod ? profile.lowLodMesh.vertexCount : 0, frames = profile.totalFrameCount,
                    stages = new[] { "FBX imported with own Humanoid Avatar", "separate meshes/atlas/prepared prefab",
                        "production VatBaker.Bake and SaveNew", "production UnitTypeBinder.CreateUnitType and ValidateBinding",
                        "raw YAML persistent references", "isolated Scenario/Catalog/menu/battlefield" }, humanAcceptance = false
                };
                Directory.CreateDirectory("Logs/M54Validation");
                File.WriteAllText("Logs/M54Validation/recipe.json", JsonUtility.ToJson(report, true));
                CaptureAppearance(root, trial, profile);
                Debug.Log("M54_RECIPE_READY " + root);
            }
            finally
            {
                if (savedSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(savedSetup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [MenuItem("MassEngine/Model Trial/M5.4 Build Windows Trial")]
        public static void BuildWindows()
        {
            string root = Root();
            string menu = root + "/ModelTrialMenu.unity", battle = root + "/ModelTrialBattlefield.unity";
            Load<SceneAsset>(menu); Load<SceneAsset>(battle);
            // A fresh Unity invocation re-resolves all YAML references before the standalone build.
            var scenario = Load<ScenarioConfig>(root + "/Settings/TrialScenario.asset");
            foreach (var unit in scenario.unitTypes)
            {
                var validation = UnitTypeBinder.ValidateBinding(unit);
                if (!validation.IsValid) throw new BuildFailedException(string.Join("\n", validation.Errors));
            }
            string output = Path.GetFullPath(Argument("--model-trial-build=") ?? "Builds/M54Trial");
            Directory.CreateDirectory(output);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { menu, battle }, locationPathName = Path.Combine(output, "ModelTrial.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException("M5.4 build failed: " + result.summary.result);
            CopyLicense(output);
            WritePlayerSupportFiles(output);
            File.WriteAllText(Path.Combine(output, "README.txt"),
                "M5.4 UnityChan pipeline trial - unofficial validation build\n" +
                "This work is provided under Unity-Chan License Terms. © Unity Technologies Japan/UCL\n" +
                "See UnityChanLicense for the supplied UCL 2.02 terms and notices. Not an official Unity product.\n" +
                "Launch ModelTrial.exe to play; enter the trial, edit deployment, start, settle and restart.\n" +
                "The default 110k sandbox and its source assets are not modified. Human acceptance remains pending.\n");
            Debug.Log("M54_BUILD_READY " + output);
        }

        private static void CaptureAppearance(string root, UnitTypeConfig unit, VATProfile profile)
        {
            string output = "Logs/M54Validation/appearance";
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
            var go = new GameObject("UnityChan License Notice", typeof(Canvas));
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
            File.Copy(PlayerReadmeSource, Path.Combine(output, "开始验收.md"), true);
            File.WriteAllText(Path.Combine(output, "Start-Trial.cmd"),
                "@echo off\r\nstart \"\" /D \"%~dp0\" \"%~dp0ModelTrial.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n",
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
        private static string Root()
        {
            string root = (Argument("--model-trial-root=") ?? DefaultRoot).Replace('\\', '/').TrimEnd('/');
            if (!root.StartsWith("Assets/Game/M54Trial", StringComparison.Ordinal) || root.Contains("..")) throw new ArgumentException("Use an Assets/Game/M54Trial* isolated root.");
            return root;
        }
        private static string Argument(string prefix)
        { foreach (string value in Environment.GetCommandLineArgs()) if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length).Trim('"'); return null; }
        private static void CopyLicense(string output)
        {
            foreach (string file in Directory.GetFiles(LicenseSource, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = file.Substring(LicenseSource.Length).TrimStart('/', '\\');
                string target = Path.Combine(output, "UnityChanLicense", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, true);
            }
        }
    }
}
