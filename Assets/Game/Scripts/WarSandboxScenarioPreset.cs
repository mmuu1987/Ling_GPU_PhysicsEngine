using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Reusable authoring snapshot for one complete war-sandbox battlefield. The asset
    /// is inert at runtime; applying it is an explicit editor action.
    /// </summary>
    [CreateAssetMenu(menuName = "MassEngine/War Sandbox Scenario Preset")]
    public sealed class WarSandboxScenarioPreset : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [HideInInspector] public int schemaVersion = CurrentSchemaVersion;
        [TextArea] public string description;
        public ScenarioConfig scenarioConfig;
        public MassEngineSystemConfig systemConfig;

        [Header("Battlefield")]
        public bool pauseOnStart = true;
        public WarSandboxGameMode gameMode = WarSandboxGameMode.Annihilation;
        public float moveWaypointArrivalRadius = 8f;
        public int maxMoveRoutePoints = 8;
        public Vector3 controlPointCenter;
        public float controlPointRadius = 30f;
        public float controlPointCaptureSeconds = 20f;
        public bool staticObstaclesEnabled;
        public bool useCustomStaticObstacleLayout;
        public float staticObstacleClearance = 2f;
        public StaticObstacleRect[] staticObstacles = Array.Empty<StaticObstacleRect>();

        [Header("Forces")]
        public WarSandboxDeploymentSnapshot[] deployments = Array.Empty<WarSandboxDeploymentSnapshot>();

        [Header("Engine Snapshot")]
        public WarSandboxSimulationSnapshot simulation = new WarSandboxSimulationSnapshot();
        public WarSandboxFlowSnapshot flow = new WarSandboxFlowSnapshot();
        public WarSandboxCombatSnapshot combat = new WarSandboxCombatSnapshot();

        public bool CaptureFrom(MassEngineManager manager, WarSandboxBattleController controller)
        {
            if (manager == null || controller == null || manager.scenarioConfig == null)
                return false;

            EnsureSnapshotObjects();
            schemaVersion = CurrentSchemaVersion;
            scenarioConfig = manager.scenarioConfig;
            systemConfig = manager.systemConfig;

            pauseOnStart = controller.pauseOnStart;
            gameMode = controller.gameMode;
            moveWaypointArrivalRadius = controller.moveWaypointArrivalRadius;
            maxMoveRoutePoints = controller.maxMoveRoutePoints;
            controlPointCenter = controller.controlPointCenter;
            controlPointRadius = controller.controlPointRadius;
            controlPointCaptureSeconds = controller.controlPointCaptureSeconds;
            staticObstaclesEnabled = controller.staticObstaclesEnabled;
            useCustomStaticObstacleLayout = controller.useCustomStaticObstacleLayout;
            staticObstacleClearance = controller.staticObstacleClearance;
            staticObstacles = CloneObstacles(controller.staticObstacles);

            var capturedDeployments = new List<WarSandboxDeploymentSnapshot>();
            UnitTypeConfig[] unitTypes = scenarioConfig.unitTypes;
            if (unitTypes != null)
            {
                for (int i = 0; i < unitTypes.Length; i++)
                {
                    UnitTypeConfig unitType = unitTypes[i];
                    if (unitType != null && unitType.spawnConfig != null)
                        capturedDeployments.Add(WarSandboxDeploymentSnapshot.Capture(unitType));
                }
            }
            deployments = capturedDeployments.ToArray();

            simulation.Capture(systemConfig != null ? systemConfig.simulationConfig : null);
            flow.Capture(systemConfig != null ? systemConfig.runtimeFlowConfig : null);
            combat.Capture(systemConfig != null ? systemConfig.runtimeCombatConfig : null);
            return true;
        }

        public bool ApplyTo(MassEngineManager manager, WarSandboxBattleController controller)
        {
            if (manager == null || controller == null || scenarioConfig == null)
                return false;

            EnsureSnapshotObjects();
            manager.scenarioConfig = scenarioConfig;
            manager.systemConfig = systemConfig;
            controller.manager = manager;
            controller.pauseOnStart = pauseOnStart;
            controller.gameMode = gameMode;
            controller.moveWaypointArrivalRadius = Mathf.Max(1f, moveWaypointArrivalRadius);
            controller.maxMoveRoutePoints = Mathf.Clamp(maxMoveRoutePoints, 2, 16);
            controller.controlPointCenter = controlPointCenter;
            controller.controlPointRadius = Mathf.Max(2f, controlPointRadius);
            controller.controlPointCaptureSeconds = Mathf.Max(5f, controlPointCaptureSeconds);
            controller.staticObstaclesEnabled = staticObstaclesEnabled;
            controller.useCustomStaticObstacleLayout = useCustomStaticObstacleLayout;
            controller.staticObstacleClearance = Mathf.Max(0f, staticObstacleClearance);
            controller.staticObstacles = CloneObstacles(staticObstacles);

            if (deployments != null)
            {
                for (int i = 0; i < deployments.Length; i++)
                    deployments[i]?.Apply();
            }

            simulation.Apply(systemConfig != null ? systemConfig.simulationConfig : null);
            flow.Apply(systemConfig != null ? systemConfig.runtimeFlowConfig : null);
            combat.Apply(systemConfig != null ? systemConfig.runtimeCombatConfig : null);
            controller.RebuildArmyStates();
            return true;
        }

        private void EnsureSnapshotObjects()
        {
            if (simulation == null)
                simulation = new WarSandboxSimulationSnapshot();
            if (flow == null)
                flow = new WarSandboxFlowSnapshot();
            if (combat == null)
                combat = new WarSandboxCombatSnapshot();
        }

        private static StaticObstacleRect[] CloneObstacles(StaticObstacleRect[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<StaticObstacleRect>();
            var clone = new StaticObstacleRect[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }
    }

    [Serializable]
    public sealed class WarSandboxDeploymentSnapshot
    {
        public UnitTypeConfig unitType;
        public int teamId;
        public int unitCount;
        public Vector3 spawnCenter;
        public float formationDensity;
        public float formationAspect;
        public Vector3 spawnSize;

        public static WarSandboxDeploymentSnapshot Capture(UnitTypeConfig source)
        {
            SpawnConfig spawn = source.spawnConfig;
            return new WarSandboxDeploymentSnapshot
            {
                unitType = source,
                teamId = source.teamId,
                unitCount = spawn.unitCount,
                spawnCenter = spawn.spawnCenter,
                formationDensity = spawn.formationDensity,
                formationAspect = spawn.formationAspect,
                spawnSize = spawn.spawnSize
            };
        }

        public void Apply()
        {
            if (unitType == null || unitType.spawnConfig == null)
                return;

            unitType.teamId = teamId;
            SpawnConfig spawn = unitType.spawnConfig;
            spawn.unitCount = Mathf.Max(1, unitCount);
            spawn.spawnCenter = spawnCenter;
            spawn.formationDensity = Mathf.Clamp(formationDensity, 0.05f, SpawnConfig.PackingLimitPerSquareMeter);
            spawn.formationAspect = Mathf.Clamp(formationAspect, 0.1f, 10f);
            spawn.spawnSize = spawnSize;
        }
    }

    [Serializable]
    public sealed class WarSandboxSimulationSnapshot
    {
        public bool captured;
        public Vector2 simulationWorldSize;
        public float boundaryPadding;
        public float cellSize;
        public int maxAgentsPerCell;

        public void Capture(SimulationConfig source)
        {
            captured = source != null;
            if (!captured)
                return;
            simulationWorldSize = source.simulationWorldSize;
            boundaryPadding = source.boundaryPadding;
            cellSize = source.cellSize;
            maxAgentsPerCell = source.maxAgentsPerCell;
        }

        public void Apply(SimulationConfig target)
        {
            if (!captured || target == null)
                return;
            target.simulationWorldSize = simulationWorldSize;
            target.boundaryPadding = Mathf.Max(0f, boundaryPadding);
            target.cellSize = Mathf.Max(0.1f, cellSize);
            target.maxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);
        }
    }

    [Serializable]
    public sealed class WarSandboxFlowSnapshot
    {
        public bool captured;
        public int flowFieldResolution;
        public float flowFieldCellSize;
        public Vector2 flowFieldOrigin;
        public bool flowFieldEnabled;
        public bool defenderFlowFieldEnabled;
        public float dynamicFlowUpdateInterval;
        public float dynamicDefenderFlowUpdateInterval;
        public bool runtimeFlowPreviewEnabled;
        public FlowFieldPreviewMode runtimeFlowPreviewMode;
        public bool runtimeDynamicAttackerFlowEnabled;
        public bool runtimeDynamicDefenderFlowEnabled;
        public int dynamicFlowSectorCount;
        public float dynamicFlowTargetStopRadius;
        public int dynamicFlowMinDefendersPerTarget;
        public int dynamicDefenderFlowSectorCount;
        public float dynamicDefenderFlowTargetStopRadius;
        public int dynamicDefenderFlowMinAttackersPerTarget;

        public void Capture(RuntimeFlowConfig source)
        {
            captured = source != null;
            if (!captured)
                return;
            flowFieldResolution = source.flowFieldResolution;
            flowFieldCellSize = source.flowFieldCellSize;
            flowFieldOrigin = source.flowFieldOrigin;
            flowFieldEnabled = source.flowFieldEnabled;
            defenderFlowFieldEnabled = source.defenderFlowFieldEnabled;
            dynamicFlowUpdateInterval = source.dynamicFlowUpdateInterval;
            dynamicDefenderFlowUpdateInterval = source.dynamicDefenderFlowUpdateInterval;
            runtimeFlowPreviewEnabled = source.runtimeFlowPreviewEnabled;
            runtimeFlowPreviewMode = source.runtimeFlowPreviewMode;
            runtimeDynamicAttackerFlowEnabled = source.runtimeDynamicAttackerFlowEnabled;
            runtimeDynamicDefenderFlowEnabled = source.runtimeDynamicDefenderFlowEnabled;
            dynamicFlowSectorCount = source.dynamicFlowSectorCount;
            dynamicFlowTargetStopRadius = source.dynamicFlowTargetStopRadius;
            dynamicFlowMinDefendersPerTarget = source.dynamicFlowMinDefendersPerTarget;
            dynamicDefenderFlowSectorCount = source.dynamicDefenderFlowSectorCount;
            dynamicDefenderFlowTargetStopRadius = source.dynamicDefenderFlowTargetStopRadius;
            dynamicDefenderFlowMinAttackersPerTarget = source.dynamicDefenderFlowMinAttackersPerTarget;
        }

        public void Apply(RuntimeFlowConfig target)
        {
            if (!captured || target == null)
                return;
            target.flowFieldResolution = Mathf.Max(16, flowFieldResolution);
            target.flowFieldCellSize = Mathf.Max(0.1f, flowFieldCellSize);
            target.flowFieldOrigin = flowFieldOrigin;
            target.flowFieldEnabled = flowFieldEnabled;
            target.defenderFlowFieldEnabled = defenderFlowFieldEnabled;
            target.dynamicFlowUpdateInterval = Mathf.Max(0f, dynamicFlowUpdateInterval);
            target.dynamicDefenderFlowUpdateInterval = Mathf.Max(0f, dynamicDefenderFlowUpdateInterval);
            target.runtimeFlowPreviewEnabled = runtimeFlowPreviewEnabled;
            target.runtimeFlowPreviewMode = runtimeFlowPreviewMode;
            target.runtimeDynamicAttackerFlowEnabled = runtimeDynamicAttackerFlowEnabled;
            target.runtimeDynamicDefenderFlowEnabled = runtimeDynamicDefenderFlowEnabled;
            target.dynamicFlowSectorCount = Mathf.Clamp(dynamicFlowSectorCount, 1, 8);
            target.dynamicFlowTargetStopRadius = Mathf.Max(0f, dynamicFlowTargetStopRadius);
            target.dynamicFlowMinDefendersPerTarget = Mathf.Max(1, dynamicFlowMinDefendersPerTarget);
            target.dynamicDefenderFlowSectorCount = Mathf.Clamp(dynamicDefenderFlowSectorCount, 1, 8);
            target.dynamicDefenderFlowTargetStopRadius = Mathf.Max(0f, dynamicDefenderFlowTargetStopRadius);
            target.dynamicDefenderFlowMinAttackersPerTarget = Mathf.Max(1, dynamicDefenderFlowMinAttackersPerTarget);
        }
    }

    [Serializable]
    public sealed class WarSandboxCombatSnapshot
    {
        public bool captured;
        public float defenderGuardRadius;
        public float deathClipDuration;

        public void Capture(RuntimeCombatConfig source)
        {
            captured = source != null;
            if (!captured)
                return;
            defenderGuardRadius = source.defenderGuardRadius;
            deathClipDuration = source.deathClipDuration;
        }

        public void Apply(RuntimeCombatConfig target)
        {
            if (!captured || target == null)
                return;
            target.defenderGuardRadius = Mathf.Max(0f, defenderGuardRadius);
            target.deathClipDuration = Mathf.Max(0.01f, deathClipDuration);
        }
    }
}
