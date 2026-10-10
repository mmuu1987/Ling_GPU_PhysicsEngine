#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public class UnitPreviewRadiusPlayModeTests
    {
        private sealed class Fixture : IDisposable
        {
            public UnitTypeConfig config;
            public readonly List<Object> owned = new List<Object>();
            private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
            public Fixture(float radius, Vector3 size, Vector3 offset)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh mesh;
                try { mesh = Own(Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh)); }
                finally { Object.DestroyImmediate(cube); }
                var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Scale(vertices[i], size) + offset;
                mesh.vertices = vertices; mesh.RecalculateBounds();
                var positions = Own(new Texture2D(vertices.Length, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
                var normals = Own(new Texture2D(vertices.Length, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point });
                for (int i = 0; i < vertices.Length; i++)
                {
                    var v = vertices[i]; var n = mesh.normals[i];
                    positions.SetPixel(i, 0, new Color(v.x, v.y, v.z, 1)); normals.SetPixel(i, 0, new Color(n.x, n.y, n.z, 1));
                }
                positions.Apply(); normals.Apply();
                var texture = Own(new Texture2D(1, 1)); texture.SetPixel(0, 0, Color.blue); texture.Apply();
                var mat = Own(new Material(Shader.Find("Universal Render Pipeline/MassEngine/VatInstancedNoShadow")) { enableInstancing = true });
                mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", Color.white);
                var profile = Own(ScriptableObject.CreateInstance<VATProfile>());
                profile.cleanMesh = mesh; profile.positionTexture = positions; profile.normalTexture = normals;
                profile.textureWidth = vertices.Length; profile.textureHeight = profile.rowsPerFrame = profile.totalFrameCount = profile.frameRate = 1;
                profile.idle = new VATProfile.VATClipWindow { startFrame = 0, frameCount = 1, frameRate = 1, loop = true };
                profile.move = profile.attack = profile.death = profile.idle;
                var render = Own(ScriptableObject.CreateInstance<RenderConfig>()); render.nearMesh = mesh; render.nearMaterial = mat; render.vatProfile = profile;
                config = Own(ScriptableObject.CreateInstance<UnitTypeConfig>()); config.renderConfig = render;
                config.flockingConfig = Own(ScriptableObject.CreateInstance<FlockingConfig>()); config.flockingConfig.agentRadius = radius;
            }
            public void Dispose() { foreach (var value in owned) if (value != null) Object.DestroyImmediate(value); }
        }
        private static string Output
        {
            get
            {
                string prefix = Environment.GetCommandLineArgs().Any(a => a.StartsWith("--interaction-p2-output=")) ? "--interaction-p2-output=" : "--interaction-p1-output=";
                var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
                if (arg == null) return null;
                string path = Path.Combine(arg.Substring(prefix.Length), "radius-evidence"); Directory.CreateDirectory(path); return path;
            }
        }
        private static Color32[] Read(RenderTexture target, string name)
        {
            var old = RenderTexture.active; var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target; tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); tex.Apply();
                if (Output != null) File.WriteAllBytes(Path.Combine(Output, name + ".png"), tex.EncodeToPNG());
                return tex.GetPixels32();
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(tex); }
        }
        private static bool Gold(Color32 c) => c.a > 240 && c.r > 100 && c.r < 180 && c.g > 40 && c.g < 105 && c.b < 50;
        private static Camera ReferenceCamera(UnitModelPreviewRenderer preview, int width, int height)
        {
            var cam = new GameObject("Independent metric ruler camera").AddComponent<Camera>(); cam.enabled = false;
            cam.fieldOfView = UnitModelPreviewRenderer.FieldOfView; cam.aspect = (float)width / height;
            cam.nearClipPlane = preview.NearClip; cam.farClipPlane = preview.CameraDistance * 4 + 20;
            cam.transform.position = preview.CameraPosition; cam.transform.LookAt(preview.FrameBounds.center);
            return cam;
        }
        // A gold pixel is unprojected by a real Camera ray onto world Y=0. Its measured
        // distance from the AGENT ORIGIN must match the known fixture length; neither the
        // preview matrix nor the mesh bounds centre is used as the measurement ruler.
        [TestCase(1f, false, 1f, 1f)]
        [TestCase(.45f, false, 1f, 1f)]
        [TestCase(4.8f, false, 1f, 1f)]
        [TestCase(.55f, true, 1f, 1f)]
        [TestCase(.55f, true, 2f, .5f)]
        [TestCase(.55f, false, .5f, 1.5f)]
        public void RingMeasuresKnownWorldLengthInBothPreviewSizes(float radius, bool asymmetric, float sx, float sz)
        {
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var size = new Vector3(.35f, 1.7f, .3f) * Mathf.Max(.4f, radius);
            var offset = new Vector3(asymmetric ? radius * 1.5f : 0, size.y * .5f, asymmetric ? radius * .3f : 0);
            try
            {
                using (var fixture = new Fixture(radius, size, offset))
                using (var preview = new UnitModelPreviewRenderer(fixture.config))
                {
                    var before = fixture.owned.Select(o => EditorJsonUtility.ToJson(o)).ToArray();
                    preview.SetSubjectScale(new Vector3(sx, 1, sz));
                    Assert.That(preview.HasRadiusRing, Is.True); Assert.That(preview.RadiusCenter, Is.EqualTo(Vector3.zero));
                    float expected = radius * Mathf.Max(sx, sz);
                    foreach (var resolution in new[] { new Vector2Int(214, 320), new Vector2Int(512, 512) })
                    {
                        preview.Render(0, resolution.x, resolution.y);
                        string name = $"r{radius}-asym{asymmetric}-scale{sx}x{sz}-{resolution.x}x{resolution.y}";
                        var pixels = Read(preview.Texture, name); var cam = ReferenceCamera(preview, resolution.x, resolution.y);
                        try
                        {
                            var plane = new Plane(Vector3.up, Vector3.zero); var errors = new List<float>();
                            for (int i = 0; i < pixels.Length; i++) if (Gold(pixels[i]))
                            {
                                float u = ((i % resolution.x) + .5f) / resolution.x, v = ((i / resolution.x) + .5f) / resolution.y;
                                var ray = cam.ViewportPointToRay(new Vector3(u, v, 0));
                                Assert.That(plane.Raycast(ray, out float enter), Is.True);
                                errors.Add(Mathf.Abs(ray.GetPoint(enter).magnitude / expected - 1));
                            }
                            Assert.That(errors.Count, Is.GreaterThan(40), "Metric ring must be visibly rendered, not just a property");
                            errors.Sort(); float p95 = errors[(int)((errors.Count - 1) * .95f)];
                            Assert.That(p95, Is.LessThan(.02f), "World ruler disagrees with visible ring (includes raster footprint)");
                            // An independent Camera must also agree with preview projection, across the entire circle.
                            for (int i = 0; i < 128; i++)
                            {
                                float angle = i * Mathf.PI * 2 / 128; var world = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * expected;
                                var uv = cam.WorldToViewportPoint(world);
                                var clip = preview.ProjectionMatrix * preview.ViewMatrix * new Vector4(world.x, world.y, world.z, 1);
                                Assert.That(clip.x / clip.w * .5f + .5f, Is.EqualTo(uv.x).Within(.00001));
                                Assert.That(clip.y / clip.w * .5f + .5f, Is.EqualTo(uv.y).Within(.00001));
                                Assert.That(uv.x, Is.InRange(.01f, .99f)); Assert.That(uv.y, Is.InRange(.01f, .99f));
                            }
                            Debug.Log($"P1_WORLD_RULER {name} expected={expected} goldPixels={errors.Count} relativeErrorP95={p95}");
                            if (Output != null) File.WriteAllText(Path.Combine(Output, name + ".txt"), $"expectedWorldRadius={expected}\nagentAnchor=(0,0,0)\ngoldPixels={errors.Count}\nrelativeErrorP95={p95}\n");
                        }
                        finally { Object.DestroyImmediate(cam.gameObject); }
                    }
                    // Orbit never changes circle units/anchor. Default fit must contain both the model and ring.
                    for (int i = 0; i < 8; i++)
                    {
                        preview.Rotate(new Vector2(112.5f, i % 2 == 0 ? 250 : -250)); preview.Render(0, 214, 320);
                        var b = preview.FrameBounds;
                        for (int corner = 0; corner < 8; corner++)
                        {
                            var p = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                            var clip = preview.ProjectionMatrix * preview.ViewMatrix * new Vector4(p.x, p.y, p.z, 1);
                            Assert.That(Mathf.Abs(clip.x / clip.w), Is.LessThan(1)); Assert.That(Mathf.Abs(clip.y / clip.w), Is.LessThan(1));
                        }
                        Assert.That(preview.ContactRadius, Is.EqualTo(expected).Within(.000001));
                    }
                    preview.Scroll(5); preview.Render(0, 214, 320); Assert.That(preview.ContactRadius, Is.EqualTo(expected).Within(.000001));
                    preview.ResetView(); preview.Render(0, 214, 320);
                    Assert.That(fixture.owned.Select(o => EditorJsonUtility.ToJson(o)).ToArray(), Is.EqualTo(before), "Preview must not mutate subject assets");
                    var target = preview.Texture; preview.Dispose(); preview.Dispose();
                    Assert.That(preview.Texture, Is.Null); Assert.That(preview.HasRadiusRing, Is.False);
                    // Runtime Object.Destroy is end-of-frame; the RT itself is immediately released.
                    Assert.That(target == null || !target.IsCreated(), Is.True);
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
        }
        private static int PreviewNativeResources() => Resources.FindObjectsOfTypeAll<Material>().Count(x => x.name.StartsWith("Unit preview", StringComparison.Ordinal))
            + Resources.FindObjectsOfTypeAll<Mesh>().Count(x => x.name.StartsWith("Unit preview", StringComparison.Ordinal))
            + Resources.FindObjectsOfTypeAll<RenderTexture>().Count(x => x.name == "Unit preview target");

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator WidgetSwitchAndCloseReclaimsEveryOwnedPreviewResource()
        {
            yield return null;
            int baseline = PreviewNativeResources();
            var go = new GameObject("P1 preview lifecycle", typeof(RectTransform)); go.SetActive(false);
            go.AddComponent<UnityEngine.UI.RawImage>(); var widget = go.AddComponent<UnitModelPreviewWidget>();
            try
            {
                using (var a = new Fixture(.55f, new Vector3(.4f, 1.6f, .4f), new Vector3(0, .8f, 0)))
                using (var b = new Fixture(2f, new Vector3(2, 2, 1), new Vector3(1, 1, 0)))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        widget.Bind(a.config, null); go.SetActive(true); yield return null;
                        Assert.That(widget.IsLive, Is.True); Assert.That(widget.Renderer.HasRadiusRing, Is.True);
                        var first = widget.Renderer;
                        widget.Bind(b.config, null); yield return null;
                        Assert.That(first.HasRadiusRing, Is.False); Assert.That(first.Texture, Is.Null);
                        Assert.Throws<ObjectDisposedException>(() => first.Render(0, 128, 128));
                        foreach (var field in new[] { "agents", "visible", "arguments", "commands" })
                            Assert.That(typeof(UnitModelPreviewRenderer).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(first), Is.Null);
                        Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(2));
                        go.SetActive(false); yield return null; yield return null;
                        Assert.That(widget.IsLive, Is.False); Assert.That(PreviewNativeResources(), Is.EqualTo(baseline), "Cycle " + i + " leaked preview-native resources");
                    }
                }
            }
            finally { Object.DestroyImmediate(go); }
            yield return null;
            Assert.That(PreviewNativeResources(), Is.EqualTo(baseline));
        }

        private static void FrontField(WarSandboxFrontEnd front, string name, object value) => typeof(WarSandboxFrontEnd)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(front, value);
        private static void FrontDraw(WarSandboxFrontEnd front, string name) => typeof(WarSandboxFrontEnd)
            .GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(front, null);

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ActualLibraryAndExpandedUiRenderTheSameReadOnlyRadius()
        {
            Assert.That(WarSandboxSceneSession.Instance, Is.Null, "This case requires its own empty TestRunner scene");
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var oldSize = WarSandboxUGUI.ScreenSizeOverride;
            var owner = new GameObject("P1 actual library fixture"); var session = owner.AddComponent<WarSandboxSceneSession>();
            session.enterDefaultOnStart = false; session.enabled = false;
            session.catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
            Assert.That(session.catalog, Is.Not.Null);
            var choices = WarSandboxRosterChoices.Library(session.catalog); Assert.That(choices.Count, Is.GreaterThan(0));
            var front = owner.AddComponent<WarSandboxFrontEnd>(); front.enabled = false;
            var ui = new WarSandboxUGUI(owner.transform, "P1 actual UI", 200); FrontField(front, "ui", ui);
            front.LibraryStore = new WarSandboxUnitStatStore(Path.Combine(Output ?? Application.temporaryCachePath, "p1-ui-readonly-globals.json"));
            front.OpenLibrary();
            var cameraObject = new GameObject("P1 UI capture camera"); var cam = cameraObject.AddComponent<Camera>(); cam.enabled = false;
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.nearClipPlane = .1f; cam.farClipPlane = 20; cam.cullingMask = 1 << 31;
            var canvas = owner.GetComponentInChildren<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1;
            string before = JsonUtility.ToJson(session.catalog);
            try
            {
                foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
                {
                    WarSandboxUGUI.ScreenSizeOverride = resolution;
                    var target = new RenderTexture(resolution.x, resolution.y, 24, RenderTextureFormat.ARGB32); target.Create(); cam.targetTexture = target;
                    cam.aspect = (float)resolution.x / resolution.y; cam.orthographicSize = resolution.y * .5f;
                    try
                    {
                        FrontField(front, "unitPreviewOpen", false);
                        ui.Begin(); FrontDraw(front, "DrawLibrary"); ui.End(); Canvas.ForceUpdateCanvases(); yield return null;
                        var inline = owner.GetComponentsInChildren<UnitModelPreviewWidget>().Single();
                        Assert.That(inline.IsLive && inline.Renderer.HasRadiusRing, Is.True);
                        float radius = inline.Renderer.ContactRadius;
                        var texts = owner.GetComponentsInChildren<UnityEngine.UI.Text>();
                        Assert.That(texts.Any(t => t.transform.parent.name == "lib-radius-value" && t.text.Contains("预览")), Is.True);
                        Assert.That(owner.GetComponentsInChildren<UnityEngine.UI.InputField>().Any(t => t.gameObject.name == "lib-r-agentRadius-field"), Is.True);
                        CaptureUi(owner, cam, target, inline, "library-" + resolution.x + "x" + resolution.y);
                        FrontField(front, "enlargedUnit", choices[0]); FrontField(front, "unitPreviewOpen", true);
                        ui.Begin(); FrontDraw(front, "DrawLibrary"); FrontDraw(front, "DrawExpandedUnitPreview"); ui.End(); Canvas.ForceUpdateCanvases(); yield return null;
                        var expanded = owner.GetComponentsInChildren<UnitModelPreviewWidget>().Single();
                        Assert.That(expanded.IsLive && expanded.Renderer.HasRadiusRing, Is.True);
                        Assert.That(expanded.Renderer.ContactRadius, Is.EqualTo(radius));
                        Assert.That(inline.IsLive, Is.False, "Hidden inline preview must release GPU resources");
                        CaptureUi(owner, cam, target, expanded, "expanded-" + resolution.x + "x" + resolution.y);
                    }
                    finally { cam.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
                }
                Assert.That(JsonUtility.ToJson(session.catalog), Is.EqualTo(before));
                Assert.That(File.Exists(Path.Combine(Output ?? Application.temporaryCachePath, "p1-ui-readonly-globals.json")), Is.False, "Read-only UI must not save");
            }
            finally
            {
                WarSandboxUGUI.ScreenSizeOverride = oldSize; ShaderUtil.allowAsyncCompilation = async;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(owner);
            }
            yield return null;
        }
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator RealLibrarySmallLargeAndWingedSubjectsKeepTemplateRadius()
        {
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
            var choices = WarSandboxRosterChoices.Library(catalog);
            var sorted = choices.OrderBy(x => UnitPreviewRadius.Read(x.config).BaseRadius).ToList();
            var selected = new[] { choices[0], sorted.First(), sorted.Last(), choices.FirstOrDefault(x => x.templateId.Contains("dragon")) ?? sorted.Last() }.Distinct().ToArray();
            try
            {
                foreach (var entry in selected)
                {
                    string config = EditorJsonUtility.ToJson(entry.config), flock = EditorJsonUtility.ToJson(entry.config.flockingConfig);
                    using (var preview = new UnitModelPreviewRenderer(entry.config))
                    {
                        preview.Render(preview.IdleDuration * .15f, 512, 512);
                        Assert.That(preview.ContactRadius, Is.EqualTo(new DefaultSwordUnit(entry.config).BuildGpuSettings().agentRadius));
                        var pixels = Read(preview.Texture, "real-" + entry.templateId);
                        Assert.That(pixels.Count(Gold), Is.GreaterThan(10), entry.templateId + " must visibly show its independent physical ring");
                        Debug.Log($"P1_REAL_SUBJECT {entry.templateId} radius={preview.ContactRadius} anchor={preview.RadiusCenter} frame={preview.FrameBounds}");
                    }
                    Assert.That(EditorJsonUtility.ToJson(entry.config), Is.EqualTo(config)); Assert.That(EditorJsonUtility.ToJson(entry.config.flockingConfig), Is.EqualTo(flock));
                    yield return null;
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
        }

        private static void CaptureUi(GameObject owner, Camera cam, RenderTexture target, UnitModelPreviewWidget widget, string name)
        {
            foreach (var t in owner.GetComponentsInChildren<Transform>()) t.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases(); cam.Render(); var pixels = Read(target, name);
            Assert.That(pixels.Count(p => p.r + p.g + p.b > 10), Is.GreaterThan(pixels.Length / 2), "Reject blank UI evidence");
            Assert.That(pixels.Distinct().Count(), Is.GreaterThan(100), "Require actual rendered UI, not only a solid background");
            var corners = new Vector3[4]; ((RectTransform)widget.transform).GetWorldCorners(corners);
            var a = cam.WorldToViewportPoint(corners[0]); var b = cam.WorldToViewportPoint(corners[2]);
            int gold = 0;
            for (int y = Mathf.Clamp(Mathf.FloorToInt(a.y * target.height), 0, target.height); y < Mathf.Clamp(Mathf.CeilToInt(b.y * target.height), 0, target.height); y++)
                for (int x = Mathf.Clamp(Mathf.FloorToInt(a.x * target.width), 0, target.width); x < Mathf.Clamp(Mathf.CeilToInt(b.x * target.width), 0, target.width); x++)
                    if (Gold(pixels[y * target.width + x])) gold++;
            Assert.That(gold, Is.GreaterThan(5), "Actual RawImage must display the physical circle, not only the renderer texture");
            Debug.Log($"P1_UI_PIXELS {name} goldInModelViewport={gold} colors={pixels.Distinct().Count()}");
        }
    }
}
#endif

