using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MassEngine.UiPrototype.Editor
{
    /// <summary>
    /// Menus for the B-style UI Toolkit prototype: build scene + PanelSettings, open preview, capture screenshots.
    /// Uses playModeStartScene so the scene the user has open is never changed.
    /// </summary>
    [InitializeOnLoad]
    public static class UiProtoBSetup
    {
        const string Root = "Assets/Game/UiPrototype";
        const string UiDir = Root + "/UI";
        sealed class Variant { public string key, scene, rootUxml, outDir, label; public bool toy; }
        static readonly Variant B = new Variant { key = "B", scene = Root + "/UiProtoB.unity", rootUxml = "Root", outDir = "outputs/UiProtoB", label = "B 战术指挥" };
        static readonly Variant Toy = new Variant { key = "Toy", scene = Root + "/UiProtoToy.unity", rootUxml = "RootToy", outDir = "outputs/UiProtoToy", label = "Q 版", toy = true };
        static Variant Cur => SessionState.GetString(VariantKey, "B") == "Toy" ? Toy : B;
        const string VariantKey = "UiProtoB.Variant";
        const string PanelPath = UiDir + "/UiProtoB.PanelSettings.asset";
        const string ThemePath = UiDir + "/UiProtoBTheme.tss";
        const string Menu = "Tools/UI 原型/";
        const string PreviewKey = "UiProtoB.Preview", CaptureKey = "UiProtoB.Capture";
        static double deadline;
        static bool captureStarted;

        static UiProtoBSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(CaptureKey, false) && EditorApplication.isPlaying) Hook();
        }

        [MenuItem(Menu + "Q 版 · 打开预览（进入 Play）", false, 10)] public static void PreviewToy() => Preview(Toy);
        [MenuItem(Menu + "Q 版 · 截图（自动进 Play，自动退出）", false, 11)] public static void CaptureToy() => Capture(Toy);
        [MenuItem(Menu + "Q 版 · 重建场景", false, 12)] public static void RebuildToy() { SetVariant(Toy); Ensure(Toy, true); }
        [MenuItem(Menu + "B 战术指挥 · 打开预览（进入 Play）", false, 30)] public static void PreviewB() => Preview(B);
        [MenuItem(Menu + "B 战术指挥 · 截图（自动进 Play，自动退出）", false, 31)] public static void CaptureB() => Capture(B);
        [MenuItem(Menu + "B 战术指挥 · 重建场景", false, 32)] public static void RebuildB() { SetVariant(B); Ensure(B, true); }

        static void SetVariant(Variant v) => SessionState.SetString(VariantKey, v.key);

        static void Preview(Variant v)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            SetVariant(v);
            if (!Ensure(v, false)) return;
            SessionState.SetBool(PreviewKey, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(v.scene);
            EditorApplication.EnterPlaymode();
        }

        static void Capture(Variant v)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("ERROR already playing"); return; }
            SetVariant(v);
            Directory.CreateDirectory(v.outDir);
            File.WriteAllText(Path.Combine(v.outDir, "status.txt"), Stamp() + " requested " + v.label + "\n");
            if (!Ensure(v, false)) return;
            SessionState.SetBool(CaptureKey, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(v.scene);
            Application.runInBackground = true;
            Log("entering play");
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            bool mine = SessionState.GetBool(PreviewKey, false) || SessionState.GetBool(CaptureKey, false);
            if (!mine) return;
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                EditorSceneManager.playModeStartScene = null;
                if (SessionState.GetBool(CaptureKey, false)) Hook();
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                if (SessionState.GetBool(CaptureKey, false)) Log("finished");
                SessionState.SetBool(PreviewKey, false);
                SessionState.SetBool(CaptureKey, false);
                EditorSceneManager.playModeStartScene = null;
                EditorApplication.update -= Tick;
            }
        }

        static void Hook()
        {
            captureStarted = false;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Log("play entered");
        }

        static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup > deadline) { Log("ERROR timeout"); Stop(); return; }
            var app = Object.FindFirstObjectByType<UiProtoBApp>();
            if (app == null) return;
            if (!captureStarted)
            {
                if (!app.IsReady) return;
                captureStarted = true;
                app.StartCapture(Path.GetFullPath(Cur.outDir));
                Log("capture started");
                return;
            }
            if (app.CaptureDone) { Log("capture done\n" + app.CaptureLog); Stop(); }
        }

        static void Stop()
        {
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static void Log(string m)
        {
            var dir = Cur.outDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "status.txt"), Stamp() + " " + m + "\n");
            Debug.Log("[UiProtoB] " + m);
        }

        static string Stamp() => System.DateTime.Now.ToString("HH:mm:ss");

        static VisualTreeAsset Load(string n) => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "/" + n + ".uxml");

        static bool Ensure(Variant v, bool force)
        {
            var rootUxml = Load(v.rootUxml);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (rootUxml == null || theme == null) { Log("ERROR missing Root.uxml or theme"); return false; }
            FixTextures();

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, PanelPath); }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.clearColor = false;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            if (!force && File.Exists(v.scene)) return true;

            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.04f, 0.06f);
            var go = new GameObject("UiProtoB");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = rootUxml;
            var app = go.AddComponent<UiProtoBApp>();
            app.opsRow = Load("OpsRow");
            app.compCard = Load("CompCard");
            app.armyCard = Load("ArmyCard");
            app.toy = v.toy;
            if (v.toy) cam.backgroundColor = new Color(0.75f, 0.89f, 0.95f);
            EditorSceneManager.SaveScene(scene, v.scene);
            if (active.IsValid()) SceneManager.SetActiveScene(active);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            Log("scene built: " + v.scene);
            return true;
        }

        static void FixTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { UiDir + "/Textures" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool dirty = ti.mipmapEnabled || !ti.alphaIsTransparency || ti.textureCompression != TextureImporterCompression.Uncompressed
                             || ti.npotScale != TextureImporterNPOTScale.None || ti.wrapMode != TextureWrapMode.Clamp;
                if (!dirty) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.SaveAndReimport();
            }
        }
    }
}
