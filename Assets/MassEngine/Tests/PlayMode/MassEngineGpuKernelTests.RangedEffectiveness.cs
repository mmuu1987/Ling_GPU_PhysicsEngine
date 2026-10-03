#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using MassEngine.Projectiles;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;

namespace MassEngine.Tests
{
    public sealed partial class MassEngineGpuKernelTests
    {
        [UnityTest]
        public IEnumerator RangedShortHitsReuseSlotsBeforeLongestLifetime()
        {
            BuildRangedScenario(true, false, 4);
            for (int i = 0; i < 90; i++)
            {
                DispatchOneFrame(true); AsyncGPUReadback.WaitAllRequests();
                yield return null;
            }
            uint active = ReadProjectileInstanceCount();
            Debug.Log("RANGED_POOL_DIAG capacity=" + buffers.MaxProjectiles + " generated=" + projectileManager.TotalLaunched +
                " requested=" + projectileManager.TotalRequested + " reclaimed=" + projectileManager.ReclaimedSlots +
                " deferred=" + projectileManager.CapacityDeferralCount + " rejected=" + projectileManager.OverflowCount + " gpuActive=" + active + " cpuActive=" + projectileManager.ActiveCount);
            Assert.Greater(projectileManager.TotalLaunched, buffers.MaxProjectiles * 2,
                "Short-lived impacts must release slots before the configured five-second timeout.");
        }

