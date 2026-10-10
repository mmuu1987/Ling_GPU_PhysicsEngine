#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>
    /// Integration evidence for overnight task 2: loads the shipped dragon battlefield, starts the battle and, at the
    /// first splash impact, renders the scene camera plus a close-up camera. Evidence: Logs/AgentImpactFx/scene-*.png.
    /// </summary>
    public sealed class ProjectileImpactFxSceneTests
    {
        // Newest dragon integration (IntegrateFire05).
        private const string ScenePath = "Assets/Game/Content/Characters/Dragons/Prepared07/Integrated/dragonBattlefield.unity";
        private static readonly FieldInfo FxField = typeof(MassEngineManager).GetField("projectileImpactFx", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TimeField = typeof(MassEngineManager).GetField("projectileSimulationTime", BindingFlags.Instance | BindingFlags.NonPublic);

        private static int Shoot(Camera camera, string file, Vector3? lookAt, float distance)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            Camera cam = camera;
            GameObject temp = null;
            if (lookAt.HasValue)
            {
                temp = new GameObject("ImpactCloseUp"); cam = temp.AddComponent<Camera>(); cam.enabled = false;
                cam.CopyFrom(camera); cam.fieldOfView = 45f;
                temp.transform.position = lookAt.Value + new Vector3(0f, distance * 0.75f, -distance * 0.66f);
                temp.transform.LookAt(lookAt.Value);
            }
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = previousTarget;
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = previous;
            int fire = 0;
            foreach (var c in tex.GetPixels32()) if (c.r > 170 && c.r > c.b + 60) fire++;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentImpactFx"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            if (temp != null) Object.DestroyImmediate(temp);
            return fire;
        }

        /// <summary>Leaves an empty scene behind so later camera-based tests do not see the battlefield.</summary>
        [UnityTearDown] public IEnumerator UnloadBattlefield()
        {
            var empty = SceneManager.CreateScene("ImpactFxEmpty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
            yield return null;
        }

        [UnityTest, Timeout(300000)] public IEnumerator DragonBattlefieldShowsImpactRings()
        {
            Assert.IsNotNull(FxField); Assert.IsNotNull(TimeField);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            MassEngineManager manager = null;
            for (int i = 0; i < 300 && (manager == null || !manager.isActiveAndEnabled); i++) { manager = Object.FindFirstObjectByType<MassEngineManager>(); yield return null; }
            Assert.IsNotNull(manager, "Battlefield has a MassEngineManager.");
            for (int i = 0; i < 30; i++) yield return null;
            var camera = Camera.main != null ? Camera.main : (Camera.allCamerasCount > 0 ? Camera.allCameras[0] : Object.FindFirstObjectByType<Camera>());
            Assert.IsNotNull(camera, "Battlefield has a camera.");
            Debug.Log("IMPACT_SCENE camera=" + camera.transform.position + " fov=" + camera.fieldOfView + " cameras=" + Camera.allCamerasCount + " name=" + camera.name + " fxAllocatedBeforeStart=" + (FxField.GetValue(manager) != null));
            manager.StartBattle();
            float started = Time.realtimeSinceStartup; uint last = 0; int shots = 0; int maxOverview = 0, maxClose = 0;
            var counter = new uint[1];
            while (Time.realtimeSinceStartup - started < 150f && shots < 3)
            {
                yield return null;
                var fx = FxField.GetValue(manager) as ProjectileImpactFx;
                if (fx == null || !fx.IsValid) continue;
                fx.Counter.GetData(counter);
                if (counter[0] == last) continue;
                last = counter[0];
                var ring = new ProjectileImpactFx.ImpactData[fx.Capacity]; fx.Ring.GetData(ring);
                var newest = ring[(int)((last - 1) % (uint)fx.Capacity)];
                float now = (float)TimeField.GetValue(manager);
                // Let the ring grow a little (it starts at 30 % radius), then render with the draw queued this frame.
                for (int i = 0; i < 4; i++) yield return null;
                shots++;
                int overview = Shoot(camera, "scene-overview-" + shots + ".png", null, 0f);
                int close = Shoot(camera, "scene-close-" + shots + ".png", newest.position, 28f);
                maxOverview = Mathf.Max(maxOverview, overview); maxClose = Mathf.Max(maxClose, close);
                Debug.Log("IMPACT_SCENE shot=" + shots + " impacts=" + last + " at=" + newest.position + " r=" + newest.radius + " stamped=" + newest.time + " now=" + now + " overviewFire=" + overview + " closeFire=" + close);
            }
            Debug.Log("IMPACT_SCENE done impacts=" + last + " maxOverview=" + maxOverview + " maxClose=" + maxClose);
            Assert.Greater(last, 0u, "Dragons produced splash impacts on the shipped battlefield.");
            Assert.Greater(maxClose, 300, "The impact ring is clearly visible in a close-up of the real battlefield.");
        }
    }
}
#endif
