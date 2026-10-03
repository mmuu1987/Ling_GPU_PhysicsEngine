#if UNITY_EDITOR
using System;
using System.Collections;
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>Dispatches the shipped projectile kernel, not a CPU collision mirror.</summary>
    public sealed class ProjectileTerrainGpuTests
    {
        private const string ProjectilePath = "Assets/MassEngine/Projectiles/Shaders/ProjectileSimulation.compute";
        private const string CombatPath = "Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute";
        private const int Damage = 7;
        private ComputeShader shader;

        [SetUp]
        public void SetUp()
        {
            Assert.True(SystemInfo.supportsComputeShaders, "M6.2 GPU coverage requires a graphics device.");
            var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectilePath);
            Assert.NotNull(asset);
            shader = Object.Instantiate(asset); // Keyword changes must not leak into flat regression tests.
            shader.EnableKeyword("MASS_TERRAIN_ENABLED");
            shader.DisableKeyword("MASS_PROJECTILE_UNITS");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(shader);

        [TestCase(false)]
        [TestCase(true)]
        public void NarrowRidgeBlocksLargeSweepWithBothEndpointsAboveGround(bool reverse)
        {
            // One vertex column, beyond 128 crossed cells: endpoints and sparse ray samples
            // cannot establish visibility. Both travel directions must stop on the near face.
            var surface = Heightfield(257, 2, new Vector2(256, 2), (x, z) => x == 137 ? 6 : 0);
            Vector3 start = new Vector3(reverse ? 255.75f : .25f, 1, .7f);
            Vector3 end = new Vector3(reverse ? .25f : 255.75f, 1, .7f);
            Vector2 target = new Vector2(reverse ? 5 : 250, .7f);
            var result = Sweep(surface, start, end, target);
            Assert.AreEqual(0, result.Damage, "The target behind the ridge must not receive damage.");
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.AreEqual(0u, result.ActiveCount, "Ground contact must leave the draw list in this dispatch.");
            Assert.That(result.Projectile.position.x,
                Is.EqualTo(reverse ? 137 + 5f / 6 : 136 + 1f / 6).Within(.001f));
        }

        [Test]
        public void DiagonalCornerSweepCannotSkipSingleVertexPeak()
        {
            var surface = Heightfield(65, 65, new Vector2(64, 64), (x, z) => x == 32 && z == 32 ? 6 : 0);
            var result = Sweep(surface, new Vector3(.25f, 1, .25f), new Vector3(63.75f, 1, 63.75f),
                new Vector2(62, 62));
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.That(result.Projectile.position.x, Is.EqualTo(31 + 1f / 6).Within(.001f));
            Assert.That(result.Projectile.position.z, Is.EqualTo(result.Projectile.position.x).Within(.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SweepUsesMeshFixedDiagonalInsteadOfBilinearHeight(bool transpose)
        {
            var surface = Heightfield(2, 2, Vector2.one, (x, z) => x == 1 && z == 1 ? 8 : 0);
            Vector3 start = transpose ? new Vector3(.75f, 2, .1f) : new Vector3(.1f, 2, .75f);
            Vector3 end = transpose ? new Vector3(.75f, 2, .9f) : new Vector3(.9f, 2, .75f);
            var result = Sweep(surface, start, end, new Vector2(.95f, .95f), .05f);
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            // a-b-d / a-d-c gives h=8*min(u,v): h=2 at .25, not the bilinear .3333.
            float coordinate = transpose ? result.Projectile.position.z : result.Projectile.position.x;
            Assert.That(coordinate, Is.EqualTo(.25f).Within(.0001f));
        }

        [TestCase(.25f, .75f, 2f)]
        [TestCase(1f, 1f, 8f)]
        public void VerticalSweepHitsSurfaceIncludingPositiveWorldBoundary(float x, float z, float height)
        {
            var surface = Heightfield(2, 2, Vector2.one, (ix, iz) => ix == 1 && iz == 1 ? 8 : 0);
            var result = Sweep(surface, new Vector3(x, 12, z), new Vector3(x, -2, z), new Vector2(.1f, .1f), .05f);
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.That(result.Projectile.position.y, Is.EqualTo(height).Within(.0001f));
        }

        [Test]
        public void TerrainBehindTargetDoesNotConsumeEarlierTargetHit()
        {
            var surface = Heightfield(11, 2, new Vector2(10, 2), (x, z) => x == 8 ? 4 : 0);
            var result = Sweep(surface, new Vector3(1, 1, 1), new Vector3(9, 1, 1), new Vector2(3, 1));
            Assert.AreEqual(Damage, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.AreEqual(0u, result.ActiveCount);
        }

        [Test]
        public void SphereEntryBeforeTerrainWinsEvenWhenClosestPointIsBehindTerrain()
        {
            // Target centre (5,3,1), radius 2.2: entry x=4.0835; terrain x=4.5;
            // closest approach x=5. Comparing the terrain against closestT loses this hit.
            var surface = Heightfield(11, 2, new Vector2(10, 2), (x, z) => x >= 5 ? 2 : 0);
            var result = Sweep(surface, new Vector3(1, 1, 1), new Vector3(9, 1, 1), new Vector2(5, 1), 2.2f);
            Assert.AreEqual(Damage, result.Damage, "Sphere entry must win before the rising terrain.");
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
        }

        [Test]
        public void ClearSweepStaysActiveAndTerrainTargetOutsideIsRejected()
        {
            var surface = Heightfield(2, 2, new Vector2(20, 2), (x, z) => 4);
            var result = Sweep(surface, new Vector3(1, 5, 1), new Vector3(2, 5, 1), new Vector2(18, 1));
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(0, result.Projectile.targetAgentIndex);
            Assert.AreEqual(1u, result.ActiveCount);
            result = Sweep(surface, new Vector3(1, 5, 1), new Vector3(2, 5, 1), new Vector2(21, 1));
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.AreEqual(0u, result.ActiveCount);
        }

        [Test]
        public void SweepEnteringTerrainClipsToBoundsBeforeFindingGroundContact()
        {
            var surface = Heightfield(2, 2, new Vector2(10, 2), (x, z) => 1);
            var result = Sweep(surface, new Vector3(-5, 4, 1), new Vector3(5, 0, 1), new Vector2(9, 1));
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.That(result.Projectile.position.x, Is.EqualTo(2.5f).Within(.0001f));
            Assert.That(result.Projectile.position.y, Is.EqualTo(1).Within(.0001f));
        }

        [Test]
        public void ProjectileAlreadyBelowSurfaceIsConsumedAtStartOfSweep()
        {
            var surface = Heightfield(2, 2, new Vector2(20, 2), (x, z) => 4);
            var start = new Vector3(1, 3, 1);
            var result = Sweep(surface, start, new Vector3(2, 3, 1), new Vector2(18, 1));
            Assert.AreEqual(0, result.Damage);
            Assert.AreEqual(-1, result.Projectile.targetAgentIndex);
            Assert.That(result.Projectile.position, Is.EqualTo(start));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AsyncRequestsAimAndHitBothDirectionsWithoutHeightDamageBonus(bool terrainEnabled)
        {
            // Actual AsyncGPUReadback -> ProjectileGpuManager -> actual SimulateProjectiles.
            // One simultaneous uphill and downhill shot also catches asymmetric aim offsets.
            var surface = Heightfield(2, 2, new Vector2(20, 2), (x, z) => x * 10);
            using (var rig = new RequestRig(shader, terrainEnabled ? surface : null, new Vector2(2, 1), new Vector2(18, 1)))
            {
                rig.ConsumeRequests();
                Assert.AreEqual(2, rig.Manager.TotalLaunched);
                var shots = rig.ReadProjectiles();
                Assert.That(shots[0].position.y, Is.EqualTo(terrainEnabled ? 2 : 1).Within(.0001f));
                Assert.That(shots[1].position.y, Is.EqualTo(terrainEnabled ? 10 : 1).Within(.0001f));
                Assert.That(shots[0].velocity.y, Is.EqualTo(terrainEnabled ? 40 * 8 / Mathf.Sqrt(15.55f * 15.55f + 64) : 0).Within(.0001f));
                Assert.That(shots[1].velocity.y, Is.EqualTo(-shots[0].velocity.y).Within(.0001f));
                rig.Simulate(.5f);
                Assert.That(rig.ReadDamage(), Is.EqualTo(new[] { Damage, Damage }), "Altitude does not modify damage.");
                foreach (var shot in rig.ReadProjectiles())
                    Assert.AreEqual(-1, shot.targetAgentIndex);
                rig.Manager.Dispose();
                Assert.IsNull(rig.Manager.Surface, "Dispose must release the borrowed surface reference.");
            }
        }

        [Test]
        public void AsyncTerrainRequestsRejectOutsideSourceAndTargetWithoutFlatFallback()
        {
            var surface = Heightfield(2, 2, new Vector2(20, 2), (x, z) => 4);
            // Request 0 has an outside source, request 1 an outside target.
            using (var rig = new RequestRig(shader, surface, new Vector2(-1, 1), new Vector2(18, 1)))
            {
                rig.ConsumeRequests();
                Assert.AreEqual(0, rig.Manager.TotalLaunched);
                foreach (var shot in rig.ReadProjectiles())
                    Assert.AreEqual(-1, shot.targetAgentIndex);
            }
        }

        [UnityTest]
        public IEnumerator CompletedLaunchSnapshotSurvivesDelayedConsumption()
        {
            var surface = Heightfield(2, 2, new Vector2(20, 2), (x, z) => x * 10);
            using (var rig = new RequestRig(shader, surface, new Vector2(2, 1), new Vector2(18, 1)))
            {
                rig.PollRequests();
                AsyncGPUReadback.WaitAllRequests();
                // Native request storage has expired by now; the owner must retain the
                // completed snapshot even when its next Update is several frames later.
                yield return null; yield return null; yield return null;
                rig.PollRequests();
                Assert.AreEqual(2, rig.Manager.TotalLaunched);
                rig.Simulate(.5f);
                Assert.That(rig.ReadDamage(), Is.EqualTo(new[] { Damage, Damage }));
            }
        }

        [UnityTest]
        public IEnumerator ClearingPoolInvalidatesPendingLaunchCallbacks()
        {
            using (var rig = new RequestRig(shader, null, new Vector2(2, 1), new Vector2(18, 1)))
            {
                rig.PollRequests();
                rig.Manager.ClearAllProjectiles();
                AsyncGPUReadback.WaitAllRequests();
                yield return null;
                rig.ConsumeRequests();
                Assert.AreEqual(0, rig.Manager.TotalLaunched, "A callback from before Clear must not resurrect old shots.");
            }
        }

        [Test]
        public void LargeLaunchSnapshotDrainsWithoutLosingTailOrNewRequests()
        {
            using (var rig = new RequestRig(shader, null, new Vector2(2, 1), new Vector2(18, 1), 5010))
            {
                rig.SetRequests(5000, 1);
                rig.ConsumeRequests();
                Assert.AreEqual(4096, rig.Manager.TotalLaunched, "Keep the per-call launch budget.");
                rig.SetRequests(7, 0); // New GPU work while the previous snapshot is draining.
                rig.PollRequests();
                Assert.AreEqual(5001, rig.Manager.TotalLaunched, "Drain both a partial agent and the later agent.");
                var shots = rig.ReadProjectiles();
                Assert.AreEqual(2, shots[5000].sourceAgentIndexPlusOne, "The later agent must not starve.");
                rig.ConsumeRequests();
                Assert.AreEqual(5008, rig.Manager.TotalLaunched, "New counters must survive draining the old snapshot.");
                rig.ConsumeRequests();
                Assert.AreEqual(5008, rig.Manager.TotalLaunched, "Do not replay a consumed snapshot.");
            }
        }

        [Test]
        public void ClearingPoolDiscardsPartiallyConsumedLaunchSnapshot()
        {
            using (var rig = new RequestRig(shader, null, new Vector2(2, 1), new Vector2(18, 1), 5010))
            {
                rig.SetRequests(5000, 1); rig.ConsumeRequests();
                Assert.AreEqual(4096, rig.Manager.TotalLaunched);
                rig.Manager.ClearAllProjectiles();
                int afterClear = rig.Manager.TotalLaunched;
                rig.ConsumeRequests();
                Assert.AreEqual(afterClear, rig.Manager.TotalLaunched, "Old snapshot tail must not survive a reset.");
                rig.SetRequests(1, 1); rig.ConsumeRequests();
                Assert.AreEqual(afterClear + 2, rig.Manager.TotalLaunched, "Reset must allow fresh launches from index zero.");
            }
        }

        private static TerrainSurface Heightfield(int width, int depth, Vector2 size, Func<int, int, float> height)
        {
            var heights = new float[width * depth];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                    heights[z * width + x] = height(x, z);
            return new TerrainSurface("projectile-test", 1, width, depth, Vector2.zero, size, 80,
                heights, new bool[(width - 1) * (depth - 1)]);
        }

        private struct SweepResult
        {
            public ProjectileGpuData Projectile;
            public int Damage;
            public uint ActiveCount;
        }

        private SweepResult Sweep(TerrainSurface surface, Vector3 start, Vector3 end, Vector2 target, float radius = .25f)
        {
            using (var gpu = new TerrainSurfaceGpu(surface))
            using (var pool = new ComputeBuffer(1, ProjectileGpuData.Stride))
            using (var positions = new ComputeBuffer(1, 8))
            using (var hp = new ComputeBuffer(1, 4))
            using (var teams = new ComputeBuffer(1, 4))
            using (var damage = new ComputeBuffer(1, 4))
            using (var active = new ComputeBuffer(1, 4, ComputeBufferType.Append))
            using (var args = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments))
            {
                var shot = ProjectileGpuData.CreateEmpty();
                shot.position = start; shot.velocity = end - start;
                shot.targetAgentIndex = 0; shot.sourceTeamId = 0; shot.hitRadius = radius;
                shot.damage = Damage; shot.maxLifetime = 10;
                pool.SetData(new[] { shot }); positions.SetData(new[] { target });
                hp.SetData(new[] { 100 }); teams.SetData(new[] { 1 }); damage.SetData(new[] { 0 });
                int simulate = shader.FindKernel("SimulateProjectiles");
                gpu.Bind(shader, simulate);
                BindSimulation(shader, simulate, pool, positions, hp, teams, damage, 1, 1);
                shader.Dispatch(simulate, 1, 1, 1);
                int collect = shader.FindKernel("CollectActiveProjectiles");
                active.SetCounterValue(0); args.SetData(new uint[5]);
                shader.SetBuffer(collect, "projectileBuffer", pool);
                shader.SetBuffer(collect, "activeProjectileIndices", active);
                shader.Dispatch(collect, 1, 1, 1);
                ComputeBuffer.CopyCount(active, args, sizeof(uint));
                var result = new ProjectileGpuData[1]; var received = new int[1]; var draw = new uint[5];
                pool.GetData(result); damage.GetData(received); args.GetData(draw);
                return new SweepResult { Projectile = result[0], Damage = received[0], ActiveCount = draw[1] };
            }
        }

        private static void BindSimulation(ComputeShader compute, int kernel, ComputeBuffer pool, ComputeBuffer positions,
            ComputeBuffer hp, ComputeBuffer teams, ComputeBuffer damage, int count, float dt)
        {
            compute.SetBuffer(kernel, "projectileBuffer", pool);
            compute.SetBuffer(kernel, "agentPositionReadBuffer", positions);
            compute.SetBuffer(kernel, "hpReadBuffer", hp);
            compute.SetBuffer(kernel, "teamIdReadBuffer", teams);
            compute.SetBuffer(kernel, "pendingDamageBuffer", damage);
            compute.SetInt("maxProjectiles", count); compute.SetFloat("currentTime", dt); compute.SetFloat("deltaTime", dt);
        }

        private sealed class RequestRig : IDisposable
        {
            public readonly ProjectileGpuManager Manager = new ProjectileGpuManager();
            private readonly ComputeShader projectile, combat;
            private readonly TerrainSurfaceGpu gpu;
            private readonly ComputeBuffer pool;
            private readonly ComputeBuffer requests = new ComputeBuffer(2, 4);
            private readonly ComputeBuffer positions = new ComputeBuffer(2, 8);
            private readonly ComputeBuffer targets = new ComputeBuffer(2, 4);
            private readonly ComputeBuffer hp = new ComputeBuffer(2, 4);
            private readonly ComputeBuffer teams = new ComputeBuffer(2, 4);
            private readonly ComputeBuffer damage = new ComputeBuffer(2, 4);
            private readonly UnitTypeGpuSettings[] settings;
            private readonly ComputeBuffer bodies = new ComputeBuffer(2, AgentData.StrideBytes);
            private readonly ComputeBuffer types = new ComputeBuffer(2, 4);
            private readonly ComputeBuffer unitSettings = new ComputeBuffer(2, UnitTypeGpuSettings.StrideBytes);
            private readonly ComputeBuffer grid = new ComputeBuffer(1, 4);
            private readonly ComputeBuffer indices = new ComputeBuffer(2, 4);

            public RequestRig(ComputeShader shader, TerrainSurface surface, Vector2 source, Vector2 target, int poolCapacity = 2)
            {
                pool = new ComputeBuffer(poolCapacity, ProjectileGpuData.Stride);
                Assert.True(SystemInfo.supportsAsyncGPUReadback);
                projectile = shader;
                combat = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(CombatPath));
                combat.DisableKeyword("MASS_TERRAIN_ENABLED"); // Only ClearLaunchRequests is dispatched here.
                if (surface != null) { projectile.EnableKeyword("MASS_TERRAIN_ENABLED"); gpu = new TerrainSurfaceGpu(surface); }
                else projectile.DisableKeyword("MASS_TERRAIN_ENABLED");
                settings = new[] { MakeSettings(0), MakeSettings(1) };
                projectile.EnableKeyword("MASS_PROJECTILE_UNITS");
                var agentData = new AgentData[2];
                var points = new[] { source, target };
                for (int i = 0; i < 2; i++)
                {
                    float y = surface != null && surface.TrySample(points[i], out var sample) ? sample.Position.y : 0;
                    agentData[i] = new AgentData { position = new Vector3(points[i].x, y, points[i].y), scale = Vector3.one };
                }
                bodies.SetData(agentData); types.SetData(new[] { 0, 1 }); unitSettings.SetData(settings);
                grid.SetData(new uint[] { 2 }); indices.SetData(new uint[] { 0, 1 });
                positions.SetData(new[] { source, target }); targets.SetData(new[] { 1, 0 });
                requests.SetData(new[] { 1, 1 }); hp.SetData(new[] { 100, 100 });
                teams.SetData(new[] { 0, 1 }); damage.SetData(new[] { 0, 0 });
                Manager.Initialize(projectile, combat, pool, poolCapacity, requests, 2);
                Manager.Surface = surface; // Mirrors the owner-injects-after-Initialize contract.
                Manager.ClearAllProjectiles();
            }

            public void SetRequests(int first, int second) => requests.SetData(new[] { first, second });

            public void ConsumeRequests()
            {
                Manager.ProcessLaunchRequests(requests, positions, targets, new[] { 0, 1 }, settings, 2, 0);
                // Synchronization is test-only; production continues to use its asynchronous path.
                AsyncGPUReadback.WaitAllRequests();
                Manager.ProcessLaunchRequests(requests, positions, targets, new[] { 0, 1 }, settings, 2, 0);
                AsyncGPUReadback.WaitAllRequests();
                var counts = new int[2]; requests.GetData(counts);
                Assert.That(counts, Is.EqualTo(new[] { 0, 0 }), "Captured launch requests must still be cleared immediately.");
            }

            public void PollRequests() => Manager.ProcessLaunchRequests(requests, positions, targets, new[] { 0, 1 }, settings, 2, 0);

            public void Simulate(float dt)
            {
                int kernel = projectile.FindKernel("SimulateProjectiles");
                gpu?.Bind(projectile, kernel);
                BindSimulation(projectile, kernel, pool, positions, hp, teams, damage, 2, dt);
                projectile.SetBuffer(kernel, "agentBuffer", bodies);
                projectile.SetBuffer(kernel, "unitTypeIndexReadBuffer", types);
                projectile.SetBuffer(kernel, "unitTypeSettings", unitSettings);
                projectile.SetBuffer(kernel, "gridCountsReadBuffer", grid);
                projectile.SetBuffer(kernel, "gridAgentIndicesReadBuffer", indices);
                projectile.SetInts("gridResolution", 1, 1);
                projectile.SetVector("gridOrigin", Vector4.zero);
                projectile.SetFloat("cellSize", 32); projectile.SetInt("maxAgentsPerCell", 2);
                projectile.SetFloat("projectileQueryRadius", .45f);
                projectile.Dispatch(kernel, 1, 1, 1);
            }

            public ProjectileGpuData[] ReadProjectiles()
            {
                var result = new ProjectileGpuData[pool.count]; pool.GetData(result); return result;
            }

            public int[] ReadDamage()
            {
                var result = new int[2]; damage.GetData(result); return result;
            }

            public void Dispose()
            {
                AsyncGPUReadback.WaitAllRequests();
                Manager.Dispose(); gpu?.Dispose();
                pool.Release(); requests.Release(); positions.Release(); targets.Release();
                hp.Release(); teams.Release(); damage.Release(); Object.DestroyImmediate(combat);
                bodies.Release(); types.Release(); unitSettings.Release(); grid.Release(); indices.Release();
            }

            private static UnitTypeGpuSettings MakeSettings(int team)
            {
                var s = UnitTypeGpuSettings.CreateDefaults(team);
                s.attackDamage = Damage; s.projectileRange = 50; s.projectileSpeed = 40;
                s.projectileGravity = 0; s.projectileHitRadius = .25f; s.projectileMaxLifetime = 3;
                // Equal authored launch/aim heights isolate uphill/downhill symmetry.
                s.projectileOriginHeight = s.projectileTargetHeight = 1;
                return s;
            }
        }
    }
}
#endif
