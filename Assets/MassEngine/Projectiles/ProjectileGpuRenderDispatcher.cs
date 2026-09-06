using System.Collections.Generic;
using UnityEngine;
using static MassEngine.MassGpuShaderPropertyIds;

namespace MassEngine.Projectiles
{
    /// <summary>
    /// Draws every live projectile in a single Graphics.DrawMeshInstancedIndirect call.
    /// The instance count comes from projectileDrawArgsBuffer, which the
    /// CollectActiveProjectiles kernel fills via ComputeBuffer.CopyCount - never from a
    /// CPU-side estimate, so a slot released on the GPU this frame stops rendering the
    /// same frame. No GameObject, no Transform and no readback is involved.
    /// </summary>
    public sealed class ProjectileGpuRenderDispatcher
    {
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        // One warning per distinct reason: a projectile system that silently draws
        // nothing is otherwise indistinguishable from one that never fires.
        private readonly HashSet<string> reportedSkips = new HashSet<string>();

        private Mesh fallbackMesh;
        private Mesh configuredMesh;
        private ComputeBuffer configuredArgs;
        private ProjectileRenderConfig blockConfig;
        // Reused so the per-frame path allocates nothing; SetVectorArray copies the contents.
        private readonly Vector4[] teamColorUpload = new Vector4[ProjectileRenderConfig.MaxTeamColors];

        public void Draw(ProjectileRenderConfig config, MassGpuBufferManager buffers, Bounds bounds)
        {
            if (config == null || !config.renderProjectiles)
                return;

            if (buffers == null || !buffers.IsAllocated || buffers.MaxProjectiles <= 0)
                return;

            ComputeBuffer projectiles = buffers.projectileBuffer;
            ComputeBuffer activeIndices = buffers.activeProjectileIndexBuffer;
            ComputeBuffer args = buffers.projectileDrawArgsBuffer;
            if (projectiles == null || activeIndices == null || args == null)
            {
                WarnOnce("buffers", "MassEngine: projectile trails skipped - projectile GPU buffers are not allocated. Simulation is unaffected.");
                return;
            }

            if (config.material == null)
            {
                WarnOnce("material", "MassEngine: projectile trails skipped - ProjectileRenderConfig has no material. Assign ProjectileTrail.mat to see tracers; simulation is unaffected.");
                return;
            }

            Mesh mesh = ResolveMesh(config);
            if (mesh == null)
            {
                WarnOnce("mesh", "MassEngine: projectile trails skipped - no tracer mesh could be resolved or built. Simulation is unaffected.");
                return;
            }

            // The mesh half of the draw args is written once per mesh (and once per buffer
            // generation, so a reallocated args buffer is re-armed). SetArgs also zeroes the
            // instance count, which costs this one frame of tracers - deliberately cheaper
            // than ever replaying a stale count.
            if (configuredMesh != mesh || !ReferenceEquals(configuredArgs, args))
            {
                buffers.ConfigureProjectileDrawArgs(mesh);
                configuredMesh = mesh;
                configuredArgs = args;
                return;
            }

            if (blockConfig != config)
                FillBlock(config);

            block.SetBuffer(ProjectileBufferId, projectiles);
            block.SetBuffer(ActiveProjectileIndicesId, activeIndices);

            Graphics.DrawMeshInstancedIndirect(
                mesh,
                0,
                config.material,
                bounds,
                args,
                0,
                block,
                config.shadowCasting,
                config.receiveShadows);
        }

        /// <summary>
        /// The mesh the tracers will actually be drawn with: the config override when set,
        /// otherwise the built-in unit quad. A null mesh in the config is a supported
        /// default, not an error.
        /// </summary>
        public Mesh ResolveMesh(ProjectileRenderConfig config)
        {
            if (config != null && config.mesh != null)
                return config.mesh;

            return GetFallbackMesh();
        }

        /// <summary>
        /// Drops the procedural mesh and forgets the configured draw args, so a later
        /// reinitialize re-arms them instead of drawing against a released buffer.
        /// </summary>
        public void Release()
        {
            if (fallbackMesh != null)
            {
                Object.Destroy(fallbackMesh);
                fallbackMesh = null;
            }

            configuredMesh = null;
            configuredArgs = null;
            blockConfig = null;
            block.Clear();
            reportedSkips.Clear();
        }

        private void FillBlock(ProjectileRenderConfig config)
        {
            // Prefilled off the render path, exactly like ResolvedUnitTypeRuntime does for
            // agents: Draw then only rebinds the two buffers.
            block.Clear();
            // SetVectorArray hands the values through untouched, unlike SetColor, so the
            // gamma-to-linear step SetColor would have done has to happen here or every
            // tracer renders too bright in a linear project.
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int teamId = 0; teamId < teamColorUpload.Length; teamId++)
            {
                Color color = config.ResolveTeamColor(teamId);
                teamColorUpload[teamId] = linear ? (Vector4)color.linear : (Vector4)color;
            }

            block.SetVectorArray(ProjectileTeamColorsId, teamColorUpload);
            block.SetFloat(ProjectileTrailWidthId, Mathf.Max(0.001f, config.trailWidth));
            block.SetFloat(ProjectileTrailLengthScaleId, Mathf.Max(0f, config.trailLengthScale));
            block.SetFloat(ProjectileTrailMinLengthId, Mathf.Max(0f, config.trailMinLength));
            blockConfig = config;
        }

        /// <summary>
        /// Unit quad on the local XY plane, stretched by the shader into a tracer: local +x
        /// is the head of the trail and uv.x fades it out towards the tail.
        /// </summary>
        private Mesh GetFallbackMesh()
        {
            if (fallbackMesh != null)
                return fallbackMesh;

            fallbackMesh = new Mesh { name = "MassEngineProjectileTracerQuad" };
            fallbackMesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
            });
            fallbackMesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            });
            fallbackMesh.SetNormals(new List<Vector3>
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
            });
            fallbackMesh.SetTriangles(new List<int> { 0, 1, 2, 0, 2, 3 }, 0);
            fallbackMesh.RecalculateBounds();
            return fallbackMesh;
        }

        private void WarnOnce(string key, string message)
        {
            if (reportedSkips.Add(key))
                Debug.LogWarning(message);
        }
    }
}
