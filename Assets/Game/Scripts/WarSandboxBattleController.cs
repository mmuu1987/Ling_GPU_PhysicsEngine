using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Thin game-layer coordinator for the current two-army engine. It translates
    /// designer/player intent into MassEngine runtime overrides; it never writes config
    /// assets and owns no duplicate simulation.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [AddComponentMenu("MassEngine/War Sandbox Battle Controller")]
    public sealed class WarSandboxBattleController : MonoBehaviour
    {
        [Header("Engine")]
        public MassEngineManager manager;
        public bool pauseOnStart = true;

        [Header("Runtime")]
        [Range(0, 1)] public int selectedTeam;
        [Min(1f)] public float moveWaypointArrivalRadius = 8f;
        [Range(2, 16)] public int maxMoveRoutePoints = 8;

        [Header("Battle Rules")]
        public WarSandboxGameMode gameMode = WarSandboxGameMode.Annihilation;
        public Vector3 controlPointCenter = Vector3.zero;
        [Min(2f)] public float controlPointRadius = 30f;
        [Min(5f)] public float controlPointCaptureSeconds = 20f;

        private readonly ArmyRuntimeState[] armies =
        {
            new ArmyRuntimeState { teamId = 0, displayName = "攻方" },
            new ArmyRuntimeState { teamId = 1, displayName = "守方" }
        };
        private readonly List<Vector3>[] moveRoutes =
        {
            new List<Vector3>(),
            new List<Vector3>()
        };

        private WarSandboxBattlePhase phase = WarSandboxBattlePhase.Setup;
        private WarSandboxBattleResult battleResult;
        private float simulationSpeed = 1f;
        private float controlPointProgress;
        private bool initialized;

        public WarSandboxBattlePhase Phase { get { return phase; } }
        public WarSandboxBattleResult BattleResult { get { return battleResult; } }
        public float SimulationSpeed { get { return simulationSpeed; } }
        public float ControlPointProgress { get { return controlPointProgress; } }
        public int AttackersInControlPoint { get { return TelemetrySnapshot.attackers.observationZoneCount; } }
        public int DefendersInControlPoint { get { return TelemetrySnapshot.defenders.observationZoneCount; } }
        public ArmyRuntimeState SelectedArmy { get { return GetArmy(selectedTeam); } }
        public BattleTelemetrySnapshot TelemetrySnapshot
        {
            get { return manager != null && manager.Telemetry != null ? manager.Telemetry.Snapshot : default; }
        }

        private void Reset()
        {
            manager = GetComponent<MassEngineManager>();
        }

        private void Awake()
        {
            ResolveManager();
            RebuildArmyStates();
        }

        private void Start()
        {
            if (manager == null)
                return;

            if (pauseOnStart)
            {
                manager.PauseBattle();
                phase = WarSandboxBattlePhase.Setup;
            }
            else
            {
                phase = manager.IsBattleRunning ? WarSandboxBattlePhase.Running : WarSandboxBattlePhase.Setup;
            }
        }

        private void Update()
        {
            if (!initialized)
                RebuildArmyStates();

            ConfigureControlPointTelemetry();
            AdvanceMoveRoutes();
            EvaluateVictory();
            EvaluateControlPoint();
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
                Time.timeScale = 1f;
        }

        public ArmyRuntimeState GetArmy(int teamId)
        {
            return teamId >= 0 && teamId < armies.Length ? armies[teamId] : null;
        }

        public bool SelectArmy(int teamId)
        {
            if (teamId < 0 || teamId >= armies.Length)
                return false;

            selectedTeam = teamId;
            return true;
        }

        public bool IssueOrder(ArmyOrder order)
        {
            return IssueOrderInternal(order, true);
        }

        public bool IssueMoveOrder(int teamId, Vector3 target, bool append)
        {
            ArmyRuntimeState army = GetArmy(teamId);
            if (army == null)
                return false;

            List<Vector3> route = moveRoutes[teamId];
            bool hasActiveMoveRoute = army.hasOrder && army.currentOrder.type == ArmyOrderType.Move && route.Count > 0;
            if (!append || !hasActiveMoveRoute)
                return IssueOrder(ArmyOrder.Move(teamId, target));

            if (route.Count >= Mathf.Clamp(maxMoveRoutePoints, 2, 16))
                return false;

            route.Add(target);
            return true;
        }

        public bool SetGameMode(WarSandboxGameMode value)
        {
            if (phase != WarSandboxBattlePhase.Setup)
                return false;

            gameMode = value;
            controlPointProgress = 0f;
            ConfigureControlPointTelemetry();
            return true;
        }

        public int GetMoveRoutePointCount(int teamId)
        {
            return teamId >= 0 && teamId < moveRoutes.Length ? moveRoutes[teamId].Count : 0;
        }

        public bool TryGetMoveRoutePoint(int teamId, int routeIndex, out Vector3 point)
        {
            point = default;
            if (teamId < 0 || teamId >= moveRoutes.Length || routeIndex < 0 || routeIndex >= moveRoutes[teamId].Count)
                return false;

            point = moveRoutes[teamId][routeIndex];
            return true;
        }

        private bool IssueOrderInternal(ArmyOrder order, bool replaceRoute)
        {
            ResolveManager();
            ArmyRuntimeState army = GetArmy(order.teamId);
            if (manager == null || army == null)
                return false;

            if (replaceRoute)
            {
                moveRoutes[order.teamId].Clear();
                if (order.type == ArmyOrderType.Move && order.hasTarget)
                    moveRoutes[order.teamId].Add(order.target);
            }

            switch (order.type)
            {
                case ArmyOrderType.Attack:
                    manager.ClearFlowTargetOverride(order.teamId);
                    manager.SetTeamNavigationOverride(order.teamId, true, true);
                    break;

                case ArmyOrderType.Move:
                    if (!order.hasTarget)
                        return false;
                    manager.SetTeamNavigationOverride(order.teamId, true, false);
                    manager.SetFlowTargetOverride(order.teamId, order.target);
                    break;

                case ArmyOrderType.Hold:
                    manager.ClearFlowTargetOverride(order.teamId);
                    manager.SetTeamNavigationOverride(order.teamId, false, false);
                    break;

                case ArmyOrderType.Retreat:
                    manager.SetTeamNavigationOverride(order.teamId, true, false);
                    manager.SetFlowTargetOverride(order.teamId, army.spawnCenter);
                    order.target = army.spawnCenter;
                    order.hasTarget = true;
                    break;

                default:
                    return false;
            }

            army.currentOrder = order;
            army.hasOrder = true;
            manager.StartBattle();
            phase = WarSandboxBattlePhase.Running;
            return true;
        }

        public bool StartDefaultBattle()
        {
            if (!initialized)
                RebuildArmyStates();

            bool issuedAnyOrder = false;
            for (int teamId = 0; teamId < armies.Length; teamId++)
            {
                if (armies[teamId].initialUnitCount > 0)
                {
                    issuedAnyOrder |= gameMode == WarSandboxGameMode.ControlPoint
                        ? IssueMoveOrder(teamId, controlPointCenter, false)
                        : IssueOrder(ArmyOrder.Attack(teamId));
                }
            }

            return issuedAnyOrder;
        }

        public bool RestartWithDefaultOrders()
        {
            ResetBattle();
            return StartDefaultBattle();
        }

        public int GetAliveUnitCount(int teamId)
        {
            ArmyRuntimeState army = GetArmy(teamId);
            if (army == null)
                return 0;

            BattleTelemetrySnapshot snapshot = TelemetrySnapshot;
            if (!snapshot.valid)
                return army.initialUnitCount;

            return teamId == 0 ? snapshot.aliveAttackers : snapshot.aliveDefenders;
        }

        public void StartOrResumeBattle()
        {
            ResolveManager();
            if (manager == null)
                return;

            manager.StartBattle();
            phase = WarSandboxBattlePhase.Running;
        }

        public void PauseBattle()
        {
            ResolveManager();
            if (manager == null)
                return;

            manager.PauseBattle();
            if (!IsTerminalPhase(phase))
                phase = WarSandboxBattlePhase.Paused;
        }

        public void TogglePause()
        {
            if (phase == WarSandboxBattlePhase.Running)
                PauseBattle();
            else if (!IsTerminalPhase(phase))
                StartOrResumeBattle();
        }

        public void ResetBattle()
        {
            ResolveManager();
            if (manager == null)
                return;

            manager.PauseBattle();
            manager.ResetScenario();
            manager.PauseBattle();

            for (int i = 0; i < armies.Length; i++)
            {
                armies[i].currentOrder = default;
                armies[i].hasOrder = false;
                moveRoutes[i].Clear();
            }

            simulationSpeed = 1f;
            Time.timeScale = 1f;
            phase = WarSandboxBattlePhase.Setup;
            battleResult = default;
            controlPointProgress = 0f;
            initialized = false;
            RebuildArmyStates();
            ConfigureControlPointTelemetry();
        }

        public void SetSimulationSpeed(float speed)
        {
            simulationSpeed = Mathf.Clamp(speed, 0.25f, 4f);
            Time.timeScale = simulationSpeed;
        }

        public void RebuildArmyStates()
        {
            ResolveManager();
            if (manager == null || manager.scenarioConfig == null || manager.scenarioConfig.unitTypes == null)
                return;

            int[] counts = new int[2];
            Vector3[] weightedCenters = new Vector3[2];
            UnitTypeConfig[] unitTypes = manager.scenarioConfig.unitTypes;

            for (int i = 0; i < unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = unitTypes[i];
                if (unitType == null || unitType.spawnConfig == null || unitType.teamId < 0 || unitType.teamId >= armies.Length)
                    continue;

                int count = Mathf.Max(0, unitType.spawnConfig.unitCount);
                counts[unitType.teamId] += count;
                weightedCenters[unitType.teamId] += unitType.spawnConfig.spawnCenter * count;
            }

            for (int teamId = 0; teamId < armies.Length; teamId++)
            {
                armies[teamId].initialUnitCount = counts[teamId];
                armies[teamId].spawnCenter = counts[teamId] > 0
                    ? weightedCenters[teamId] / counts[teamId]
                    : Vector3.zero;
            }

            initialized = counts[0] > 0 || counts[1] > 0;
        }

        private void EvaluateVictory()
        {
            if (phase != WarSandboxBattlePhase.Running || manager == null || manager.Telemetry == null)
                return;

            BattleTelemetrySnapshot snapshot = manager.Telemetry.Snapshot;
            if (!snapshot.valid || snapshot.totalAgents <= 0)
                return;

            bool attackersDefeated = armies[0].initialUnitCount > 0 && snapshot.aliveAttackers <= 0;
            bool defendersDefeated = armies[1].initialUnitCount > 0 && snapshot.aliveDefenders <= 0;
            if (!attackersDefeated && !defendersDefeated)
                return;

            WarSandboxBattlePhase resultPhase;
            if (attackersDefeated && defendersDefeated)
                resultPhase = WarSandboxBattlePhase.Draw;
            else
                resultPhase = attackersDefeated
                    ? WarSandboxBattlePhase.DefenderVictory
                    : WarSandboxBattlePhase.AttackerVictory;

            CompleteBattle(resultPhase, snapshot, WarSandboxVictoryReason.Annihilation);
        }

        private void EvaluateControlPoint()
        {
            if (phase != WarSandboxBattlePhase.Running || gameMode != WarSandboxGameMode.ControlPoint)
                return;

            BattleTelemetrySnapshot snapshot = TelemetrySnapshot;
            if (!snapshot.valid)
                return;

            controlPointProgress = WarSandboxControlPoint.ResolveProgress(
                controlPointProgress,
                snapshot.attackers.observationZoneCount,
                snapshot.defenders.observationZoneCount,
                Time.deltaTime,
                controlPointCaptureSeconds);

            if (controlPointProgress >= 1f)
                CompleteBattle(WarSandboxBattlePhase.AttackerVictory, snapshot, WarSandboxVictoryReason.ControlPoint);
            else if (controlPointProgress <= -1f)
                CompleteBattle(WarSandboxBattlePhase.DefenderVictory, snapshot, WarSandboxVictoryReason.ControlPoint);
        }

        private void CompleteBattle(
            WarSandboxBattlePhase resultPhase,
            BattleTelemetrySnapshot snapshot,
            WarSandboxVictoryReason victoryReason)
        {
            phase = resultPhase;
            battleResult = WarSandboxBattleResult.Capture(
                phase,
                armies[0].initialUnitCount,
                armies[1].initialUnitCount,
                snapshot,
                victoryReason);
            manager.PauseBattle();
        }

        private void ConfigureControlPointTelemetry()
        {
            ResolveManager();
            if (manager == null || manager.Telemetry == null)
                return;

            manager.Telemetry.ConfigureObservationZone(
                controlPointCenter,
                controlPointRadius,
                gameMode == WarSandboxGameMode.ControlPoint);
        }

        private void AdvanceMoveRoutes()
        {
            if (phase != WarSandboxBattlePhase.Running)
                return;

            BattleTelemetrySnapshot snapshot = TelemetrySnapshot;
            if (!snapshot.valid)
                return;

            for (int teamId = 0; teamId < moveRoutes.Length; teamId++)
            {
                List<Vector3> route = moveRoutes[teamId];
                if (route.Count <= 1)
                    continue;

                TeamSpatialTelemetry team = teamId == 0 ? snapshot.attackers : snapshot.defenders;
                if (!team.valid || !WarSandboxMoveRoute.HasReached(team.centroid, route[0], moveWaypointArrivalRadius))
                    continue;

                route.RemoveAt(0);
                IssueOrderInternal(ArmyOrder.Move(teamId, route[0]), false);
            }
        }

        private void ResolveManager()
        {
            if (manager == null)
                manager = GetComponent<MassEngineManager>();
        }

        private static bool IsTerminalPhase(WarSandboxBattlePhase value)
        {
            return value == WarSandboxBattlePhase.AttackerVictory ||
                   value == WarSandboxBattlePhase.DefenderVictory ||
                   value == WarSandboxBattlePhase.Draw;
        }
    }
}
