using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Thin game-layer coordinator for a battle of any number of armies. It translates
    /// designer/player intent into MassEngine runtime overrides; it never writes config
    /// assets and owns no duplicate simulation.
    ///
    /// Army slots are indexed by raw teamId and sized from the scenario. Combat, victory and
    /// navigation are all per team: every army in the scenario owns a slice of the flow field
    /// and takes its own orders.
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
        [Min(0)] public int selectedTeam;
        [Min(1f)] public float moveWaypointArrivalRadius = 8f;
        [Range(2, 16)] public int maxMoveRoutePoints = 8;

        [Header("Battle Rules")]
        public WarSandboxGameMode gameMode = WarSandboxGameMode.Annihilation;
        public Vector3 controlPointCenter = Vector3.zero;
        [Min(2f)] public float controlPointRadius = 30f;
        [Min(5f)] public float controlPointCaptureSeconds = 20f;

        [Header("Static Obstacles")]
        public bool staticObstaclesEnabled;
        [Tooltip("Use the custom obstacle array below instead of the built-in two-wall layout.")]
        public bool useCustomStaticObstacleLayout;
        [Min(0f)] public float staticObstacleClearance = 2f;
        public StaticObstacleRect[] staticObstacles =
        {
            new StaticObstacleRect(new Vector2(0f, -90f), new Vector2(14f, 110f)),
            new StaticObstacleRect(new Vector2(0f, 90f), new Vector2(14f, 110f))
        };

        // Indexed by raw teamId and widened by RebuildArmyStates to whatever the scenario
        // fields. An unused teamId in the middle stays an empty army rather than shifting the
        // ones after it, so the index a caller passes always means the same team.
        private ArmyRuntimeState[] armies = BuildArmies(MinimumArmyCount);
        private List<Vector3>[] moveRoutes = BuildMoveRoutes(MinimumArmyCount);
        private static readonly StaticObstacleRect[] DefaultStaticObstacles =
        {
            new StaticObstacleRect(new Vector2(0f, -90f), new Vector2(14f, 110f)),
            new StaticObstacleRect(new Vector2(0f, 90f), new Vector2(14f, 110f))
        };

        // The attacker and defender slots exist even in a scenario that fields neither, because
        // the flow fields, the HUD and the control-point mode all still name those two teams.
        private const int MinimumArmyCount = 2;

        private int[] victoryInitialCounts;
        private int[] victoryAliveCounts;
        private WarSandboxBattlePhase phase = WarSandboxBattlePhase.Setup;
        private WarSandboxBattleResult battleResult;
        private float simulationSpeed = 1f;
        private float controlPointProgress;
        private bool initialized;
        private WarSandboxStaticObstaclePresenter obstaclePresenter;

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
            ApplyStaticObstacleSettings();
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
            ApplyStaticObstacleSettings();
            AdvanceMoveRoutes();
            EvaluateVictory();
            EvaluateControlPoint();
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
                Time.timeScale = 1f;
            if (manager != null)
                manager.SetStaticObstacles(null, 0f);
        }

        private static ArmyRuntimeState[] BuildArmies(int teamCount)
        {
            ArmyRuntimeState[] built = new ArmyRuntimeState[Mathf.Max(MinimumArmyCount, teamCount)];
            for (int teamId = 0; teamId < built.Length; teamId++)
                built[teamId] = new ArmyRuntimeState { teamId = teamId, displayName = DefaultArmyName(teamId) };

            return built;
        }

        private static List<Vector3>[] BuildMoveRoutes(int teamCount)
        {
            List<Vector3>[] built = new List<Vector3>[Mathf.Max(MinimumArmyCount, teamCount)];
            for (int teamId = 0; teamId < built.Length; teamId++)
                built[teamId] = new List<Vector3>();

            return built;
        }

        /// <summary>
        /// Teams 0 and 1 keep the names the HUD has always shown them under; anything past that
        /// is numbered, because nothing in a many-army battle makes one of them "the defender".
        /// </summary>
        private static string DefaultArmyName(int teamId)
        {
            if (teamId == 0)
                return "攻方";
            if (teamId == 1)
                return "守方";

            return "第" + (teamId + 1) + "军团";
        }

        /// <summary>
        /// Grows the army slots to cover teamCount, keeping every existing state object so a
        /// standing order survives the widening. Never shrinks: a slot whose units are gone
        /// reports initialUnitCount 0, which the victory rule already treats as "did not field
        /// an army", and dropping it would invalidate a teamId a caller still holds.
        /// </summary>
        private void EnsureArmyCapacity(int teamCount)
        {
            int required = Mathf.Max(MinimumArmyCount, teamCount);
            if (armies != null && armies.Length >= required)
                return;

            ArmyRuntimeState[] grownArmies = BuildArmies(required);
            List<Vector3>[] grownRoutes = BuildMoveRoutes(required);
            if (armies != null)
            {
                for (int teamId = 0; teamId < armies.Length; teamId++)
                {
                    grownArmies[teamId] = armies[teamId];
                    grownRoutes[teamId] = moveRoutes[teamId];
                }
            }

            armies = grownArmies;
            moveRoutes = grownRoutes;
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

            ResolveManager();
            if (manager != null)
                target = manager.ResolvePointOutsideStaticObstacles(target);

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

        public bool SetStaticObstaclesEnabled(bool value)
        {
            if (phase != WarSandboxBattlePhase.Setup)
                return false;

            staticObstaclesEnabled = value;
            ApplyStaticObstacleSettings();
            return true;
        }

        public int GetStaticObstacleCount()
        {
            StaticObstacleRect[] resolved = ResolveStaticObstacles();
            return staticObstaclesEnabled && resolved != null
                ? Mathf.Min(resolved.Length, StaticObstacleMath.MaxObstacleCount)
                : 0;
        }

        public bool TryGetStaticObstacle(int obstacleIndex, out StaticObstacleRect obstacle)
        {
            obstacle = default;
            StaticObstacleRect[] resolved = ResolveStaticObstacles();
            if (!staticObstaclesEnabled || resolved == null || obstacleIndex < 0 || obstacleIndex >= resolved.Length)
                return false;
            obstacle = resolved[obstacleIndex];
            return obstacle.IsValid;
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

            if (order.type == ArmyOrderType.Move && order.hasTarget)
                order.target = manager.ResolvePointOutsideStaticObstacles(order.target);

            if (replaceRoute)
            {
                moveRoutes[order.teamId].Clear();
                if (order.type == ArmyOrderType.Move && order.hasTarget)
                    moveRoutes[order.teamId].Add(order.target);
            }

            switch (order.type)
            {
                case ArmyOrderType.Attack:
                    ClearFlowTarget(order.teamId);
                    ApplyTeamNavigation(order.teamId, true, true);
                    break;

                case ArmyOrderType.Move:
                    if (!order.hasTarget)
                        return false;
                    ApplyTeamNavigation(order.teamId, true, false);
                    ApplyFlowTarget(order.teamId, order.target);
                    break;

                case ArmyOrderType.Hold:
                    ClearFlowTarget(order.teamId);
                    ApplyTeamNavigation(order.teamId, false, false);
                    break;

                case ArmyOrderType.Retreat:
                    ApplyTeamNavigation(order.teamId, true, false);
                    ApplyFlowTarget(order.teamId, army.spawnCenter);
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

            // Past the teams the telemetry sample covers GetAliveCount returns 0, which would
            // read as annihilated; fall back to the roster instead of inventing a defeat.
            return teamId < snapshot.TeamCount ? snapshot.GetAliveCount(teamId) : army.initialUnitCount;
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
            ApplyStaticObstacleSettings();
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

            UnitTypeConfig[] unitTypes = manager.scenarioConfig.unitTypes;

            // First pass only sizes the slots; the counting pass below needs them to exist.
            int teamCount = MinimumArmyCount;
            for (int i = 0; i < unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = unitTypes[i];
                if (unitType == null || unitType.spawnConfig == null || unitType.teamId < 0)
                    continue;

                teamCount = Mathf.Max(teamCount, unitType.teamId + 1);
            }

            EnsureArmyCapacity(teamCount);

            int[] counts = new int[armies.Length];
            Vector3[] weightedCenters = new Vector3[armies.Length];

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

            initialized = false;
            for (int teamId = 0; teamId < counts.Length; teamId++)
                initialized |= counts[teamId] > 0;
        }

        private void EvaluateVictory()
        {
            if (phase != WarSandboxBattlePhase.Running || manager == null || manager.Telemetry == null)
                return;

            BattleTelemetrySnapshot snapshot = manager.Telemetry.Snapshot;
            if (!snapshot.valid || snapshot.totalAgents <= 0)
                return;

            if (victoryInitialCounts == null || victoryInitialCounts.Length != armies.Length)
            {
                victoryInitialCounts = new int[armies.Length];
                victoryAliveCounts = new int[armies.Length];
            }

            for (int teamId = 0; teamId < armies.Length; teamId++)
            {
                victoryInitialCounts[teamId] = armies[teamId].initialUnitCount;
                victoryAliveCounts[teamId] = GetAliveUnitCount(teamId);
            }

            if (!WarSandboxVictory.TryResolveAnnihilation(
                    victoryInitialCounts,
                    victoryAliveCounts,
                    out WarSandboxBattlePhase resultPhase,
                    out int winnerTeamId))
                return;

            CompleteBattle(resultPhase, snapshot, WarSandboxVictoryReason.Annihilation, winnerTeamId);
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
            WarSandboxVictoryReason victoryReason,
            int winnerTeamId = -1)
        {
            phase = resultPhase;
            // The attacker/defender totals stay in the result because every reader of it is still
            // written around those two; a many-army battle names its winner in winnerTeamId.
            battleResult = WarSandboxBattleResult.Capture(
                phase,
                armies[0].initialUnitCount,
                armies[1].initialUnitCount,
                snapshot,
                victoryReason,
                winnerTeamId);
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

                TeamSpatialTelemetry team = snapshot.GetTeam(teamId);
                if (!team.valid || !WarSandboxMoveRoute.HasReached(team.centroid, route[0], moveWaypointArrivalRadius))
                    continue;

                route.RemoveAt(0);
                IssueOrderInternal(ArmyOrder.Move(teamId, route[0]), false);
            }
        }

        /// <summary>
        /// Whether the engine allocated a flow field slice for this team. Every team in the
        /// scenario does; the check remains as a guard against an order for a team the engine
        /// never heard of (a stale HUD selection after the scenario shrank, say).
        /// </summary>
        private bool IsNavigableTeam(int teamId)
        {
            return manager != null && teamId >= 0 && teamId < manager.NavigableTeamCount;
        }

        private void ApplyTeamNavigation(int teamId, bool enabled, bool dynamicTargeting)
        {
            if (IsNavigableTeam(teamId))
                manager.SetTeamNavigationOverride(teamId, enabled, dynamicTargeting);
        }

        private void ApplyFlowTarget(int teamId, Vector3 point)
        {
            if (IsNavigableTeam(teamId))
                manager.SetFlowTargetOverride(teamId, point);
        }

        private void ClearFlowTarget(int teamId)
        {
            if (IsNavigableTeam(teamId))
                manager.ClearFlowTargetOverride(teamId);
        }

        private void ResolveManager()
        {
            if (manager == null)
                manager = GetComponent<MassEngineManager>();
        }

        private void ApplyStaticObstacleSettings()
        {
            ResolveManager();
            StaticObstacleRect[] active = staticObstaclesEnabled ? ResolveStaticObstacles() : null;
            if (manager != null)
                manager.SetStaticObstacles(active, staticObstacleClearance);

            if (!Application.isPlaying)
                return;
            if (obstaclePresenter == null)
                obstaclePresenter = GetComponent<WarSandboxStaticObstaclePresenter>();
            if (obstaclePresenter == null)
                obstaclePresenter = gameObject.AddComponent<WarSandboxStaticObstaclePresenter>();
            obstaclePresenter.Sync(active);
        }

        private StaticObstacleRect[] ResolveStaticObstacles()
        {
            return useCustomStaticObstacleLayout ? staticObstacles : DefaultStaticObstacles;
        }

        private static bool IsTerminalPhase(WarSandboxBattlePhase value)
        {
            return value == WarSandboxBattlePhase.AttackerVictory ||
                   value == WarSandboxBattlePhase.DefenderVictory ||
                   value == WarSandboxBattlePhase.ArmyVictory ||
                   value == WarSandboxBattlePhase.Draw;
        }
    }
}
