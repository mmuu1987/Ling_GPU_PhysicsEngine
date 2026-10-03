using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MassEngine.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Source-completeness evidence, not another bake. Instantiates every mesh in the ORIGINAL FBX,
    /// samples its original clips and draws all original submeshes with their source solid colours.
    /// Runtime comparisons use the shipped config and actual procedural GPU VAT pass. Source views
    /// are unlit geometry evidence, NOT an independent validation of the author's material shader.
    /// No importer, source/config asset, scene, personal data or historical evidence is written.
    /// </summary>
    public static class PlatformerSourceReview
    {
        [Serializable] public sealed class SourceReport
        {
            public string method = "Original FBX: all native baked-skinned/rigid meshes and all submeshes; original clips; source solid colours, neutral unlit shader. Game: existing config, full-LOD procedural indirect VAT GPU pass. No rebake.";
            public bool technicalCapturePassed;
            public string humanAcceptance = "Not performed; user playtesting remains paused.";
            public string graphicsDevice;
            public SourceUnit[] units;
        }
        [Serializable] public sealed class SourceUnit
        {
            public string name, source, sourceSha256, templateId;
            public string[] meshNames, boneNames, materialNames, clips;
            public int originalVertices, originalTriangles, gameVertices, gameTriangles;
            public bool allTriangleCountsMatch;
            public string[] images;
        }
        private sealed class Part : IDisposable
        {
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Material[] materials;
            public void Dispose()
            {
                Object.DestroyImmediate(mesh);
                if (materials != null) foreach (var m in materials) Object.DestroyImmediate(m);
            }
        }
        public static void Capture01()
        {
            string output = Environment.GetEnvironmentVariable("WAR_SANDBOX_QUALITY_OUTPUT");
            Require(!string.IsNullOrWhiteSpace(output), "Runner must supply a fresh quality output directory.");
            Require(!File.Exists(Path.Combine(output, "source-review.json")), "Never overwrite source-review evidence.");
            Directory.CreateDirectory(output);
            var catalog = Load<WarSandboxBattlefieldCatalog>("Assets/Game/OfficialRoster/Version06/Catalog.asset");
            var report = new SourceReport { graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName };
            var units = new List<SourceUnit>();
            foreach (string name in new[] { "Crab", "Enemy", "Skull" })
            {
                string key = name.ToLowerInvariant(), source = "Assets/CharacterPilotSource/QuaterniusPlatformerBatch6/" + name + ".fbx";
                string id = "roster-platformer-" + key;
                var config = catalog.templates.First(t => t.templateId == id).config;
                var runtime = ResolvedUnitTypeRuntime.Resolve(config, 1f);
                var model = Load<GameObject>(source);
                var originals = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh)
                    .Concat(model.GetComponentsInChildren<MeshFilter>(true).Where(f => f.GetComponent<MeshRenderer>() != null).Select(f => f.sharedMesh)).ToArray();
                var clips = new[] { Clip(source, "Idle"), Clip(source, "Walk"), Clip(source, "Bite_InPlace"), Clip(source, "Death") };
                var row = new SourceUnit
                {
                    name = name, source = source, sourceSha256 = Sha(source), templateId = id,
                    meshNames = model.GetComponentsInChildren<Renderer>(true).Select(s => s.name).ToArray(),
                    boneNames = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).Where(b => b != null).Select(b => b.name).Distinct().ToArray(),
                    materialNames = model.GetComponentsInChildren<Renderer>(true).SelectMany(s => s.sharedMaterials).Select(m => m.name).Distinct().ToArray(),
                    clips = clips.Select(c => c.name).ToArray(), originalVertices = originals.Sum(m => m.vertexCount),
                    originalTriangles = originals.Sum(Triangles), gameVertices = runtime.GetMesh(0).vertexCount, gameTriangles = Triangles(runtime.GetMesh(0))
                };
                row.allTriangleCountsMatch = row.originalTriangles == row.gameTriangles;
                Require(row.allTriangleCountsMatch, name + ": original/full-game triangle count differs; investigate before claiming completeness.");
                var files = new List<string>();
                for (int c = 0; c < clips.Length; c++)
                {
                    string file = key + "-original-" + c + ".png";
                    CaptureOriginal(model, clips[c], clips[c].length * .4f, Path.Combine(output, file)); files.Add(file);
                }
                Bounds bounds = runtime.GetMesh(0).bounds;
                bounds.Encapsulate(runtime.GetMesh(1).bounds); bounds.Encapsulate(runtime.GetMesh(2).bounds);
                using (var capture = new VatAppearanceRegressionCapture(bounds, 640))
                {
                    var states = new[] { AgentState.Idle, AgentState.Move, AgentState.Attack, AgentState.Dead };
                    var durations = new[] { runtime.idleClipDuration, runtime.moveClipDuration, runtime.attackClipDuration, runtime.deathClipDuration };
                    for (int c = 0; c < states.Length; c++)
                    {
                        var samples = new List<Color32[]>();
                        for (int f = 0; f < 3; f++)
                        {
                            var pixels = capture.Capture(runtime, 0, states[c], durations[c] * new[] { .15f, .45f, .8f }[f]);
                            Check(pixels); samples.Add(pixels);
                            string file = key + "-game-" + c + "-" + f + ".png";
                            Save(Path.Combine(output, file), capture.Size, pixels); files.Add(file);
                        }
                        Require(VatAppearanceRegressionImageChecks.HasMotion(samples, out int changed), name + " " + states[c] + " has no detectable motion.");
                    }
                }
                row.images = files.ToArray(); units.Add(row);
                Debug.Log("SOURCE_QUALITY_CAPTURE " + name + " original=" + row.originalVertices + "/" + row.originalTriangles + " game=" + row.gameVertices + "/" + row.gameTriangles);
            }
            report.units = units.ToArray(); report.technicalCapturePassed = true;
            WriteNew(Path.Combine(output, "source-review.json"), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(report, true)));
            Debug.Log("SOURCE_QUALITY_REVIEW_READY " + output);
        }
        private static void CaptureOriginal(GameObject model, AnimationClip clip, float time, string path)
        {
            var stage = new GameObject("Read-only original FBX source view") { hideFlags = HideFlags.HideAndDontSave };
            stage.SetActive(false);
            var instance = Object.Instantiate(model, stage.transform, false);
            foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var parts = new List<Part>();
            RenderTexture target = null; Texture2D texture = null; Shader sourceShader = null;
            RenderTexture old = RenderTexture.active;
            bool oldAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // Error/compiling placeholders are not source evidence.
            Matrix4x4 oldView = Shader.GetGlobalMatrix("unity_MatrixV"), oldProjection = Shader.GetGlobalMatrix("unity_MatrixP");
            try
            {
                clip.SampleAnimation(instance, time);
                // A direct source-geometry draw must not depend on a camera having initialized URP.
                // The previous failed URP-Unlit fixture is kept in source-01/02; no import/bake was changed.
                sourceShader = ShaderUtil.CreateShaderAsset(@"
Shader ""Hidden/WarSandbox/ReadOnlySourceColour"" {
Properties { _Color (""Source solid colour"", Color) = (1,1,1,1) }
SubShader { Tags { ""RenderType""=""Opaque"" }
Pass { Cull Off ZWrite On ZTest LEqual
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""UnityCG.cginc""
struct appdata { float4 vertex : POSITION; };
struct v2f { float4 position : SV_POSITION; };
float4 _Color;
v2f vert(appdata v) { v2f o; o.position = UnityObjectToClipPos(v.vertex); return o; }
fixed4 frag(v2f i) : SV_Target { return fixed4(_Color.rgb, 1); }
ENDCG
} } }");
                sourceShader.hideFlags = HideFlags.HideAndDontSave;
                var shader = sourceShader; Require(shader != null && shader.isSupported, "Missing/unsupported independent source geometry shader.");
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin) { mesh = new Mesh(); skin.BakeMesh(mesh, false); }
                    else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = Object.Instantiate(filter.sharedMesh);
                    else throw new InvalidOperationException("Unsupported original renderer; do not silently omit it: " + renderer.name);
                    mesh.RecalculateBounds();
                    Require(renderer.sharedMaterials.Length >= mesh.subMeshCount, "Missing original material slot: " + renderer.name);
                    var materials = renderer.sharedMaterials.Take(mesh.subMeshCount).Select(source =>
                    {
                        Require(source != null && source.mainTexture == null, "Source view only supports the verified solid-colour sources; do not drop textures.");
                        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                        m.SetColor("_Color", source.color); return m;
                    }).ToArray();
                    parts.Add(new Part { mesh = mesh, matrix = renderer.localToWorldMatrix, materials = materials });
                }
                var positions = parts.SelectMany(p => p.mesh.vertices.Select(v => p.matrix.MultiplyPoint3x4(v))).ToArray();
                Require(positions.Length > 0, "Original source has no geometry.");
                var bounds = new Bounds(positions[0], Vector3.zero); foreach (var p in positions) bounds.Encapsulate(p);
                float radius = Mathf.Max(.1f, bounds.extents.magnitude) * 1.12f;
                var eye = bounds.center + new Vector3(3, 1.8f, -5).normalized * radius * 4;
                var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye, Quaternion.LookRotation(bounds.center - eye, Vector3.up), Vector3.one).inverse;
                var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-radius, radius, -radius, radius, .01f, radius * 10 + 10), true);
                target = new RenderTexture(640, 640, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { hideFlags = HideFlags.HideAndDontSave };
                Require(target.Create(), "Cannot create original geometry render target.");
                using (var commands = new CommandBuffer())
                {
                    commands.SetRenderTarget(target); commands.ClearRenderTarget(true, true, new Color(.04f, .055f, .08f, 0));
                    commands.SetViewProjectionMatrices(view, projection); commands.SetViewport(new Rect(0, 0, 640, 640));
                    foreach (var part in parts) for (int submesh = 0; submesh < part.mesh.subMeshCount; submesh++) commands.DrawMesh(part.mesh, part.matrix, part.materials[submesh], submesh, 0);
                    Graphics.ExecuteCommandBuffer(commands);
                }
                RenderTexture.active = target; texture = new Texture2D(640, 640, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, 640, 640), 0, 0, false); texture.Apply(false, false);
                var pixels = texture.GetPixels32();
                VatAppearanceRegressionImageChecks.NormalizeReadbackOrientationInPlace(pixels, 640, 640, SystemInfo.graphicsUVStartsAtTop);
                Save(path, 640, pixels); // Keep failed fixture images too, never infer art quality from an error shader.
                Require(!ShaderUtil.ShaderHasError(shader), "Original neutral shader has compilation errors.");
                Check(pixels);
            }
            finally
            {
                foreach (var p in parts) p.Dispose();
                RenderTexture.active = old;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (texture != null) Object.DestroyImmediate(texture);
                if (sourceShader != null) Object.DestroyImmediate(sourceShader);
                Object.DestroyImmediate(stage);
                ShaderUtil.allowAsyncCompilation = oldAsync;
                using (var restore = new CommandBuffer()) { restore.SetViewProjectionMatrices(oldView, oldProjection); Graphics.ExecuteCommandBuffer(restore); }
            }
        }
        private static AnimationClip Clip(string path, string name) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name, StringComparison.Ordinal)) ?? throw new InvalidOperationException("Missing original clip " + name);
        private static int Triangles(Mesh mesh) { int count = 0; for (int s = 0; s < mesh.subMeshCount; s++) count += (int)mesh.GetIndexCount(s) / 3; return count; }
        private static string Sha(string path) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing " + path);
        private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
        private static void Check(Color32[] pixels) { Require(VatAppearanceRegressionImageChecks.IsVisibleAndNotPink(VatAppearanceRegressionImageChecks.Measure(pixels), out string error), error); }
        private static void Save(string path, int size, Color32[] pixels)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try { t.SetPixels32(pixels); t.Apply(false, false); WriteNew(path, t.EncodeToPNG()); } finally { Object.DestroyImmediate(t); }
        }
        private static void WriteNew(string path, byte[] bytes) { using (var file = new FileStream(path, FileMode.CreateNew)) file.Write(bytes, 0, bytes.Length); }
    }
}