        private void PoolPrivate(string method)
        {
            typeof(ProjectileGpuManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(projectileManager, null);
        }
        private void QueuePoolShot()
        {
            projectileManager.LaunchProjectile(new Vector3(-2,1,0), Vector3.zero, 4, new Vector3(2,1,0),
                1, 0, 20, 0, .08f, 5, launchTime: 0);
        }
        [Test]
        public void PoolSnapshotDoesNotReclaimALeaseCreatedAfterItsWatermark()
        {
            BuildRangedScenario(true, false, 4);
            QueuePoolShot(); PoolPrivate("FlushPendingProjectiles");
            var shots = new ProjectileGpuData[buffers.MaxProjectiles]; buffers.projectileBuffer.GetData(shots);
            shots[0].targetAgentIndex = -1; buffers.projectileBuffer.SetData(shots);
            PoolPrivate("RequestPoolSnapshot"); // Both slots empty, but only the first has a lease yet.
            QueuePoolShot(); PoolPrivate("FlushPendingProjectiles");
            AsyncGPUReadback.WaitAllRequests();
            Assert.AreEqual(1, projectileManager.ActiveCount, "Old free observation must not recycle the new second slot.");
            Assert.AreEqual(1, projectileManager.ReclaimedSlots);
            QueuePoolShot(); PoolPrivate("FlushPendingProjectiles");
            QueuePoolShot();
            Assert.AreEqual(3, projectileManager.TotalLaunched, "A fourth shot must not overwrite either live lease.");
            Assert.AreEqual(1, projectileManager.OverflowCount);
        }
        [Test]
        public void ClearingPoolInvalidatesOutstandingRecyclingSnapshot()
        {
            BuildRangedScenario(true, false, 4);
            QueuePoolShot(); PoolPrivate("FlushPendingProjectiles");
            PoolPrivate("RequestPoolSnapshot");
            projectileManager.ClearAllProjectiles();
            QueuePoolShot(); PoolPrivate("FlushPendingProjectiles");
            AsyncGPUReadback.WaitAllRequests();
            Assert.AreEqual(1, projectileManager.ActiveCount);
            Assert.AreEqual(0, projectileManager.ReclaimedSlots);
            var shots = new ProjectileGpuData[buffers.MaxProjectiles]; buffers.projectileBuffer.GetData(shots);
            Assert.AreEqual(4, shots[0].targetAgentIndex);
        }

        private void ConfigureRangedRows(bool muzzleBlocked = false, bool impossible = false, bool undersizedPool = false)
        {
            BuildScenario(16, 16, attackerProjectileRange: 20, attackerProjectileSpeed: 12,
                attackerProjectileGravity: -9.8f, attackerProjectileHitRadius: .08f, attackerProjectileMaxLifetime: 3);
            var owned = new System.Collections.Generic.List<ScriptableObject>(createdConfigs);
            foreach (var unit in scenario.unitTypes)
            {
                unit.combatConfig.attackInterval = .8f; unit.combatConfig.attackDamage = 1;
                unit.combatConfig.projectileOriginHeight = 1.3f; unit.combatConfig.projectileTargetHeight = 1;
                unit.flockingConfig.agentRadius = .5f; unit.flockingConfig.separationStrength = 0;
                var movement = ScriptableObject.CreateInstance<MovementConfig>();
                movement.maxSpeed = 2; movement.velocityDamping = 1;
                unit.movementConfig = movement; owned.Add(movement);
            }
            createdConfigs = owned.ToArray();
            registry.RegisterFromScenario(scenario);
            buffers.ReleaseAll();
            buffers.Allocate(fixtureTotalAgents,64,16,16,16,registry.UnitTypeCount,
                projectileCapacity: undersizedPool ? 2 : ProjectilePoolBudget.Calculate(scenario,fixtureTotalAgents));
            registry.InitializeAll(buffers, orchestrator);
            initialAgents = new AgentData[fixtureTotalAgents]; initialTeamIds = new int[fixtureTotalAgents];
            initialHp = new int[fixtureTotalAgents]; initialUnitTypeIndices = new int[fixtureTotalAgents];
            registry.GenerateAgents(initialAgents);
            registry.FillCombatArrays(initialTeamIds,initialHp,initialUnitTypeIndices);
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                initialHp[i] = (i < 4 || (i >= 16 && i < 20)) ? 2000 : 0;
                initialAgents[i].position = new Vector3(6, 0, 6);
                initialAgents[i].velocity = Vector3.zero; initialAgents[i].scale = Vector3.one;
            }
            for (int i = 0; i < 4; i++)
            {
                initialAgents[i].position = new Vector3(i%2 == 0 ? -4 : -2.2f, 0, i<2 ? -1.5f : 1.5f);
                initialAgents[i+16].position = new Vector3(i%2 == 0 ? 3 : 4.5f, 0, i<2 ? -1.5f : 1.5f);
            }
            if (muzzleBlocked) initialAgents[1].position = new Vector3(-3.4f, 0, -1.5f);
            if (impossible)
            {
                for (int i=0;i<fixtureTotalAgents;i++) initialHp[i] = (i==0 || i==1 || i==16) ? 2000 : 0;
                initialAgents[1].scale = new Vector3(8,4,8);
                initialUnitTypeIndices[1] = 1; // Non-shooting friendly body; same team buffer remains authoritative.
            }
            buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            fixtureTeamStances = new[] { (int)TeamStance.Advance, (int)TeamStance.Hold };
            settingsCache = new UnitTypeGpuSettings[registry.UnitTypeCount];
            InitializeProjectileManager(); dispatchedFrames = 0;
        }
        private bool SeenSource(int source)
        {
            var shots = new ProjectileGpuData[buffers.MaxProjectiles]; buffers.projectileBuffer.GetData(shots);
            foreach (var shot in shots) if (shot.targetAgentIndex >= 0 && shot.sourceAgentIndexPlusOne == source+1) return true;
            return false;
        }
        [UnityTest]
        public IEnumerator RearRowsActuallyLaunchOverFriendlyFrontAndDamageEnemy()
        {
            ConfigureRangedRows(); bool rear0 = false, rear2 = false; uint maxDraw = 0;
            for (int i=0;i<250;i++)
            {
                DispatchOneFrame(true); AsyncGPUReadback.WaitAllRequests();
                rear0 |= SeenSource(0); rear2 |= SeenSource(2); maxDraw = System.Math.Max(maxDraw,ReadProjectileInstanceCount());
                yield return null;
            }
            var hp = new int[fixtureTotalAgents]; buffers.combatBuffers.hpReadBuffer.GetData(hp);
            int enemyDamage = 0; for (int i=16;i<20;i++) enemyDamage += 2000-hp[i];
            Debug.Log("RANGED_ROWS requested="+projectileManager.TotalRequested+" generated="+projectileManager.TotalLaunched+
                " rear0="+rear0+" rear2="+rear2+" maxDraw="+maxDraw+" enemyDamage="+enemyDamage);
            Assert.True(rear0 && rear2, "Both actual rear sources must create real in-flight projectiles.");
            Assert.Greater(maxDraw,0); Assert.Greater(enemyDamage,0);
            for (int i=0;i<4;i++) Assert.AreEqual(2000,hp[i], "Solid friends must not take friendly fire.");
        }
        [UnityTest]
        public IEnumerator BlockedMuzzleFindsGapWithSmallLateralMoveThenFires()
        {
            ConfigureRangedRows(true); bool blocked = false, fired = false; float moved = 0;
            Vector3 origin = initialAgents[0].position;
            for (int i=0;i<250;i++)
            {
                DispatchOneFrame(true); AsyncGPUReadback.WaitAllRequests();
                blocked |= CongestionState(0)[7] < 0;
                fired |= SeenSource(0); moved = Mathf.Max(moved,Vector3.Distance(origin,CongestionAgents()[0].position));
                yield return null;
            }
            Debug.Log("RANGED_SIDE blocked="+blocked+" fired="+fired+" maxDisplacement="+moved);
            Assert.True(blocked, "Fixture must start with a genuinely obstructed muzzle.");
            Assert.True(fired, "A reachable side gap must enable real firing.");
            Assert.Greater(moved,.1f); Assert.Less(moved,2.1f, "Not unbounded rank rotation or forward pushing.");
        }
        [UnityTest]
        public IEnumerator UnclearableBodyWaitsWithinBudgetPauseFreezesAndNewHoldCancelsSearch()
        {
            ConfigureRangedRows(true,true); bool fired = false; Vector3 origin = initialAgents[0].position;
            for (int i=0;i<300;i++)
            {
                DispatchOneFrame(true); AsyncGPUReadback.WaitAllRequests(); fired |= SeenSource(0); yield return null;
            }
            var before = CongestionAgents()[0]; var state = CongestionState(0);
            Assert.False(fired, "Do not fire through an enclosing friendly body.");
            Assert.Less(Vector3.Distance(origin,before.position),2.1f);
            Assert.LessOrEqual(before.velocity.magnitude,.081f, "Only slow contact correction may remain.");
            Assert.AreNotEqual((int)AgentState.Attack,before.currentState);
            for (int i=0;i<30;i++) DispatchOneFrame(false);
            Assert.AreEqual(before.position,CongestionAgents()[0].position);
            CollectionAssert.AreEqual(state,CongestionState(0));
            fixtureTeamStances[0] = (int)TeamStance.Hold;
            buffers.combatBuffers.NotifyMovementCommand(0); DispatchOneFrame(true);
            Assert.Less(CongestionState(0)[10],.05f, "New command must discard the old side-search clock.");
            for (int i=0;i<40;i++) DispatchOneFrame(true);
            Assert.That(Vector3.Distance(before.position,CongestionAgents()[0].position),Is.LessThan(.01f));
            fixtureTeamStances[0] = (int)TeamStance.Advance;
            attackerFlowEnabled = true; attackerFlowTargetMode = 1;
            attackerFlowTargetPoint = new Vector3(-4,0,6);
            var flow = new Vector2[buffers.FlowCellCount*buffers.TeamCount];
            for (int i=0;i<flow.Length;i++) flow[i] = Vector2.up;
            buffers.flowFieldDirectionsBuffer.SetData(flow);
            buffers.combatBuffers.NotifyMovementCommand(0);
            float z = CongestionAgents()[0].position.z;
            for (int i=0;i<40;i++) DispatchOneFrame(true);
            Assert.Greater(CongestionAgents()[0].position.z-z,.1f, "Explicit Move must not inherit the exhausted automatic side-step budget.");
        }

