#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxEntrySmoke
    {
        private IEnumerator RunLocalPlans(bool seed)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-plan-cycle") >= 0)
            {
                yield return RunPlanCycle(seed);
                yield break;
            }
            stage = seed ? "plan-seed" : "plan-reload";
            string directory = ArgumentValue("--war-sandbox-plan-directory");
            Require(!string.IsNullOrEmpty(directory), "Plan smoke needs --war-sandbox-plan-directory.");
            var sourceCatalog = session.catalog;
            var entry = sourceCatalog.entries[0];
            Require(session.TryEnterBattlefield(entry.id, false, out var error), error);
            yield return WaitForLoad();
            var controller = session.Controller; var manager = controller.manager;
            var editor = manager.GetComponent<WarSandboxRuntimeDeployment>();
            editor.PlanStore = new WarSandboxLocalPlanStore(directory);
            var sourceScenario = manager.scenarioConfig;
            string sourceJson = JsonUtility.ToJson(manager.scenarioConfig);
            var originalUnits = new string[sourceScenario.unitTypes.Length];
            for (int i = 0; i < originalUnits.Length; i++)
                originalUnits[i] = JsonUtility.ToJson(sourceScenario.unitTypes[i]) + JsonUtility.ToJson(sourceScenario.unitTypes[i].spawnConfig);
            if (seed)
            {
                Require(!Directory.Exists(directory), "Plan smoke seed directory already exists.");
                Require(editor.TryBeginEdit(false, out error), error);
                for (int i = 0; i < editor.Draft.Count; i++)
                {
                    var value = editor.Draft[i]; value.count = 64; value.manualSize = Vector3.zero; value.density = 0.5f; value.aspect = 2;
                    value.center = value.teamId == 0 ? new Vector3(i % 2 == 0 ? -50 : -28, 0, 0) :
                        value.teamId == 1 ? new Vector3(i % 2 == 0 ? 50 : 28, 0, 0) :
                        new Vector3(i % 2 == 0 ? -10 : 10, 0, i < 4 ? 58 : 78);
                    editor.Draft.Set(i, value);
                }
                Require(editor.TrySavePlan("A", "平面基线 A", false, out error), error);
                var changed = editor.Draft.Snapshot(); changed[0].count = 32;
                var changedRules = editor.Draft.Rules; changedRules.gameMode = WarSandboxGameMode.ControlPoint;
                changedRules.controlPointCenter = new Vector3(0, 0, 0); changedRules.controlPointRadius = 20; changedRules.controlPointCaptureSeconds = 8;
                editor.Draft.Replace(changed, changedRules);
                Require(editor.TrySavePlan("B", "据点实验 B", false, out error), error);
                manager.GetComponent<WarSandboxDeploymentHUD>().OpenPlanLibrary(); yield return null;
                yield return Screenshot("plans-desktop.png");
                Screen.SetResolution(560, 800, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(0.5f); yield return Screenshot("plans-narrow.png");
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(0.5f);
                Require(editor.TryLoadPlan("A", out error), error); Require(editor.Draft[0].count == 64, "Plan A did not reload.");
                Require(editor.TryApply(out error), error); yield return null;
                Require(manager.UnitTypes.TotalAgentCount == 384 && controller.gameMode == WarSandboxGameMode.Annihilation, "Plan A was not applied.");
                Require(editor.TryBeginEdit(false, out error), error); Require(editor.TryLoadPlan("B", out error), error);
                Require(editor.TryApply(out error), error); yield return null;
                Require(manager.UnitTypes.TotalAgentCount == 352 && controller.gameMode == WarSandboxGameMode.ControlPoint, "Plan B was not applied.");
                Require(File.Exists(Path.Combine(directory, "A.json")) && File.Exists(Path.Combine(directory, "B.json")), "A/B files were not written.");
                Require(JsonUtility.ToJson(sourceScenario) == sourceJson && JsonUtility.ToJson(manager.scenarioConfig) != sourceJson,
                    "Plan smoke did not create an independent runtime deployment.");
                var buffers = manager.Buffers;
                Require(session.TryReturnToMenu(false, out error), error); yield return WaitForLoad();
                Require(!buffers.IsAllocated, "Plan seed left its GPU allocation alive.");
                checks.Add("plan seed: A/B saved, loaded, applied, source runtime isolated");
            }
            else
            {
                Require(Directory.Exists(directory), "Plan smoke reload directory is missing.");
                Require(editor.TryBeginEdit(false, out error), error); Require(editor.TryLoadPlan("B", out error), error);
                Require(editor.TryApply(out error), error); yield return null;
                Require(manager.UnitTypes.TotalAgentCount == 352 && controller.gameMode == WarSandboxGameMode.ControlPoint,
                    "Plan B did not survive process restart.");
                Require(controller.StartDefaultBattle(), "Reloaded plan could not start a battle.");
                yield return new WaitForSecondsRealtime(0.8f);
                Require(manager.IsBattleRunning, "Reloaded plan did not remain playable.");
                Require(session.TryReturnToMenu(true, out error), error); yield return WaitForLoad();
                checks.Add("plan reload: fresh process loaded B and started battle");
            }
            Require(JsonUtility.ToJson(sourceScenario) == sourceJson, "Plan smoke rewrote the source scenario.");
            for (int i = 0; i < originalUnits.Length; i++)
                Require(JsonUtility.ToJson(sourceScenario.unitTypes[i]) + JsonUtility.ToJson(sourceScenario.unitTypes[i].spawnConfig) == originalUnits[i],
                    "Plan smoke rewrote a source unit or spawn config.");
        }

        private string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
#endif
