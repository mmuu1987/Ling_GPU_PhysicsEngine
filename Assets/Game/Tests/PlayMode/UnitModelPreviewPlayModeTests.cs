#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public sealed class UnitModelPreviewPlayModeTests
    {
        private static WarSandboxBattlefieldCatalog Catalog => AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset");
        private static Color32[] Read(RenderTexture rt, string name = null)
        {
            var previous = RenderTexture.active; var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
                var pixels = texture.GetPixels32();
                if (name != null)
                {
                    texture.SetPixels32(pixels); texture.Apply();
                    var output = Environment.GetEnvironmentVariable("TOY_UI_OUTPUT"); if (!string.IsNullOrEmpty(output)) File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
                }
                return pixels;
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
        }
        private static Color32[] CaptureMarkerThroughUi(UnitTypeConfig config, string label)
        {
            var previousSize = WarSandboxUGUI.ScreenSizeOverride; var previousTarget = RenderTexture.active;
            GameObject owner = null, cameraObject = null; WarSandboxUGUI ui = null;
            RenderTexture target = null; Texture2D readback = null;
            try
            {
                WarSandboxUGUI.ScreenSizeOverride = new Vector2(320, 320);
                owner = new GameObject("Top-view marker UI"); ui = new WarSandboxUGUI(owner.transform, "Marker canvas", 300);
                ui.Begin(); ui.ModelPreview("marker", new Rect(0, 0, 640, 640), config, null); ui.End();
                var widget = owner.GetComponentInChildren<UnitModelPreviewWidget>(); Assert.That(widget.IsLive, Is.True);
                cameraObject = new GameObject("Marker UI capture camera"); var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                target = new RenderTexture(320, 320, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = owner.GetComponentInChildren<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                foreach (var t in owner.GetComponentsInChildren<Transform>()) t.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; readback = new Texture2D(320, 320, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0, 0, 320, 320), 0, 0); readback.Apply();
                var output = Environment.GetEnvironmentVariable("TOY_UI_OUTPUT");
                if (!string.IsNullOrEmpty(output)) File.WriteAllBytes(Path.Combine(output, label + ".png"), readback.EncodeToPNG());
                return readback.GetPixels32(); // Actual RawImage/canvas output: do NOT flip this a second time.
            }
            finally
            {
                RenderTexture.active = previousTarget; WarSandboxUGUI.ScreenSizeOverride = previousSize; ui?.Dispose();
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (owner != null) Object.DestroyImmediate(owner);
                if (readback != null) Object.DestroyImmediate(readback);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
        [TestCase("Universal Render Pipeline/MassEngine/VatInstancedNoShadow")]
        [TestCase("Universal Render Pipeline/MassEngine/LitInstancedAgent")]
        public void AboveCameraShowsGreenTopNotRedUnderside(string shaderName)
        {
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            GameObject cube = null; Mesh mesh = null; Texture2D positions = null, normals = null, atlas = null;
            Material material = null; VATProfile profile = null; RenderConfig render = null; UnitTypeConfig config = null;
            try
            {
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.SetActive(false);
                mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
                var vertices = mesh.vertices; var ns = mesh.normals; var uv = new Vector2[vertices.Length];
                positions = new Texture2D(vertices.Length, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
                normals = new Texture2D(vertices.Length, 1, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
                for (int i = 0; i < vertices.Length; i++)
                {
                    positions.SetPixel(i, 0, new Color(vertices[i].x, vertices[i].y, vertices[i].z, 1));
                    normals.SetPixel(i, 0, new Color(ns[i].x, ns[i].y, ns[i].z, 1));
                    uv[i] = new Vector2((ns[i].y > .5f ? 1.5f : ns[i].y < -.5f ? 2.5f : .5f) / 3, .5f);
                }
                mesh.uv = uv; positions.Apply(); normals.Apply();
                atlas = new Texture2D(3, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                atlas.SetPixels(new[] { Color.blue, Color.green, Color.red }); atlas.Apply();
                material = new Material(Shader.Find(shaderName)) { enableInstancing = true };
                material.SetTexture("_BaseMap", atlas); material.SetColor("_BaseColor", Color.white);
                profile = ScriptableObject.CreateInstance<VATProfile>(); profile.cleanMesh = mesh; profile.positionTexture = positions; profile.normalTexture = normals;
                profile.textureWidth = vertices.Length; profile.textureHeight = profile.rowsPerFrame = profile.totalFrameCount = profile.frameRate = 1;
                profile.idle = new VATProfile.VATClipWindow { startFrame = 0, frameCount = 1, frameRate = 1, loop = true };
                profile.move = profile.attack = profile.death = profile.idle;
                render = ScriptableObject.CreateInstance<RenderConfig>(); render.vatProfile = profile; render.nearMesh = mesh; render.nearMaterial = material;
                config = ScriptableObject.CreateInstance<UnitTypeConfig>(); config.renderConfig = render;
                using (var preview = new UnitModelPreviewRenderer(config))
                {
                    bool previousCull = GL.invertCulling;
                    preview.RadiusRingEnabled = false; preview.ShadowEnabled = false; preview.Render(0, 320, 320);
                    Assert.That(GL.invertCulling, Is.EqualTo(previousCull), "Preview must restore global culling state");
                    var pixels = Read(preview.Texture, shaderName.EndsWith("VatInstancedNoShadow") ? "marker-above-no-shadow" : "marker-above-lit");
                    int green = pixels.Count(p => p.a > 127 && p.g > 80 && p.g > p.r * 2 && p.g > p.b * 2);
                    int red = pixels.Count(p => p.a > 127 && p.r > 80 && p.r > p.g * 2 && p.r > p.b * 2);
                    Debug.Log("TOP_VIEW_MARKER " + shaderName + " green=" + green + " red=" + red);
                    Assert.That(green, Is.GreaterThan(200), "Above view must see green top, not the red underside. red=" + red);
                    Assert.That(red, Is.LessThan(10), "Closed opaque cube cannot show its underside from above.");
                    // With the same unflipped sampling used by RawImage the top face must be above the side-face centre.
                    float gy = pixels.Select((p,i) => new {p,i}).Where(v => v.p.g > 80 && v.p.g > v.p.r * 2 && v.p.g > v.p.b * 2).Average(v => v.i / 320f / 320f);
                    float by = pixels.Select((p,i) => new {p,i}).Where(v => v.p.b > 80 && v.p.b > v.p.r * 2 && v.p.b > v.p.g * 2).Average(v => v.i / 320f / 320f);
                    Assert.That(gy, Is.GreaterThan(by), "Final UI orientation must put the top above the sides.");
                    var uiPixels = CaptureMarkerThroughUi(config, shaderName.EndsWith("VatInstancedNoShadow") ? "marker-ui-no-shadow" : "marker-ui-lit");
                    int uiGreen = uiPixels.Count(p => p.g > 80 && p.g > p.r * 2 && p.g > p.b * 2);
                    int uiRed = uiPixels.Count(p => p.r > 80 && p.r > p.g * 2 && p.r > p.b * 2);
                    Assert.That(uiGreen, Is.GreaterThan(200), "Actual RawImage must show the green top");
                    Assert.That(uiRed, Is.LessThan(10), "Actual RawImage must hide the red underside");
                    float uiGy = uiPixels.Select((p,i) => new {p,i}).Where(v => v.p.g > 80 && v.p.g > v.p.r * 2 && v.p.g > v.p.b * 2).Average(v => v.i / 320f / 320f);
                    float uiBy = uiPixels.Select((p,i) => new {p,i}).Where(v => v.p.b > 80 && v.p.b > v.p.r * 2 && v.p.b > v.p.g * 2).Average(v => v.i / 320f / 320f);
                    Assert.That(uiGy, Is.GreaterThan(uiBy), "Actual UI top marker must remain above the sides");
                    preview.Rotate(new Vector2(0, 300)); preview.Render(0, 320, 320);
                    pixels = Read(preview.Texture, shaderName.EndsWith("VatInstancedNoShadow") ? "marker-below-no-shadow" : "marker-below-lit");
                    Assert.That(pixels.Count(p => p.a > 127 && p.r > 80 && p.r > p.g * 2 && p.r > p.b * 2), Is.GreaterThan(200), "Below view must expose the red underside.");
                }
            }
            finally
            {
                foreach (var obj in new Object[] { cube, mesh, positions, normals, atlas, material, profile, render, config }) if (obj != null) Object.DestroyImmediate(obj);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }
        private static int Changed(Color32[] a, Color32[] b) => a.Where((p, i) => (p.a > 127 || b[i].a > 127) && (Math.Abs(p.r - b[i].r) + Math.Abs(p.g - b[i].g) + Math.Abs(p.b - b[i].b) + Math.Abs(p.a - b[i].a) > 24)).Count();
        [UnityTest]
        public IEnumerator AllRepresentativesRenderAnimateAndReleaseWithoutAssetChanges()
        {
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            try
            {
                var choices = WarSandboxRosterChoices.Library(Catalog); Assert.That(choices.Count, Is.EqualTo(33));
                foreach (var entry in choices)
                {
                    string configBefore = EditorJsonUtility.ToJson(entry.config.renderConfig), matBefore = EditorJsonUtility.ToJson(entry.config.renderConfig.nearMaterial);
                    var light = Shader.GetGlobalVector("_MainLightColor"); bool fog = Shader.IsKeywordEnabled("FOG_LINEAR");
                    RenderTexture owned;
                    using (var preview = new UnitModelPreviewRenderer(entry.config))
                    {
                        preview.RadiusRingEnabled = false; preview.Render(preview.IdleDuration * .15f, 256, 320); owned = preview.Texture;
                        var withShadow = Read(owned);
                        Assert.That(withShadow.Count(p => p.a > 2 && p.a < 100), Is.GreaterThan(5), entry.templateId + " missing soft shadow");
                        var shadowCenter = preview.ShadowCenter; float shadowRadius = preview.ShadowRadius;
                        preview.RadiusRingEnabled = false; preview.ShadowEnabled = false; preview.Render(preview.IdleDuration * .15f, 256, 320);
                        var withoutShadow = Read(owned);
                        Assert.That(withShadow.Where((p, i) => p.a > 127 && !p.Equals(withoutShadow[i])).Count(), Is.EqualTo(0), "Shadow must not recolor the opaque character");
                        preview.ShadowEnabled = true; preview.Render(preview.IdleDuration * .15f, 256, 320);
                        var first = Read(owned); Assert.That(first.Count(p => p.a > 127), Is.GreaterThan(100), entry.templateId + " blank");
                        Assert.That(first.Count(p => p.a > 127 && p.r > 200 && p.b > 200 && p.g < 75), Is.LessThan(first.Count(p => p.a > 127) * .9f), entry.templateId + " pink");
                        // Default framing must retain all body parts (bounds include baked animation).
                        for (int x = 0; x < 256; x++) { Assert.That(first[x].a, Is.LessThan(128), entry.templateId); Assert.That(first[319 * 256 + x].a, Is.LessThan(128), entry.templateId); }
                        for (int y = 0; y < 320; y++) { Assert.That(first[y * 256].a, Is.LessThan(128), entry.templateId); Assert.That(first[y * 256 + 255].a, Is.LessThan(128), entry.templateId); }
                        preview.Render(preview.IdleDuration * .65f, 256, 320); var second = Read(owned);
                        Assert.That(Changed(first, second), Is.GreaterThan(3), entry.templateId + " frozen idle");
                        float fixedSize = preview.ViewHalfHeight; float fixedDistance = preview.CameraDistance;
                        preview.Rotate(new Vector2(180, 30)); preview.Render(preview.IdleDuration * .65f, 256, 320);
                        Assert.That(preview.CameraDistance, Is.EqualTo(fixedDistance).Within(.000001f), "Rotation cannot change perspective distance");
                        Assert.That(preview.ViewHalfHeight, Is.EqualTo(fixedSize).Within(.000001f), entry.templateId + " rotation must not zoom");
                        Assert.That(Changed(second, Read(owned)), Is.GreaterThan(20), entry.templateId + " rotation ignored");
                        preview.Scroll(999); Assert.That(preview.Zoom, Is.EqualTo(.6f)); preview.Render(.2f, 256, 320); Assert.That(preview.CameraDistance, Is.GreaterThan(preview.NearClip * 4)); preview.Scroll(-999); Assert.That(preview.Zoom, Is.EqualTo(1.8f));
                        Assert.That(preview.ShadowCenter, Is.EqualTo(shadowCenter)); Assert.That(preview.ShadowRadius, Is.EqualTo(shadowRadius));
                        preview.ResetView(); Assert.That(preview.Zoom, Is.EqualTo(1));
                        preview.Render(.2f, 256, 320); Read(owned, "preview-" + entry.templateId);
                        Assert.That(Shader.GetGlobalVector("_MainLightColor"), Is.EqualTo(light)); Assert.That(Shader.IsKeywordEnabled("FOG_LINEAR"), Is.EqualTo(fog));
                    }
                    Assert.That(owned.IsCreated(), Is.False);
                    Assert.That(EditorJsonUtility.ToJson(entry.config.renderConfig), Is.EqualTo(configBefore)); Assert.That(EditorJsonUtility.ToJson(entry.config.renderConfig.nearMaterial), Is.EqualTo(matBefore));
                    yield return null;
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
        }
        [Test]
        public void DragDirectionsAndProjectionStayStableAcrossOrbit()
        {
            using (var preview = new UnitModelPreviewRenderer(WarSandboxRosterChoices.Library(Catalog)[0].config))
            {
                Assert.That(preview.Yaw, Is.EqualTo(152));
                Assert.That(preview.Pitch, Is.EqualTo(28));
                Assert.That(UnitModelPreviewRenderer.FieldOfView, Is.EqualTo(30));
                float yaw = preview.Yaw, pitch = preview.Pitch;
                preview.Rotate(new Vector2(-20, -20));
                Assert.That(Mathf.DeltaAngle(yaw, preview.Yaw), Is.LessThan(0), "Left drag: orbit opposite to apparent model rotation");
                Assert.That(preview.Pitch, Is.GreaterThan(pitch), "Down drag: orbit opposite to apparent model rotation");
                preview.Rotate(new Vector2(20, 20));
                Assert.That(Mathf.DeltaAngle(yaw, preview.Yaw), Is.EqualTo(0).Within(.00001f));
                Assert.That(preview.Pitch, Is.EqualTo(pitch).Within(.00001f));
                preview.Render(.2f, 320, 240); float size = preview.ViewHalfHeight;
                float distance = preview.CameraDistance; var projection = preview.ProjectionMatrix;
                Assert.That(projection.m32, Is.EqualTo(-1)); Assert.That(projection.m33, Is.EqualTo(0));
                for (int i = 0; i < 36; i++)
                {
                    preview.Rotate(new Vector2(25, i < 18 ? 20 : -20)); preview.Render(.2f, 320, 240);
                    Assert.That(preview.CameraDistance, Is.EqualTo(distance).Within(.000001f));
                    Assert.That(preview.ProjectionMatrix, Is.EqualTo(projection));
                    Assert.That(preview.ViewHalfHeight, Is.EqualTo(size).Within(.000001f));
                }
                preview.Scroll(1); preview.Render(.2f, 320, 240); Assert.That(preview.ViewHalfHeight, Is.LessThan(size));
                Assert.That(preview.CameraDistance, Is.LessThan(distance)); Assert.That(preview.ProjectionMatrix, Is.EqualTo(projection));
                preview.ResetView(); preview.Render(.2f, 320, 240); Assert.That(preview.ViewHalfHeight, Is.EqualTo(size).Within(.000001f));
                Assert.That(preview.Yaw, Is.EqualTo(152));
                Assert.That(preview.Pitch, Is.EqualTo(28)); Assert.That(preview.CameraDistance, Is.EqualTo(distance).Within(.000001f));
            }
        }
        [UnityTest]
        public IEnumerator WidgetCapturesInputSwitchesExactConfigAndReleasesOnHide()
        {
            var owner = new GameObject("Preview lifecycle test"); var ui = new WarSandboxUGUI(owner.transform, "Preview UI", 300);
            try
            {
                var entries = WarSandboxRosterChoices.Library(Catalog);
                ui.Begin(); ui.ModelPreview("subject", new Rect(0, 0, 320, 360), entries[0].config, entries[0].unitPreview); ui.End(); yield return null;
                var stage = owner.GetComponentsInChildren<Image>().Single(x => x.name == "subject-stage");
                Assert.That(stage.color, Is.EqualTo((Color)new Color32(164, 190, 202, 255)));
                var widget = owner.GetComponentInChildren<UnitModelPreviewWidget>(); Assert.That(widget.IsLive, Is.True);
                var rt = widget.Renderer.Texture; var yaw = widget.Renderer.Yaw;
                var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(50, 20), scrollDelta = Vector2.up };
                ExecuteEvents.Execute(widget.gameObject, pointer, ExecuteEvents.dragHandler); ExecuteEvents.Execute(widget.gameObject, pointer, ExecuteEvents.scrollHandler);
                Assert.That(widget.Renderer.Yaw, Is.Not.EqualTo(yaw)); Assert.That(widget.Renderer.Zoom, Is.LessThan(1));
                widget.ResetView(); Assert.That(widget.Renderer.Zoom, Is.EqualTo(1));
                ui.Begin(); ui.ModelPreview("subject", new Rect(0, 0, 320, 360), entries[1].config, entries[1].unitPreview); ui.End();
                Assert.That(widget.Config, Is.SameAs(entries[1].config)); Assert.That(rt.IsCreated(), Is.False);
                rt = widget.Renderer.Texture; ui.Begin(); ui.End(); Assert.That(widget.IsLive, Is.False); Assert.That(rt.IsCreated(), Is.False);
                ui.Begin(); ui.ModelPreview("subject", new Rect(0, 0, 320, 360), entries[1].config, entries[1].unitPreview); ui.End(); Assert.That(widget.IsLive, Is.True);
                ui.SetVisible(false); Assert.That(widget.IsLive, Is.False);
                ui.SetVisible(true); yield return null; Assert.That(widget.IsLive, Is.True);
                ui.Begin(); ui.ModelPreview("subject", new Rect(0, 0, 320, 360), null, entries[0].unitPreview); ui.End();
                Assert.That(widget.IsLive, Is.False); Assert.That(widget.GetComponent<RawImage>().texture, Is.SameAs(entries[0].unitPreview));
            }
            finally { ui.Dispose(); Object.Destroy(owner); }
            yield return null;
        }
    }
}
#endif

