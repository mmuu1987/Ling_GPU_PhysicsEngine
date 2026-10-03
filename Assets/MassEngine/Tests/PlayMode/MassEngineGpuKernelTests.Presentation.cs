#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MassEngine.Tests
{
    public sealed partial class MassEngineGpuKernelTests
    {
        [UnityTest]
        public IEnumerator RangedCycleRetainsSimulationSafetyCapDuringLongFrames()
        {
            BuildRangedScenario(true, false, distance: 6);
            projectileProcessingEnabled = false; projectileSimulationEnabled = false;
            scenario.unitTypes[0].combatConfig.attackInterval = 1;
            scenario.unitTypes[0].combatConfig.attackReleasePhase = .5f;
            var targets = new int[fixtureTotalAgents];
            for (int i = 0; i < targets.Length; i++) targets[i] = i < AttackerCount ? AttackerCount : 0;
            buffers.combatBuffers.targetAgentIndexBuffer.SetData(targets);
            for (int frame = 0; frame < 9; frame++) DispatchOneFrame(true, .1f);
            var counts = new int[fixtureTotalAgents]; buffers.combatBuffers.launchRequestBuffer.GetData(counts);
            Assert.AreEqual(0, counts[0], "The existing 50ms cap must apply to ranged time too.");
            for (int frame = 0; frame < 2; frame++) DispatchOneFrame(true, .1f);
            yield return null;
            buffers.combatBuffers.launchRequestBuffer.GetData(counts);
            Assert.AreEqual(1, counts[0]);
        }

        [UnityTest]
        public IEnumerator BlockedEngageRemainsTacticalEngageButDisplaysIdle()
        {
            // Hold intentionally refuses out-of-range acquisition, so it cannot
            // represent blocked pursuit. Constrain actual advance below the stop
            // threshold instead and keep the normal Engage tactical behaviour.
            fixtureTeamStances = new[] { (int)TeamStance.Advance, (int)TeamStance.Advance };
            var owned = new System.Collections.Generic.List<ScriptableObject>(createdConfigs);
            foreach (var unit in scenario.unitTypes)
            {
                var movement = ScriptableObject.CreateInstance<MovementConfig>();
                movement.maxSpeed = .01f;
                unit.movementConfig = movement;
                owned.Add(movement);
            }
            createdConfigs = owned.ToArray();
            registry.ReleaseAll(); registry.RegisterFromScenario(scenario);
            registry.InitializeAll(buffers, orchestrator);
            for (int i = 0; i < initialAgents.Length; i++)
                initialAgents[i].position.x = initialTeamIds[i] == 0 ? -3 : 3;
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            for (int frame = 0; frame < 20; frame++) DispatchOneFrame(true);
            yield return null;
            var result = new AgentData[fixtureTotalAgents]; buffers.agentBuffer.GetData(result);
            Assert.AreEqual((int)AgentState.Engage, result[0].currentState);
            Assert.AreEqual((int)AgentState.Idle, result[0].presentationState);
            Assert.That(result[0].locomotionSpeed, Is.LessThan(.05f));
        }

        [UnityTest]
        public IEnumerator RangedWindupCycleAndPauseStayAlignedAtLowDecisionCadence()
        {
            BuildRangedScenario(true, false, distance: 6);
            projectileProcessingEnabled = false; // Observe GPU requests before async consumption.
            projectileSimulationEnabled = false;
            scenario.unitTypes[0].combatConfig.attackInterval = 1;
            scenario.unitTypes[0].combatConfig.attackReleasePhase = .5f;
            lodNearRadius = 0; lodMidRadius = 0; simFarInterval = 4;
            var counts = new int[fixtureTotalAgents];
            for (int frame = 0; frame < 20; frame++) DispatchOneFrame(true);
            buffers.combatBuffers.launchRequestBuffer.GetData(counts);
            Assert.AreEqual(0, counts[0], "Must finish first-shot preparation before release.");
            for (int frame = 20; frame < 100; frame++) DispatchOneFrame(true);
            buffers.combatBuffers.launchRequestBuffer.GetData(counts);
            Assert.AreEqual(2, counts[0], "One release per one-second cycle, not per render/decision frame.");
            var before = new AgentData[fixtureTotalAgents]; buffers.agentBuffer.GetData(before);
            var cooldown = new float[fixtureTotalAgents]; buffers.combatBuffers.attackCooldownBuffer.GetData(cooldown);
            float expectedPhase = Mathf.Repeat(.5f - cooldown[0], 1f) * settingsCache[0].attackClipDuration;
            Assert.That(before[0].currentAnimationTime, Is.EqualTo(expectedPhase).Within(.001f));
            for (int frame = 0; frame < 10; frame++) DispatchOneFrame(false);
            yield return null;
            var after = new AgentData[fixtureTotalAgents]; buffers.agentBuffer.GetData(after);
            buffers.combatBuffers.launchRequestBuffer.GetData(counts);
            Assert.AreEqual(2, counts[0]);
            Assert.That(after[0].currentAnimationTime, Is.EqualTo(before[0].currentAnimationTime).Within(.0001f));
            var pausedCooldown = new float[fixtureTotalAgents];
            buffers.combatBuffers.attackCooldownBuffer.GetData(pausedCooldown);
            Assert.That(pausedCooldown[0], Is.EqualTo(cooldown[0]).Within(.0001f));
            for (int frame = 0; frame < 50; frame++) DispatchOneFrame(true);
            buffers.combatBuffers.launchRequestBuffer.GetData(counts);
            Assert.AreEqual(3, counts[0], "Continue must resume, not restart or immediately fire a free shot.");
        }
    }
}
#endif
