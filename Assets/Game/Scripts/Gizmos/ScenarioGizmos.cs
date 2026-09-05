using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/Scenario Gizmos")]
    public sealed class ScenarioGizmos : MonoBehaviour
    {
        [Header("Source")]
        public MassEngineManager manager;
        public ScenarioConfig scenarioOverride;
        public MassEngineSystemConfig systemOverride;

        [Header("Simulation")]
        public bool drawSimulationBounds = true;
        public bool drawSimulationGrid;
        [Min(1)] public int simulationGridStride = 8;
        public Color simulationColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        [Header("Spawn Areas")]
        public bool drawSpawnAreas = true;
        [Range(0f, 1f)] public float spawnFillAlpha = 0.12f;
        [Range(0f, 1f)] public float spawnOutlineAlpha = 0.9f;
        // Teams 2 and up are coloured by WarSandboxTeamPalette instead, which is what the runtime
        // HUD uses - one army, one colour, whichever view you are looking at.
        public Color attackerColor = new Color(0.95f, 0.22f, 0.16f, 1f);
        public Color defenderColor = new Color(0.16f, 0.44f, 1f, 1f);

        [Header("Flow Fields")]
        public bool drawFlowFieldBounds = true;
        public bool drawFlowFieldGrid;
        [Min(1)] public int flowFieldGridStride = 8;
        public Color attackerFlowFieldColor = new Color(1f, 0.72f, 0.12f, 1f);
        public Color defenderFlowFieldColor = new Color(0.08f, 0.9f, 0.72f, 1f);

        [Header("Flow Preview Texture")]
        public bool drawRuntimeFlowPreviewTexture = true;
        [Range(0f, 1f)] public float flowPreviewAlpha = 0.75f;
        [Min(0f)] public float flowPreviewYOffset = 0.04f;
        public bool drawAttackerFlowPreview = true;
        public bool drawDefenderFlowPreview;

        [Header("Configured Targets")]
        public bool drawConfiguredTargets = true;

        [Header("Labels")]
        public bool drawLabels = true;
        [Min(0f)] public float labelYOffset = 2f;

        private void Reset()
        {
            manager = GetComponent<MassEngineManager>();
        }

        private void OnValidate()
        {
            if (manager == null)
                manager = GetComponent<MassEngineManager>();

            spawnFillAlpha = Mathf.Clamp01(spawnFillAlpha);
            spawnOutlineAlpha = Mathf.Clamp01(spawnOutlineAlpha);
            simulationGridStride = Mathf.Max(1, simulationGridStride);
            flowFieldGridStride = Mathf.Max(1, flowFieldGridStride);
            flowPreviewAlpha = Mathf.Clamp01(flowPreviewAlpha);
            flowPreviewYOffset = Mathf.Max(0f, flowPreviewYOffset);
            labelYOffset = Mathf.Max(0f, labelYOffset);
        }

        private void OnDrawGizmos()
        {
            MassEngineManager sourceManager = manager != null ? manager : GetComponent<MassEngineManager>();
            DrawSimulation(sourceManager);
            DrawFlowFields(sourceManager);
            DrawUnits(sourceManager);
        }

        private void DrawSimulation(MassEngineManager sourceManager)
        {
            if (!drawSimulationBounds)
                return;

            if (ScenarioGizmoResolver.TryResolveSimulation(sourceManager, systemOverride, simulationColor, out ScenarioGizmoSimulation simulation))
            {
                ScenarioGizmoDrawer.DrawSimulationBounds(
                    simulation,
                    drawSimulationGrid,
                    simulationGridStride,
                    drawLabels,
                    labelYOffset + 0.5f);
            }
        }

        private void DrawFlowFields(MassEngineManager sourceManager)
        {
            if (!drawFlowFieldBounds)
                return;

            if (ScenarioGizmoResolver.TryResolveAttackerFlow(sourceManager, systemOverride, attackerFlowFieldColor, out ScenarioGizmoFlowField attackerFlow))
            {
                if (drawRuntimeFlowPreviewTexture && drawAttackerFlowPreview)
                    ScenarioGizmoDrawer.DrawFlowPreviewTexture(attackerFlow, ResolveAttackerPreviewTexture(sourceManager), flowPreviewAlpha, flowPreviewYOffset);
                ScenarioGizmoDrawer.DrawFlowField(attackerFlow, drawFlowFieldGrid, flowFieldGridStride, drawLabels, labelYOffset + 1f);
            }

            if (ScenarioGizmoResolver.TryResolveDefenderFlow(sourceManager, systemOverride, defenderFlowFieldColor, out ScenarioGizmoFlowField defenderFlow))
            {
                if (drawRuntimeFlowPreviewTexture && drawDefenderFlowPreview)
                    ScenarioGizmoDrawer.DrawFlowPreviewTexture(defenderFlow, ResolveDefenderPreviewTexture(sourceManager), flowPreviewAlpha, flowPreviewYOffset + 0.01f);
                ScenarioGizmoDrawer.DrawFlowField(defenderFlow, drawFlowFieldGrid, flowFieldGridStride, drawLabels, labelYOffset + 1.5f);
            }
        }

        private void DrawUnits(MassEngineManager sourceManager)
        {
            List<ScenarioGizmoUnit> units = ScenarioGizmoResolver.ResolveUnits(
                sourceManager,
                scenarioOverride,
                attackerColor,
                defenderColor);

            // Mirror the runtime rules so the gizmo never promises an order the GPU
            // will not execute: a team's flow master switch must be ON, and only the
            // FIRST unit type per team with an active target wins (see
            // UnitTypeRegistry.TryGetConfiguredFlowTarget).
            // Keyed by teamId rather than an attacker/defender pair: a scenario may field more
            // armies than the two the doctrine names, and each of them shadows its own unit types.
            RuntimeFlowConfig flow = ResolveFlowConfig(sourceManager);
            var winnerByTeam = new Dictionary<int, int>();
            for (int i = 0; i < units.Count; i++)
            {
                ScenarioGizmoUnit unit = units[i];
                if (unit.Movement == null || !unit.Movement.useConfiguredFlowTarget)
                    continue;
                if (!winnerByTeam.ContainsKey(unit.TeamId))
                    winnerByTeam.Add(unit.TeamId, i);
            }

            for (int i = 0; i < units.Count; i++)
            {
                ScenarioGizmoUnit unit = units[i];
                if (drawSpawnAreas)
                    ScenarioGizmoDrawer.DrawSpawnArea(unit, spawnFillAlpha, spawnOutlineAlpha, drawLabels, labelYOffset);
                if (drawConfiguredTargets)
                {
                    // RuntimeFlowConfig answers this for the runtime too, so a third army is judged
                    // by the doctrine it actually inherits. Deriving the rule here reported its
                    // configured target as ignored while the GPU went on steering it. With no config
                    // assigned, fall back to the same shape: everyone but the defender advances.
                    bool masterOn = flow != null
                        ? flow.ResolveTeamFlowEnabled(unit.TeamId)
                        : unit.TeamId != MassEngineManager.DefenderTeamId;
                    bool isWinner = winnerByTeam.TryGetValue(unit.TeamId, out int winner) && winner == i;
                    string ignoredReason = !masterOn
                        ? (unit.TeamId == MassEngineManager.DefenderTeamId
                            ? "defenderFlowFieldEnabled is OFF"
                            : "flowFieldEnabled is OFF")
                        : "shadowed by an earlier unit type on this team";
                    ScenarioGizmoDrawer.DrawConfiguredTarget(unit, labelYOffset + 1f, masterOn && isWinner, ignoredReason);
                }
            }
        }

        private RuntimeFlowConfig ResolveFlowConfig(MassEngineManager sourceManager)
        {
            if (systemOverride != null && systemOverride.runtimeFlowConfig != null)
                return systemOverride.runtimeFlowConfig;
            return sourceManager != null && sourceManager.systemConfig != null
                ? sourceManager.systemConfig.runtimeFlowConfig
                : null;
        }

        private static Texture ResolveAttackerPreviewTexture(MassEngineManager sourceManager)
        {
            return ResolvePreviewTexture(sourceManager, MassEngineManager.AttackerTeamId);
        }

        private static Texture ResolveDefenderPreviewTexture(MassEngineManager sourceManager)
        {
            return ResolvePreviewTexture(sourceManager, MassEngineManager.DefenderTeamId);
        }

        /// <summary>
        /// Preview textures are one per team now. This gizmo still draws only the two configured
        /// armies - a further army is visible through FlowFieldPreviewHUD at runtime.
        /// </summary>
        private static Texture ResolvePreviewTexture(MassEngineManager sourceManager, int teamId)
        {
            return sourceManager != null && sourceManager.Buffers != null
                ? sourceManager.Buffers.GetFlowPreviewTexture(teamId)
                : null;
        }
    }
}
