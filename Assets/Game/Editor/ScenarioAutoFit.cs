using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// One-click physical re-fit of the open scenario: reads head counts + spawn
    /// centers from the scene's MassEngineManager, derives a consistent
    /// world / grid / flow-field parameter set via ScenarioPhysics (the same ledger
    /// the runtime warning uses) and writes it into the config assets with Undo.
    /// Designer workflow: set unit counts / formation intent → Auto-Fit → Play.
    /// Teams 0 and 1 are symmetrically re-centered from their resolved footprint so
    /// their edge-to-edge engagement gap remains stable as head counts change.
    /// Spawn footprints themselves are runtime-derived from formationDensity and are
    /// NOT written here; manual spawnSize overrides are left untouched (they are intent).
    /// </summary>
    public static class ScenarioAutoFit
    {
        [MenuItem("MassEngine/Auto-Fit Scenario")]
        public static void AutoFit()
        {
            AutoFit(WarSandboxFormationLayout.DefaultEngagementGap);
        }

        public static void AutoFit(float engagementGap)
        {
            MassEngineManager manager = Object.FindFirstObjectByType<MassEngineManager>();
            if (manager == null)
            {
                Debug.LogError("Auto-Fit: no MassEngineManager in the open scene.");
                return;
            }

            if (manager.scenarioConfig == null || manager.scenarioConfig.unitTypes == null || manager.scenarioConfig.unitTypes.Length == 0)
            {
                Debug.LogError("Auto-Fit: manager has no scenarioConfig / unit types.", manager);
                return;
            }

            MassEngineSystemConfig system = manager.systemConfig;
            SimulationConfig simulation = system != null ? system.simulationConfig : null;
            RuntimeFlowConfig flow = system != null ? system.runtimeFlowConfig : null;
            if (simulation == null)
            {
                Debug.LogError("Auto-Fit: systemConfig.simulationConfig is not assigned; nothing to write to.", manager);
                return;
            }

            engagementGap = Mathf.Max(0f, engagementGap);
            FitHostileSpawnCenters(manager.scenarioConfig.unitTypes, engagementGap);

            LodConfig lodConfig = system != null ? system.lodConfig : null;
            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(manager.scenarioConfig.unitTypes, simulation, flow, lodConfig);

            Undo.RecordObject(simulation, "Auto-Fit Scenario");
            simulation.simulationWorldSize = report.SuggestedWorldSize;
            simulation.cellSize = report.SuggestedCellSize;
            simulation.maxAgentsPerCell = report.SuggestedMaxAgentsPerCell;
            EditorUtility.SetDirty(simulation);

            string flowNote = "flow config not assigned — flow field NOT refit";
            if (flow != null)
            {
                Undo.RecordObject(flow, "Auto-Fit Scenario");
                flow.flowFieldResolution = report.SuggestedFlowResolution;
                flow.flowFieldCellSize = report.SuggestedFlowCellSize;
                flow.flowFieldOrigin = report.SuggestedFlowOrigin;
                EditorUtility.SetDirty(flow);
                flowNote = "flow " + report.SuggestedFlowResolution + "@" + report.SuggestedFlowCellSize + "m";
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                "Auto-Fit Scenario applied for " + report.TotalAgents + " agents: world " +
                report.SuggestedWorldSize.x + "x" + report.SuggestedWorldSize.y +
                ", cellSize " + report.SuggestedCellSize +
                ", maxAgentsPerCell " + report.SuggestedMaxAgentsPerCell + ", " + flowNote +
                ", engagement gap " + engagementGap + "m" +
                ". LOD radii are a camera/visual choice and were not changed — for large worlds consider raising " +
                "LodConfig near/mid radii and setting maxRenderDistance.", manager);

            // Re-run the ledger so the console confirms the scenario is now consistent.
            ScenarioPhysicsReport after = ScenarioPhysics.Evaluate(manager.scenarioConfig.unitTypes, simulation, flow, lodConfig);
            if (after.HasIssues)
                Debug.LogWarning(after.Describe(), manager);
            else
                Debug.Log("Scenario physics check after Auto-Fit: OK.", manager);
        }

        private static void FitHostileSpawnCenters(UnitTypeConfig[] unitTypes, float engagementGap)
        {
            var fittedSpawns = new HashSet<SpawnConfig>();
            for (int i = 0; i < unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = unitTypes[i];
                SpawnConfig spawn = unitType != null ? unitType.spawnConfig : null;
                if (spawn == null || !fittedSpawns.Add(spawn) || (unitType.teamId != 0 && unitType.teamId != 1))
                    continue;

                Vector3 fittedCenter = WarSandboxFormationLayout.ResolveCenteredSpawnCenter(
                    spawn, unitType.teamId, engagementGap);
                if (spawn.spawnCenter == fittedCenter)
                    continue;

                Undo.RecordObject(spawn, "Auto-Fit Army Deployment");
                spawn.spawnCenter = fittedCenter;
                EditorUtility.SetDirty(spawn);
            }
        }
    }
}
