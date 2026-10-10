#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MassEngine.Tests
{
    public sealed partial class MassEngineGpuKernelTests
    {
        private string P3Output()
        {
            const string prefix = "--interaction-p3-output=";
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            if (arg == null) Assert.Ignore("P3 qualification is opt-in and must preserve existing artifacts.");
            return arg.Substring(prefix.Length);
        }
        private void P3OfficialSettings(float strength, int template)
        {
            P3Output();
            var owned = new List<ScriptableObject>(createdConfigs);
            for (int i = 0; i < 2; i++)
            {
                int sourceId = template < 0 ? (i == 0 ? 0 : 4) : template;
                var source = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>("Assets/Game/Content/Battlefields/FormationSetup/Template" + sourceId + ".asset");
                Assert.NotNull(source); Assert.AreEqual(48, source.flockingConfig.separationStrength);
                Assert.That(source.flockingConfig.agentRadius, Is.EqualTo(.55f).Within(.0001f));
                var flock = UnityEngine.Object.Instantiate(source.flockingConfig); flock.separationStrength = strength;
                var move = UnityEngine.Object.Instantiate(source.movementConfig);
                var combat = UnityEngine.Object.Instantiate(source.combatConfig);
                scenario.unitTypes[i].flockingConfig = flock; scenario.unitTypes[i].movementConfig = move; scenario.unitTypes[i].combatConfig = combat;
                owned.Add(flock); owned.Add(move); owned.Add(combat);
            }
            createdConfigs = owned.ToArray(); registry.RegisterFromScenario(scenario);
            registry.FillGpuSettings(settingsCache); buffers.UploadUnitTypeSettings(settingsCache);
            for (int i = 0; i < initialHp.Length; i++) initialHp[i] = scenario.unitTypes[initialUnitTypeIndices[i]].combatConfig.maxHp;
            Assert.That(settingsCache.All(x => Mathf.Approximately(x.agentRadius, .55f) && Mathf.Approximately(x.separationStrength, strength)), Is.True);
        }
        [Serializable] private sealed class P3KernelReceipt
        {
            public string kind; public int template, gateWaitingAgentFrames, attackSamples, hpLost;
            public float strength, corridorWidth = 1.6f, radius = .55f, allPassedAtSeconds = -1, maxSide, maxContactDistance, reverseProgress;
            public bool passed;
        }
        private IEnumerator P3Corridor(float strength, int template)
        {
            P3OfficialSettings(strength, template);
            var r = new P3KernelReceipt { kind = "corridor-release-pause-reverse", template = template, strength = strength };
            fixtureTeamStances = new[] { (int)TeamStance.Advance, (int)TeamStance.Advance };
            attackerFlowEnabled = true; attackerFlowRebuild = false; attackerFlowTargetMode = 1; attackerFlowTargetPoint = new Vector3(6, 0, 0);
            var flow = new Vector2[buffers.FlowCellCount * buffers.TeamCount]; for (int i = 0; i < flow.Length; i++) flow[i] = Vector2.right;
            buffers.flowFieldDirectionsBuffer.SetData(flow);
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                initialTeamIds[i] = 0; initialAgents[i].scale = Vector3.one; initialAgents[i].velocity = Vector3.zero;
                initialAgents[i].position = i < 4 ? new Vector3(-6 + i * 1.2f, 0, 0) : new Vector3(-6 + (i-4)*1.5f, 0, 6);
            }
            staticObstacleCount = 3;
            staticObstacleRects[0] = new Vector4(-7.2f, -7.2f, 1, -.8f);
            staticObstacleRects[1] = new Vector4(-7.2f, .8f, 1, 4);
            staticObstacleRects[2] = new Vector4(-.1f, -.8f, .1f, .8f);
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            for (int frame = 0; frame < 200; frame++)
            {
                DispatchOneFrame(true);
                for (int i = 0; i < 4; i++) if (CongestionState(i)[5] > 0) r.gateWaitingAgentFrames++;
                if ((frame & 31) == 0) yield return null;
            }
            Assert.Greater(r.gateWaitingAgentFrames, 0, "Blocked followers must naturally wait; no timer injected.");
            staticObstacleCount = 2; // Remove only the gate. Wall width/radius/agents stay unchanged.
            for (int frame = 0; frame < 600; frame++)
            {
                DispatchOneFrame(true); var agents = CongestionAgents(); bool all = true;
                for (int i = 0; i < 4; i++)
                {
                    var p = agents[i].position; Assert.IsFalse(float.IsNaN(p.x) || float.IsInfinity(p.x));
                    if (p.x < 1 && p.x > -7) { r.maxSide = Mathf.Max(r.maxSide, Mathf.Abs(p.z)); Assert.LessOrEqual(Mathf.Abs(p.z) + .55f, .82f, "Body crossed corridor wall instead of passing it."); }
                    if (p.x <= 2) all = false;
                }
                if (all) { r.allPassedAtSeconds = (frame + 1) * FrameDt; break; }
                if ((frame & 31) == 0) yield return null;
            }
            Assert.Greater(r.allPassedAtSeconds, 0, "All four corridor soldiers must clear the outlet within 12 seconds.");
            var before = CongestionAgents(); for (int i = 0; i < 20; i++) DispatchOneFrame(false); var paused = CongestionAgents();
            for (int i = 0; i < 4; i++) Assert.That(Vector3.Distance(before[i].position, paused[i].position), Is.LessThan(.0001f));
            for (int i = 0; i < flow.Length; i++) flow[i] = Vector2.left; buffers.flowFieldDirectionsBuffer.SetData(flow);
            attackerFlowTargetPoint = new Vector3(-6, 0, 0); buffers.combatBuffers.NotifyMovementCommand(0);
            for (int i = 0; i < 300; i++) { DispatchOneFrame(true); if ((i & 31) == 0) yield return null; }
            var after = CongestionAgents(); r.reverseProgress = before.Take(4).Average(x => x.position.x) - after.Take(4).Average(x => x.position.x);
            Assert.Greater(r.reverseProgress, 2, "The new reverse command must release the queue and make progress.");
            r.passed = true; File.WriteAllText(Path.Combine(P3Output(), "corridor-"+template+"-"+strength+".json"), JsonUtility.ToJson(r,true));
        }
        private IEnumerator P3Melee(float strength)
        {
            P3OfficialSettings(strength,-1); var r = new P3KernelReceipt { kind="melee-reach-hold-self-defense",template=-1,strength=strength };
            for (int i = 0; i < fixtureTotalAgents; i++) { initialAgents[i].scale=Vector3.one;initialAgents[i].position=new Vector3(i<4?-.5f:.5f,0,(i%4)*1.5f); }
            fixtureTeamStances = new[] { (int)TeamStance.Hold, (int)TeamStance.Hold };
            buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            var hp=new int[fixtureTotalAgents];var targets=new int[fixtureTotalAgents];
            for(int frame=0;frame<300;frame++)
            {
                DispatchOneFrame(true);var agents=CongestionAgents();buffers.combatBuffers.hpReadBuffer.GetData(hp);buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);
                for(int i=0;i<agents.Length;i++)
                {
                    if(hp[i]<=0 || agents[i].currentState!=(int)AgentState.Attack || targets[i]<0 || hp[targets[i]]<=0)continue;
                    float d=Vector2.Distance(new Vector2(agents[i].position.x,agents[i].position.z),new Vector2(agents[targets[i]].position.x,agents[targets[i]].position.z));
                    r.maxContactDistance=Mathf.Max(r.maxContactDistance,d);r.attackSamples++;
                    Assert.LessOrEqual(d,settingsCache[initialUnitTypeIndices[i]].attackRange+.35f,"Sampled contact moved outside melee reach tolerance.");
                }
                if((frame&31)==0)yield return null;
            }
            r.hpLost=Enumerable.Range(0,hp.Length).Sum(i=>initialHp[i]-Mathf.Max(0,hp[i]));Assert.Greater(r.attackSamples,0);Assert.Greater(r.hpLost,0,"Hold permits nearby self-defense, not ceasefire.");
            r.passed=true;File.WriteAllText(Path.Combine(P3Output(),"melee-"+strength+".json"),JsonUtility.ToJson(r,true));
        }
        [UnityTest] public IEnumerator P3NarrowMale24() { return P3Corridor(24,0); }
        [UnityTest] public IEnumerator P3NarrowMale48() { return P3Corridor(48,0); }
        [UnityTest] public IEnumerator P3NarrowKnight24() { return P3Corridor(24,4); }
        [UnityTest] public IEnumerator P3NarrowKnight48() { return P3Corridor(48,4); }
        [UnityTest] public IEnumerator P3MeleeHold24() { return P3Melee(24); }
        [UnityTest] public IEnumerator P3MeleeHold48() { return P3Melee(48); }
    }
}
#endif
