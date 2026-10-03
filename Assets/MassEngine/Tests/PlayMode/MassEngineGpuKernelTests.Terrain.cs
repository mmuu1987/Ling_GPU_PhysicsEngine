#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace MassEngine.Tests
{
    public sealed partial class MassEngineGpuKernelTests
    {
        private TerrainNavigationRuntime fixtureTerrain;

        private void ConfigureTerrain(float slope, bool wall = false)
        {
            var heights = new float[17 * 17];
            var blocked = new bool[16 * 16];
            for (int z = 0; z < 17; z++)
                for (int x = 0; x < 17; x++) heights[z * 17 + x] = x * slope;
            if (wall)
                for (int z = 0; z < 11; z++) blocked[z * 16 + 7] = true;
            var surface = new TerrainSurface("movement-test", 1, 17, 17, new Vector2(-8, -8),
                new Vector2(16, 16), 80, heights, blocked);
            fixtureTerrain = new TerrainNavigationRuntime(new TerrainNavigationGrid(surface,
                surface.Origin, 1, 16, 16, .05f, .25f), buffers.TeamCount);
        }

        private void UploadTerrainAgents(Func<int, Vector2> locate)
        {
            for (int i = 0; i < initialAgents.Length; i++)
            {
                Vector2 p = locate(i);
                Assert.True(fixtureTerrain.Navigation.IsWalkable(p), "Invalid test spawn " + p);
                Assert.True(fixtureTerrain.Navigation.Surface.TrySample(p, out var sample));
                initialAgents[i].position = sample.Position;
                initialAgents[i].velocity = Vector3.zero;
            }
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
        }

        private void DisableLocalCombat()
        {
            foreach (var unit in scenario.unitTypes)
            {
                unit.combatConfig.attackRange = .01f;
                unit.combatConfig.targetAcquireRadius = .01f;
            }
            fixtureTeamStances = new[] { (int)TeamStance.Advance, (int)TeamStance.Advance };
        }

        private static TeamFlowFrameSettings TerrainPoint(Vector3 target) => new TeamFlowFrameSettings
        {
            enabled = true, rebuildThisFrame = true, targetMode = 1, targetPoint = target, targetStopRadius = .3f
        };

        private PipelineFrameContext QueueThreeTerrainFields()
        {
            AllocateFixtureBuffers(3); ConfigureTerrain(0);
            var flows = new TeamFlowFrameSettings[3];
            var density = new uint[3 * 256];
            density[8 * 16 + 13] = 1;
            density[256 + 8 * 16 + 2] = 1;
            density[512 + 13 * 16 + 8] = 1;
            buffers.runtimeFlowTargetDensityBuffer.SetData(density);
            for (int team = 0; team < 3; team++) flows[team] = new TeamFlowFrameSettings { enabled = true, dynamicFlowEnabled = true };
            var context = new PipelineFrameContext { battleStarted = true, teamFlows = flows };
            for (int team = 0; team < 3; team++) fixtureTerrain.Rebuild(buffers, context, team);
            AsyncGPUReadback.WaitAllRequests();
            return context;
        }

        private Vector2 TerrainDirection(int team)
        {
            var directions = new Vector2[3 * 256]; buffers.flowFieldDirectionsBuffer.GetData(directions);
            return directions[team * 256 + 8 * 16 + 8];
        }

        [UnityTest]
        public IEnumerator TerrainDynamicWorkIsSpreadAcrossFramesAndOwnsItsReadbackSnapshot()
        {
            var context = QueueThreeTerrainFields();
            fixtureTerrain.Tick(buffers, context);
            Assert.AreEqual(1, fixtureTerrain.CompletedFields);
            buffers.runtimeFlowTargetDensityBuffer.SetData(new uint[3 * 256]);
            yield return null; yield return null; // Completed GPU request storage may now be disposed.
            fixtureTerrain.Tick(buffers, context); Assert.AreEqual(2, fixtureTerrain.CompletedFields);
            yield return null;
            fixtureTerrain.Tick(buffers, context); Assert.AreEqual(3, fixtureTerrain.CompletedFields);
            Assert.Greater(TerrainDirection(0).x, .9f);
            Assert.Less(TerrainDirection(1).x, -.9f);
            Assert.Greater(TerrainDirection(2).y, .9f);
        }

        [Test]
        public void TerrainDynamicRoundRobinDoesNotStarveLaterTeams()
        {
            var context = QueueThreeTerrainFields();
            fixtureTerrain.Tick(buffers, context);
            var density = new uint[3 * 256]; density[8 * 16 + 2] = 1;
            buffers.runtimeFlowTargetDensityBuffer.SetData(density);
            fixtureTerrain.Rebuild(buffers, context, 0); AsyncGPUReadback.WaitAllRequests();
            fixtureTerrain.Tick(buffers, context);
            Assert.Greater(TerrainDirection(0).x, .9f, "Team 0 must wait behind the older team 1 and 2 requests.");
            Assert.Less(TerrainDirection(1).x, -.9f);
            fixtureTerrain.Tick(buffers, context); Assert.Greater(TerrainDirection(2).y, .9f);
            fixtureTerrain.Tick(buffers, context); Assert.Less(TerrainDirection(0).x, -.9f);
            Assert.AreEqual(4, fixtureTerrain.CompletedFields);
        }

        [Test]
        public void TerrainExplicitCommandAndHoldInvalidateQueuedDynamicTargets()
        {
            var context = QueueThreeTerrainFields();
            fixtureTerrain.Tick(buffers, context);
            context.teamFlows[1] = TerrainPoint(new Vector3(5.5f, 0, .5f));
            fixtureTerrain.Rebuild(buffers, context, 1); // Explicit command is immediate.
            context.teamFlows[2] = new TeamFlowFrameSettings();
            fixtureTerrain.Rebuild(buffers, context, 2);
            for (int i = 0; i < 4; i++) fixtureTerrain.Tick(buffers, context);
            Assert.AreEqual(2, fixtureTerrain.CompletedFields);
            Assert.Greater(TerrainDirection(1).x, .9f, "An old dynamic target must not overwrite the explicit command.");
            Assert.AreEqual(Vector2.zero, TerrainDirection(2), "Hold must clear the queued route.");
        }

        [TestCase(1)]
        [TestCase(4)]
        public void TerrainArmiesClimbAndDescendAtSurfaceSpeedIncludingLightFrames(int interval)
        {
            ConfigureTerrain(.5f);
            DisableLocalCombat();
            lodNearRadius = lodMidRadius = 0;
            simFarInterval = interval;
            UploadTerrainAgents(i => new Vector2(i < AttackerCount ? -5 : 5, i < AttackerCount ? -2 : 2));
            fixtureTeamFlows = new[] { TerrainPoint(new Vector3(5, 0, -2)), TerrainPoint(new Vector3(-5, 0, 2)) };
            AgentData[] previous = (AgentData[])initialAgents.Clone();
            var current = new AgentData[fixtureTotalAgents];
            for (int frame = 0; frame < 200; frame++)
            {
                DispatchOneFrame(true);
                for (int team = 0; team < fixtureTeamFlows.Length; team++) fixtureTeamFlows[team].rebuildThisFrame = false;
                buffers.agentBuffer.GetData(current);
                for (int i = 0; i < current.Length; i++)
                {
                    var p = current[i].position;
                    Assert.True(fixtureTerrain.Navigation.Surface.TrySample(new Vector2(p.x, p.z), out var sample));
                    Assert.That(p.y, Is.EqualTo(sample.Position.y).Within(.0002f), "Grounding on frame " + frame);
                    Assert.That(Vector3.Distance(previous[i].position, p), Is.LessThanOrEqualTo(settingsCache[initialUnitTypeIndices[i]].maxSpeed * FrameDt + .0003f));
                }
                Array.Copy(current, previous, current.Length);
            }
            Assert.That(current[0].position.x, Is.GreaterThan(3), "Uphill team did not reach its own objective.");
            Assert.That(current[AttackerCount].position.x, Is.LessThan(-3), "Downhill team read the wrong flow slice.");
        }

        [Test]
        public void TerrainFlowRoutesAroundWallAndNeverUsesDirectFallback()
        {
            ConfigureTerrain(.1f, true);
            DisableLocalCombat();
            UploadTerrainAgents(i => new Vector2(i < AttackerCount ? -4 : 5, -4));
            fixtureTeamFlows = new[] { TerrainPoint(new Vector3(4, 0, -4)), new TeamFlowFrameSettings() };
            float maximumZ = -4;
            var current = new AgentData[fixtureTotalAgents];
            for (int frame = 0; frame < 600; frame++)
            {
                DispatchOneFrame(true);
                fixtureTeamFlows[0].rebuildThisFrame = false;
                buffers.agentBuffer.GetData(current);
                foreach (var agent in current)
                    Assert.True(fixtureTerrain.Navigation.IsWalkable(new Vector2(agent.position.x, agent.position.z)), "Unit entered a forbidden cell.");
                maximumZ = Mathf.Max(maximumZ, current[0].position.z);
            }
            Assert.That(maximumZ, Is.GreaterThan(4), "A route must pass the inflated wall's northern end.");
            Assert.That(Vector2.Distance(new Vector2(current[0].position.x, current[0].position.z), new Vector2(4, -4)), Is.LessThan(1));
        }

        [Test]
        public void TerrainDynamicTargetsUseEnemyDensityAndDisposeReleasesGpuStorage()
        {
            ConfigureTerrain(.5f);
            DisableLocalCombat();
            UploadTerrainAgents(i => new Vector2(i < AttackerCount ? -5 : 5, 0));
            fixtureTeamFlows = new[] {
                new TeamFlowFrameSettings { enabled = true, rebuildThisFrame = true, dynamicFlowEnabled = true, minAgentsPerTarget = 1 },
                new TeamFlowFrameSettings { enabled = true, rebuildThisFrame = true, dynamicFlowEnabled = true, minAgentsPerTarget = 1 }
            };
            DispatchOneFrame(true);
            AsyncGPUReadback.WaitAllRequests();
            for (int frame = 0; frame < 50; frame++)
            {
                fixtureTeamFlows[0].rebuildThisFrame = fixtureTeamFlows[1].rebuildThisFrame = false;
                DispatchOneFrame(true);
            }
            var agents = new AgentData[fixtureTotalAgents]; buffers.agentBuffer.GetData(agents);
            Assert.That(fixtureTerrain.CompletedFields, Is.EqualTo(2));
            Assert.That(agents[0].position.x, Is.GreaterThan(-3));
            Assert.That(agents[AttackerCount].position.x, Is.LessThan(3));
            fixtureTerrain.Dispose();
            Assert.False(fixtureTerrain.IsAllocated);
            Assert.False(shaderSet.CombatSimulationShader.IsKeywordEnabled(TerrainNavigationRuntime.Keyword));
        }

        [TestCase(.5f, true)]
        [TestCase(4f, false)]
        public void TerrainMeleeUsesThreeDimensionalRange(float slope, bool expectDamage)
        {
            ConfigureTerrain(slope);
            fixtureTeamStances = new[] { (int)TeamStance.Hold, (int)TeamStance.Hold };
            UploadTerrainAgents(i => new Vector2(i < AttackerCount ? -.5f : .5f, (i % AttackerCount) * 1.5f));
            for (int frame = 0; frame < 65; frame++) DispatchOneFrame(true);
            var hp = new int[fixtureTotalAgents]; buffers.combatBuffers.hpReadBuffer.GetData(hp);
            Assert.That(Array.Exists(hp, value => value < 100), Is.EqualTo(expectDamage));
        }

        [Test]
        public void TerrainPausedAndDeadAgentsStayAnchored()
        {
            ConfigureTerrain(.5f);
            UploadTerrainAgents(i => new Vector2(i < AttackerCount ? -5 : 5, 0));
            for (int i = 0; i < initialAgents.Length; i++) initialAgents[i].position.y = -100;
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            DispatchOneFrame(false);
            var dead = new int[fixtureTotalAgents];
            buffers.combatBuffers.hpReadBuffer.SetData(dead);
            buffers.combatBuffers.hpWriteBuffer.SetData(dead);
            DispatchOneFrame(true);
            var agents = new AgentData[fixtureTotalAgents]; buffers.agentBuffer.GetData(agents);
            foreach (var agent in agents)
            {
                fixtureTerrain.Navigation.Surface.TrySample(new Vector2(agent.position.x, agent.position.z), out var sample);
                Assert.That(agent.position.y, Is.EqualTo(sample.Position.y).Within(.0001f));
                Assert.That(agent.currentState, Is.EqualTo((int)AgentState.Dead));
            }
        }
    }
}
#endif
