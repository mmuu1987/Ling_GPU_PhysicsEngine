#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MassEngine.Game
{
    // Explicit opt-in diagnostic only. Ordinary launches do not allocate or read back anything.
    public sealed class CaptureDefaultProbe : MonoBehaviour
    {
        [Serializable]
        public sealed class Sample
        {
            public float seconds, progress;
            public int owner;
            public bool contested;
            public int[] stances, alive, stateAlive, attacking, engaging, inZone;
            public long[] hp;
        }

        [Serializable]
        public sealed class Receipt
        {
            public bool passed, defectReproduced, osInputTest, forcedVictory;
            public bool automaticOrdersOnlyUntilResult = true;
            public string stage, error, buildGuid, mode, victoryReason;
            public int units, positionChecks, winner = -1;
            public float observedSeconds, contestedSeconds;
            public int[] initialAlive, finalAlive, peakAttacking;
            public long[] initialHp;
            public List<string> checks = new List<string>();
            public List<Sample> samples = new List<Sample>();
        }

        private readonly Receipt receipt = new Receipt();
        private string output;
        private WarSandboxSceneSession session;
        private WarSandboxBattleController controller;
        private WarSandboxRuntimeDeployment deployment;
        private WarSandboxDeploymentEntry[] expectedDraft;
        private AgentData[] agents;
        private int[] teamIds, health;
        private float started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--capture-default-probe")) return;
            string outArg = args.FirstOrDefault(a => a.StartsWith("--terrain-output=", StringComparison.Ordinal));
            string modeArg = args.FirstOrDefault(a => a.StartsWith("--capture-default-mode=", StringComparison.Ordinal));
            if (outArg == null || modeArg == null) throw new ArgumentException("Capture probe requires isolated output and mode.");
            var go = new GameObject("Capture default opt-in regression");
            DontDestroyOnLoad(go);
            var probe = go.AddComponent<CaptureDefaultProbe>();
            probe.output = outArg.Substring("--terrain-output=".Length);
            probe.receipt.mode = modeArg.Substring("--capture-default-mode=".Length);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private void Verify(bool condition, string message)
        {
            Require(condition, message);
            receipt.checks.Add(message);
        }

        private static Button Button(string name) => FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Single(b => b.name == name && b.gameObject.activeInHierarchy);

        private IEnumerator Start()
        {
            receipt.buildGuid = Application.buildGUID;
            Require(receipt.mode == "baseline" || receipt.mode == "fixed", "Unknown probe mode");
            Require(Path.IsPathFullyQualified(output), "Output must be an absolute isolated path");
            Directory.CreateDirectory(output);
            var stack = new Stack<IEnumerator>();
            stack.Push(Run());
            while (stack.Count > 0)
            {
                bool moved;
                object next;
                try
                {
                    moved = stack.Peek().MoveNext();
                    next = moved ? stack.Peek().Current : null;
                }
                catch (Exception ex)
                {
                    receipt.error = ex.ToString();
                    Finish(1);
                    yield break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            receipt.passed = receipt.mode == "fixed";
            Finish(receipt.passed ? 0 : 1);
        }

        private void WriteJson(string name)
        {
            string path = Path.Combine(output, name);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private void Finish(int code)
        {
            if (controller != null) controller.PauseBattle();
            WriteJson("receipt.json");
            Debug.Log("CAPTURE_DEFAULT_RESULT " + JsonUtility.ToJson(receipt));
            Application.Quit(code);
        }

        private void PrepareBuffers()
        {
            int count = controller.manager.Buffers.AgentCount;
            Require(count == 2048, "Default deployment count changed");
            receipt.units = count;
            agents = new AgentData[count]; teamIds = new int[count]; health = new int[count];
            controller.manager.Buffers.combatBuffers.teamIdBuffer.GetData(teamIds);
        }

        private Sample Read()
        {
            var buffers = controller.manager.Buffers;
            buffers.agentBuffer.GetData(agents);
            buffers.combatBuffers.hpReadBuffer.GetData(health);
            int teams = controller.ArmyCount;
            var s = new Sample {
                seconds = Time.realtimeSinceStartup - started,
                progress = controller.ControlPointCaptureProgress,
                owner = controller.ControlPointOwnerTeamId,
                contested = controller.IsControlPointContested,
                stances = new int[buffers.teamStanceBuffer.count],
                alive = new int[teams], stateAlive = new int[teams],
                attacking = new int[teams], engaging = new int[teams],
                inZone = new int[teams], hp = new long[teams]
            };
            buffers.teamStanceBuffer.GetData(s.stances);
            for (int i = 0; i < agents.Length; i++)
            {
                int team = teamIds[i];
                Require(team >= 0 && team < teams, "Invalid GPU team identity");
                if (health[i] > 0) { s.alive[team]++; s.hp[team] += health[i]; }
                if (agents[i].currentState == 4) continue;
                s.stateAlive[team]++;
                if (agents[i].currentState == 3) s.attacking[team]++;
                if (agents[i].currentState == 2) s.engaging[team]++;
                if (controller.manager.TerrainNavigation != null)
                    Require(controller.manager.TerrainNavigation.IsWalkable(new Vector2(agents[i].position.x, agents[i].position.z)),
                        "Live unit is in a blocked navigation cell: " + i);
                receipt.positionChecks++;
            }
            for (int team = 0; team < teams; team++) s.inZone[team] = controller.GetControlPointUnitCount(team);
            return s;
        }

        private IEnumerator Enter(int season)
        {
            Button("card-" + season).onClick.Invoke();
            yield return new WaitForSecondsRealtime(.3f);
            Button("card-" + season + "-enter").onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 120;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(.6f);
            Require(session.State == WarSandboxEntryState.Battle && !session.HelpOpen, "Battlefield did not load");
            controller = session.Controller;
            deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            Require(deployment.IsEditing && deployment.Draft.Snapshot().Sum(e => e.count) == 2048, "Default draft changed");
        }

        private IEnumerator Run()
        {
            receipt.stage = "load";
            yield return new WaitForSecondsRealtime(3);
            session = WarSandboxSceneSession.Instance;
            Button("home-play").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.3f);
            yield return Enter(0);
            expectedDraft = deployment.Draft.Snapshot();
            var rules = deployment.Draft.Rules;
            rules.gameMode = WarSandboxGameMode.ControlPoint;
            deployment.Draft.Replace(deployment.Draft.Snapshot(), rules, deployment.Draft.Stats);
            Require(deployment.TryValidate(out string validationError), validationError);
            Button("deployment-start").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.3f);
            Require(controller.Phase == WarSandboxBattlePhase.Running && !deployment.IsEditing, "Default battle did not start");
            Require(controller.gameMode == WarSandboxGameMode.ControlPoint, "Wrong game mode");
            // No post-start Move, Hold, Attack, damage or result injection in this natural round.
            PrepareBuffers();
            started = Time.realtimeSinceStartup;
            Sample initial = Read();
            receipt.initialAlive = initial.alive;
            receipt.initialHp = initial.hp;
            receipt.peakAttacking = new int[controller.ArmyCount];
            Require(initial.alive.SequenceEqual(new[] { 1024, 1024 }), "Initial armies are not the unmodified default");
            receipt.stage = "default-control-point";
            float nextSample = 0;
            float previousSample = 0;
            bool previousContested = false;
            int firstFrame = Time.frameCount;
            float limit = receipt.mode == "baseline" ? 90 : 180;
            while (!controller.BattleResult.valid && Time.realtimeSinceStartup - started < limit)
            {
                float elapsed = Time.realtimeSinceStartup - started;
                if (elapsed > 10) Require((Time.frameCount - firstFrame) / elapsed >= 10, "Under 10 fps; stop functional check");
                if (elapsed >= nextSample)
                {
                    Sample s = Read();
                    receipt.samples.Add(s);
                    if (previousContested && s.contested) receipt.contestedSeconds += s.seconds - previousSample;
                    previousSample = s.seconds; previousContested = s.contested;
                    for (int team = 0; team < s.attacking.Length; team++)
                        receipt.peakAttacking[team] = Math.Max(receipt.peakAttacking[team], s.attacking[team]);
                    receipt.observedSeconds = elapsed;
                    nextSample = elapsed + 1;
                    WriteJson("progress.json");
                }
                yield return null;
            }
            Sample final = Read();
            receipt.samples.Add(final);
            receipt.observedSeconds = final.seconds;
            receipt.finalAlive = final.alive;
            if (receipt.mode == "baseline")
            {
                receipt.defectReproduced = !controller.BattleResult.valid && receipt.contestedSeconds >= 10 &&
                    final.alive.SequenceEqual(receipt.initialAlive) && final.hp.SequenceEqual(receipt.initialHp) &&
                    receipt.samples.All(s => s.stances.Take(2).All(x => x == (int)TeamStance.MoveOnly)) &&
                    receipt.peakAttacking.All(x => x == 0);
                receipt.stage = receipt.defectReproduced ? "expected-defect-reproduced" : "baseline-inconclusive";
                receipt.error = receipt.defectReproduced ? "Both default capture armies remain MoveOnly: contested without attacks, HP loss or natural result." : "Expected baseline defect was not reproduced.";
                yield break;
            }

            Verify(controller.BattleResult.valid, "Default capture round reaches a natural result without corrective orders");
            Verify(receipt.peakAttacking.All(x => x > 0), "Both armies performed real GPU attacks");
            Verify(final.hp.Sum() < receipt.initialHp.Sum(), "Real combat reduced GPU HP");
            Verify(receipt.samples.All(s => s.stances.Take(2).All(x => x == (int)TeamStance.Advance)), "Default objective keeps combat-enabled Advance stance");
            var result = controller.BattleResult;
            receipt.victoryReason = result.victoryReason.ToString();
            receipt.winner = result.winnerTeamId;
            Verify(result.victoryReason == WarSandboxVictoryReason.Annihilation || result.victoryReason == WarSandboxVictoryReason.ControlPoint,
                "Natural result is annihilation or capture, not manual/forced victory");
            Verify(final.alive.SequenceEqual(final.stateAlive), "GPU HP/state alive counts agree at settlement");
            for (int team = 0; team < 2; team++)
            {
                var army = result.GetArmy(team);
                Verify(army.initial == 1024 && army.survivors + army.Casualties == army.initial, "Result arithmetic team " + team);
                Verify(army.survivors == final.alive[team], "Result matches final GPU counts team " + team);
            }
            string frozen = JsonUtility.ToJson(result);
            Verify(!controller.IssueOrder(ArmyOrder.Attack(0)) && !controller.EndBattle(), "Terminal state rejects ordinary commands");
            controller.StartOrResumeBattle();
            yield return new WaitForSecondsRealtime(.6f);
            Verify(JsonUtility.ToJson(controller.BattleResult) == frozen && Read().alive.SequenceEqual(final.alive), "Terminal state remains frozen");

            receipt.stage = "restart-and-explicit-overrides";
            Button("result-restart").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.4f);
            PrepareBuffers();
            Sample restarted = Read();
            Verify(!controller.BattleResult.valid && controller.Phase == WarSandboxBattlePhase.Running &&
                   restarted.alive.SequenceEqual(new[] { 1024, 1024 }), "Restart restores both full armies");
            Verify(restarted.stances.Take(2).All(x => x == (int)TeamStance.Advance), "Restart restores default objective combat doctrine");
            Verify(Mathf.Abs(controller.SimulationSpeed - 1) < .001f && controller.ControlPointCaptureProgress == 0, "Restart clears speed and point progress");
            Verify(controller.IssueMoveOrder(0, new Vector3(-100, 0, 55), false), "Explicit Move replaces the default objective");
            Verify(controller.IssueOrder(ArmyOrder.Hold(1)), "Explicit Hold replaces the default objective");
            yield return new WaitForSecondsRealtime(.4f);
            Sample overridden = Read();
            Verify(overridden.stances[0] == (int)TeamStance.MoveOnly && overridden.stances[1] == (int)TeamStance.HoldHere,
                "GPU explicit MoveOnly/HoldHere semantics are preserved");
            Verify(overridden.attacking[0] == 0 && overridden.engaging[0] == 0, "Explicit moving army does not retain combat state");
            Verify(controller.EndBattle(), "Manual end remains available after the separate override check");
            yield return new WaitForSecondsRealtime(.4f);
            Verify(controller.BattleResult.victoryReason == WarSandboxVictoryReason.ManualEnd && controller.BattleResult.winnerTeamId == -1,
                "Manual end does not invent a winner");
            Button("result-edit").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.4f);
            Verify(deployment.IsEditing && controller.Phase == WarSandboxBattlePhase.Setup && !controller.BattleResult.valid, "Return to deployment clears result state");
            var actual = deployment.Draft.Snapshot();
            Verify(actual.Length == expectedDraft.Length, "Draft composition count preserved");
            for (int i = 0; i < actual.Length; i++)
                Verify(actual[i].template == expectedDraft[i].template && actual[i].teamId == expectedDraft[i].teamId &&
                       actual[i].count == expectedDraft[i].count && Vector3.Distance(actual[i].center, expectedDraft[i].center) < .001f &&
                       Mathf.Abs(actual[i].density - expectedDraft[i].density) < .0001f &&
                       Mathf.Abs(actual[i].aspect - expectedDraft[i].aspect) < .0001f &&
                       Vector3.Distance(actual[i].manualSize, expectedDraft[i].manualSize) < .001f,
                    "Draft identity/count/placement preserved " + i);
            Button("deployment-start").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.4f);
            Verify(Read().stances.Take(2).All(x => x == (int)TeamStance.Advance), "Reapply starts the correct default doctrine again");
            Verify(controller.EndBattle(), "Separate reentry setup can end");
            yield return new WaitForSecondsRealtime(.3f);
            Button("result-menu").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.3f);
            if (session.ConfirmationOpen) Button("modal-confirm").onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 120;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(.6f);
            Verify(session.State == WarSandboxEntryState.Menu && session.Controller == null, "Catalog unloads the previous controller");
            yield return Enter(2);
            Verify(controller.Phase == WarSandboxBattlePhase.Setup && !controller.BattleResult.valid &&
                   controller.GetMoveRoutePointCount(0) == 0 && controller.GetMoveRoutePointCount(1) == 0 &&
                   controller.ControlPointCaptureProgress == 0 && Mathf.Abs(controller.SimulationSpeed - 1) < .001f,
                "Fresh battlefield has no previous order/result/speed/capture residue");
            receipt.stage = "complete";
        }
    }
}
#endif
