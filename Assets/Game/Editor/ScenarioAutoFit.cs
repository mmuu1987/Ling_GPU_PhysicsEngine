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
    /// their edge-to-edge engagement gap remains stable as head counts change. Scenario order
    /// is rank order within a team: the first unit type it lists gets the gap, the rest stack
    /// up behind it, which is how a melee screen and its archers keep their relative places.
    /// Spawn footprints themselves are runtime-derived from formationDensity and are
    /// NOT written here; manual spawnSize overrides are left untouched (they are intent).
    ///
    /// Teams 2 and up keep the spawn center they were authored with. That is the contract, not a
    /// gap: only the front line has a symmetric answer. Placing extra armies on a ring would need
    /// the footprint rotated to face the middle, and spawn rects are axis-aligned - on a ring they
    /// would overlap their neighbours. World/grid/flow sizing below still counts every team.
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
                ", engagement gap " + engagementGap + "m (front line only; teams 2+ keep their authored centers)" +
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
            // X depth each front-line team has already spent, indexed by teamId. Without it every
            // block of a team lands flush against the gap, stacked on top of its own ranks.
            float[] deployedDepth = new float[2];
            for (int i = 0; i < unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = unitTypes[i];
                SpawnConfig spawn = unitType != null ? unitType.spawnConfig : null;
                // Teams 2 and up are placed by the designer - see the class summary.
                if (spawn == null || (unitType.teamId != 0 && unitType.teamId != 1))
                    continue;
                // Unit types sharing one SpawnConfig are one block sharing one rank, not two.
                if (!fittedSpawns.Add(spawn))
                    continue;

                Vector3 fittedCenter = WarSandboxFormationLayout.ResolveRankedSpawnCenter(
                    spawn, unitType.teamId, engagementGap, deployedDepth[unitType.teamId]);
                deployedDepth[unitType.teamId] += Mathf.Max(0f, spawn.ResolveSpawnSize().x);
                if (spawn.spawnCenter == fittedCenter)
                    continue;

                Undo.RecordObject(spawn, "Auto-Fit Army Deployment");
                spawn.spawnCenter = fittedCenter;
                EditorUtility.SetDirty(spawn);
            }
        }
    }
}
