#if UNITY_EDITOR
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>Opt-in projectile impact splash (dragon fireball). Shipped kernel, runtime unit-collision variant.</summary>
    public sealed class ProjectileSplashGpuTests
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

        // 0 shooter(team0), 1 enemy in the shot path, 2 enemy 2m beside the impact, 3 enemy far away,
        // 4 ally beside the impact, 5 dead enemy beside the impact.
        private static readonly Vector2[] Positions = { new Vector2(0, 1), new Vector2(4, 1), new Vector2(4, 3), new Vector2(4, 6.5f), new Vector2(3, 2.2f), new Vector2(3.5f, 0) };
        private static readonly int[] Teams = { 0, 1, 1, 1, 0, 1 };
        private static readonly int[] Health = { 100, 100, 100, 100, 100, 0 };

        private int[] Fire(float splash, bool groundShot = false, bool overflow = false)
        {
            int n = Positions.Length, width = overflow ? 1 : 8, height = overflow ? 1 : 6, capacity = overflow ? 1 : 4, cells = width * height;
            var agents = new AgentData[n];
            for (int i = 0; i < n; i++) agents[i] = new AgentData { position = new Vector3(Positions[i].x, 0, Positions[i].y), scale = Vector3.one };
            var unit = UnitTypeGpuSettings.CreateDefaults(0); unit.agentRadius = .5f;
            var counts = new uint[cells]; var indices = new uint[cells * capacity];
            for (uint i = 0; i < n; i++)
            {
                if (Health[i] <= 0) continue;
                int cell = overflow ? 0 : Mathf.Clamp(Mathf.FloorToInt((Positions[i].y + 2) / 2), 0, height - 1) * width + Mathf.Clamp(Mathf.FloorToInt((Positions[i].x + 2) / 2), 0, width - 1);
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
                Assert.AreEqual(0f, shot.splashRadius, "Default must stay single-target.");
                shot.position = groundShot ? new Vector3(4, .5f, 8.5f) : new Vector3(0, 1, 1);
                shot.velocity = groundShot ? new Vector3(0, -1, 0) : new Vector3(10, 0, 0);
                shot.sourceAgentIndexPlusOne = 1; shot.sourceTeamId = 0; shot.targetAgentIndex = 1;
                shot.damage = 7; shot.hitRadius = .08f; shot.maxLifetime = 10; shot.splashRadius = splash;
                pool.SetData(new[] { shot }); body.SetData(agents); pos.SetData(Positions);
                hp.SetData(Health); team.SetData(Teams); damage.SetData(new int[n]); type.SetData(new int[n]);
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
                projectile.Dispatch(k, 1, 1, 1);
                var result = new ProjectileGpuData[1]; var hits = new int[n];
                pool.GetData(result); damage.GetData(hits);
                Assert.AreEqual(-1, result[0].targetAgentIndex, "Shot must be consumed at impact.");
                return hits;
            }
        }

        [Test] public void ZeroSplashKeepsLegacySingleTargetDamage()
        {
            CollectionAssert.AreEqual(new[] { 0, 7, 0, 0, 0, 0 }, Fire(0));
        }
        [Test] public void SplashDamagesNearbyEnemiesOnceWithoutFriendlyFireOrCorpses()
        {
            // Direct hit is not doubled; enemy 2m away takes splash; far enemy, ally, dead unit and shooter untouched.
            CollectionAssert.AreEqual(new[] { 0, 7, 7, 0, 0, 0 }, Fire(2.5f));
        }
        [Test] public void GroundImpactStillSplashes()
        {
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 7, 0, 0 }, Fire(2.5f, groundShot: true));
        }
        [Test] public void OverflowedBucketsUseExactFallbackWithSameResult()
        {
            CollectionAssert.AreEqual(new[] { 0, 7, 7, 0, 0, 0 }, Fire(2.5f, overflow: true));
        }
        [Test] public void LayoutStaysSixtyFourBytes()
        {
            Assert.AreEqual(64, System.Runtime.InteropServices.Marshal.SizeOf<ProjectileGpuData>());
            Assert.AreEqual(144, System.Runtime.InteropServices.Marshal.SizeOf<UnitTypeGpuSettings>());
        }
    }
}
#endif
