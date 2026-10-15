#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        // Explicit opt-in: reuse the historical loops without their 110k scene or many screenshots.
        private bool IsLightLoop => Array.IndexOf(Environment.GetCommandLineArgs(), "--terrain-cycle-light") >= 0;
        private bool CaptureLightLoopImage(string name) => !IsLightLoop ||
            (report.phase == "seed" && name == "terrain-a-setup.png") ||
            (report.phase == "playability-seed" && name == "control-point-settled-ui.png") ||
            (report.phase == "playability-reload" && name == "manual-four-armies-ui.png");

        private void VerifyLoadedPlan(WarSandboxPlanFile plan)
        {
            var field = session.CurrentBattlefield;
            Require(field != null && field.id == plan.battlefieldId && field.contentVersion == plan.battlefieldVersion &&
                field.terrainId == plan.terrainId && field.terrainVersion == plan.terrainVersion &&
                deployment.WorldSize == new Vector2(plan.worldWidth, plan.worldDepth) &&
                manager.systemConfig.simulationConfig.boundaryPadding == plan.boundaryPadding,
                "Restored battlefield/terrain/space identity differs.");
            Require(JsonUtility.ToJson(WarSandboxPlanRules.From(deployment.Draft.Rules)) == JsonUtility.ToJson(plan.rules),
                "Restored rules differ from the saved plan.");
            var entries = deployment.Draft.Snapshot();
            Require(entries.Length == plan.entries.Length, "Restored formation count differs.");
            for (int i = 0; i < entries.Length; i++)
            {
                Require(session.catalog.TryGetTemplateId(entries[i].template, out string id, out int revision), "Restored template lacks identity.");
                Require(JsonUtility.ToJson(WarSandboxPlanEntry.From(entries[i], id, revision)) == JsonUtility.ToJson(plan.entries[i]),
                    "Restored template/army/count/position/formation differs at " + i);
            }
            report.actions.Add("Verified every saved entry, rule and battlefield/terrain identity: " + plan.planId);
        }

        private void VerifyAppliedPlan(WarSandboxPlanFile plan)
        {
            Require(JsonUtility.ToJson(WarSandboxPlanRules.From(controller.CaptureBattlefieldRules())) == JsonUtility.ToJson(plan.rules),
                "Applied battle rules differ from disk.");
            Require(manager.scenarioConfig.unitTypes.Length == plan.entries.Length, "Applied formation count differs.");
            for (int i = 0; i < plan.entries.Length; i++)
            {
                var saved = plan.entries[i]; var actual = manager.scenarioConfig.unitTypes[i]; var spawn = actual.spawnConfig;
                Require(session.catalog.TryResolveTemplate(saved.templateId, saved.templateRevision, out var template, out string error), error);
                Require(actual != template && spawn != template.spawnConfig && actual.teamId == saved.teamId &&
                    spawn.unitCount == saved.count && spawn.spawnCenter.x == saved.center.x && spawn.spawnCenter.z == saved.center.z &&
                    spawn.formationDensity == saved.density && spawn.formationAspect == saved.aspect && spawn.spawnSize == saved.manualSize.ToRuntime(),
                    "Applied owned formation differs from disk at " + i);
            }
            report.actions.Add("Verified applied owned runtime formations and rules: " + plan.planId);
        }

        private string DraftFingerprint()
        {
            string text = JsonUtility.ToJson(WarSandboxPlanRules.From(deployment.Draft.Rules));
            foreach (var entry in deployment.Draft.Snapshot()) text += JsonUtility.ToJson(entry);
            return text;
        }

        private void VerifyFailedPlanLoads(WarSandboxPlanFile good)
        {
            if (!IsLightLoop) return;
            const string corrupt = "loop-corrupt", wrong = "loop-wrong-field";
            Require(!File.Exists(PlanPath(corrupt)) && !File.Exists(PlanPath(wrong)), "Negative fixtures must be fresh.");
            string before = DraftFingerprint(), goodHash = HashFile(PlanPath(good.planId));
            var buffers = manager.Buffers; var scenario = manager.scenarioConfig;
            try
            {
                File.WriteAllText(PlanPath(corrupt), "{broken");
                var incompatible = JsonUtility.FromJson<WarSandboxPlanFile>(JsonUtility.ToJson(good));
                incompatible.planId = wrong; incompatible.battlefieldId = "loop-not-this-battlefield";
                Require(deployment.PlanStore.TrySave(incompatible, false, out string error), error);
                foreach (string slot in new[] { "loop-missing", corrupt, wrong })
                {
                    Require(!deployment.TryLoadPlan(slot, out error) && !string.IsNullOrEmpty(error), "Invalid plan unexpectedly loaded: " + slot);
                    Require(DraftFingerprint() == before && ReferenceEquals(manager.Buffers, buffers) && buffers.IsAllocated &&
                        manager.scenarioConfig == scenario && deployment.IsEditing, "Failed load mutated draft or GPU world: " + slot);
                }
                Require(!deployment.TrySavePlan(good.planId, "Unconfirmed overwrite", false, out error) && !string.IsNullOrEmpty(error) &&
                    HashFile(PlanPath(good.planId)) == goodHash, "Unconfirmed overwrite changed a valid plan.");
                report.actions.Add("Rejected missing/corrupt/wrong-battlefield loads without draft/GPU mutation; refused unconfirmed overwrite.");
            }
            finally { File.Delete(PlanPath(corrupt)); File.Delete(PlanPath(wrong)); }
        }
    }
}
#endif
