#if UNITY_EDITOR
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>Opt-in splash impact effect ring (ProjectileImpactFx): shipped kernel with the impact variant enabled.</summary>
    public sealed class ProjectileImpactFxGpuTests
    {
        private ComputeShader projectile;
        [SetUp] public void SetUp()
        {
            Assert.True(SystemInfo.supportsComputeShaders, "A graphics device is required; do not use -nographics.");
            projectile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Projectiles/Shaders/ProjectileSimulation.compute"));
            projectile.EnableKeyword("MASS_PROJECTILE_UNITS");
            projectile.DisableKeyword("MASS_TERRAIN_ENABLED");
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(projectile); }

        // 0 shooter(team0), 1 enemy in the shot path, 2 enemy 2m beside the impact, 3 enemy far away.
        private static readonly Vector2[] Positions = { new Vector2(0, 1), new Vector2(4, 1), new Vector2(4, 3), new Vector2(4, 6.5f) };
        private static readonly int[] Teams = { 0, 1, 1, 1 };

        private int[] Fire(float splash, ProjectileImpactFx fx, out ProjectileImpactFx.ImpactData[] ring, out uint count)
        {
            int n = Positions.Length, width = 8, height = 6, capacity = 4, cells = width * height;
            var agents = new AgentData[n];
            for (int i = 0; i < n; i++) agents[i] = new AgentData { position = new Vector3(Positions[i].x, 0, Positions[i].y), scale = Vector3.one };
            var unit = UnitTypeGpuSettings.CreateDefaults(0); unit.agentRadius = .5f;
            var counts = new uint[cells]; var indices = new uint[cells * capacity];
            for (uint i = 0; i < n; i++)
            {
                int cell = Mathf.Clamp(Mathf.FloorToInt((Positions[i].y + 2) / 2), 0, height - 1) * width + Mathf.Clamp(Mathf.FloorToInt((Positions[i].x + 2) / 2), 0, width - 1);
                uint slot = counts[cell]++;
                if (slot < capacity) indices[cell * capacity + slot] = i;
            }
            using (var pool = new ComputeBuffer(1, ProjectileGpuData.Stride))
            using (var body = new ComputeBuffer(n, AgentData.StrideBytes))
            using (var pos = new ComputeBuffer(n, 8))
            using (var hp = new ComputeBuffer(n, 4))
            using (var team = new ComputeBuffer(n, 4))
            using (var damage = new ComputeBuffer(n, 4))
            using (var type = new ComputeBuffer(n, 4))
            using (var settings = new ComputeBuffer(1, UnitTypeGpuSettings.StrideBytes))
            using (var grid = new ComputeBuffer(cells, 4))
            using (var ids = new ComputeBuffer(cells * capacity, 4))
            {
                var shot = ProjectileGpuData.CreateEmpty();
                shot.position = new Vector3(0, 1, 1); shot.velocity = new Vector3(10, 0, 0);
                shot.sourceAgentIndexPlusOne = 1; shot.sourceTeamId = 0; shot.targetAgentIndex = 1;
                shot.damage = 7; shot.hitRadius = .08f; shot.maxLifetime = 10; shot.splashRadius = splash;
                pool.SetData(new[] { shot }); body.SetData(agents); pos.SetData(Positions);
                hp.SetData(new[] { 100, 100, 100, 100 }); team.SetData(Teams); damage.SetData(new int[n]); type.SetData(new int[n]);
                settings.SetData(new[] { unit }); grid.SetData(counts); ids.SetData(indices);
                int k = projectile.FindKernel("SimulateProjectiles");
                projectile.SetBuffer(k, "projectileBuffer", pool); projectile.SetBuffer(k, "agentBuffer", body);
                projectile.SetBuffer(k, "agentPositionReadBuffer", pos); projectile.SetBuffer(k, "hpReadBuffer", hp);
                projectile.SetBuffer(k, "teamIdReadBuffer", team); projectile.SetBuffer(k, "pendingDamageBuffer", damage);
                projectile.SetBuffer(k, "unitTypeIndexReadBuffer", type); projectile.SetBuffer(k, "unitTypeSettings", settings);
                projectile.SetBuffer(k, "gridCountsReadBuffer", grid); projectile.SetBuffer(k, "gridAgentIndicesReadBuffer", ids);
                projectile.SetInt("maxProjectiles", 1); projectile.SetInt("maxAgentsPerCell", capacity);
                projectile.SetInts("gridResolution", width, height);
                projectile.SetVector("gridOrigin", new Vector4(-2, -2, 0, 0)); projectile.SetFloat("cellSize", 2);
                projectile.SetFloat("projectileQueryRadius", .5f);
                projectile.SetFloat("deltaTime", 1); projectile.SetFloat("currentTime", 1);
                fx.Bind(projectile, k);
                projectile.Dispatch(k, 1, 1, 1);
                var hits = new int[n]; damage.GetData(hits);
                ring = new ProjectileImpactFx.ImpactData[fx.Capacity]; fx.Ring.GetData(ring);
                var c = new uint[1]; fx.Counter.GetData(c); count = c[0];
                return hits;
            }
        }

        [Test] public void SplashImpactIsRecordedAtTheImpactPoint()
        {
            using (var fx = new ProjectileImpactFx(8))
            {
                Fire(2.5f, fx, out var ring, out uint count);
                Assert.AreEqual(1u, count, "One splash impact -> one ring entry.");
                Assert.AreEqual(2.5f, ring[0].radius, 1e-5f);
                Assert.AreEqual(1f, ring[0].time, 1e-5f, "Stamped with the simulation clock (currentTime).");
                Assert.AreEqual(0f, ring[0].groundY, 1e-5f, "Flat world ground.");
                Assert.That(ring[0].position.x, Is.InRange(3f, 4.6f), "Impact is at the struck enemy.");
                Assert.AreEqual(0f, ring[1].radius, "Untouched slots stay empty.");
            }
        }
        [Test] public void SingleTargetShotsRecordNothing()
        {
            using (var fx = new ProjectileImpactFx(8))
            {
                var hits = Fire(0f, fx, out var ring, out uint count);
                Assert.AreEqual(0u, count);
                Assert.AreEqual(0f, ring[0].radius);
                CollectionAssert.AreEqual(new[] { 0, 7, 0, 0 }, hits, "Legacy single-target damage in the impact variant.");
            }
        }
        [Test] public void ImpactVariantKeepsSplashDamage()
        {
            using (var fx = new ProjectileImpactFx(8))
                CollectionAssert.AreEqual(new[] { 0, 7, 7, 0 }, Fire(2.5f, fx, out _, out _));
        }
        [Test] public void RingWrapsInsteadOfOverflowing()
        {
            using (var fx = new ProjectileImpactFx(1))
            {
                Fire(2.5f, fx, out _, out _);
                Fire(3f, fx, out var ring, out uint count);
                Assert.AreEqual(2u, count);
                Assert.AreEqual(3f, ring[0].radius, 1e-5f, "Newest impact overwrites the oldest slot.");
            }
        }
        [Test] public void LayoutAndDefaultsKeepLegacyBehaviour()
        {
            Assert.AreEqual(32, ProjectileImpactFx.Stride);
            var config = ScriptableObject.CreateInstance<ProjectileRenderConfig>();
            try
            {
                Assert.IsNull(config.impactMaterial, "No impact effect unless a scene opts in.");
                using (var fx = new ProjectileImpactFx(4))
                    Assert.DoesNotThrow(() => new ProjectileGpuRenderDispatcher().DrawImpacts(config, fx, new Bounds(Vector3.zero, Vector3.one * 100), 0f));
            }
            finally { Object.DestroyImmediate(config); }
        }
        [Test] public void ImpactAndTrailShadersCompile()
        {
            foreach (string path in new[] { "Assets/MassEngine/Projectiles/Shaders/ProjectileImpact.shader", "Assets/MassEngine/Projectiles/Shaders/ProjectileTrail.shader" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Assert.IsNotNull(shader, path);
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader), path + " has compile errors.");
                Assert.IsTrue(shader.isSupported, path + " unsupported.");
            }
        }
    }
}
#endif
