#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxEntrySmoke
    {
        private IEnumerator RunPlanCycle(bool seed)
        {
            string directory = ArgumentValue("--war-sandbox-plan-directory");
            Require(!string.IsNullOrEmpty(directory), "Plan cycle requires an isolated plan directory.");
            Require(seed ? !Directory.Exists(directory) : Directory.Exists(directory), "Plan cycle directory precondition failed.");
            string prefix = seed ? "seed" : "reload";
            stage = prefix + "-ui-entry";
            yield return Screenshot("catalog-1280.png");
            Screen.SetResolution(560, 800, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
            yield return Screenshot("catalog-560.png");
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
            Require(session.TryEnterBattlefield("open-battle", false, out var error), error); yield return WaitForLoad();
            var controller = session.Controller; var manager = controller.manager;
            var editor = manager.GetComponent<WarSandboxRuntimeDeployment>(); var hud = manager.GetComponent<WarSandboxDeploymentHUD>();
            editor.PlanStore = new WarSandboxLocalPlanStore(directory);
            if (seed)
            {
                yield return ClickUI("edit");
                Require(editor.IsEditing, "uGUI edit action did not open the draft.");
                for (int i = 0; i < editor.Draft.Count; i++)
                {
                    var value = editor.Draft[i]; value.count = 64; value.manualSize = Vector3.zero; value.density = 0.5f; value.aspect = 2;
                    value.center = value.teamId == 0 ? new Vector3(i % 2 == 0 ? -50 : -28, 0, 0) :
                        value.teamId == 1 ? new Vector3(i % 2 == 0 ? 50 : 28, 0, 0) : new Vector3(i % 2 == 0 ? -10 : 10, 0, 78);
                    editor.Draft.Set(i, value);
                }
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Screenshot("deployment-1280.png");
                Screen.SetResolution(1920, 1080, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
                yield return Screenshot("deployment-1920.png");
                Screen.SetResolution(560, 800, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
                yield return Screenshot("deployment-560.png");
                yield return ClickUI("tab-map"); yield return Screenshot("deployment-map-560.png");
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
                yield return ClickUI("deployment-plans");
                SetUIField("plans-slot", "A"); SetUIField("plans-name", "歼灭基线 A");
                yield return ClickUI("plans-save"); Require(editor.PlanStore.Exists("A"), "uGUI did not save A.");
                var changed = editor.Draft.Snapshot(); changed[0].count = 32;
                var rules = editor.Draft.Rules; rules.gameMode = WarSandboxGameMode.ControlPoint;
                rules.controlPointCenter = Vector3.zero; rules.controlPointRadius = 20; rules.controlPointCaptureSeconds = 8;
                editor.Draft.Replace(changed, rules); yield return new WaitForSecondsRealtime(0.3f);
                SetUIField("plans-slot", "B"); SetUIField("plans-name", "据点实验 B");
                yield return ClickUI("plans-save"); Require(editor.PlanStore.Exists("B"), "uGUI did not save B.");
                byte[] before = File.ReadAllBytes(Path.Combine(directory, "B.json"));
                yield return ClickUI("plans-save"); yield return Screenshot("overwrite-confirmation.png");
                yield return ClickUI("plans-confirm-no");
                Require(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(Path.Combine(directory, "B.json"))), "Cancelled overwrite changed the saved plan.");
                yield return Screenshot("plans-1280.png");
                Screen.SetResolution(560, 800, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
                yield return Screenshot("plans-560.png");
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed); yield return new WaitForSecondsRealtime(0.5f);
                editor.CancelEditing();
                checks.Add("uGUI: edit, saved A/B, Unicode names, cancelled overwrite preserves file, responsive captures");
            }
            var initialBuffers = manager.Buffers;
            Require(session.TryReturnToMenu(false, out error), error); yield return WaitForLoad();
            Require(!initialBuffers.IsAllocated, "Initial scene resources retained.");
            foreach (string slot in new[] { "A", "B" })
            {
                stage = prefix + "-natural-" + slot;
                Require(session.TryEnterBattlefield("open-battle", false, out error), error); yield return WaitForLoad();
                controller = session.Controller; manager = controller.manager;
                editor = manager.GetComponent<WarSandboxRuntimeDeployment>(); hud = manager.GetComponent<WarSandboxDeploymentHUD>();
                editor.PlanStore = new WarSandboxLocalPlanStore(directory);
                var source = manager.scenarioConfig; string sourceJson = JsonUtility.ToJson(source);
                var unitJson = new string[source.unitTypes.Length];
                for (int i = 0; i < unitJson.Length; i++) unitJson[i] = JsonUtility.ToJson(source.unitTypes[i]) + JsonUtility.ToJson(source.unitTypes[i].spawnConfig);
                byte[] diskBefore = File.ReadAllBytes(Path.Combine(directory, slot + ".json"));
                yield return ClickUI("edit"); yield return ClickUI("deployment-plans");
                Require(editor.PlanStore.TryListSlots(out var slots, out error), error);
                int index = Array.IndexOf(slots, slot); Require(index >= 0, "Missing plan " + slot);
                yield return ClickUI("plan-item-" + index); yield return ClickUI("plans-load"); yield return ClickUI("plans-confirm-yes");
                Require(editor.Draft[0].count == (slot == "A" ? 64 : 32), "Plan UI did not load " + slot);
                yield return ClickUI("deployment-apply");
                Require(!editor.IsEditing, "Apply UI did not leave edit mode.");
                int expected = slot == "A" ? 384 : 352;
                var mode = slot == "A" ? WarSandboxGameMode.Annihilation : WarSandboxGameMode.ControlPoint;
                Require(manager.UnitTypes.TotalAgentCount == expected && controller.gameMode == mode, "Wrong loaded plan deployment/rules.");
                var initial = new int[controller.ArmyCount];
                for (int team = 0; team < initial.Length; team++) initial[team] = controller.GetArmy(team).initialUnitCount;
                var camera = manager.cullingCamera; camera.transform.position = new Vector3(0, 160, -120); camera.transform.LookAt(new Vector3(0, 0, 20));
                yield return Screenshot(slot + "-setup-1280.png");
                yield return ClickUI("start");
                float started = Time.realtimeSinceStartup, nextLog = started;
                while (!controller.BattleResult.valid && Time.realtimeSinceStartup - started < 180f)
                {
                    Require(errors.Count == 0, string.Join("\n", errors));
                    if (Time.realtimeSinceStartup >= nextLog)
                    {
                        Debug.Log("PLAN_CYCLE " + prefix + " " + slot + " simulation=" + controller.TelemetrySnapshot.battleSeconds.ToString("F1"));
                        nextLog = Time.realtimeSinceStartup + 20;
                    }
                    yield return null;
                }
                var result = controller.BattleResult;
                Require(result.valid && result.battleSeconds > 0 && !manager.IsBattleRunning, "Plan did not settle naturally: " + slot);
                var survivors = new int[initial.Length];
                for (int team = 0; team < initial.Length; team++)
                { survivors[team] = controller.GetAliveUnitCount(team); Require(survivors[team] >= 0 && survivors[team] <= initial[team], "Invalid survivor count."); }
                if (result.phase == WarSandboxBattlePhase.Draw)
                    Require(result.winnerTeamId == -1 && Array.TrueForAll(survivors, n => n == 0), "Invalid draw result.");
                else
                {
                    Require(result.winnerTeamId >= 0 && result.winnerTeamId < survivors.Length && survivors[result.winnerTeamId] > 0, "Winner is not a surviving army.");
                    if (result.victoryReason == WarSandboxVictoryReason.Annihilation)
                        for (int team = 0; team < survivors.Length; team++) Require(team == result.winnerTeamId || survivors[team] == 0, "Annihilation left an enemy alive.");
                    else Require(mode == WarSandboxGameMode.ControlPoint && controller.ControlPointCaptureProgress >= 1 && controller.ControlPointOwnerTeamId == result.winnerTeamId, "Invalid capture victory.");
                }
                battles.Add(new FullBattle { entryId = prefix + "-plan-" + slot, mode = mode.ToString(), phase = result.phase.ToString(),
                    victoryReason = result.victoryReason.ToString(), winnerTeamId = result.winnerTeamId, simulationSeconds = result.battleSeconds,
                    realSeconds = Time.realtimeSinceStartup - started, initial = initial, survivors = survivors });
                yield return Screenshot(slot + "-settlement.png");
                string settled = JsonUtility.ToJson(result);
                Require(!controller.IssueOrder(ArmyOrder.Attack(0)), "Terminal battle accepted an ordinary order.");
                controller.StartOrResumeBattle(); yield return new WaitForSecondsRealtime(0.3f);
                Require(!manager.IsBattleRunning && JsonUtility.ToJson(controller.BattleResult) == settled, "Finished battle mutated.");
                controller.ResetBattle();
                Require(controller.Phase == WarSandboxBattlePhase.Setup && !controller.BattleResult.valid && controller.gameMode == mode, "Reset retained result or lost rules.");
                for (int team = 0; team < initial.Length; team++)
                    Require(controller.GetAliveUnitCount(team) == initial[team] && !controller.GetArmy(team).hasOrder && controller.GetMoveRoutePointCount(team) == 0, "Reset retained casualties/orders.");
                Require(controller.StartDefaultBattle(), "Full-strength restart failed."); yield return new WaitForSecondsRealtime(0.6f);
                Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Restart reused settlement.");
                controller.ResetBattle();
                Require(JsonUtility.ToJson(source) == sourceJson, "Source scenario changed.");
                for (int i = 0; i < unitJson.Length; i++)
                    Require(JsonUtility.ToJson(source.unitTypes[i]) + JsonUtility.ToJson(source.unitTypes[i].spawnConfig) == unitJson[i], "Source template changed.");
                Require(Convert.ToBase64String(diskBefore) == Convert.ToBase64String(File.ReadAllBytes(Path.Combine(directory, slot + ".json"))), "Battle rewrote plan file.");
                var owned = manager.scenarioConfig; var buffers = manager.Buffers;
                Require(session.TryReturnToMenu(false, out error), error); yield return WaitForLoad();
                Require(!buffers.IsAllocated && owned == null && session.Controller == null, "Return retained runtime configs/GPU.");
                Require(FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length == 0, "Simulation leaked into menu.");
                checks.Add(prefix + " " + slot + ": UI load/apply/start, natural result, frozen settlement, full-strength restart, unchanged source/files, GPU/config release");
            }
            // A genuinely different catalog entry must still load its own defaults after plan play.
            Require(session.TryEnterBattlefield("walled-point", false, out error), error); yield return WaitForLoad();
            controller = session.Controller;
            Require(controller.gameMode == WarSandboxGameMode.ControlPoint && controller.staticObstaclesEnabled && controller.manager.UnitTypes.TotalAgentCount == 110000,
                "Plan leaked into a different battlefield or changed the original 110k deployment.");
            var finalBuffers = controller.manager.Buffers;
            Require(session.TryReturnToMenu(false, out error), error); yield return WaitForLoad();
            Require(!finalBuffers.IsAllocated, "Last battlefield GPU allocation retained.");
            checks.Add("different battlefield retained its original rules and 110k deployment, then released GPU");
            stage = prefix + "-plan-cycle-complete";
        }
        private IEnumerator ClickUI(string name)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            Button found = null;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name && button.isActiveAndEnabled) { found = button; break; }
            Require(found != null && found.interactable, "UI button is missing/disabled: " + name);
            found.onClick.Invoke(); yield return new WaitForSecondsRealtime(0.25f);
        }
        private void SetUIField(string name, string value)
        {
            foreach (var field in FindObjectsByType<InputField>(FindObjectsSortMode.None))
                if (field.name == name && field.isActiveAndEnabled) { field.text = value; return; }
            Require(false, "UI input is missing: " + name);
        }
    }
}
#endif
