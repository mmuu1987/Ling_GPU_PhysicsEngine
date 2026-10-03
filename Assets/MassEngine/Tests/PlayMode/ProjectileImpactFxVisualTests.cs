#if UNITY_EDITOR
using System.Collections;
using System.IO;
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>
    /// Renders the opt-in impact ring/flash through the real URP camera path and counts fire-coloured pixels,
    /// so "the effect is actually on screen" is verified rather than only "the kernel recorded it".
    /// Evidence PNGs go to Logs/AgentImpactFx/visual-*.png.
    /// </summary>
    public sealed class ProjectileImpactFxVisualTests
    {
        private const string ShaderPath = "Assets/MassEngine/Projectiles/Shaders/ProjectileImpact.shader";

        private static IEnumerator Render(string name, float distance, bool withImpact, float age, int[] result)
        {
            var go = new GameObject("ImpactVisualCamera");
            var camera = go.AddComponent<Camera>();
            var rt = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.47f, 0.52f, 1f);
            camera.fieldOfView = 50f; camera.farClipPlane = 2000f;
            go.transform.position = new Vector3(0f, distance * 0.7f, -distance * 0.7f);
            go.transform.LookAt(Vector3.zero);
            camera.enabled = false;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader, ShaderPath);
            var material = new Material(shader) { enableInstancing = true }; // shader defaults = what new builds ship
            var config = ScriptableObject.CreateInstance<ProjectileRenderConfig>();
            config.impactMaterial = material;
            var dispatcher = new ProjectileGpuRenderDispatcher();
            var fx = new ProjectileImpactFx(4);
            try
            {
                var ring = new ProjectileImpactFx.ImpactData[4];
                if (withImpact) ring[0] = new ProjectileImpactFx.ImpactData { position = new Vector3(0f, 0.6f, 0f), radius = 3f, time = 1f, groundY = 0f };
                fx.Ring.SetData(ring);
                var bounds = new Bounds(Vector3.zero, Vector3.one * 500f);
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    // Batch-mode test runs never reach WaitForEndOfFrame; queue the draw and render this camera explicitly.
                    dispatcher.DrawImpacts(config, fx, bounds, 1f + age);
                    camera.Render();
                }
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = previous;
                int fire = 0;
                foreach (var c in tex.GetPixels32()) if (c.r > 170 && c.r > c.b + 60) fire++;
                result[0] = fire;
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AgentImpactFx"));
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "visual-" + name + ".png"), tex.EncodeToPNG());
                Debug.Log("IMPACT_VISUAL " + name + " firePixels=" + fire);
                Object.DestroyImmediate(tex);
            }
            finally
            {
                fx.Dispose(); dispatcher.Release(); Object.DestroyImmediate(config); Object.DestroyImmediate(material);
                camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            }
        }

        [UnityTest, Timeout(120000)] public IEnumerator ImpactRingIsVisibleThroughTheUrpCamera()
        {
            var none = new int[1]; var near = new int[1]; var far = new int[1]; var expired = new int[1];
            yield return Render("empty", 20f, false, 0.1f, none);
            yield return Render("near", 20f, true, 0.1f, near);
            yield return Render("far", 90f, true, 0.1f, far);
            yield return Render("expired", 20f, true, 5f, expired);
            Assert.AreEqual(0, none[0], "No impact -> nothing drawn.");
            Assert.Greater(near[0], 400, "A 3 m splash ring 20 m away must cover a clearly visible fire-coloured area.");
            Assert.Greater(far[0], 20, "Still visible from a battlefield-overview distance.");
            Assert.AreEqual(0, expired[0], "Expired impacts disappear.");
        }
    }
}
#endif
