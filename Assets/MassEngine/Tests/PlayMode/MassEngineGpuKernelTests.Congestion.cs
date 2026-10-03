#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MassEngine.Tests
{
    public sealed partial class MassEngineGpuKernelTests
    {
        private void ConfigureCongestionQueue(bool blockers = true, int cadence = 1)
        {
            var owned = new List<ScriptableObject>(createdConfigs);
            for (int type = 0; type < 2; type++)
            {
                var movement = ScriptableObject.CreateInstance<MovementConfig>();
                movement.maxSpeed = type == 0 ? 1.5f : .01f;
                movement.velocityDamping = 1;
                movement.flowFieldWeight = 1; movement.flowFieldResponsiveness = 8;
                scenario.unitTypes[type].movementConfig = movement; owned.Add(movement);
                scenario.unitTypes[type].flockingConfig.separationStrength = type == 0 ? 100 : 0;
            }
            createdConfigs = owned.ToArray();
            registry.RegisterFromScenario(scenario);
            fixtureTeamStances = new[] { (int)TeamStance.Advance, (int)TeamStance.Advance };
            attackerFlowEnabled = true; attackerFlowRebuild = false; attackerFlowTargetMode = 1;
            attackerFlowTargetPoint = new Vector3(6, 0, 0);
            var flow = new Vector2[buffers.FlowCellCount * buffers.TeamCount];
            for (int i = 0; i < flow.Length; i++) flow[i] = Vector2.right;
            buffers.flowFieldDirectionsBuffer.SetData(flow);
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                initialTeamIds[i] = 0; // Two unit types in one friendly queue; no enemy/attack pretext.
                initialAgents[i].position = new Vector3(i < AttackerCount ? -1.8f : blockers ? -1 : 5,
                    0, -3 + 2 * (i % AttackerCount));
                initialAgents[i].velocity = Vector3.zero;
            }
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            if (cadence > 1) { lodNearRadius = .01f; lodMidRadius = .02f; simFarInterval = cadence; }
        }

        private float[] CongestionState(int index)
        {
            var state = new float[CombatBufferSet.CongestionWordsPerAgent];
            buffers.combatBuffers.engagementSlotAssignmentBuffer.GetData(state, 0,
                fixtureTotalAgents + index * CombatBufferSet.CongestionWordsPerAgent, state.Length);
            return state;
        }
        private AgentData[] CongestionAgents()
        {
            var data = new AgentData[fixtureTotalAgents]; buffers.agentBuffer.GetData(data); return data;
        }
        private void ReachCongestionWait()
        {
            for (int i = 0; i < 100; i++)
            {
                DispatchOneFrame(true);
                if (CongestionState(0)[5] > 1) return;
            }
            Assert.Fail("A real friendly queue never entered a timed wait; no wait state was injected.");
        }

        [UnityTest]
        public IEnumerator CongestionQueueReallyStopsAndIdlesWithStaggeredRetries()
        {
            ConfigureCongestionQueue(); ReachCongestionWait();
            var before = CongestionAgents();
            float first = CongestionState(0)[5], min = first, max = first;
            for (int i = 1; i < AttackerCount; i++)
            { float value = CongestionState(i)[5]; min = Mathf.Min(min, value); max = Mathf.Max(max, value); }
            Assert.Greater(max - min, .1f, "Do not restart every queued unit on the same clock.");
            for (int frame = 0; frame < 35; frame++)
            {
                DispatchOneFrame(true);
                var current = CongestionAgents()[0];
                Assert.Greater(CongestionState(0)[5], 0);
                Assert.AreEqual(AgentState.Idle, (AgentState)current.presentationState);
                Assert.AreEqual(0, current.locomotionSpeed);
                Assert.Less(Vector3.Distance(before[0].position, current.position), .08f,
                    "Idle animation must not hide continuing forward propulsion.");
            }
            Assert.That(CongestionState(0)[5], Is.EqualTo(first - .7f).Within(.005f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionReleasedLaneResumesWithoutACommand()
        {
            ConfigureCongestionQueue(); ReachCongestionWait();
            var agents = CongestionAgents(); float start = agents[0].position.x;
            for (int i = AttackerCount; i < fixtureTotalAgents; i++) agents[i].position.x = 6;
            // Move the blockers only, preserving the naturally reached wait and all simulation state.
            buffers.agentBuffer.SetData(agents);
            var positions = new Vector2[agents.Length];
            for (int i = 0; i < agents.Length; i++) positions[i] = new Vector2(agents[i].position.x, agents[i].position.z);
            buffers.agentPositionReadBuffer.SetData(positions); buffers.agentPositionWriteBuffer.SetData(positions);
            for (int frame = 0; frame < 150; frame++) DispatchOneFrame(true);
            Assert.Greater(CongestionAgents()[0].position.x, start + .6f);
            Assert.AreEqual(0, CongestionState(0)[5]);
            Assert.AreEqual(AgentState.Move, (AgentState)CongestionAgents()[0].presentationState);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionNewCommandWakesImmediatelyOnFarLod()
        {
            ConfigureCongestionQueue(cadence: 4); ReachCongestionWait();
            var flow = new Vector2[buffers.FlowCellCount * buffers.TeamCount];
            for (int i = 0; i < flow.Length; i++) flow[i] = Vector2.left;
            buffers.flowFieldDirectionsBuffer.SetData(flow); attackerFlowTargetPoint = new Vector3(-6, 0, 0);
            buffers.combatBuffers.NotifyMovementCommand(0);
            float x = CongestionAgents()[0].position.x;
            DispatchOneFrame(true);
            Assert.AreEqual(0, CongestionState(0)[5]);
            Assert.AreEqual(1, CongestionState(0)[6]);
            for (int frame = 0; frame < 20; frame++) DispatchOneFrame(true);
            Assert.Less(CongestionAgents()[0].position.x, x - .1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionPauseFreezesTimerAndDeathAndResetClearIt()
        {
            ConfigureCongestionQueue(cadence: 4); ReachCongestionWait();
            float remaining = CongestionState(0)[5]; Vector3 position = CongestionAgents()[0].position;
            for (int frame = 0; frame < 20; frame++) DispatchOneFrame(false);
            Assert.AreEqual(remaining, CongestionState(0)[5]); Assert.AreEqual(position, CongestionAgents()[0].position);
            var hp = (int[])initialHp.Clone(); hp[0] = 0; buffers.combatBuffers.hpReadBuffer.SetData(hp);
            DispatchOneFrame(true);
            Assert.AreEqual(0, CongestionState(0)[5]);
            Assert.AreEqual(AgentState.Dead, (AgentState)CongestionAgents()[0].currentState);
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            foreach (float value in CongestionState(0)) Assert.AreEqual(0, value);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionClearLaneDoesNotInventAWaitingState()
        {
            ConfigureCongestionQueue(blockers: false);
            for (int frame = 0; frame < 70; frame++)
            {
                DispatchOneFrame(true); Assert.AreEqual(0, CongestionState(0)[5]);
            }
            Assert.Greater(CongestionAgents()[0].position.x, initialAgents[0].position.x + .7f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionNarrowCorridorWaitsAndThenPasses()
        {
            ConfigureCongestionQueue();
            staticObstacleCount = 2;
            staticObstacleRects[0] = new Vector4(-7, -7, 7, -.8f);
            staticObstacleRects[1] = new Vector4(-7, .8f, 7, 7);
            for (int i = 0; i < fixtureTotalAgents; i++)
                initialAgents[i].position = new Vector3(i < AttackerCount ? -1.8f - i : -1 + (i - AttackerCount) * 1.3f, 0, 0);
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            ReachCongestionWait();
            var agents = CongestionAgents(); float start = agents[0].position.x;
            for (int i = AttackerCount; i < fixtureTotalAgents; i++) agents[i].position.x = 6;
            buffers.agentBuffer.SetData(agents);
            var positions = new Vector2[agents.Length];
            for (int i = 0; i < agents.Length; i++) positions[i] = new Vector2(agents[i].position.x, agents[i].position.z);
            buffers.agentPositionReadBuffer.SetData(positions); buffers.agentPositionWriteBuffer.SetData(positions);
            for (int frame = 0; frame < 160; frame++) DispatchOneFrame(true);
            Assert.Greater(CongestionAgents()[0].position.x, start + .5f);
            Assert.Less(Mathf.Abs(CongestionAgents()[0].position.z), .8f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionOtherArmyCommandDoesNotWakeThisQueue()
        {
            ConfigureCongestionQueue(); ReachCongestionWait();
            float before = CongestionState(0)[5];
            buffers.combatBuffers.NotifyMovementCommand(1);
            DispatchOneFrame(true);
            Assert.That(CongestionState(0)[5], Is.EqualTo(before - FrameDt).Within(.0001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionAttackTakesPriorityOverWaiting()
        {
            ConfigureCongestionQueue(); ReachCongestionWait();
            var teams = (int[])initialTeamIds.Clone(); teams[AttackerCount] = 1;
            buffers.combatBuffers.teamIdBuffer.SetData(teams);
            for (int i = 0; i < 8; i++) DispatchOneFrame(true);
            Assert.AreEqual(AgentState.Attack, (AgentState)CongestionAgents()[0].currentState);
            Assert.AreEqual(0, CongestionState(0)[5]);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionOffCadenceCommandWithInstantSteeringStaysFinite()
        {
            ConfigureCongestionQueue(cadence: 4);
            scenario.unitTypes[0].movementConfig.flowFieldResponsiveness = 100;
            scenario.unitTypes[0].movementConfig.velocityDamping = 20;
            buffers.combatBuffers.NotifyMovementCommand(0);
            DispatchOneFrame(true); // alpha = 0, elapsedIntervals = 0: never evaluate pow(0,0).
            var agent = CongestionAgents()[0];
            Assert.False(float.IsNaN(agent.position.x) || float.IsInfinity(agent.position.x));
            Assert.False(float.IsNaN(agent.velocity.x) || float.IsInfinity(agent.velocity.x));
            Assert.AreEqual(1, CongestionState(0)[6]);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionForcedCommandDecisionDoesNotAdvanceMeleeClockTwice()
        {
            lodNearRadius = .01f; lodMidRadius = .02f; simFarInterval = 4;
            for (int i = 0; i < 8; i++) DispatchOneFrame(true);
            var before = new float[fixtureTotalAgents];
            buffers.combatBuffers.attackCooldownBuffer.GetData(before);
            Assert.Greater(before[0], 0);
            buffers.combatBuffers.NotifyMovementCommand(0);
            DispatchOneFrame(true); // Frame 9 is a forced decision, not a normal four-frame beat.
            var after = new float[fixtureTotalAgents]; buffers.combatBuffers.attackCooldownBuffer.GetData(after);
            Assert.AreEqual(before[0], after[0]);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CongestionSamePointReissueAndHoldCancelWaiting()
        {
            ConfigureCongestionQueue(); ReachCongestionWait();
            buffers.combatBuffers.NotifyMovementCommand(0); // Even an identical destination is a fresh order.
            DispatchOneFrame(true); Assert.AreEqual(0, CongestionState(0)[5]);
            ReachCongestionWait();
            fixtureTeamStances[0] = (int)TeamStance.Hold;
            buffers.combatBuffers.NotifyMovementCommand(0);
            DispatchOneFrame(true); Assert.AreEqual(0, CongestionState(0)[5]);
            yield return null;
        }
    }
}
#endif
