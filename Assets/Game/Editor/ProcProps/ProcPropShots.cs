// Screenshot helper for the procedural props pilot (2026-10-13).
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    /// <summary>Same-viewpoint screenshots of 森林丘陵 vs its procedural copy → outputs/ProcProps/shots/*.jpg.</summary>
    public static class ProcPropShots
    {
        const string OutDir = "outputs/ProcProps/shots";

        // (camera x, height above ground, z) -> (target x, height above ground, z)
        static readonly Vector3[,] Views =
        {
            { new Vector3(-230f, 55f, 215f), new Vector3(-230f, 8f, 330f) },   // north pass, ridge woods
            { new Vector3(-250f, 45f, -30f), new Vector3(-370f, 8f, 10f) },    // west flank woods + outcrop
            { new Vector3(-120f, 35f, -245f), new Vector3(-120f, 5f, -330f) }, // south pass, wood edge close-up
            { new Vector3(330f, 30f, -300f), new Vector3(400f, 3f, -380f) },   // open ground outside the core
        };

        // 2026-10-13: menu removed - the comparison map 森林丘陵·程序化道具 was deleted.
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcProps] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[ProcProps] 场景 " + SceneManager.GetSceneAt(i).name + " 有未保存的修改，已取消截图。"); return; }
            string restore = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(OutDir);
            var terrain = new ForestHillsMap();
            try
            {
                Shoot(new ForestHillsMap().ScenePath, "orig", terrain);
                Shoot(new ForestHillsProcMap().ScenePath, "proc", terrain);
                Debug.Log("[ProcProps] 截图完成 → " + OutDir);
            }
            finally
            {
                if (!string.IsNullOrEmpty(restore) && File.Exists(restore)) EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
            }
        }

        // Views for the new procedural maps: overview, north flank, centre spine.
        static readonly Vector3[,] MapViews =
        {
            { new Vector3(-380f, 120f, 0f), new Vector3(60f, 0f, 0f) },
            { new Vector3(-150f, 80f, 170f), new Vector3(-200f, 5f, 380f) },
            { new Vector3(-90f, 30f, -140f), new Vector3(10f, 5f, -40f) },
        };

        [MenuItem("Tools/战场 1024/程序化地图/截图（5 张程序化地图）", false, 80)]
        static void MapsMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcProps] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[ProcProps] 场景 " + SceneManager.GetSceneAt(i).name + " 有未保存的修改，已取消截图。"); return; }
            string restore = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(OutDir);
            try
            {
                foreach (MapSpec m in new MapSpec[] { new RockySteppeMap(), new AutumnWoodsMap(), new SnowHighlandsMap(), new StoneForestMap(), new DeepWoodsMap() })
                {
                    if (!File.Exists(m.ScenePath)) { Debug.LogWarning("[ProcProps] 未生成，跳过：" + m.ScenePath); continue; }
                    Shoot(m.ScenePath, m.Folder, m, MapViews);
                    EditorUtility.UnloadUnusedAssetsImmediate(); System.GC.Collect();
                }
                Debug.Log("[ProcProps] 地图截图完成 → " + OutDir);
            }
            finally
            {
                if (!string.IsNullOrEmpty(restore) && File.Exists(restore)) EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
            }
        }

        static void Shoot(string scenePath, string tag, MapSpec terrain) => Shoot(scenePath, tag, terrain, Views);

        // 展示图：低机位电影感 / 高空斜俯瞰 / 中线沿路，2× 超采样输出（本地缩小）。
        static readonly Vector3[,] ShowViews =
        {
            { new Vector3(-150f, 16f, 205f), new Vector3(-215f, 12f, 335f) },
            { new Vector3(-560f, 300f, -560f), new Vector3(0f, 0f, 30f) },
            { new Vector3(60f, 14f, -330f), new Vector3(-10f, 7f, -150f) },
            { new Vector3(-330f, 40f, -40f), new Vector3(-470f, 20f, 60f) },
        };

        [MenuItem("Tools/战场 1024/程序化地图/渲染 秋枫林地（展示图）", false, 81)]
        static void ShowAutumn()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcProps] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[ProcProps] 场景 " + SceneManager.GetSceneAt(i).name + " 有未保存的修改，已取消。"); return; }
            string restore = SceneManager.GetActiveScene().path;
            Directory.CreateDirectory(OutDir);
            var m = new AutumnWoodsMap();
            try { Shoot(m.ScenePath, "Show_" + m.Folder, m, ShowViews, 3840, 2160); Debug.Log("[ProcProps] 展示图完成 → " + OutDir); }
            finally { if (!string.IsNullOrEmpty(restore) && File.Exists(restore)) EditorSceneManager.OpenScene(restore, OpenSceneMode.Single); }
        }

        static void Shoot(string scenePath, string tag, MapSpec terrain, Vector3[,] views) => Shoot(scenePath, tag, terrain, views, 1280, 720);

        static void Shoot(string scenePath, string tag, MapSpec terrain, Vector3[,] views, int W, int H)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[ProcProps] 场景没有主相机：" + scenePath); return; }
            bool async = ShaderUtil.allowAsyncCompilation;
            var rt = new RenderTexture(W, H, 24);
            var img = new Texture2D(W, H, TextureFormat.RGB24, false);
            var oldTarget = cam.targetTexture; var active = RenderTexture.active;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                for (int v = 0; v < views.GetLength(0); v++)
                {
                    Vector3 c = views[v, 0], t = views[v, 1];
                    c.y += terrain.Height(c.x, c.z); t.y += terrain.Height(t.x, t.z);
                    cam.transform.position = c; cam.transform.LookAt(t);
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, W, H), 0, 0); img.Apply();
                    File.WriteAllBytes(OutDir + "/" + tag + "_" + v + ".jpg", img.EncodeToJPG(W > 2000 ? 92 : 85));
                }
            }
            finally
            {
                cam.targetTexture = oldTarget; RenderTexture.active = active;
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(img);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }
    }
}
