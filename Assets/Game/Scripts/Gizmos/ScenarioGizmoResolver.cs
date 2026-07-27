using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    internal static class ScenarioGizmoResolver
    {
        public static List<ScenarioGizmoUnit> ResolveUnits(
            MassEngineManager manager,
            ScenarioConfig scenarioOverride,
            Color attackerColor,
            Color defenderColor,
            Color neutralColor)
        {
            var units = new List<ScenarioGizmoUnit>();
            ScenarioConfig scenario = scenarioOverride != null
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
                units.Add(new ScenarioGizmoUnit(config, roleName, color));
            }

            return units;
        }

        public static bool TryResolveSimulation(
            MassEngineManager manager,
            MassEngineSystemConfig systemOverride,
            Color color,
            out ScenarioGizmoSimulation simulation)
        {
            MassEngineSystemConfig system = systemOverride != null
                ? systemOverride
                : manager != null ? manager.systemConfig : null;
            SimulationConfig config = system != null ? system.simulationConfig : null;
            if (config == null)
            {
                simulation = default;
                return false;
            }

            simulation = new ScenarioGizmoSimulation(config.simulationWorldSize, config.cellSize, color);
            return true;
        }

        public static bool TryResolveAttackerFlow(
            MassEngineManager manager,
            MassEngineSystemConfig systemOverride,
            Color color,
            out ScenarioGizmoFlowField flowField)
        {
            RuntimeFlowConfig flow = ResolveFlow(manager, systemOverride);
            if (flow == null || !flow.flowFieldEnabled)
            {
                flowField = default;
                return false;
            }

            flowField = new ScenarioGizmoFlowField(
                "Attacker Flow Field",
                flow.flowFieldOrigin,
                flow.flowFieldResolution,
                flow.flowFieldCellSize,
                color);
            return true;
        }

        public static bool TryResolveDefenderFlow(
            MassEngineManager manager,
            MassEngineSystemConfig systemOverride,
            Color color,
            out ScenarioGizmoFlowField flowField)
        {
            RuntimeFlowConfig flow = ResolveFlow(manager, systemOverride);
            if (flow == null || !flow.defenderFlowFieldEnabled)
            {
                flowField = default;
                return false;
            }

            flowField = new ScenarioGizmoFlowField(
                "Defender Flow Field",
                flow.flowFieldOrigin,
                flow.flowFieldResolution,
                flow.flowFieldCellSize,
                color);
            return true;
        }

        private static RuntimeFlowConfig ResolveFlow(MassEngineManager manager, MassEngineSystemConfig systemOverride)
        {
            MassEngineSystemConfig system = systemOverride != null
                ? systemOverride
                : manager != null ? manager.systemConfig : null;
            return system != null ? system.runtimeFlowConfig : null;
        }
    }
}
