#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class CommandEvidence
        {
            public string method = "Real controller commands and GPU readbacks in authored 512/384-agent scenes; no injected positions, HP, waits or results. 30 FPS functional cap, not performance/human acceptance.";
            public List<string> checks = new List<string>();
            public List<CommandSceneEvidence> scenes = new List<CommandSceneEvidence>();
        }
        [Serializable] private sealed class CommandSceneEvidence
        {
            public string id, rejectedReason;
            public int population, samples, groundedSamples, westCrossed, eastCrossed, westReturned, eastReturned;
            public int westTotal, eastTotal, maximumEastWaiting, completedFields, westUphillReached, eastUphillReached;
            public float maxGroundError, pauseMaxDisplacement, holdCentroidDisplacement, redirectProgress;
            public float ascentSeconds, retreatSeconds;
            public bool routeAdvanced, retreatReached, completed;
            public Vector3 westCentroid, eastCentroid, westAscentCenter, eastAscentCenter, westRetreatCenter, eastRetreatCenter;
        }
        private sealed class CommandWorld
        {
            public readonly AgentData[] agents;
            public readonly int[] teams, hp;
            public readonly float[] words;
            public CommandWorld(int count)
            {
                agents = new AgentData[count]; teams = new int[count]; hp = new int[count];
                words = new float[count * (1 + CombatBufferSet.CongestionWordsPerAgent)];
            }
            public Vector3 Center(int team)
            {
                Vector3 sum = Vector3.zero; int count = 0;
                for (int i = 0; i < agents.Length; i++) if (teams[i] == team && hp[i] > 0) { sum += agents[i].position; count++; }
                Require(count > 0, "Command probe army disappeared."); return sum / count;
            }
            public float Wait(int i) => words[agents.Length + i * CombatBufferSet.CongestionWordsPerAgent + 5];
        }
        private void CommandCheck(bool ok, string label)
        {
            Require(ok, "Command validation: " + label);
            if (!report.commands.checks.Contains(label)) report.commands.checks.Add(label);
        }
        private CommandWorld ReadCommandWorld(CommandSceneEvidence evidence)
        {
            var world = new CommandWorld(manager.UnitTypes.TotalAgentCount);
            manager.Buffers.agentBuffer.GetData(world.agents);
            manager.Buffers.combatBuffers.teamIdBuffer.GetData(world.teams);
            manager.Buffers.combatBuffers.hpReadBuffer.GetData(world.hp);
            manager.Buffers.combatBuffers.engagementSlotAssignmentBuffer.GetData(world.words);
            evidence.samples++;
            int eastWaiting = 0;
            for (int i = 0; i < world.agents.Length; i++)
            {
                Require(world.hp[i] > 0, "Noncombat command validation lost a unit.");
                Vector3 p = world.agents[i].position;
                Require(!float.IsNaN(p.x + p.y + p.z) && !float.IsInfinity(p.x + p.y + p.z), "Nonfinite command position.");
                if (world.teams[i] == 2 && world.Wait(i) > 0) eastWaiting++;
                if (manager.TerrainSurface == null) continue;
                var xz = new Vector2(p.x, p.z);
                Require(manager.TerrainNavigation.IsWalkable(xz) && manager.TerrainSurface.TrySample(xz, out _), "Agent entered an unwalkable terrain cell.");
                manager.TerrainSurface.TrySample(xz, out var ground);
                evidence.maxGroundError = Mathf.Max(evidence.maxGroundError, Mathf.Abs(p.y - ground.Position.y));
                evidence.groundedSamples++;
            }
            Require(evidence.maxGroundError < .01f, "Agent detached from terrain.");
            evidence.maximumEastWaiting = Mathf.Max(evidence.maximumEastWaiting, eastWaiting);
            evidence.westCentroid = world.Center(0);
            if (manager.TerrainSurface != null) evidence.eastCentroid = world.Center(2);
            return world;
        }
        private static float CommandDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private static float CommandProgress(CommandWorld before, CommandWorld after, int team, Vector3 target)
        {
            float sum = 0; int count = 0;
            for (int i = 0; i < before.agents.Length; i++) if (before.teams[i] == team)
            {
                Vector3 direction = target - before.agents[i].position; direction.y = 0;
                sum += Vector3.Dot(after.agents[i].position - before.agents[i].position, direction.normalized); count++;
            }
            return sum / Mathf.Max(1, count);
        }
        private IEnumerator CommandPauseAppend(CommandSceneEvidence evidence, Vector3 appended)
        {
            Stage(evidence.id + "-pause-and-append");
            controller.PauseBattle(); yield return null; yield return null;
            var before = ReadCommandWorld(evidence);
            var active = controller.GetArmy(0).currentOrder;
            CommandCheck(controller.IssueMoveOrder(0, appended, true), evidence.id + ": append accepted while paused");
            yield return new WaitForSecondsRealtime(.6f);
            var after = ReadCommandWorld(evidence);
            for (int i = 0; i < before.agents.Length; i++)
            {
                evidence.pauseMaxDisplacement = Mathf.Max(evidence.pauseMaxDisplacement, Vector3.Distance(before.agents[i].position, after.agents[i].position));
                Require(before.hp[i] == after.hp[i] && before.Wait(i) == after.Wait(i), "Pause changed HP or congestion timers.");
            }
            CommandCheck(!manager.IsBattleRunning && controller.Phase == WarSandboxBattlePhase.Paused &&
                evidence.pauseMaxDisplacement < .00001f && controller.GetMoveRoutePointCount(0) == 2 &&
                controller.GetArmy(0).currentOrder.target == active.target, evidence.id + ": pause freezes units and timers without activating appended waypoint");
            controller.StartOrResumeBattle();
            CommandCheck(manager.IsBattleRunning && controller.GetMoveRoutePointCount(0) == 2 &&
                controller.GetArmy(0).currentOrder.target == active.target, evidence.id + ": resume preserves current leg and queued waypoint");
        }
        private IEnumerator CommandHold(CommandSceneEvidence evidence, int team)
        {
            Stage(evidence.id + "-hold-" + team);
            CommandCheck(controller.IssueOrder(ArmyOrder.Hold(team)), evidence.id + ": Hold accepted " + team);
            yield return new WaitForSecondsRealtime(.6f);
            var before = ReadCommandWorld(evidence);
            yield return new WaitForSecondsRealtime(1);
            var after = ReadCommandWorld(evidence);
            evidence.holdCentroidDisplacement = Mathf.Max(evidence.holdCentroidDisplacement, CommandDistance(before.Center(team), after.Center(team)));
            for (int i = 0; i < after.agents.Length; i++) if (after.teams[i] == team)
                Require(after.Wait(i) == 0, "Hold retained a congestion timer.");
            CommandCheck(controller.GetArmy(team).currentOrder.type == ArmyOrderType.Hold && controller.GetMoveRoutePointCount(team) == 0 &&
                evidence.holdCentroidDisplacement < .75f, evidence.id + ": Hold clears route/waits and stops old-route travel " + team);
        }
        private IEnumerator RunCommandsSmoke()
        {
            report.commands = new CommandEvidence();
            var audio = WarSandboxAudio.Ensure();
            Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Wrong isolated command-probe settings.");
            audio.SetMuted(true);
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == "launch-open", "Wrong initial command battlefield.");
            BindBattle(false); VerifyFullStrength();
            yield return RunFlatCommands();
            yield return Leave(false); VerifySources(); VerifyPlans();
            yield return Enter("launch-mountain", true); VerifyFullStrength();
            yield return RunMountainCommands();
            yield return Leave(false); VerifySources(); VerifyPlans();
            Require(!System.IO.File.Exists(settingsFile), "Command probe wrote settings.");
            Stage("complete");
        }
        private IEnumerator RunFlatCommands()
        {
            var e = new CommandSceneEvidence { id = "flat", population = manager.UnitTypes.TotalAgentCount };
            report.commands.scenes.Add(e); Require(e.population == 512, "Changed flat population.");
            Require(controller.IssueOrder(ArmyOrder.Hold(1)), controller.CommandError);
            Vector3 first = new Vector3(-25, 0, 0), second = new Vector3(-25, 0, -28);
            Require(controller.IssueMoveOrder(0, first, false), controller.CommandError);
            yield return new WaitForSecondsRealtime(1);
            yield return CommandPauseAppend(e, second);
            Stage("flat-auto-next-waypoint");
            float deadline = Time.realtimeSinceStartup + 55;
            while (controller.GetMoveRoutePointCount(0) == 2 && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(.25f); ReadCommandWorld(e);
                Require(manager.IsBattleRunning, "Flat waypoint run stopped.");
            }
            e.routeAdvanced = controller.GetMoveRoutePointCount(0) == 1 && controller.GetArmy(0).currentOrder.target == second;
            CommandCheck(e.routeAdvanced, "flat: telemetry activates next waypoint");
            yield return new WaitForSecondsRealtime(2);
            var before = ReadCommandWorld(e);
            Vector3 replacement = new Vector3(-85, 0, -50);
            Require(controller.IssueMoveOrder(0, replacement, false), controller.CommandError);
            CommandCheck(controller.GetMoveRoutePointCount(0) == 1 && controller.GetArmy(0).currentOrder.target == replacement,
                "flat: replacement discards old route");
            Stage("flat-retarget");
            yield return new WaitForSecondsRealtime(4);
            e.redirectProgress = CommandProgress(before, ReadCommandWorld(e), 0, replacement);
            CommandCheck(e.redirectProgress > .5f, "flat: army moves along replacement direction");
            yield return CommandHold(e, 0);
            Stage("flat-retreat");
            Require(controller.IssueOrder(ArmyOrder.Retreat(0)), controller.CommandError);
            Vector3 home = controller.GetArmy(0).spawnCenter;
            CommandCheck(CommandDistance(controller.GetArmy(0).currentOrder.target, home) < .001f && controller.GetMoveRoutePointCount(0) == 0,
                "flat: Retreat replaces Hold with authored home target");
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 50)
            {
                yield return new WaitForSecondsRealtime(.25f);
                e.retreatSeconds = Time.realtimeSinceStartup - started;
                if (CommandDistance(ReadCommandWorld(e).Center(0), home) <= 10) { e.retreatReached = true; break; }
            }
            CommandCheck(e.retreatReached, "flat: retreat returns army centroid to home vicinity");
            yield return Click("reset"); VerifyFullStrength();
            e.completed = true; WriteReport();
        }
        private void CountMountainGates(CommandWorld world, CommandSceneEvidence e)
        {
            e.westCrossed = e.eastCrossed = e.westReturned = e.eastReturned = e.westTotal = e.eastTotal = 0;
            for (int i = 0; i < world.agents.Length; i++)
            {
                Vector3 p = world.agents[i].position;
                if (world.teams[i] == 0)
                {
                    e.westTotal++;
                    if (p.x > -188 && p.y > 34) e.westCrossed++;
                    if (p.x < -230 && p.y < 20) e.westReturned++;
                }
                if (world.teams[i] == 2)
                {
                    e.eastTotal++;
                    if (p.x < 85 && p.y > 35) e.eastCrossed++;
                    if (p.x > 110 && p.y < 30) e.eastReturned++;
                }
            }
        }
        private IEnumerator RunMountainCommands()
        {
            var e = new CommandSceneEvidence { id = "mountain", population = manager.UnitTypes.TotalAgentCount };
            report.commands.scenes.Add(e); Require(e.population == 384, "Changed mountain population.");
            for (int team = 0; team < 3; team++) Require(controller.IssueOrder(ArmyOrder.Hold(team)), controller.CommandError);
            Vector3 westFirst = new Vector3(-220, 0, 0), westGoal = new Vector3(-165, 0, 8), eastGoal = new Vector3(45, 0, 0);
            Require(controller.IssueMoveOrder(0, westFirst, false), controller.CommandError);
            Require(controller.IssueMoveOrder(2, new Vector3(205, 0, 0), false), controller.CommandError);
            Stage("mountain-initial-directions");
            var before = ReadCommandWorld(e);
            yield return new WaitForSecondsRealtime(3);
            var outward = ReadCommandWorld(e);
            CommandCheck(outward.Center(0).x > before.Center(0).x + .5f && outward.Center(2).x > before.Center(2).x + .5f,
                "mountain: both armies follow initial commands");
            Require(controller.IssueMoveOrder(2, eastGoal, false), controller.CommandError);
            yield return new WaitForSecondsRealtime(3);
            e.redirectProgress = CommandProgress(outward, ReadCommandWorld(e), 2, eastGoal);
            CommandCheck(e.redirectProgress > .5f, "mountain: opposite retarget replaces old terrain direction");
            yield return CommandPauseAppend(e, westGoal);
            var old = controller.GetArmy(0).currentOrder; int points = controller.GetMoveRoutePointCount(0);
            var oldBuffers = manager.Buffers;
            bool replaceRejected = !controller.IssueMoveOrder(0, new Vector3(-80, 0, 73), false);
            bool appendRejected = !controller.IssueMoveOrder(0, new Vector3(-80, 0, 73), true);
            e.rejectedReason = controller.CommandError;
            CommandCheck(replaceRejected && appendRejected && !string.IsNullOrEmpty(e.rejectedReason) &&
                controller.GetArmy(0).currentOrder.target == old.target && controller.GetMoveRoutePointCount(0) == points &&
                ReferenceEquals(oldBuffers, manager.Buffers), "mountain: rejected cliff replacement/append preserve live order, route and allocation");
            Stage("mountain-ascent-all-agents");
            float started = Time.realtimeSinceStartup, nextLog = started;
            bool reached = false;
            while (Time.realtimeSinceStartup - started < 150)
            {
                yield return new WaitForSecondsRealtime(.5f);
                var world = ReadCommandWorld(e); CountMountainGates(world, e);
                e.ascentSeconds = Time.realtimeSinceStartup - started;
                e.routeAdvanced = controller.GetMoveRoutePointCount(0) == 1 && CommandDistance(controller.GetArmy(0).currentOrder.target, westGoal) < .001f;
                Require(manager.IsBattleRunning, "Mountain ascent unexpectedly stopped.");
                reached = e.routeAdvanced && e.westCrossed == 128 && e.eastCrossed == 128 &&
                    CommandDistance(world.Center(0), westGoal) <= 10 && CommandDistance(world.Center(2), eastGoal) <= 10;
                if (Time.realtimeSinceStartup >= nextLog || reached)
                {
                    Debug.Log("COMMAND_ASCENT seconds=" + e.ascentSeconds + " west=" + e.westCrossed + "/128 east=" + e.eastCrossed + "/128");
                    WriteReport(); nextLog = Time.realtimeSinceStartup + 10;
                }
                if (reached) break;
            }
            CommandCheck(reached, "mountain: all 128 west and 128 east units cross uphill gates and both centroids approach goals");
            e.westUphillReached = e.westCrossed; e.eastUphillReached = e.eastCrossed;
            e.westAscentCenter = e.westCentroid; e.eastAscentCenter = e.eastCentroid;
            cameraTarget = new Vector3(-50, 25, 0);
            renderCamera.transform.position = new Vector3(-50, 260, -250); renderCamera.transform.LookAt(cameraTarget);
            yield return CaptureWorld("mountain-all-uphill.png");
            yield return CommandHold(e, 0); yield return CommandHold(e, 2);
            Stage("mountain-retreat-all-agents");
            Require(controller.IssueOrder(ArmyOrder.Retreat(0)) && controller.IssueOrder(ArmyOrder.Retreat(2)), controller.CommandError);
            Vector3 westHome = controller.GetArmy(0).spawnCenter, eastHome = controller.GetArmy(2).spawnCenter;
            CommandCheck(controller.GetMoveRoutePointCount(0) == 0 && controller.GetMoveRoutePointCount(2) == 0 &&
                CommandDistance(controller.GetArmy(0).currentOrder.target, westHome) < .001f &&
                CommandDistance(controller.GetArmy(2).currentOrder.target, eastHome) < .001f, "mountain: Retreat clears routes and uses grounded home targets");
            started = Time.realtimeSinceStartup; nextLog = started;
            while (Time.realtimeSinceStartup - started < 150)
            {
                yield return new WaitForSecondsRealtime(.5f);
                var world = ReadCommandWorld(e); CountMountainGates(world, e);
                e.retreatSeconds = Time.realtimeSinceStartup - started;
                Require(manager.IsBattleRunning, "Mountain retreat unexpectedly stopped.");
                e.retreatReached = e.westReturned == 128 && e.eastReturned == 128 &&
                    CommandDistance(world.Center(0), westHome) <= 10 && CommandDistance(world.Center(2), eastHome) <= 10;
                if (Time.realtimeSinceStartup >= nextLog || e.retreatReached)
                {
                    Debug.Log("COMMAND_RETREAT seconds=" + e.retreatSeconds + " west=" + e.westReturned + "/128 east=" + e.eastReturned + "/128");
                    WriteReport(); nextLog = Time.realtimeSinceStartup + 10;
                }
                if (e.retreatReached) break;
            }
            CommandCheck(e.retreatReached, "mountain: all 256 commanded units descend and both army centroids return home");
            e.westRetreatCenter = e.westCentroid; e.eastRetreatCenter = e.eastCentroid;
            e.completedFields = manager.TerrainCompletedFields;
            yield return CaptureWorld("mountain-all-returned.png");
            yield return Click("reset"); VerifyFullStrength();
            var reset = ReadCommandWorld(e);
            for (int i = 0; i < reset.agents.Length; i++) Require(reset.Wait(i) == 0, "Restart retained wait timers.");
            CommandCheck(controller.GetMoveRoutePointCount(0) == 0 && controller.GetMoveRoutePointCount(2) == 0 &&
                !manager.IsBattleRunning, "mountain: restart clears old routes and waits");
            e.completed = true; WriteReport();
        }
    }
}
#endif
