using System.Collections.Generic;
using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("MassGPUPhysics/Stage7/Scenario Gizmos")]
    public sealed class Stage7ScenarioGizmos_Stage7 : MonoBehaviour
    {
        [Header("Source")]
        public MassGpuSystemManager_Stage7 manager;
        public ScenarioConfig_Stage7 scenarioOverride;
        public Stage7SystemConfig systemOverride;

        [Header("Simulation")]
        public bool drawSimulationBounds = true;
        public bool drawSimulationGrid;
        [Min(1)] public int simulationGridStride = 8;
        public Color simulationColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        [Header("Spawn Areas")]
        public bool drawSpawnAreas = true;
        [Range(0f, 1f)] public float spawnFillAlpha = 0.12f;
        [Range(0f, 1f)] public float spawnOutlineAlpha = 0.9f;
        public Color attackerColor = new Color(0.95f, 0.22f, 0.16f, 1f);
        public Color defenderColor = new Color(0.16f, 0.44f, 1f, 1f);
        public Color neutralColor = new Color(0.7f, 0.7f, 0.7f, 1f);

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
            manager = GetComponent<MassGpuSystemManager_Stage7>();
        }

        private void OnValidate()
        {
            if (manager == null)
                manager = GetComponent<MassGpuSystemManager_Stage7>();

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
            MassGpuSystemManager_Stage7 sourceManager = manager != null ? manager : GetComponent<MassGpuSystemManager_Stage7>();
            DrawSimulation(sourceManager);
            DrawFlowFields(sourceManager);
            DrawUnits(sourceManager);
        }

        private void DrawSimulation(MassGpuSystemManager_Stage7 sourceManager)
        {
            if (!drawSimulationBounds)
                return;

            if (Stage7ScenarioGizmoResolver.TryResolveSimulation(sourceManager, systemOverride, simulationColor, out Stage7ScenarioGizmoSimulation simulation))
            {
                Stage7ScenarioGizmoDrawer.DrawSimulationBounds(
                    simulation,
                    drawSimulationGrid,
                    simulationGridStride,
                    drawLabels,
                    labelYOffset + 0.5f);
            }
        }

        private void DrawFlowFields(MassGpuSystemManager_Stage7 sourceManager)
        {
            if (!drawFlowFieldBounds)
                return;

            if (Stage7ScenarioGizmoResolver.TryResolveAttackerFlow(sourceManager, systemOverride, attackerFlowFieldColor, out Stage7ScenarioGizmoFlowField attackerFlow))
            {
                if (drawRuntimeFlowPreviewTexture && drawAttackerFlowPreview)
                    Stage7ScenarioGizmoDrawer.DrawFlowPreviewTexture(attackerFlow, ResolveAttackerPreviewTexture(sourceManager), flowPreviewAlpha, flowPreviewYOffset);
                Stage7ScenarioGizmoDrawer.DrawFlowField(attackerFlow, drawFlowFieldGrid, flowFieldGridStride, drawLabels, labelYOffset + 1f);
            }

            if (Stage7ScenarioGizmoResolver.TryResolveDefenderFlow(sourceManager, systemOverride, defenderFlowFieldColor, out Stage7ScenarioGizmoFlowField defenderFlow))
            {
                if (drawRuntimeFlowPreviewTexture && drawDefenderFlowPreview)
                    Stage7ScenarioGizmoDrawer.DrawFlowPreviewTexture(defenderFlow, ResolveDefenderPreviewTexture(sourceManager), flowPreviewAlpha, flowPreviewYOffset + 0.01f);
                Stage7ScenarioGizmoDrawer.DrawFlowField(defenderFlow, drawFlowFieldGrid, flowFieldGridStride, drawLabels, labelYOffset + 1.5f);
            }
        }

        private void DrawUnits(MassGpuSystemManager_Stage7 sourceManager)
        {
            List<Stage7ScenarioGizmoUnit> units = Stage7ScenarioGizmoResolver.ResolveUnits(
                sourceManager,
                scenarioOverride,
                attackerColor,
                defenderColor,
                neutralColor);

            for (int i = 0; i < units.Count; i++)
            {
                Stage7ScenarioGizmoUnit unit = units[i];
                if (drawSpawnAreas)
                    Stage7ScenarioGizmoDrawer.DrawSpawnArea(unit, spawnFillAlpha, spawnOutlineAlpha, drawLabels, labelYOffset);
                if (drawConfiguredTargets)
                    Stage7ScenarioGizmoDrawer.DrawConfiguredTarget(unit, labelYOffset + 1f);
            }
        }

        private static Texture ResolveAttackerPreviewTexture(MassGpuSystemManager_Stage7 sourceManager)
        {
            return sourceManager != null && sourceManager.Buffers != null
                ? sourceManager.Buffers.runtimeAttackerFlowPreviewTexture
                : null;
        }

        private static Texture ResolveDefenderPreviewTexture(MassGpuSystemManager_Stage7 sourceManager)
        {
            return sourceManager != null && sourceManager.Buffers != null
                ? sourceManager.Buffers.runtimeDefenderFlowPreviewTexture
                : null;
        }
    }
}
