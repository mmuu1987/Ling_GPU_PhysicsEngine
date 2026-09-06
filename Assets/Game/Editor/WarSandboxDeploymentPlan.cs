using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Editor-only snapshot of deployment intent and its fitted spatial dimensions.
    /// Unit assets identify the roster; mutable deployment values are copied, not read
    /// back from those assets when loading. Combat/render settings remain shared.
    /// </summary>
    public sealed class WarSandboxDeploymentPlan : ScriptableObject
    {
        private const int CurrentVersion = 1;

        [SerializeField] private int version;
        [SerializeField] private float engagementGap;
        [SerializeField] private UnitDeployment[] units = new UnitDeployment[0];
        [SerializeField] private Vector2 worldSize;
        [SerializeField] private float boundaryPadding;
        [SerializeField] private float cellSize;
        [SerializeField] private int maxAgentsPerCell;
        [SerializeField] private int flowResolution;
        [SerializeField] private float flowCellSize;
        [SerializeField] private Vector2 flowOrigin;

        public float EngagementGap { get { return engagementGap; } }
        public int UnitTypeCount { get { return units != null ? units.Length : 0; } }

        [Serializable]
        private sealed class UnitDeployment
        {
            public UnitTypeConfig unitType;
            public SpawnConfig spawn;
            public int teamId;
            public int unitCount;
            public Vector3 center;
            public float density;
            public float aspect;
            public Vector3 size;

            public UnitDeployment(UnitTypeConfig source)
            {
                unitType = source;
                spawn = source.spawnConfig;
                teamId = source.teamId;
                unitCount = spawn.unitCount;
                center = spawn.spawnCenter;
                density = spawn.formationDensity;
                aspect = spawn.formationAspect;
                size = spawn.spawnSize;
            }

            public void Apply()
            {
                unitType.teamId = teamId;
                unitType.spawnConfig = spawn;
                spawn.unitCount = unitCount;
                spawn.spawnCenter = center;
                spawn.formationDensity = density;
                spawn.formationAspect = aspect;
                spawn.spawnSize = size;
            }
        }

        public bool TryCapture(MassEngineManager manager, float gap, out string error)
        {
            if (!TryResolveTarget(manager, out SimulationConfig simulation, out RuntimeFlowConfig flow, out error))
                return false;

            UnitTypeConfig[] roster = manager.scenarioConfig.unitTypes;
            if (roster == null || roster.Length == 0)
            {
                error = "The scenario has no unit types to save.";
                return false;
            }

            var captured = new UnitDeployment[roster.Length];
            for (int i = 0; i < roster.Length; i++)
            {
                if (roster[i] == null || roster[i].spawnConfig == null)
                {
                    error = "Unit type or spawn config is missing at roster entry " + (i + 1) + ".";
                    return false;
                }
                captured[i] = new UnitDeployment(roster[i]);
            }

            Undo.RecordObject(this, "Save War Sandbox Deployment");
            version = CurrentVersion;
            engagementGap = Mathf.Max(0f, gap);
            units = captured;
            worldSize = simulation.simulationWorldSize;
            boundaryPadding = simulation.boundaryPadding;
            cellSize = simulation.cellSize;
            maxAgentsPerCell = simulation.maxAgentsPerCell;
            flowResolution = flow.flowFieldResolution;
            flowCellSize = flow.flowFieldCellSize;
            flowOrigin = flow.flowFieldOrigin;
            EditorUtility.SetDirty(this);
            return true;
        }

        public bool TryApply(MassEngineManager manager, out string error)
        {
            if (!TryResolveTarget(manager, out SimulationConfig simulation, out RuntimeFlowConfig flow, out error))
                return false;
            if (version != CurrentVersion || units == null || units.Length == 0)
            {
                error = "The deployment plan is empty or uses an unsupported version.";
                return false;
            }

            // Resolve every dependency before touching any asset. A deleted template must
            // not leave half a roster loaded or partially replace the current deployment.
            var changed = new HashSet<Object> { manager.scenarioConfig, simulation, flow };
            var roster = new UnitTypeConfig[units.Length];
            for (int i = 0; i < units.Length; i++)
            {
                UnitDeployment entry = units[i];
                if (entry == null || entry.unitType == null || entry.spawn == null)
                {
                    error = "The plan's unit type or spawn config is missing at roster entry " + (i + 1) + ".";
                    return false;
                }
                roster[i] = entry.unitType;
                changed.Add(entry.unitType);
                changed.Add(entry.spawn);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Load War Sandbox Deployment");
            var changedObjects = new Object[changed.Count];
            changed.CopyTo(changedObjects);
            Undo.RecordObjects(changedObjects, "Load War Sandbox Deployment");

            for (int i = 0; i < units.Length; i++)
                units[i].Apply();
            manager.scenarioConfig.unitTypes = roster;
            simulation.simulationWorldSize = worldSize;
            simulation.boundaryPadding = boundaryPadding;
            simulation.cellSize = cellSize;
            simulation.maxAgentsPerCell = maxAgentsPerCell;
            flow.flowFieldResolution = flowResolution;
            flow.flowFieldCellSize = flowCellSize;
            flow.flowFieldOrigin = flowOrigin;

            // Loading is an exact restore, not Auto-Fit: hand-authored front-line centers
            // and the ordering of mixed-arms ranks must survive a save/load cycle.
            foreach (Object changedObject in changedObjects)
                EditorUtility.SetDirty(changedObject);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        private static bool TryResolveTarget(
            MassEngineManager manager,
            out SimulationConfig simulation,
            out RuntimeFlowConfig flow,
            out string error)
        {
            simulation = null;
            flow = null;
            error = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                error = "Deployment plans can only be saved or loaded outside Play Mode.";
                return false;
            }
            if (manager == null || manager.scenarioConfig == null || manager.systemConfig == null)
            {
                error = "Assign a manager, scenario and system config first.";
                return false;
            }
            simulation = manager.systemConfig.simulationConfig;
            flow = manager.systemConfig.runtimeFlowConfig;
            if (simulation == null || flow == null)
            {
                error = "The system config needs both simulation and runtime flow configs.";
                return false;
            }
            return true;
        }
    }
}
