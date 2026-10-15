using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-14 程序化地图 2048 (approved, step 1): one-time preparation of the 2048 configuration and template scene.
    /// Creates Assets/Game/Content/Battlefields/Map2048/{Simulation2048, Flow2048, System2048}.asset as copies of the
    /// 1024 assets with only the size fields changed, and Green2048.unity as a copy of Green1024.unity whose manager
    /// uses System2048, whose camera limits cover 2048 m, and whose deployment moves the authored armies to ±400 m.
    /// The 1024 assets and scenes are only read. Existing 2048 assets are left alone unless "重建" is used.
    /// No dialogs (remote-safe); log: Logs/Battlefield2048/setup.txt.
    /// </summary>
    public static class Battlefield2048Setup
    {
        public const string MenuRoot = "Tools/战场 2048/";
        public const string Dir = "Assets/Game/Content/Battlefields/Map2048";
        public const string TemplateScene = Dir + "/Green2048.unity";
        const string Src = "Assets/Game/Content/Battlefields/Map1024";
        const string Log = "Logs/Battlefield2048/setup.txt";
        // 1024 template: camera horizontal limit 960, envelope 1600 / 960. 2048: twice the horizontal reach, same height.
        const float CameraHorizontal = 1920f, EnvelopeDistance = 3200f;
        /// <summary>Authored centres ±128 m → ±400 m (front gap of the 100k validation footprint ≈545 m).</summary>
        public const float SpawnOutwardOffset = 272f;

        [MenuItem(MenuRoot + "准备模板（配置与场景）", false, 1)] static void Menu() => Run(false);
        [MenuItem(MenuRoot + "重建模板（覆盖 2048 配置与场景）", false, 2)] static void MenuRebuild() => Run(true);

        public static void Run(bool rebuild)
        {
            var log = new List<string> { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " rebuild=" + rebuild };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("请先退出 Play 模式。");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new Exception("场景 " + SceneManager.GetSceneAt(i).name + " 有未保存的修改。");
                string restore = SceneManager.GetActiveScene().path;
                Battlefield1024Generator.EnsureFolder(Dir);

                var sim = CopyConfig("Simulation1024", "Simulation2048", rebuild, log);
                Set(sim, so => { var p = Prop(so, "simulationWorldSize"); p.vector2Value = new Vector2(2048f, 2048f); }, log);
                var flow = CopyConfig("Flow1024", "Flow2048", rebuild, log);
                Set(flow, so =>
                {
                    Prop(so, "flowFieldResolution").intValue = 512;
                    Prop(so, "flowFieldCellSize").floatValue = 4f;
                    Prop(so, "flowFieldOrigin").vector2Value = new Vector2(-1024f, -1024f);
                }, log);
                var system = CopyConfig("System1024", "System2048", rebuild, log);
                Set(system, so =>
                {
                    Prop(so, "simulationConfig").objectReferenceValue = sim;
                    Prop(so, "runtimeFlowConfig").objectReferenceValue = flow;
                }, log);
                AssetDatabase.SaveAssets();

                if (rebuild && File.Exists(TemplateScene)) { AssetDatabase.DeleteAsset(TemplateScene); log.Add("deleted " + TemplateScene); }
                if (!File.Exists(TemplateScene))
                {
                    Battlefield1024Generator.Require(AssetDatabase.CopyAsset(Src + "/Green1024.unity", TemplateScene), "复制 Green1024.unity 失败");
                    var scene = EditorSceneManager.OpenScene(TemplateScene, OpenSceneMode.Single);
                    system = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Dir + "/System2048.asset");
                    int managers = 0, comforts = 0, envelopes = 0, deployments = 0;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var m in root.GetComponentsInChildren<MassEngineManager>(true))
                        { var so = new SerializedObject(m); Prop(so, "systemConfig").objectReferenceValue = system; so.ApplyModifiedPropertiesWithoutUndo(); managers++; }
                        foreach (var c in root.GetComponentsInChildren<BattlefieldCameraComfort24>(true))
                        { log.Add("comfort " + c.name + " horizontalLimit " + c.horizontalLimit + "→" + CameraHorizontal + " maximumHeight " + c.maximumHeight); c.horizontalLimit = CameraHorizontal; EditorUtility.SetDirty(c); comforts++; }
                        foreach (var b in root.GetComponentsInChildren<TerrainScaleBounds>(true))
                        {
                            log.Add("envelope " + b.name + " distance " + b.maximumDistance + "→" + EnvelopeDistance + " radius " + b.maximumHorizontalRadius + "→" + CameraHorizontal + " height " + b.maximumHeight);
                            b.maximumDistance = EnvelopeDistance; b.maximumHorizontalRadius = CameraHorizontal; EditorUtility.SetDirty(b); envelopes++;
                        }
                        foreach (var d in root.GetComponentsInChildren<WarSandboxRuntimeDeployment>(true))
                        { var so = new SerializedObject(d); Prop(so, "authoredSpawnOutwardOffset").floatValue = SpawnOutwardOffset; so.ApplyModifiedPropertiesWithoutUndo(); deployments++; }
                        foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                            log.Add("camera " + cam.name + " pos " + cam.transform.position + " far " + cam.farClipPlane + " enabled " + cam.enabled);
                    }
                    Battlefield1024Generator.Require(managers == 1 && comforts >= 1 && deployments == 1,
                        "模板组件数量不符：manager " + managers + " comfort " + comforts + " deployment " + deployments);
                    log.Add("scene " + TemplateScene + " managers " + managers + " comforts " + comforts + " envelopes " + envelopes + " deployments " + deployments);
                    EditorSceneManager.MarkSceneDirty(scene);
                    Battlefield1024Generator.Require(EditorSceneManager.SaveScene(scene), "保存 Green2048.unity 失败");
                    if (!string.IsNullOrEmpty(restore) && File.Exists(restore) && restore != TemplateScene) EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
                }
                else log.Add("keep existing " + TemplateScene);
                AssetDatabase.SaveAssets();
                log.Add("OK");
                Debug.Log("[Battlefield2048] 模板准备完成\n" + string.Join("\n", log));
            }
            catch (Exception e) { log.Add("ERROR " + e.Message); Debug.LogException(e); }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Log));
                File.WriteAllText(Log, string.Join("\n", log) + "\n");
            }
        }

        static ScriptableObject CopyConfig(string from, string to, bool rebuild, List<string> log)
        {
            string src = Src + "/" + from + ".asset", dst = Dir + "/" + to + ".asset";
            Battlefield1024Generator.Require(File.Exists(src), "缺少 " + src);
            if (rebuild && File.Exists(dst)) AssetDatabase.DeleteAsset(dst);
            if (!File.Exists(dst)) { Battlefield1024Generator.Require(AssetDatabase.CopyAsset(src, dst), "复制失败 " + src); log.Add("copied " + src + " → " + dst); }
            else log.Add("keep " + dst);
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(dst);
            Battlefield1024Generator.Require(asset != null, "无法加载 " + dst);
            return asset;
        }

        static void Set(Object asset, Action<SerializedObject> edit, List<string> log)
        {
            var so = new SerializedObject(asset); edit(so); so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset);
            log.Add("set " + asset.name);
        }

        static SerializedProperty Prop(SerializedObject so, string name)
        {
            var p = so.FindProperty(name);
            Battlefield1024Generator.Require(p != null, so.targetObject.GetType().Name + " 缺少字段 " + name);
            return p;
        }
    }
}
