#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        private bool IsPresetRun => report.phase == "presets" || report.phase == "presets-ui" || report.phase == "motion-review" || report.phase == "congestion" || report.phase == "commands" || report.phase == "readability" || report.phase == "ranged" || report.phase == "official-catalog";

        private IEnumerator RunPresets()
        {
            if (report.phase == "congestion") { yield return RunCongestionSmoke(); yield break; }
            if (report.phase == "commands") { yield return RunCommandsSmoke(); yield break; }
            if (report.phase == "readability") { yield return RunReadabilitySmoke(); yield break; }
            if (report.phase == "ranged") { yield return RunRangedSmoke(); yield break; }
            bool review = report.phase == "motion-review";
            bool full = report.phase == "presets" || review;
            if (review)
            {
                var audio = WarSandboxAudio.Ensure();
                Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Wrong isolated settings.");
                audio.SetMuted(true); // In-memory only; leave player preferences untouched.
                report.actions.Add("Functional smoke: 30 FPS cap; four small presets only; no 100k/performance claim.");
            }
            var entries = session.catalog.entries;
            string[] expectedIds = { "launch-open", "launch-mountain", "launch-point", "launch-three", "launch-standard" };
            int[] populations = { 512, 384, 512, 768, 100000 };
            Require(entries.Length == expectedIds.Length && session.catalog.defaultEntryId == expectedIds[0], "Wrong launch catalog.");
            // Keep the normal startup path enabled: the player must auto-enter a light Setup battle.
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == expectedIds[0], "First launch did not enter the quick battle.");
            for (int preset = 0; preset < (review ? 4 : entries.Length); preset++)
            {
                var entry = entries[preset];
                Require(entry.id == expectedIds[preset], "Preset identity/order mismatch.");
                Stage("preset-" + entry.id);
                if (preset > 0)
                {
                    yield return Click("card-" + preset + "-enter");
                    yield return WaitForLoad(WarSandboxEntryState.Battle);
                }
                Require(session.CurrentEntryId == entry.id, "Menu entered the wrong preset.");
                BindBattle(entry.terrainSurface != null);
                Require(manager.UnitTypes.TotalAgentCount == populations[preset], "Authored population changed.");
                VerifyFullStrength();
                yield return new WaitForSecondsRealtime(.25f);
                Require(FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.transform.parent.name == "preset-briefing" && t.text == entry.briefing),
                    "Preset instructions are absent from Setup.");
                if (!full) Require(entry.preview != null, "Final menu preview is absent.");
                if (full && preset < 4) yield return RoundTripPresetPlan(entry.id);
                yield return CaptureWorld(entry.id + "-setup.png");
                if (!full) yield return CapturePresetUI(entry.id + "-ui.png");
                if (full && preset < 4)
                {
                    yield return NaturalBattle(entry.id);
                    if (preset == 0) Require(report.battles.Last().realSeconds < 300, "Quick battle exceeds five minutes.");
                    if (preset == 2) Require(report.battles.Last().result.victoryReason == WarSandboxVictoryReason.ControlPoint,
                        "Central preset did not demonstrate a capture victory.");
                }
                else
                {
                    yield return Click("start"); yield return new WaitForSecondsRealtime(1);
                    Require(manager.IsBattleRunning, "Preset did not start.");
                    yield return Click("reset"); VerifyFullStrength();
                }
                yield return Leave(full && preset < 4);
                VerifySources(); VerifyPlans();
                if (preset == 0 && !full)
                {
                    yield return new WaitForSecondsRealtime(.2f);
                    renderCamera = FindFirstObjectByType<Camera>(); Require(renderCamera != null, "Menu camera absent.");
                    CreateCaptureTarget();
                    yield return CapturePresetUI("catalog-ui.png"); ReleaseCamera();
                }
            }
            if (review) Require(!System.IO.File.Exists(settingsFile), "Smoke test wrote settings.");
            Stage("complete");
        }

        private IEnumerator RoundTripPresetPlan(string slot)
        {
            yield return Click("edit");
            var expected = deployment.Draft.Snapshot();
            yield return Click("deployment-plans"); Field("plans-slot", slot); Field("plans-name", session.CurrentDisplayName);
            yield return Click("plans-save");
            Require(deployment.PlanStore.TryLoad(slot, out var saved, out string error), error);
            planHashes.Add(slot, HashFile(PlanPath(slot)));
            yield return Click("plans-back");
            yield return Click("roster-item-0"); Field("input-count", (expected[0].count + 8).ToString());
            yield return Click("field-update");
            Require(deployment.Draft[0].count != expected[0].count, "Draft edit did not take effect.");
            yield return Click("deployment-plans");
            Require(deployment.PlanStore.TryListSlots(out var slots, out error), error);
            yield return Click("plan-item-" + Array.IndexOf(slots, slot));
            yield return Click("plans-load"); yield return Click("plans-confirm-yes");
            Require(deployment.Draft.Count == expected.Length, "Plan lost formations.");
            for (int i = 0; i < expected.Length; i++)
            {
                var loaded = deployment.Draft[i];
                Require(session.catalog.TryGetTemplateId(loaded.template, out string id, out int revision), "Template identity missing.");
                Require(JsonUtility.ToJson(WarSandboxPlanEntry.From(loaded, id, revision)) == JsonUtility.ToJson(saved.entries[i]),
                    "Loaded formation differs from saved preset.");
            }
            var oldBuffers = manager.Buffers;
            yield return Click("deployment-apply");
            Require(!deployment.IsEditing && !oldBuffers.IsAllocated && manager.scenarioConfig != sourceScenario, "Apply did not isolate the preset.");
            for (int i = 0; i < expected.Length; i++)
            {
                var actual = manager.scenarioConfig.unitTypes[i];
                Require(actual != expected[i].template && actual.spawnConfig != expected[i].template.spawnConfig &&
                    actual.spawnConfig.unitCount == expected[i].count && actual.teamId == expected[i].teamId, "Preset runtime copy differs.");
            }
            Require(manager.UnitTypes.TotalAgentCount == expected.Sum(e => e.count), "Preset population did not restore.");
            VerifyFullStrength(); VerifySources(); VerifyPlans(); report.appliedPlans++;
        }

        private IEnumerator CapturePresetUI(string filename)
        {
            if (!CaptureLightLoopImage(filename)) yield break;
            var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled).ToArray();
            var cameras = canvases.Select(c => c.worldCamera).ToArray();
            var distances = canvases.Select(c => c.planeDistance).ToArray();
            int previousMask = renderCamera.cullingMask;
            try
            {
                renderCamera.cullingMask = -1;
                for (int i = 0; i < canvases.Length; i++)
                { canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = renderCamera; canvases[i].planeDistance = 1; }
                Canvas.ForceUpdateCanvases();
                yield return CaptureWorld(filename);
            }
            finally
            {
                renderCamera.cullingMask = previousMask;
                for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null)
                { canvases[i].renderMode = RenderMode.ScreenSpaceOverlay; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i]; }
            }
        }
    }
}
#endif
