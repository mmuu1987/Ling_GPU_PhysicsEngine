#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class PlayabilityEvidence
        {
            public bool settingsPauseAndResume, settingsPersisted, manualEndWithoutWinner;
            public float volume;
            public bool muted;
            public string settingsHash;
            public int playedCues, commandCues, startCues, pointCues, finishCues;
            public WarSandboxBattleResult manualResult;
        }
        [Serializable] private sealed class PlayabilityReceipt
        {
            public bool passed;
            public string runId, processStartedUtc, buildGuid, planHash, settingsHash;
            public int processId;
        }
        private const string PlayabilityReceiptName = "playability-receipt.json", FourArmySlot = "four-armies";
        private string settingsFile;
        private bool IsPlayabilityRun => report.phase == "playability-seed" || report.phase == "playability-reload";

        private void InitializePlayabilitySettings(string defaultPlans)
        {
            settingsFile = AbsoluteArgument("--war-sandbox-settings-file=");
            Require(Disjoint(settingsFile, Application.persistentDataPath) && Disjoint(settingsFile, defaultPlans) &&
                Disjoint(settingsFile, output) && Disjoint(settingsFile, planDirectory), "Validation settings must be isolated from player data and evidence.");
            Require(report.phase == "playability-seed" ? !File.Exists(settingsFile) && !Directory.Exists(settingsFile) : File.Exists(settingsFile),
                "Seed needs a fresh settings file; reload needs the saved file.");
            report.playability = new PlayabilityEvidence();
        }

        private IEnumerator RunPlayability()
        {
            bool seed = report.phase == "playability-seed";
            string expectedSettingsHash = null;
            if (!seed)
            {
                var receipt = JsonUtility.FromJson<PlayabilityReceipt>(File.ReadAllText(Path.Combine(planDirectory, PlayabilityReceiptName)));
                Require(receipt != null && receipt.passed && receipt.buildGuid == report.buildGuid && receipt.runId != report.runId &&
                    (receipt.processId != report.processId || receipt.processStartedUtc != report.processStartedUtc), "Reload needs a passed receipt from another process of this build.");
                planHashes.Add(FourArmySlot, receipt.planHash); expectedSettingsHash = receipt.settingsHash;
                VerifyPlans(); Require(HashFile(settingsFile) == expectedSettingsHash, "Saved settings changed between processes.");
                report.crossProcess = true;
            }
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == "launch-open", "Normal startup did not enter the light battle.");
            BindBattle(false); VerifyFullStrength();
            var audio = WarSandboxAudio.Ensure();
            Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Isolated settings did not load.");
            if (seed)
            {
                Require(audio.Settings.volume == WarSandboxAudioSettings.Default.volume && !audio.Settings.muted, "Fresh settings are not defaults.");
                Stage("settings-and-manual-end");
                yield return Click("nav-settings");
                Require(session.SettingsOpen && controller.Phase == WarSandboxBattlePhase.Setup, "Setup changed when opening settings.");
                SetSettingsVolume(.35f);
                yield return Click("settings-preview");
                Require(audio.Source.isPlaying && audio.PlayedCues > 0, "Preview did not play on the shared source.");
                yield return Click("settings-save"); VerifyFullStrength();
                yield return Click("start");
                yield return new WaitForSecondsRealtime(.5f);
                yield return Click("hold");
                Require(audio.RequestCount(WarSandboxSoundCue.Command) > 0, "Command feedback was not emitted.");
                int startCues = audio.RequestCount(WarSandboxSoundCue.Start);
                yield return Click("nav-settings");
                Require(session.SettingsOpen && controller.Phase == WarSandboxBattlePhase.Paused && !manager.IsBattleRunning && Time.timeScale == 1,
                    "Settings did not pause the battle independently of time scale.");
                Require(!controller.IssueMoveOrder(0, Vector3.zero, false) && !controller.EndBattle(), "Settings allowed battle commands.");
                SetSettingsVolume(.27f); yield return Click("settings-mute");
                Require(audio.Source.mute && !audio.Source.isPlaying, "Mute did not stop existing sound.");
                yield return Click("settings-save");
                Require(!session.SettingsOpen && manager.IsBattleRunning && audio.RequestCount(WarSandboxSoundCue.Start) == startCues,
                    "Closing settings failed to resume or replayed the start cue.");
                // An already paused battle must stay paused after the same modal workflow.
                yield return Click("start"); yield return Click("nav-settings"); yield return Click("settings-save");
                Require(controller.Phase == WarSandboxBattlePhase.Paused && !manager.IsBattleRunning, "Settings resumed a manually paused battle.");
                report.playability.settingsPauseAndResume = true;
                yield return Click("end-battle"); VerifyResultRows(2, true);
                report.playability.manualResult = controller.BattleResult;
                yield return CapturePresetUI("manual-two-armies-ui.png");
                string frozen = JsonUtility.ToJson(controller.BattleResult);
                Require(!controller.EndBattle(), "Manual settlement ran twice.");
                yield return Click("result-restart");
                Require(manager.IsBattleRunning && !controller.BattleResult.valid && ArmyCounts(false).SequenceEqual(new[] { 256, 256 }), "Result replay did not start at full strength.");
                Require(JsonUtility.ToJson(report.playability.manualResult) == frozen, "Replay mutated the saved result.");
                yield return Click("end-battle");
                yield return Leave(false, "result-menu");
                yield return Click("menu-settings"); yield return Click("settings-mute"); yield return Click("settings-save");
                Require(!audio.Settings.muted, "Menu settings failed to unmute.");

                yield return Enter("launch-three", false);
                yield return NaturalBattle("three-armies"); yield return Leave(false);
                yield return Enter("launch-three", false);
                yield return CreateFourArmyPlan();
                yield return NaturalBattle("four-armies"); yield return Leave(true);
                yield return Enter("launch-point", false);
                yield return NaturalBattle("control-point");
                Require(audio.RequestCount(WarSandboxSoundCue.ControlPoint) > 0 && report.battles.Last().result.victoryReason == WarSandboxVictoryReason.ControlPoint,
                    "The real control-point battle did not emit ownership feedback and a capture victory.");
                yield return Leave(false);
                yield return Click("menu-settings"); yield return Click("settings-mute");
                renderCamera = FindFirstObjectByType<Camera>(); Require(renderCamera != null, "Menu camera missing.");
                CreateCaptureTarget(); yield return CapturePresetUI("settings-ui.png"); ReleaseCamera();
                yield return Click("settings-save");
            }
            else
            {
                Stage("reload-settings-and-four-armies");
                Require(Mathf.Abs(audio.Settings.volume - .27f) < .0001f && audio.Settings.muted, "Saved volume/mute did not survive restart.");
                yield return Click("nav-settings");
                int played = audio.PlayedCues;
                yield return Click("settings-preview");
                Require(audio.PlayedCues == played && !audio.Source.isPlaying, "Muted preview produced sound.");
                yield return CapturePresetUI("settings-ui.png"); yield return Click("settings-save");
                VerifyFullStrength(); yield return Leave(false);
                yield return Enter("launch-three", false); yield return LoadFourArmyPlan();
                yield return Click("start"); yield return new WaitForSecondsRealtime(2);
                yield return Click("end-battle"); VerifyResultRows(4, true);
                report.playability.manualResult = controller.BattleResult;
                yield return CapturePresetUI("manual-four-armies-ui.png");
                string frozen = JsonUtility.ToJson(controller.BattleResult);
                yield return Click("result-edit");
                Require(deployment.IsEditing && deployment.Draft.Snapshot().Sum(e => e.count) == 896, "Result did not return the full prebattle draft.");
                yield return Click("deployment-apply"); VerifyFourArmies();
                Require(JsonUtility.ToJson(report.playability.manualResult) == frozen, "Returning to deployment mutated the result.");
                yield return Leave(true);
                Require(audio.PlayedCues == 0 && HashFile(settingsFile) == expectedSettingsHash, "Reload changed muted settings or played a cue.");
                report.playability.settingsPersisted = true;
            }
            Require(Mathf.Abs(audio.Settings.volume - .27f) < .0001f && audio.Settings.muted, "Final persisted settings differ.");
            Require(FindObjectsByType<WarSandboxAudio>(FindObjectsSortMode.None).Length == 1 && audio.GetComponents<AudioSource>().Length == 1,
                "Scene changes duplicated the shared audio service.");
            report.playability.volume = audio.Settings.volume; report.playability.muted = audio.Settings.muted;
            report.playability.settingsHash = HashFile(settingsFile); report.playability.playedCues = audio.PlayedCues;
            report.playability.commandCues = audio.RequestCount(WarSandboxSoundCue.Command);
            report.playability.startCues = audio.RequestCount(WarSandboxSoundCue.Start);
            report.playability.pointCues = audio.RequestCount(WarSandboxSoundCue.ControlPoint);
            report.playability.finishCues = audio.RequestCount(WarSandboxSoundCue.Finish);
            VerifySources(); VerifyPlans(); Stage("complete");
        }

        private IEnumerator CreateFourArmyPlan()
        {
            yield return Click("edit"); yield return Click("army-add");
            Field("input-count", "128"); Field("input-x", "0"); Field("input-z", "-105");
            yield return Click("field-update");
            Require(deployment.TryValidate(out string error), error);
            yield return Click("deployment-plans"); Field("plans-slot", FourArmySlot); Field("plans-name", "M7.2 四军团结算");
            yield return Click("plans-save");
            Require(deployment.PlanStore.TryLoad(FourArmySlot, out var saved, out error), error);
            VerifyLoadedPlan(saved);
            planHashes.Add(FourArmySlot, HashFile(PlanPath(FourArmySlot)));
            yield return Click("plans-back"); yield return Click("deployment-apply");
            VerifyFourArmies(); VerifyAppliedPlan(saved); report.appliedPlans++;
            yield return CapturePresetUI("four-armies-setup-ui.png");
        }

        private IEnumerator LoadFourArmyPlan()
        {
            yield return Click("edit");
            Require(deployment.PlanStore.TryLoad(FourArmySlot, out var saved, out string loadError), loadError);
            VerifyFailedPlanLoads(saved);
            yield return Click("deployment-plans");
            Require(deployment.PlanStore.TryListSlots(out var slots, out string error), error);
            int index = Array.IndexOf(slots, FourArmySlot); Require(index >= 0, "Four-army plan missing.");
            yield return Click("plan-item-" + index); yield return Click("plans-load"); yield return Click("plans-confirm-yes");
            VerifyLoadedPlan(saved);
            yield return Click("deployment-apply"); VerifyFourArmies(); VerifyAppliedPlan(saved); report.appliedPlans++;
        }

        private void VerifyFourArmies()
        {
            VerifyFullStrength();
            Require(controller.ArmyCount == 4 && ArmyCounts(true).SequenceEqual(new[] { 256, 256, 256, 128 }) && manager.UnitTypes.TotalAgentCount == 896,
                "Four-army deployment lost identities or troops.");
            Require(manager.scenarioConfig != sourceScenario, "Applying the plan did not isolate the authored scene.");
        }

        private void VerifyResultRows(int count, bool manual)
        {
            var result = controller.BattleResult;
            Require(result.valid && result.ArmyCount == count && !manager.IsBattleRunning, "Settlement lost fielded armies or kept running.");
            if (manual)
            {
                Require(result.phase == WarSandboxBattlePhase.Ended && result.victoryReason == WarSandboxVictoryReason.ManualEnd && result.winnerTeamId == -1,
                    "Manual end invented a winner.");
                Require(ResultText("result-title") == "手动结束", "Manual title is misleading.");
                report.playability.manualEndWithoutWinner = true;
            }
            for (int row = 0; row < result.ArmyCount; row++)
            {
                var army = result.GetArmy(row); string id = "result-army-" + army.teamId;
                Require(army.initial == controller.GetArmy(army.teamId).initialUnitCount && army.survivors >= 0 && army.survivors <= army.initial,
                    "Invalid frozen army statistics.");
                Require(ResultText(id) == army.displayName && ResultText(id + "-initial") == army.initial.ToString("N0") &&
                    ResultText(id + "-alive") == army.survivors.ToString("N0") && ResultText(id + "-losses") == army.Casualties.ToString("N0"),
                    "Visible settlement differs from the frozen army statistics.");
            }
        }
        private string ResultText(string id) => FindObjectsByType<Text>(FindObjectsSortMode.None)
            .Single(t => t.isActiveAndEnabled && t.transform.parent.name == id).text;

        private void SetSettingsVolume(float volume)
        {
            var slider = FindObjectsByType<Slider>(FindObjectsSortMode.None).Single(s => s.isActiveAndEnabled && s.name == "settings-volume");
            Require(slider.IsInteractable(), "Volume slider is disabled."); slider.value = volume;
            report.actions.Add("uGUI Slider.value: settings-volume=" + volume.ToString(CultureInfo.InvariantCulture));
        }

        private void WritePlayabilityReceipt()
        {
            var receipt = new PlayabilityReceipt { passed = true, runId = report.runId, processId = report.processId,
                processStartedUtc = report.processStartedUtc, buildGuid = report.buildGuid,
                planHash = planHashes[FourArmySlot], settingsHash = report.playability.settingsHash };
            using (var writer = new StreamWriter(new FileStream(Path.Combine(planDirectory, PlayabilityReceiptName), FileMode.CreateNew)))
                writer.Write(JsonUtility.ToJson(receipt, true));
        }
    }
}
#endif
