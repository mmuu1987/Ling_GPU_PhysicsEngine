#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class CongestionEvidence
        {
            public string method;
            public int population, maximumWaiting, trackedAgent, idleSamples, clearedByNewOrder, waitingBeforeNewOrder;
            public float firstWaitSeconds, trackedDisplacement, redirectedMeanProgress;
            public bool actualMovementCommand, naturallyEnteredWait, holdClearedWait, resetClearedWait;
        }
        private IEnumerator RunCongestionSmoke()
        {
            var evidence = new CongestionEvidence { method = "Normal 512-agent authored scene, real controller Move/Hold commands, no injected wait state, HP or outcomes. Readback is probe-only. 30 FPS functional validation, not a performance result." };
            report.congestion = evidence;
            var audio = WarSandboxAudio.Ensure();
            Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Wrong isolated audio settings.");
            audio.SetMuted(true);
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == "launch-open", "Wrong initial battlefield.");
            BindBattle(false); VerifyFullStrength();
            int count = manager.UnitTypes.TotalAgentCount;
            evidence.population = count; Require(count == 512, "Changed authored population.");
            var agents = new AgentData[count];
            var words = new float[count * (1 + CombatBufferSet.CongestionWordsPerAgent)];
            var teams = new int[count];
            manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
            Action sample = () =>
            {
                manager.Buffers.agentBuffer.GetData(agents);
                manager.Buffers.combatBuffers.engagementSlotAssignmentBuffer.GetData(words);
            };
            Func<int, float> remaining = i => words[count + i * CombatBufferSet.CongestionWordsPerAgent + 5];
            Func<int> waitingCount = () =>
            {
                int n = 0; for (int i = 0; i < count; i++) if (teams[i] == 0 && remaining(i) > 0) n++;
                return n;
            };
            cameraTarget = new Vector3(-25, 0, 0);
            renderCamera.transform.position = new Vector3(-25, 38, -44);
            renderCamera.transform.LookAt(cameraTarget);
            yield return CaptureWorld("congestion-setup.png");
            Stage("congestion-move-command");
            Require(controller.IssueOrder(ArmyOrder.Hold(1)), controller.CommandError);
            Require(controller.IssueMoveOrder(0, new Vector3(-25, 0, 0), false), controller.CommandError);
            evidence.actualMovementCommand = true;
            report.actions.Add("Controller.IssueMoveOrder team 0 to (-25,0,0); opposing army holds, no scripted blockers.");
            int tracked = -1; float deadline = Time.realtimeSinceStartup + 40;
            while (tracked < 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(.15f);
                Require(manager.IsBattleRunning, "Battle stopped before congestion observation.");
                sample(); evidence.maximumWaiting = Mathf.Max(evidence.maximumWaiting, waitingCount());
                for (int i = 0; i < count; i++)
                    if (teams[i] == 0 && remaining(i) > 1.3f) { tracked = i; break; }
            }
            Require(tracked >= 0, "Natural movement order did not produce an observable congestion wait in 40 seconds.");
            evidence.trackedAgent = tracked; evidence.firstWaitSeconds = remaining(tracked);
            evidence.naturallyEnteredWait = true;
            Vector3 stoppedPosition = agents[tracked].position;
            float scale = Mathf.Max(.01f, Mathf.Max(Mathf.Abs(agents[tracked].scale.x), Mathf.Abs(agents[tracked].scale.z)));
            Stage("congestion-stopped-idle");
            yield return CaptureWorld("congestion-wait.png");
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSecondsRealtime(.1f); sample();
                Require(remaining(tracked) > 0 && agents[tracked].presentationState == (int)AgentState.Idle &&
                    agents[tracked].locomotionSpeed == 0, "Waiting unit restarted its running animation.");
                evidence.idleSamples++;
                evidence.trackedDisplacement = Mathf.Max(evidence.trackedDisplacement, Vector3.Distance(stoppedPosition, agents[tracked].position));
                Require(evidence.trackedDisplacement < .1f * scale, "Idle animation is masking continued propulsion.");
            }
            yield return CaptureWorld("congestion-still-idle.png");
            sample(); var wasWaiting = new bool[count]; var beforeRedirect = (AgentData[])agents.Clone();
            for (int i = 0; i < count; i++)
                if (teams[i] == 0 && remaining(i) > 0) { wasWaiting[i] = true; evidence.waitingBeforeNewOrder++; }
            Require(evidence.waitingBeforeNewOrder > 0, "No queued units left for the re-command check.");
            var destination = new Vector3(-75, 0, -30);
            Stage("congestion-new-command");
            Require(controller.IssueMoveOrder(0, destination, false), controller.CommandError);
            yield return null; yield return null; sample();
            for (int i = 0; i < count; i++) if (wasWaiting[i] && remaining(i) <= 0) evidence.clearedByNewOrder++;
            Require(evidence.clearedByNewOrder == evidence.waitingBeforeNewOrder, "Accepted new command did not wake the queued army.");
            yield return new WaitForSecondsRealtime(4); sample();
            int observed = 0; float progress = 0;
            for (int i = 0; i < count; i++) if (wasWaiting[i])
            {
                Vector3 direction = destination - beforeRedirect[i].position; direction.y = 0;
                progress += Vector3.Dot(agents[i].position - beforeRedirect[i].position, direction.normalized); observed++;
            }
            evidence.redirectedMeanProgress = progress / Mathf.Max(1, observed);
            Require(evidence.redirectedMeanProgress > .15f, "Re-commanded queue made no progress along the new route.");
            yield return CaptureWorld("congestion-redirected.png");
            Require(controller.IssueOrder(ArmyOrder.Hold(0)), controller.CommandError);
            yield return null; yield return null; sample();
            evidence.holdClearedWait = waitingCount() == 0;
            Require(evidence.holdClearedWait, "Hold retained congestion waits.");
            yield return Click("reset"); VerifyFullStrength(); sample();
            evidence.resetClearedWait = waitingCount() == 0;
            Require(evidence.resetClearedWait, "Restart retained old wait timers.");
            yield return Leave(false); VerifySources(); VerifyPlans();
            Require(!System.IO.File.Exists(settingsFile), "Congestion probe wrote settings.");
            Stage("complete");
        }
    }
}
#endif
