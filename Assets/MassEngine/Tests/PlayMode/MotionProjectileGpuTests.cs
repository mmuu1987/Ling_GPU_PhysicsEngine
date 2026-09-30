#if UNITY_EDITOR
using System;
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Tests
{
    /// <summary>Small GPU regressions: production HLSL helpers and shipped projectile kernel, no CPU collision mirror.</summary>
    public sealed class MotionProjectileGpuTests
    {
        private ComputeShader motion, projectile;
        [SetUp] public void SetUp()
        {
            Assert.True(SystemInfo.supportsComputeShaders, "A graphics device is required; do not use -nographics.");
            motion = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Tests/PlayMode/MotionPresentationProbe.compute"));
            projectile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Projectiles/Shaders/ProjectileSimulation.compute"));
            projectile.EnableKeyword("MASS_PROJECTILE_UNITS");
            projectile.DisableKeyword("MASS_TERRAIN_ENABLED");
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(motion); Object.DestroyImmediate(projectile); }

        private AgentData MoveProbe(float speed, int visual = 0, float scale = 1f, float time = 0f, int state = 2, float dt = .02f)
        {
            using (var agents = new ComputeBuffer(1, AgentData.StrideBytes))
            using (var types = new ComputeBuffer(1, 4))
            using (var settings = new ComputeBuffer(1, UnitTypeGpuSettings.StrideBytes))
            {
                var agent = new AgentData { position = new Vector3(speed * dt, 0, 0), scale = Vector3.one * scale,
                    currentState = state, presentationState = visual, locomotionSpeed = speed, currentAnimationTime = time };
                var unit = UnitTypeGpuSettings.CreateDefaults(0);
                unit.idleClipDuration = 4f; unit.moveClipDuration = .533333f;
                if (state == 3) unit.projectileRange = 20;
                agents.SetData(new[] { agent }); types.SetData(new[] { 0 }); settings.SetData(new[] { unit });
                int k = motion.FindKernel("Probe");
                motion.SetBuffer(k, "agentBuffer", agents); motion.SetBuffer(k, "unitTypeIndexReadBuffer", types);
                motion.SetBuffer(k, "unitTypeSettings", settings);
                motion.SetFloat("deltaTime", dt); motion.SetInt("frameIndex", 1); motion.SetInt("battleStarted", 1);
                motion.SetVector("previousPosition", Vector3.zero); motion.SetFloat("corpseLingerSeconds", 0);
                motion.Dispatch(k, 1, 1, 1);
                var result = new AgentData[1]; agents.GetData(result); return result[0];
            }
        }
        [Test] public void StationaryEngageUsesIdleAndItsOwnLongClock()
        {
            var a = MoveProbe(0, time: .7f);
            Assert.AreEqual(2, a.currentState, "Presentation must not cancel pursuit.");
            Assert.AreEqual(0, a.presentationState);
            Assert.That(a.currentAnimationTime, Is.EqualTo(.72f).Within(.0001f), "Idle must not wrap at Move's .533 second duration.");
        }
        [Test] public void StoppingMoveResetsToIdle()
        {
            var a = MoveProbe(0, visual: 1, time: .4f);
            Assert.AreEqual(0, a.presentationState);
            Assert.That(a.currentAnimationTime, Is.EqualTo(.02f).Within(.0001f));
        }
        [Test] public void IdleMoveThresholdsHaveHysteresis()
        {
            Assert.AreEqual(0, MoveProbe(.08f, visual: 0).presentationState);
            Assert.AreEqual(1, MoveProbe(.08f, visual: 1).presentationState);
            Assert.AreEqual(1, MoveProbe(.2f, visual: 0).presentationState);
        }
        [Test] public void SlowMovementNoLongerRunsAtEightyFivePercent()
        {
            var a = MoveProbe(.6f, visual: 1);
            Assert.That(a.currentAnimationTime, Is.EqualTo(.004f).Within(.0001f));
        }
        [Test] public void LargerStrideUsesLowerRateAtSameWorldSpeed()
        {
            var human = MoveProbe(3, visual: 1);
            var large = MoveProbe(3, visual: 1, scale: 2);
            Assert.That(large.currentAnimationTime * 2, Is.EqualTo(human.currentAnimationTime).Within(.0001f));
        }
        [Test] public void DeathAgeAndRangedClockAreNotResetOrDoubleAdvanced()
        {
            Assert.That(MoveProbe(0, visual: 3, time: .7f, state: 4).currentAnimationTime, Is.EqualTo(.72f).Within(.0001f));
            Assert.That(MoveProbe(0, visual: 3, time: .3f, state: 3).currentAnimationTime, Is.EqualTo(.3f).Within(.0001f));
            Assert.That(MoveProbe(0, time: .7f, dt: 0).currentAnimationTime, Is.EqualTo(.7f).Within(.0001f));
        }

        private struct HitResult { public ProjectileGpuData Shot; public int[] Damage; }
        private HitResult Sweep(bool ally, bool deadTarget = false, bool overflow = false, bool above = false,
            bool large = false, bool ridge = false, bool noBlocker = false, bool ballistic = false, int closeLoft = 0)
        {
            const int n = 3;
            int width = closeLoft >= 3 ? 12 : 6;
            int cells = overflow ? 1 : width * 3, capacity = overflow ? 1 : 4;
            var agents = new AgentData[n];
            var positions = new[] { new Vector2(0, 1), new Vector2(4, large ? 2.5f : 1), new Vector2(ballistic ? 100 : 8, 1) };
            if (closeLoft != 0) { positions[1] = new Vector2(1.8f,1); positions[2] = new Vector2(7,1); }
            if (closeLoft >= 3) { positions[1] = new Vector2(1.414f,1); positions[2] = new Vector2(20,1); }
            for (int i = 0; i < n; i++) agents[i] = new AgentData {
                position = new Vector3(positions[i].x, 0, positions[i].y), scale = Vector3.one };
            if (above) agents[2].scale = Vector3.one * 2;
            if (large) agents[1].scale = Vector3.one * 4;
            var hp = new[] { 100, noBlocker || ballistic ? 0 : 100, deadTarget || ballistic ? 0 : 100 };
            var teams = new[] { 0, ally ? 0 : 1, 1 };
            var unit = UnitTypeGpuSettings.CreateDefaults(0); unit.agentRadius = .5f;
            var counts = new uint[cells]; var indices = new uint[cells * capacity];
            for (uint i = 0; i < n; i++)
            {
                if (hp[i] <= 0) continue;
                int cell = overflow ? 0 : Mathf.Clamp(Mathf.FloorToInt((positions[i].y + 2) / 2), 0, 2) * width
                    + Mathf.Clamp(Mathf.FloorToInt((positions[i].x + 2) / 2), 0, width-1);
                uint slot = counts[cell]++;
                if (slot < capacity) indices[cell * capacity + slot] = i;
            }
            using (var pool = new ComputeBuffer(1, ProjectileGpuData.Stride))
            using (var body = new ComputeBuffer(n, AgentData.StrideBytes))
            using (var pos = new ComputeBuffer(n, 8))
            using (var health = new ComputeBuffer(n, 4))
            using (var team = new ComputeBuffer(n, 4))
            using (var damage = new ComputeBuffer(n, 4))
            using (var type = new ComputeBuffer(n, 4))
            using (var settings = new ComputeBuffer(1, UnitTypeGpuSettings.StrideBytes))
            using (var grid = new ComputeBuffer(cells, 4))
            using (var ids = new ComputeBuffer(cells * capacity, 4))
            {
                var shot = ProjectileGpuData.CreateEmpty();
                shot.position = new Vector3(0, above ? 3 : 1, 1); shot.velocity = new Vector3(10, ballistic ? 4.9f : 0, 0);
                shot.gravity = ballistic ? -9.8f : 0;
                shot.sourceAgentIndexPlusOne = 1; shot.sourceTeamId = 0; shot.targetAgentIndex = 2;
                shot.damage = 7; shot.hitRadius = .08f; shot.maxLifetime = 10;
                if (closeLoft != 0)
                {
                    shot.position = new Vector3(.5f,1.3f,1); shot.gravity = -9.8f;
                    Vector3 aim = new Vector3(closeLoft >= 3 ? 20 : 7,1,1);
                    float t = ProjectileBallistics.FlightTime(shot.position,aim,12,-9.8f,3,closeLoft == 3 ? ProjectileBallistics.Clearance(1,1,1,1,20) : closeLoft == 1 ? 0 : 2.5f);
                    shot.velocity = (aim-shot.position)/t + Vector3.up * (4.9f*t);
                }
                pool.SetData(new[] { shot }); body.SetData(agents); pos.SetData(positions);
                health.SetData(hp); team.SetData(teams); damage.SetData(new int[n]); type.SetData(new int[n]);
                settings.SetData(new[] { unit }); grid.SetData(counts); ids.SetData(indices);
                int k = projectile.FindKernel("SimulateProjectiles");
                projectile.SetBuffer(k, "projectileBuffer", pool); projectile.SetBuffer(k, "agentBuffer", body);
                projectile.SetBuffer(k, "agentPositionReadBuffer", pos); projectile.SetBuffer(k, "hpReadBuffer", health);
                projectile.SetBuffer(k, "teamIdReadBuffer", team); projectile.SetBuffer(k, "pendingDamageBuffer", damage);
                projectile.SetBuffer(k, "unitTypeIndexReadBuffer", type); projectile.SetBuffer(k, "unitTypeSettings", settings);
                projectile.SetBuffer(k, "gridCountsReadBuffer", grid); projectile.SetBuffer(k, "gridAgentIndicesReadBuffer", ids);
                projectile.SetInt("maxProjectiles", 1); projectile.SetInt("maxAgentsPerCell", capacity);
                projectile.SetInts("gridResolution", overflow ? 1 : width, overflow ? 1 : 3);
                projectile.SetVector("gridOrigin", new Vector4(-2, -2, 0, 0)); projectile.SetFloat("cellSize", 2);
                projectile.SetFloat("projectileQueryRadius", large ? 2 : above ? 1 : .5f);
                projectile.SetFloat("deltaTime", closeLoft != 0 ? .02f : ballistic ? .1f : 1); projectile.SetFloat("currentTime", 1);
                TerrainSurfaceGpu terrain = null;
                try
                {
                    if (ridge)
                    {
                        var heights = new float[24]; heights[6] = heights[18] = 3;
                        terrain = new TerrainSurfaceGpu(new TerrainSurface("unit-ridge", 1, 12, 2, Vector2.zero,
                            new Vector2(11, 2), 80, heights, new bool[11]));
                        projectile.EnableKeyword("MASS_TERRAIN_ENABLED"); terrain.Bind(projectile, k);
                    }
                    for (int step = 0; step < (closeLoft != 0 ? 200 : ballistic ? 10 : 1); step++) projectile.Dispatch(k, 1, 1, 1);
                    var result = new ProjectileGpuData[1]; var hits = new int[n];
                    pool.GetData(result); damage.GetData(hits);
                    return new HitResult { Shot = result[0], Damage = hits };
                }
                finally { terrain?.Dispose(); }
            }
        }
        [Test] public void FriendlyBodyBlocksWithoutFriendlyFireAcrossSeveralCells()
        {
            var r = Sweep(true); Assert.AreEqual(-1, r.Shot.targetAgentIndex);
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, r.Damage);
            Assert.That(r.Shot.position.x, Is.EqualTo(3.42f).Within(.001f), "Must ignore source and stop at front entry, not centre.");
        }
        [Test] public void InterveningEnemyReceivesHitInsteadOfDesignatedTarget()
        {
            var r = Sweep(false); CollectionAssert.AreEqual(new[] { 0, 7, 0 }, r.Damage);
        }
        [Test] public void OriginalTargetDeathDoesNotEraseReleasedShot()
        {
            var r = Sweep(false, deadTarget: true); Assert.AreEqual(7, r.Damage[1]);
        }
        [Test] public void ShotAboveAllyCanHitTallerTarget()
        {
            var r = Sweep(true, above: true); CollectionAssert.AreEqual(new[] { 0, 0, 7 }, r.Damage);
        }
        [Test] public void LargeBodyOutsideSegmentCellsStillBlocks()
        {
            var r = Sweep(true, large: true); Assert.AreEqual(-1, r.Shot.targetAgentIndex);
            Assert.Less(r.Shot.position.x, 4);
        }
        [Test] public void OverflowedBucketUsesExactFallbackInsteadOfGhosting()
        {
            var r = Sweep(false, overflow: true); Assert.AreEqual(7, r.Damage[1]);
        }
        [Test] public void TerrainAndUnitsUseSameEarliestHitOrdering()
        {
            var bodyFirst = Sweep(false, ridge: true); Assert.AreEqual(7, bodyFirst.Damage[1]);
            var terrainFirst = Sweep(false, ridge: true, noBlocker: true);
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, terrainFirst.Damage);
            Assert.That(terrainFirst.Shot.position.x, Is.EqualTo(5 + 1f / 3).Within(.001f));
        }
        [Test] public void BallisticIntegrationMatchesLaunchEquationOverMultipleFrames()
        {
            var r = Sweep(false, ballistic: true);
            Assert.AreEqual(2, r.Shot.targetAgentIndex);
            Assert.That(r.Shot.position.x, Is.EqualTo(10).Within(.001f));
            Assert.That(r.Shot.position.y, Is.EqualTo(1).Within(.001f));
        }

        [Test] public void CloseLoftClearsRealFriendlyCapsuleWhereOriginalLowArcHitsIt()
        {
            var low = Sweep(true,closeLoft:1); var high = Sweep(true,closeLoft:2);
            Assert.AreEqual(0,low.Damage[2],"Control: original short arc is intercepted by the front rank.");
            Assert.AreEqual(-1,low.Shot.targetAgentIndex);
            Assert.AreEqual(7,high.Damage[2],"Loft must really reach the enemy through native swept collision.");
            Assert.AreEqual(0,high.Damage[1]);
        }

        [Test] public void TwentyMeterLoftClearsTightFrontRankWithoutPiercingIt()
        {
            var shortApex = Sweep(true,closeLoft:4); var rangeAware = Sweep(true,closeLoft:3);
            Assert.AreEqual(0,shortApex.Damage[2],"Control: a short-shot apex alone cannot clear a near neighbor at twenty meters.");
            Assert.AreEqual(-1,shortApex.Shot.targetAgentIndex);
            Assert.AreEqual(7,rangeAware.Damage[2]); Assert.AreEqual(0,rangeAware.Damage[1]);
        }
    }
}
#endif
