using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    public static class WarSandboxTerrainPrototypeBuilder
    {
        public const string Root = "Assets/Game/M61TerrainPrototype";
        public const string ScenePath = Root + "/TerrainPrototype.unity";

        public static void PrepareAndBuildWindows()
        {
            Prepare();
            BuildWindows();
        }

        [MenuItem("MassEngine/Terrain Prototype/Create M6.1 Scene")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (AssetDatabase.IsValidFolder(Root) || Directory.Exists(Root) || File.Exists(Root))
                throw new InvalidOperationException("Prototype directory already exists; refusing to overwrite: " + Root);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                AssetDatabase.CreateFolder("Assets/Game", "M61TerrainPrototype");
                var asset = TerrainPrototype.CreateAsset();
                if (!asset.TryCreateSurface(out var surface, out var error)) throw new InvalidOperationException(error);
                AssetDatabase.CreateAsset(asset, Root + "/Surface.asset");
                var mesh = TerrainPrototype.CreateMesh(surface);
                AssetDatabase.CreateAsset(mesh, Root + "/SurfaceMesh.asset");
                var shader = Shader.Find("MassEngine/Terrain Prototype");
                if (shader == null) throw new InvalidOperationException("Terrain prototype shader is missing.");
                var material = new Material(shader) { name = "Terrain Prototype" };
                AssetDatabase.CreateAsset(material, Root + "/SurfaceMaterial.mat");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var ground = new GameObject("Continuous Surface");
                ground.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/SurfaceMesh.asset");
                ground.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/SurfaceMaterial.mat");
                ground.AddComponent<MeshCollider>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(470, 370, -540);
                camera.transform.LookAt(new Vector3(0, 10, 0));
                camera.nearClipPlane = .3f; camera.farClipPlane = 3000;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .075f, .1f);
                var control = camera.gameObject.AddComponent<MyCameraManager>();
                control.ControlledCamera = camera; control.Target = ground.transform; control.FlyMoveSpeed = 70;
                var viewer = new GameObject("M6.1 Viewer And Budget Probe").AddComponent<WarSandboxTerrainPrototype>();
                viewer.terrain = AssetDatabase.LoadAssetAtPath<TerrainSurfaceAsset>(Root + "/Surface.asset");
                viewer.surfaceMesh = ground.GetComponent<MeshFilter>().sharedMesh;
                viewer.samplingShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Terrain/Shaders/TerrainSurfaceProbe.compute");
                if (viewer.terrain == null || viewer.surfaceMesh == null || viewer.samplingShader == null)
                    throw new InvalidOperationException("Prototype persisted references are incomplete.");
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save prototype scene.");
                Debug.Log("M6.1 prototype created: " + ScenePath);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [MenuItem("MassEngine/Terrain Prototype/Build Windows Viewer")]
        public static void BuildWindows()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (!File.Exists(ScenePath)) throw new BuildFailedException("Create the M6.1 prototype scene first.");
            string directory = Path.GetFullPath("Builds/M61TerrainPrototype");
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = Path.Combine(directory, "TerrainPrototype.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Terrain prototype build failed.");
            File.WriteAllText(Path.Combine(directory, "Start-Terrain.cmd"), "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"TerrainPrototype.exe\" -screen-fullscreen 0 -screen-width 1280 -screen-height 720\r\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "说明.txt"),
                "M6.1 连续地表原型：48米高地、西侧坡道、东侧窄坡道和峡谷。\r\n红色表示陡坡或人工禁行区，绿/黄色表示可通行表面。\r\n右键+WASD移动，滚轮缩放。此包用于地形观察，完整单位导航与战斗在M6.2接入。\r\n保留同目录数据和DLL。测量命令见工程Assets/方案设计/M6.1连续地表原型.md。\r\n", Encoding.UTF8);
            Debug.Log("M6.1 viewer built: " + directory);
        }
    }
}
