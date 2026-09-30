using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MassEngine.Editor
{
    /// <summary>
    /// Isolated, synchronous GPU forward-pass fixture, NOT a CPU VAT decoder or a MeshRenderer preview.
    /// Draws the actual runtime mesh/material/MPB with procedural instancing and indirect arguments.
    /// LOD is deliberately forced at a common camera scale: this tests the three render paths, not LOD classification.
    /// </summary>
    public sealed class VatAppearanceRegressionCapture : IDisposable
    {
        public const int DefaultSize = 256;
        private readonly ComputeBuffer agents;
        private readonly ComputeBuffer visible;
        private readonly ComputeBuffer arguments;
        private readonly RenderTexture target;
        private readonly Texture2D readback;
        private readonly CommandBuffer commands;
        private readonly Dictionary<string, Vector4> savedVectors = new Dictionary<string, Vector4>();
        private readonly Dictionary<string, bool> savedKeywords = new Dictionary<string, bool>();
        private readonly Matrix4x4 savedView;
        private readonly Matrix4x4 savedProjection;
        private readonly Matrix4x4 view;
        private readonly Matrix4x4 projection;
        private readonly bool savedAsyncCompilation;
        private readonly RenderTexture savedTarget;
        private bool disposed;
        public int Size { get; }

        public VatAppearanceRegressionCapture(Bounds framingBounds, int size = DefaultSize)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsInstancing ||
                !SystemInfo.supportsComputeShaders)
                throw new InvalidOperationException("M5.3 requires a real graphics device with procedural instancing; do not use -nographics.");
            if (size < 32 || size > 2048)
                throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
            savedView = Shader.GetGlobalMatrix("unity_MatrixV");
            savedProjection = Shader.GetGlobalMatrix("unity_MatrixP");
            savedTarget = RenderTexture.active;
            savedAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                // A loading/compiling placeholder is not valid appearance evidence.
                ShaderUtil.allowAsyncCompilation = false;
                float radius = Mathf.Max(.1f, framingBounds.extents.magnitude) * 1.12f;
                Vector3 eye = framingBounds.center + new Vector3(3f, 1.8f, -5f).normalized * radius * 4f;
                Quaternion rotation = Quaternion.LookRotation(framingBounds.center - eye, Vector3.up);
                view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(eye, rotation, Vector3.one).inverse;
                projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-radius, radius, -radius, radius, .01f, radius * 10f + 10f), true);

                // Fixed light/SH and no scene fog/shadows. These are restored, never saved to assets.
                foreach (string keyword in new[] { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2", "_MAIN_LIGHT_SHADOWS",
                    "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN", "_SHADOWS_SOFT",
                    "_ADDITIONAL_LIGHTS_VERTEX", "_ADDITIONAL_LIGHTS", "_FORWARD_PLUS", "_CLUSTER_LIGHT_LOOP" })
                {
                    savedKeywords.Add(keyword, Shader.IsKeywordEnabled(keyword));
                    Shader.DisableKeyword(keyword);
                }
                Vector3 light = new Vector3(.3f, .8f, -.6f).normalized;
                SetGlobal("_MainLightPosition", new Vector4(light.x, light.y, light.z, 0f));
                SetGlobal("_MainLightColor", new Vector4(.8f, .8f, .8f, 1f));
                SetGlobal("unity_LightData", new Vector4(0f, 0f, 1f, 0f));
                SetGlobal("unity_ProbesOcclusion", Vector4.one);
                SetGlobal("_AdditionalLightsCount", Vector4.zero);
                SetGlobal("unity_FogParams", Vector4.zero);
                SetGlobal("unity_SHAr", new Vector4(0f, 0f, 0f, .55f));
                SetGlobal("unity_SHAg", new Vector4(0f, 0f, 0f, .55f));
                SetGlobal("unity_SHAb", new Vector4(0f, 0f, 0f, .55f));
                SetGlobal("unity_SHBr", Vector4.zero);
                SetGlobal("unity_SHBg", Vector4.zero);
                SetGlobal("unity_SHBb", Vector4.zero);
                SetGlobal("unity_SHC", Vector4.zero);
                SetGlobal("unity_WorldTransformParams", new Vector4(0f, 0f, 0f, 1f));
                SetGlobal("_WorldSpaceCameraPos", new Vector4(eye.x, eye.y, eye.z, 1f));

                agents = new ComputeBuffer(1, Marshal.SizeOf<AgentData>(), ComputeBufferType.Structured);
                visible = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
                arguments = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                visible.SetData(new uint[] { 0 });
                target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "M53 GPU VAT capture", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1
                };
                if (!target.Create())
                    throw new InvalidOperationException("Could not create M5.3 GPU render target.");
                readback = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "M53 GPU readback", hideFlags = HideFlags.HideAndDontSave
                };
                commands = new CommandBuffer { name = "M53 actual VAT procedural indirect forward pass" };
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Color32[] Capture(ResolvedUnitTypeRuntime runtime, int lod, AgentState state, float animationTime, AgentState? presentationState = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VatAppearanceRegressionCapture));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (lod < 0 || lod > 2) throw new ArgumentOutOfRangeException(nameof(lod));
            Mesh mesh = runtime.GetMesh(lod);
            Material material = runtime.GetMaterial(lod);
            MaterialPropertyBlock block = runtime.GetBlock(lod);
            if (mesh == null || material == null || block == null)
                throw new InvalidOperationException("Resolved runtime is missing mesh/material/VAT MPB at LOD " + lod);
            if (material.shader == null || !material.shader.isSupported || !material.enableInstancing)
                throw new InvalidOperationException("Unsupported shader or disabled instancing: " + material.name);
            int pass = material.FindPass("ForwardLit");
            if (pass < 0) pass = material.FindPass("ForwardNoShadow");
            if (pass < 0) throw new InvalidOperationException("No VAT forward pass on " + material.name);

            agents.SetData(new[] { new AgentData
            {
                position = Vector3.zero, rotation = Vector3.zero, scale = Vector3.one, velocity = Vector3.zero,
                currentState = (int)state, currentAnimationTime = animationTime,
                // This fixture bypasses simulation, so it must seed the independent
                // presentation selector itself. Preserve the historical Engage->Move default.
                presentationState = (int)(presentationState ?? (state == AgentState.Engage ? AgentState.Move : state))
            } });
            arguments.SetData(new[] { mesh.GetIndexCount(0), 1u, mesh.GetIndexStart(0), (uint)mesh.GetBaseVertex(0), 0u });
            block.SetBuffer("agentBuffer", agents);
            block.SetBuffer("visibleAgentIndices", visible);
            block.SetVector("_MassCorpseSink", Vector4.zero);
            commands.Clear();
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, new Color(.04f, .055f, .08f, 0f));
            commands.SetViewProjectionMatrices(view, projection);
            commands.SetViewport(new Rect(0, 0, Size, Size));
            commands.DrawMeshInstancedIndirect(mesh, 0, material, pass, arguments, 0, block);
            Graphics.ExecuteCommandBuffer(commands);
            if (ShaderUtil.ShaderHasError(material.shader))
                throw new InvalidOperationException("VAT forward shader reports compilation errors: " + material.shader.name);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                // ReadPixels synchronizes the GPU draw before the buffer may be changed for the next sample.
                readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                readback.Apply(false, false);
                Color32[] pixels = readback.GetPixels32();
                // GL.GetGPUProjectionMatrix(..., true) introduces an RT Y flip on top-origin APIs
                // (D3D here). A raw command-buffer draw has no camera/blit stage to undo it.
                // Normalize exactly once at readback; never rotate the model or alter baked poses.
                VatAppearanceRegressionImageChecks.NormalizeReadbackOrientationInPlace(pixels, Size, Size, SystemInfo.graphicsUVStartsAtTop);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private void SetGlobal(string name, Vector4 value)
        {
            savedVectors.Add(name, Shader.GetGlobalVector(name));
            Shader.SetGlobalVector(name, value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            agents?.Release();
            visible?.Release();
            arguments?.Release();
            commands?.Release();
            RenderTexture.active = savedTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (readback != null) Object.DestroyImmediate(readback);
            foreach (var pair in savedVectors) Shader.SetGlobalVector(pair.Key, pair.Value);
            foreach (var pair in savedKeywords)
                if (pair.Value) Shader.EnableKeyword(pair.Key); else Shader.DisableKeyword(pair.Key);
            using (var restore = new CommandBuffer())
            {
                restore.SetViewProjectionMatrices(savedView, savedProjection);
                Graphics.ExecuteCommandBuffer(restore);
            }
            ShaderUtil.allowAsyncCompilation = savedAsyncCompilation;
        }
    }

    /// <summary>Assertions consume actual RGBA GPU readbacks. Alpha 0 is the clear, alpha 1 is the shipped opaque VAT pass.</summary>
    public static class VatAppearanceRegressionImageChecks
    {
        [Serializable]
        public sealed class Metrics
        {
            public int totalPixels;
            public int foregroundPixels;
            public int magentaPixels;
        }

        public static void NormalizeReadbackOrientationInPlace(Color32[] pixels, int width, int height, bool graphicsUvStartsAtTop)
        {
            if (pixels == null || width <= 0 || height <= 0 || (long)width * height != pixels.Length)
                throw new ArgumentException("Readback dimensions must match the pixel array.");
            if (!graphicsUvStartsAtTop) return;
            for (int y = 0; y < height / 2; y++)
                for (int x = 0; x < width; x++)
                {
                    int bottom = y * width + x;
                    int top = (height - 1 - y) * width + x;
                    Color32 pixel = pixels[bottom];
                    pixels[bottom] = pixels[top];
                    pixels[top] = pixel;
                }
        }

        public static Metrics Measure(Color32[] pixels)
        {
            if (pixels == null || pixels.Length == 0) throw new ArgumentException("Image has no pixels.", nameof(pixels));
            var result = new Metrics { totalPixels = pixels.Length };
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a < 128) continue;
                result.foregroundPixels++;
                if (pixel.r >= 200 && pixel.b >= 200 && pixel.g <= 75) result.magentaPixels++;
            }
            return result;
        }

        public static bool IsVisibleAndNotPink(Metrics metrics, out string error)
        {
            if (metrics == null || metrics.foregroundPixels < Math.Max(8, metrics.totalPixels / 2000))
            {
                error = "Empty/near-empty GPU render (too few opaque foreground pixels).";
                return false;
            }
            // These fixtures draw one material. Unity's error shader makes almost the whole
            // foreground magenta; scattered atlas colors (notably the shipped Female far LOD)
            // are NOT compilation failures. Capture also checks ShaderUtil.ShaderHasError.
            if ((long)metrics.magentaPixels * 10 >= (long)metrics.foregroundPixels * 9)
            {
                error = "Magenta/error-shader pixels cover at least 90% of the foreground.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public static int CountChanged(Color32[] first, Color32[] second, int tolerance = 8)
        {
            CheckPair(first, second);
            int count = 0;
            for (int i = 0; i < first.Length; i++)
            {
                Color32 a = first[i], b = second[i];
                if (a.a < 128 && b.a < 128) continue;
                if ((a.a >= 128) != (b.a >= 128) || Math.Abs(a.r - b.r) > tolerance ||
                    Math.Abs(a.g - b.g) > tolerance || Math.Abs(a.b - b.b) > tolerance) count++;
            }
            return count;
        }

        public static bool HasMotion(IList<Color32[]> samples, out int maximumChangedPixels)
        {
            if (samples == null || samples.Count < 2) throw new ArgumentException("At least two frames are required.");
            maximumChangedPixels = 0;
            for (int i = 0; i < samples.Count; i++)
                for (int j = i + 1; j < samples.Count; j++)
                    maximumChangedPixels = Math.Max(maximumChangedPixels, CountChanged(samples[i], samples[j]));
            // Low threshold deliberately catches frozen animation, not subjective image quality.
            return maximumChangedPixels >= 4;
        }

        public static float SilhouetteIoU(Color32[] first, Color32[] second)
        {
            CheckPair(first, second);
            int intersection = 0, union = 0;
            for (int i = 0; i < first.Length; i++)
            {
                bool a = first[i].a >= 128, b = second[i].a >= 128;
                if (a || b) union++;
                if (a && b) intersection++;
            }
            return union == 0 ? 0f : intersection / (float)union;
        }

        private static void CheckPair(Color32[] first, Color32[] second)
        {
            if (first == null || second == null || first.Length == 0 || first.Length != second.Length)
                throw new ArgumentException("Images must have the same nonzero pixel count.");
        }
    }
}
