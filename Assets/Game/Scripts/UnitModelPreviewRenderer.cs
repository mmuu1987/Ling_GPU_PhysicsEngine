using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MassEngine.Game
{
    /// <summary>One isolated VAT subject, no scene objects, simulation, per-frame readbacks or asset writes.
    /// Uses the same resolved mesh/material/clip contract as battle rendering. Owns all GPU resources.</summary>
    public sealed class UnitModelPreviewRenderer : IDisposable
    {
        private ComputeBuffer agents, visible, arguments;
        private CommandBuffer commands;
        private Material material;
        private Material groundShadowMaterial;
        private Mesh groundShadowMesh;
        private Material radiusRingMaterial;
        private Mesh radiusRingMesh;
        public UnitPreviewRadius PhysicalRadius { get; private set; }
        public bool RadiusRingEnabled { get; set; } = true;
        public bool HasRadiusRing => radiusRingMesh != null && radiusRingMaterial != null;
        public Vector3 SubjectScale { get; private set; } = Vector3.one;
        public Vector3 RadiusCenter => Vector3.zero; // AgentData.position, NOT the visual bounds centre.
        public float ContactRadius => PhysicalRadius.ContactRadius(SubjectScale);
        public Bounds FrameBounds { get; private set; }
        public Matrix4x4 ViewMatrix { get; private set; }
        public Vector3 CameraPosition { get; private set; }
        public const float RingHalfWidth = .012f;
        public const int RingSegments = 128;
        public bool ShadowEnabled { get; set; } = true;
        public Vector3 ShadowCenter { get; private set; }
        public float ShadowRadius { get; private set; }
        private ResolvedUnitTypeRuntime runtime;
        private readonly AgentData[] agent = new AgentData[1];
        private Bounds bounds;
        private int pass;
        public RenderTexture Texture { get; private set; }
        public UnitTypeConfig Config { get; private set; }
        public const float DefaultYaw = 152f; // VAT characters face +Z; view their front, not the back.
        public float Yaw { get; private set; } = DefaultYaw;
        public const float FieldOfView = 30f;
        public const float DefaultPitch = 28f;
        public float Pitch { get; private set; } = DefaultPitch;
        public float CameraDistance { get; private set; }
        public float NearClip { get; private set; }
        public Matrix4x4 ProjectionMatrix { get; private set; }
        public float Zoom { get; private set; } = 1;
        public float ViewHalfHeight { get; private set; }
        public float IdleDuration => runtime != null ? runtime.idleClipDuration : 1;
        private static readonly string[] Keywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2", "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN", "_SHADOWS_SOFT", "_ADDITIONAL_LIGHTS_VERTEX", "_ADDITIONAL_LIGHTS", "_FORWARD_PLUS", "_CLUSTER_LIGHT_LOOP" };
        private static readonly string[] Vectors = { "_MainLightPosition", "_MainLightColor", "unity_LightData", "unity_ProbesOcclusion", "_AdditionalLightsCount", "unity_FogParams", "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC", "unity_WorldTransformParams", "_WorldSpaceCameraPos" };
        private readonly Vector4[] saved = new Vector4[Vectors.Length];
        private readonly bool[] keywords = new bool[Keywords.Length];
        private readonly Vector4[] values = new Vector4[Vectors.Length];

        public UnitModelPreviewRenderer(UnitTypeConfig config)
        {
            try
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsInstancing || !SystemInfo.supportsComputeShaders)
                    throw new InvalidOperationException("3D preview requires a graphics device with instancing.");
                if (config == null || config.renderConfig == null || !VatProfileReader.TryRead(config.renderConfig.vatProfile, out var profile, out _))
                    throw new InvalidOperationException("Missing valid VAT preview data.");
                Config = config; runtime = ResolvedUnitTypeRuntime.Resolve(config, 1);
                var source = runtime.nearMaterial;
                if (runtime.nearMesh == null || source == null || source.shader == null || !source.shader.isSupported || !source.enableInstancing)
                    throw new InvalidOperationException("Unsupported preview mesh or material.");
                material = new Material(source) { name = "Unit preview material", hideFlags = HideFlags.HideAndDontSave };
                pass = material.FindPass("ForwardLit"); if (pass < 0) pass = material.FindPass("ForwardNoShadow");
                if (pass < 0) throw new InvalidOperationException("Missing VAT forward pass.");
                foreach (var keyword in Keywords) material.DisableKeyword(keyword);
                bounds = ResolveIdleBounds(config.renderConfig.vatProfile, profile, runtime.nearMesh.bounds);
                agents = new ComputeBuffer(1, Marshal.SizeOf<AgentData>(), ComputeBufferType.Structured);
                visible = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
                arguments = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
                visible.SetData(new uint[] { 0 });
                var mesh = runtime.nearMesh;
                arguments.SetData(new[] { mesh.GetIndexCount(0), 1u, mesh.GetIndexStart(0), (uint)mesh.GetBaseVertex(0), 0u });
                runtime.nearBlock.SetBuffer("agentBuffer", agents); runtime.nearBlock.SetBuffer("visibleAgentIndices", visible);
                runtime.nearBlock.SetVector("_MassCorpseSink", Vector4.zero);
                commands = new CommandBuffer { name = "Isolated unit UI preview" };
                CreateGroundShadow();
                PhysicalRadius = UnitPreviewRadius.Read(config);
                CreateRadiusRing();
            }
            catch { Dispose(); throw; }
        }
        private static readonly System.Collections.Generic.Dictionary<ScriptableObject, Bounds> IdleBounds = new System.Collections.Generic.Dictionary<ScriptableObject, Bounds>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBoundsCache() => IdleBounds.Clear();
        private static Bounds ResolveIdleBounds(ScriptableObject key, VatProfileData profile, Bounds fallback)
        {
            if (IdleBounds.TryGetValue(key, out var cached)) return cached;
            // One small, linear GPU readback per unique profile (not per frame). Historical
            // mesh bounds include death/travel poses, or only the bind pose. Use actual idle
            // VAT positions, including every vertex/frame, for a truthful full-body fit.
            int start = Mathf.Clamp(profile.idle.startFrame, 0, profile.totalFrameCount - 1);
            int count = Mathf.Clamp(profile.idle.frameCount, 1, profile.totalFrameCount - start);
            int width = profile.textureWidth, rows = profile.rowsPerFrame, height = rows * count;
            if (width <= 0 || height <= 0 || (long)width * height > 4194304 || height > SystemInfo.maxTextureSize ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)) return fallback;
            var old = RenderTexture.active;
            RenderTexture temporary = null; Texture2D readback = null;
            try
            {
                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit(profile.positionTexture, temporary, new Vector2(1, (float)height / profile.textureHeight), new Vector2(0, (float)(start * rows) / profile.textureHeight));
                RenderTexture.active = temporary;
                readback = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); readback.Apply(false, false);
                var pixels = readback.GetPixels(); var result = new Bounds(); bool first = true;
                for (int frame = 0; frame < count; frame++) for (int vertex = 0; vertex < profile.cleanMesh.vertexCount; vertex++)
                {
                    var p = pixels[frame * rows * width + vertex];
                    if (float.IsNaN(p.r) || float.IsNaN(p.g) || float.IsNaN(p.b) || float.IsInfinity(p.r) || float.IsInfinity(p.g) || float.IsInfinity(p.b)) return fallback;
                    var position = new Vector3(p.r, p.g, p.b);
                    if (first) { result = new Bounds(position, Vector3.zero); first = false; } else result.Encapsulate(position);
                }
                if (first || result.size.sqrMagnitude < .000001f) return fallback;
                IdleBounds[key] = result; return result;
            }
            catch (Exception e) { Debug.LogWarning("Using authored preview bounds: " + e.Message); return fallback; }
            finally { RenderTexture.active = old; if (temporary != null) RenderTexture.ReleaseTemporary(temporary); DestroyOwned(readback); }
        }
        private void CreateGroundShadow()
        {
            var shader = Resources.Load<Shader>("UnitPreviewGroundShadow");
            if (shader == null || !shader.isSupported)
            { Debug.LogWarning("Preview ground shadow unavailable; keeping the live character preview."); return; }
            groundShadowMaterial = new Material(shader) { name = "Unit preview soft ground shadow", hideFlags = HideFlags.HideAndDontSave };
            // Stable idle floor, not the animated pose. Height cap keeps swords/wings
            // from inflating the fake footprint; it is deliberately not a real silhouette.
            float height = Mathf.Max(.1f, bounds.size.y);
            ShadowRadius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * .75f, height * .18f, height * .45f);
            ShadowCenter = new Vector3(bounds.center.x, bounds.min.y - height * .008f, bounds.center.z);
            groundShadowMesh = new Mesh { name = "Unit preview ground quad", hideFlags = HideFlags.HideAndDontSave };
            groundShadowMesh.vertices = new[] { new Vector3(-1,0,-1), new Vector3(-1,0,1), new Vector3(1,0,1), new Vector3(1,0,-1) };
            groundShadowMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            groundShadowMesh.triangles = new[] { 0,1,2,0,2,3 }; groundShadowMesh.RecalculateBounds();
        }
        private void CreateRadiusRing()
        {
            if (!PhysicalRadius.Available) return;
            var shader = Resources.Load<Shader>("UnitPreviewRadiusRing");
            if (shader == null || !shader.isSupported)
            { Debug.LogWarning("Preview radius ring unavailable; no substitute shadow/ellipse is shown."); return; }
            radiusRingMaterial = new Material(shader) { name = "Unit preview physical radius ink", hideFlags = HideFlags.HideAndDontSave };
            radiusRingMesh = CreateRadiusRingMesh();
        }
        public static Mesh CreateRadiusRingMesh()
        {
            var vertices = new Vector3[RingSegments * 2]; var triangles = new int[RingSegments * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2 / RingSegments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = direction * (1 - RingHalfWidth);
                vertices[i * 2 + 1] = direction * (1 + RingHalfWidth);
                int j = (i + 1) % RingSegments, k = i * 6;
                triangles[k] = i * 2; triangles[k + 1] = j * 2; triangles[k + 2] = i * 2 + 1;
                triangles[k + 3] = i * 2 + 1; triangles[k + 4] = j * 2; triangles[k + 5] = j * 2 + 1;
            }
            var mesh = new Mesh { name = "Unit preview contact-radius ring", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateBounds(); return mesh;
        }
        /// <summary>Preview-only diagnostic scale. Never writes spawn/config/battle data.
        /// UI uses one (the shipped DefaultSpawnModule contract).</summary>
        public void SetSubjectScale(Vector3 value)
        {
            for (int i = 0; i < 3; i++)
                if (float.IsNaN(value[i]) || float.IsInfinity(value[i]) || value[i] <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Preview scale must be finite and positive.");
            SubjectScale = value;
        }
        private Bounds ResolveFrameBounds()
        {
            var result = new Bounds(Vector3.Scale(bounds.center, SubjectScale), Vector3.Scale(bounds.size, SubjectScale));
            if (RadiusRingEnabled && HasRadiusRing)
                result.Encapsulate(new Bounds(RadiusCenter, new Vector3(2, 0, 2) * ContactRadius * (1 + RingHalfWidth)));
            return result;
        }
        /// <summary>Only the independent circle/fit changes. No asset or battle-buffer write.</summary>
        public void SetRadiusOverride(float? value)
        {
            if (Config == null) throw new ObjectDisposedException(nameof(UnitModelPreviewRenderer));
            var original = UnitPreviewRadius.Read(Config);
            PhysicalRadius = value.HasValue ? original.WithValue(value.Value, "预览值") : original;
        }
        public void ResetView() { Yaw = DefaultYaw; Pitch = DefaultPitch; Zoom = 1; }
        public void Rotate(Vector2 delta) { Yaw = Mathf.Repeat(Yaw + delta.x * .4f, 360); Pitch = Mathf.Clamp(Pitch - delta.y * .25f, -20, 45); }
        public void Scroll(float amount) { Zoom = Mathf.Clamp(Zoom * Mathf.Exp(-amount * .12f), .6f, 1.8f); }
        public void Render(float time, int width, int height)
        {
            if (commands == null) throw new ObjectDisposedException(nameof(UnitModelPreviewRenderer));
            float resolutionScale = Mathf.Min(1, 768f / Mathf.Max(1, Mathf.Max(width, height)));
            width = Mathf.Max(16, Mathf.RoundToInt(width * resolutionScale)); height = Mathf.Max(16, Mathf.RoundToInt(height * resolutionScale));
            if (Texture == null || Texture.width != width || Texture.height != height)
            {
                ReleaseTexture();
                Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { name = "Unit preview target", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                if (!Texture.Create()) throw new InvalidOperationException("Could not create preview target.");
            }
            agent[0] = new AgentData { scale = SubjectScale, currentState = (int)AgentState.Idle, presentationState = (int)AgentState.Idle, currentAnimationTime = Mathf.Max(0, time) };
            agents.SetData(agent);
            FrameBounds = ResolveFrameBounds();
            float radius = Mathf.Max(.1f, FrameBounds.extents.magnitude), aspect = (float)width / height;
            // Fix FOV and fitting distance for this subject/aspect, independent of orbit.
            // Bound all yaw and allowed pitch, and reserve the full depth radius: even
            // the near side of the model fits at default zoom, not just its centre plane.
            var extent = FrameBounds.extents;
            float horizontal = new Vector2(extent.x, extent.z).magnitude;
            float pitchForMaximum = Mathf.Min(45 * Mathf.Deg2Rad, Mathf.Atan2(horizontal, extent.y));
            float vertical = extent.y * Mathf.Cos(pitchForMaximum) + horizontal * Mathf.Sin(pitchForMaximum);
            float tangent = Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad);
            float defaultDistance = Mathf.Max(.05f, Mathf.Max(vertical, horizontal / aspect)) * 1.12f / tangent + radius;
            NearClip = Mathf.Max(.01f, radius * .01f);
            CameraDistance = Mathf.Max(defaultDistance * Zoom, radius + NearClip * 4);
            ViewHalfHeight = CameraDistance * tangent; // target-plane diagnostic, not an ortho size
            Vector3 eye = FrameBounds.center + Quaternion.Euler(Pitch, Yaw, 0) * new Vector3(0, 0, -CameraDistance);
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye, Quaternion.LookRotation(FrameBounds.center - eye), Vector3.one).inverse;
            ViewMatrix = view; CameraPosition = eye;
            ProjectionMatrix = Matrix4x4.Perspective(FieldOfView, aspect, NearClip, defaultDistance * 1.8f + radius * 2 + 1);
            // SetViewProjectionMatrices accepts Unity's CPU projection. The graphics
            // backend performs API depth conversion. Pre-converting it with GL here
            // applies reversed-Z a second time and makes far surfaces win over near ones.
            var previousTarget = RenderTexture.active;
            bool previousInvertCulling = GL.invertCulling;
            var previousView = Shader.GetGlobalMatrix("unity_MatrixV"); var previousProjection = Shader.GetGlobalMatrix("unity_MatrixP");
            commands.Clear();
            for (int i = 0; i < Keywords.Length; i++) { keywords[i] = Shader.IsKeywordEnabled(Keywords[i]); commands.DisableShaderKeyword(Keywords[i]); }
            for (int i = 0; i < Vectors.Length; i++) saved[i] = Shader.GetGlobalVector(Vectors[i]);
            var light = new Vector3(.3f, .8f, -.6f).normalized;
            values[0] = new Vector4(light.x, light.y, light.z, 0); values[1] = new Vector4(.8f,.8f,.8f,1);
            values[2] = new Vector4(0,0,1,0); values[3] = Vector4.one;
            values[6] = values[7] = values[8] = new Vector4(0,0,0,.55f);
            values[13] = new Vector4(0,0,0,1); values[14] = new Vector4(eye.x,eye.y,eye.z,1);
            for (int i = 0; i < Vectors.Length; i++) commands.SetGlobalVector(Vectors[i], values[i]);
            commands.SetRenderTarget(Texture); commands.ClearRenderTarget(true, true, Color.clear);
            commands.SetViewProjectionMatrices(view, ProjectionMatrix); commands.SetViewport(new Rect(0, 0, width, height));
            commands.SetInvertCulling(false);
            if (ShadowEnabled && groundShadowMaterial != null && groundShadowMesh != null)
                commands.DrawMesh(groundShadowMesh, Matrix4x4.TRS(Vector3.Scale(ShadowCenter, SubjectScale), Quaternion.identity, Vector3.one * ShadowRadius * Mathf.Max(SubjectScale.x, SubjectScale.z)), groundShadowMaterial, 0, 0);
            commands.DrawMeshInstancedIndirect(runtime.nearMesh, 0, material, pass, arguments, 0, runtime.nearBlock);
            if (RadiusRingEnabled && HasRadiusRing)
                commands.DrawMesh(radiusRingMesh, Matrix4x4.TRS(RadiusCenter, Quaternion.identity, Vector3.one * ContactRadius), radiusRingMaterial, 0, 0);
            commands.SetInvertCulling(previousInvertCulling);
            for (int i = 0; i < Vectors.Length; i++) commands.SetGlobalVector(Vectors[i], saved[i]);
            for (int i = 0; i < Keywords.Length; i++) if (keywords[i]) commands.EnableShaderKeyword(Keywords[i]); else commands.DisableShaderKeyword(Keywords[i]);
            commands.SetViewProjectionMatrices(previousView, previousProjection);
            commands.SetRenderTarget(previousTarget != null ? new RenderTargetIdentifier(previousTarget) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget));
            commands.SetViewport(new Rect(0, 0, previousTarget != null ? previousTarget.width : Screen.width, previousTarget != null ? previousTarget.height : Screen.height));
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = previousTarget;
        }
        private static void DestroyOwned(Object value) { if (value == null) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
        private void ReleaseTexture() { if (Texture == null) return; Texture.Release(); DestroyOwned(Texture); Texture = null; }
        public void Dispose()
        {
            agents?.Release(); visible?.Release(); arguments?.Release(); commands?.Release();
            DestroyOwned(groundShadowMaterial); DestroyOwned(groundShadowMesh); groundShadowMaterial = null; groundShadowMesh = null;
            DestroyOwned(radiusRingMaterial); DestroyOwned(radiusRingMesh); radiusRingMaterial = null; radiusRingMesh = null;
            agents = visible = arguments = null; commands = null; ReleaseTexture(); DestroyOwned(material); material = null; runtime = null; Config = null;
        }
    }
}


