using System.Collections.Generic;
using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    internal static class Stage7ScenarioGizmoResolver
    {
        public static List<Stage7ScenarioGizmoUnit> ResolveUnits(
            MassGpuSystemManager_Stage7 manager,
            ScenarioConfig_Stage7 scenarioOverride,
            Color attackerColor,
            Color defenderColor,
            Color neutralColor)
        {
            var units = new List<Stage7ScenarioGizmoUnit>();
            ScenarioConfig_Stage7 scenario = scenarioOverride != null
                ? scenarioOverride
                : manager != null ? manager.scenarioConfig : null;

            if (scenario == null || scenario.unitTypes == null)
                return units;

            for (int i = 0; i < scenario.unitTypes.Length; i++)
            {
                UnitTypeConfig config = scenario.unitTypes[i];
                if (config == null)
                    continue;

                string roleName = config.teamId == 0 ? "Attacker" : config.teamId == 1 ? "Defender" : "Team " + config.teamId;
                Color color = config.teamId == 0 ? attackerColor : config.teamId == 1 ? defenderColor : neutralColor;
                units.Add(new Stage7ScenarioGizmoUnit(config, roleName, color));
            }

            return units;
        }

        public static bool TryResolveSimulation(
            MassGpuSystemManager_Stage7 manager,
            Stage7SystemConfig systemOverride,
            Color color,
            out Stage7ScenarioGizmoSimulation simulation)
        {
            Stage7SystemConfig system = systemOverride != null
                ? systemOverride
                : manager != null ? manager.systemConfig : null;
            SimulationConfig config = system != null ? system.simulationConfig : null;
            if (config == null)
            {
                simulation = default;
                return false;
            }

            simulation = new Stage7ScenarioGizmoSimulation(config.simulationWorldSize, config.cellSize, color);
            return true;
        }

        public static bool TryResolveAttackerFlow(
            MassGpuSystemManager_Stage7 manager,
            Stage7SystemConfig systemOverride,
            Color color,
            out Stage7ScenarioGizmoFlowField flowField)
        {
            RuntimeFlowConfig flow = ResolveFlow(manager, systemOverride);
            if (flow == null || !flow.flowFieldEnabled)
            {
                flowField = default;
                return false;
            }

            flowField = new Stage7ScenarioGizmoFlowField(
                "Attacker Flow Field",
                flow.flowFieldOrigin,
                flow.flowFieldResolution,
                flow.flowFieldCellSize,
                color);
            return true;
        }

        public static bool TryResolveDefenderFlow(
            MassGpuSystemManager_Stage7 manager,
            Stage7SystemConfig systemOverride,
            Color color,
            out Stage7ScenarioGizmoFlowField flowField)
        {
            RuntimeFlowConfig flow = ResolveFlow(manager, systemOverride);
            if (flow == null || !flow.defenderFlowFieldEnabled)
            {
                flowField = default;
                return false;
            }

            flowField = new Stage7ScenarioGizmoFlowField(
                "Defender Flow Field",
                flow.flowFieldOrigin,
                flow.flowFieldResolution,
                flow.flowFieldCellSize,
                color);
            return true;
        }

        private static RuntimeFlowConfig ResolveFlow(MassGpuSystemManager_Stage7 manager, Stage7SystemConfig systemOverride)
        {
            Stage7SystemConfig system = systemOverride != null
                ? systemOverride
                : manager != null ? manager.systemConfig : null;
            return system != null ? system.runtimeFlowConfig : null;
        }
    }
}