        [UnityTest]
        public IEnumerator RealFullPoolDefersVolleyAndConfigBudgetRestoresSustainedEmission()
        {
            ConfigureRangedRows(undersizedPool:true);
            for (int i=0;i<180;i++) { DispatchOneFrame(true); AsyncGPUReadback.WaitAllRequests(); yield return null; }
            int limited=projectileManager.TotalLaunched; long requested=projectileManager.TotalRequested;
            int stalled=projectileManager.CapacityDeferralCount;
            Assert.Greater(stalled,0); Assert.Greater(requested,limited);
            ConfigureRangedRows();
            for (int i=0;i<180;i++) { DispatchOneFrame(true); AsyncGPUReadback.WaitAllRequests(); yield return null; }
            Debug.Log("RANGED_CAPACITY controlGenerated="+limited+" controlRequested="+requested+" controlDeferrals="+stalled+
                " budget="+buffers.MaxProjectiles+" budgetGenerated="+projectileManager.TotalLaunched+" budgetRequested="+projectileManager.TotalRequested);
            Assert.Greater(projectileManager.TotalLaunched,limited*2);
            Assert.AreEqual(0,projectileManager.CapacityDeferralCount);
            Assert.AreEqual(0,projectileManager.OverflowCount);
        }
    }
}
#endif
