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
        public WarSandboxBattlefieldConfig battlefieldConfig;
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
        private int controlPointOwnerTeamId = -1;
        private float controlPointCaptureProgress;
        private int[] controlPointZoneCounts;
        private bool initialized;
        private WarSandboxStaticObstaclePresenter obstaclePresenter;
        private WarSandboxBattlefieldConfig appliedBattlefieldConfig;
        private WarSandboxBattlefieldRules? initialBattlefieldRules;

        public string BattlefieldRuleError { get; private set; }
        public string CommandError { get; private set; }
        public event System.Action<WarSandboxSoundCue> FeedbackRequested;
        private readonly WarSandboxTerrainValidation terrainValidation = new WarSandboxTerrainValidation();

        public bool TryResolveGroundPoint(Vector3 point, out Vector3 grounded, out string error)
        {
            grounded = point; error = null;
            ResolveManager();
            if (manager == null) { error = "战场尚未就绪。"; return false; }
            if (!manager.TryGetTerrainContext(out var surface, out _, out error)) return false;
            if (surface == null) return true;
            if (!surface.TrySample(new Vector2(point.x, point.z), out var sample))
            { error = "目标超出地表边界。"; return false; }
            grounded = sample.Position;
            return true;
        }

        public bool TryRaycastGround(Ray ray, float distance, LayerMask mask, out Vector3 point, out string error)
        {
            point = default; error = null;
            ResolveManager();
            if (manager == null) { error = "战场尚未就绪。"; return false; }
            if (!manager.TryGetTerrainContext(out var surface, out _, out error)) return false;
            if (surface != null)
            {
                if (TerrainSurfaceQueries.Raycast(surface, ray, distance, out point)) return true;
                error = "未点击到当前地表。"; return false;
            }
            if (Physics.Raycast(ray, out var hit, distance, mask, QueryTriggerInteraction.Ignore))
            { point = hit.point; return true; }
            error = "未点击到地面。"; return false;
        }

        private bool RejectCommand(string error)
        { CommandError = error; FeedbackRequested?.Invoke(WarSandboxSoundCue.Rejected); return false; }

        public bool TryValidateTerrainDeployment(WarSandboxBattlefieldRules rules, out string error)
        {
            error = null;
            ResolveManager();
            if (manager == null) { error = "战场尚未就绪。"; return false; }
            if (!manager.TryGetTerrainContext(out var surface, out _, out error)) return false;
            if (surface == null) return true;
            if (!WarSandboxDeploymentDraft.TryCapture(manager.scenarioConfig, out var draft, out error)) return false;
            return terrainValidation.TryValidate(manager, new WarSandboxDeploymentDraft(draft.Snapshot(), rules), out _, out error);
        }

        // Queued waypoints are checked without replacing the current manager target. An army
        // cannot cross components, so all initial compositions must reach the requested cell.
        private bool TryResolveMoveTarget(int teamId, Vector3 point, out Vector3 target, out string error)
        {
            target = point; error = null;
            ResolveManager();
            if (manager == null) { error = "战场尚未就绪。"; return false; }
            if (!manager.TryGetTerrainContext(out var surface, out var navigation, out error)) return false;
            if (surface == null) { target = manager.ResolvePointOutsideStaticObstacles(point); return true; }
            var xz = new Vector2(point.x, point.z);
            if (navigation == null || !navigation.IsWalkable(xz) || !surface.TrySample(xz, out var sample))
            { error = "命令被拒绝：目标位于禁行地形、障碍或地表边界外。"; return false; }
            if (manager.scenarioConfig == null || manager.scenarioConfig.unitTypes == null)
            { error = "军团部署缺失。"; return false; }
            bool hasFormation = false;
            foreach (var unit in manager.scenarioConfig.unitTypes)
            {
                if (unit == null || unit.teamId != teamId || unit.spawnConfig == null || unit.spawnConfig.unitCount <= 0) continue;
                hasFormation = true;
                var start = unit.spawnConfig.spawnCenter;
                if (!navigation.AreConnected(new Vector2(start.x, start.z), xz))
                { error = "命令被拒绝：该军团有编成无法到达目标连通区。"; return false; }
            }
            if (!hasFormation) { error = "此军团没有已部署编成。"; return false; }
            target = sample.Position;
            return true;
        }

        public WarSandboxBattlePhase Phase { get { return phase; } }
        public WarSandboxBattleResult BattleResult { get { return battleResult; } }
        public float SimulationSpeed { get { return simulationSpeed; } }
        /// <summary>
        /// Signed compatibility view: team 0 is positive, team 1 is negative, and a
        /// later team is represented by the generic owner/progress properties below.
        /// </summary>
        public float ControlPointProgress
        {
            get
            {
                if (controlPointOwnerTeamId == 0)
                    return controlPointCaptureProgress;
                if (controlPointOwnerTeamId == 1)
                    return -controlPointCaptureProgress;
                return 0f;
            }
        }
        public float ControlPointCaptureProgress { get { return controlPointCaptureProgress; } }
        public int ControlPointOwnerTeamId { get { return controlPointOwnerTeamId; } }
        public bool IsControlPointContested
        {
            get
            {
                int occupyingTeams = 0;
                for (int teamId = 0; teamId < (controlPointZoneCounts != null ? controlPointZoneCounts.Length : 0); teamId++)
                {
                    if (controlPointZoneCounts[teamId] > 0)
                        occupyingTeams++;
                }

                return occupyingTeams > 1;
            }
        }
        public int AttackersInControlPoint { get { return GetControlPointUnitCount(0); } }
        public int DefendersInControlPoint { get { return GetControlPointUnitCount(1); } }
        /// <summary>
        /// Armies the roster currently holds, sized from the scenario by RebuildArmyStates.
        /// The HUD iterates this instead of assuming the attacker/defender pair, which is what
        /// makes a third army selectable rather than invisible.
        /// </summary>
        public int ArmyCount { get { return armies != null ? armies.Length : 0; } }
        public ArmyRuntimeState SelectedArmy { get { return GetArmy(selectedTeam); } }
        public BattleTelemetrySnapshot TelemetrySnapshot
        {
            get { return manager != null && manager.Telemetry != null ? manager.Telemetry.Snapshot : default; }
        }

        public int GetControlPointUnitCount(int teamId)
        {
            return controlPointZoneCounts != null && teamId >= 0 && teamId < controlPointZoneCounts.Length
                ? controlPointZoneCounts[teamId]
                : 0;
        }

        private void Reset()
        {
            manager = GetComponent<MassEngineManager>();
        }

        private void Awake()
        {
            appliedBattlefieldConfig = null;
            initialBattlefieldRules = null;
            ResolveManager();
            EnsureBattlefieldRules();
            RebuildArmyStates();
            ApplyStaticObstacleSettings();
        }

        private void Start()
        {
            if (manager == null || !EnsureBattlefieldRules())
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
        /// <summary>
        /// The army's HUD name. The front line keeps its doctrine names; any further army is
        /// numbered, which is why a third army reads as "第3军团" rather than as another defender.
        /// </summary>
        public static string DefaultArmyName(int teamId)
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
            if (IsTerminalPhase(phase) || !WarSandboxSceneSession.AllowsBattleCommands(this)) return false;
            if (!EnsureBattlefieldRules()) return false;
            ArmyRuntimeState army = GetArmy(teamId);
            if (army == null)
                return false;

            if (!TryResolveMoveTarget(teamId, target, out target, out string error)) return RejectCommand(error);

            List<Vector3> route = moveRoutes[teamId];
            bool hasActiveMoveRoute = army.hasOrder && army.currentOrder.type == ArmyOrderType.Move && route.Count > 0;
            if (!append || !hasActiveMoveRoute)
                return IssueOrder(ArmyOrder.Move(teamId, target));

            if (route.Count >= Mathf.Clamp(maxMoveRoutePoints, 2, 16))
                return RejectCommand("路线已达到航点上限。");

            CommandError = null;
            route.Add(target);
            FeedbackRequested?.Invoke(WarSandboxSoundCue.Command);
            return true;
        }

        public bool SetGameMode(WarSandboxGameMode value)
        {
            if (!WarSandboxSceneSession.AllowsBattleCommands(this)) return false;
            if (phase != WarSandboxBattlePhase.Setup)
                return false;

            gameMode = value;
            ResetControlPointState();
            ConfigureControlPointTelemetry();
            return true;
        }

        public WarSandboxBattlefieldRules CaptureBattlefieldRules()
        {
            return new WarSandboxBattlefieldRules
            {
                gameMode = gameMode,
                controlPointCenter = controlPointCenter,
                controlPointRadius = controlPointRadius,
                controlPointCaptureSeconds = controlPointCaptureSeconds,
                staticObstaclesEnabled = staticObstaclesEnabled,
                staticObstacleClearance = staticObstacleClearance,
                staticObstacles = ResolveStaticObstacles()
            }.Copy();
        }

        public bool TryApplyBattlefieldConfig(WarSandboxBattlefieldConfig config, out string error)
        {
            error = null;
            if (phase != WarSandboxBattlePhase.Setup)
            {
                error = "Battlefield rules can only be applied during Setup.";
                return false;
            }
            if (config == null)
            {
                error = "Choose a battlefield rules asset.";
                return false;
            }
            if (!config.TryCreateSnapshot(out WarSandboxBattlefieldRules snapshot, out error)) return false;
            if (!TryValidateTerrainDeployment(snapshot, out error)) return false;

            ApplyBattlefieldRules(snapshot);
            battlefieldConfig = config;
            appliedBattlefieldConfig = config;
            initialBattlefieldRules = snapshot.Copy();
            BattlefieldRuleError = null;
            return true;
        }

        private bool EnsureBattlefieldRules()
        {
            if (battlefieldConfig == null)
            {
                appliedBattlefieldConfig = null;
                initialBattlefieldRules = null;
                BattlefieldRuleError = null;
                return true;
            }
            if (appliedBattlefieldConfig == battlefieldConfig && initialBattlefieldRules.HasValue)
            {
                BattlefieldRuleError = null;
                return true;
            }
            if (TryApplyBattlefieldConfig(battlefieldConfig, out string error)) return true;
            BattlefieldRuleError = error;
            ResolveManager();
            if (manager != null) manager.PauseBattle();
            return false;
        }

        private void ApplyBattlefieldRules(WarSandboxBattlefieldRules rules)
        {
            gameMode = rules.gameMode;
            controlPointCenter = rules.controlPointCenter;
            controlPointRadius = rules.controlPointRadius;
            controlPointCaptureSeconds = rules.controlPointCaptureSeconds;
            staticObstaclesEnabled = rules.staticObstaclesEnabled;
            staticObstacleClearance = rules.staticObstacleClearance;
            useCustomStaticObstacleLayout = true;
            staticObstacles = rules.Copy().staticObstacles;
            ResetControlPointState();
            ConfigureControlPointTelemetry();
            ApplyStaticObstacleSettings();
        }

        public bool SetStaticObstaclesEnabled(bool value)
        {
            if (!WarSandboxSceneSession.AllowsBattleCommands(this)) return false;
            if (phase != WarSandboxBattlePhase.Setup)
                return false;

            var candidate = CaptureBattlefieldRules(); candidate.staticObstaclesEnabled = value;
            if (!TryValidateTerrainDeployment(candidate, out string error)) return RejectCommand(error);
            CommandError = null;
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
            if (IsTerminalPhase(phase) || !WarSandboxSceneSession.AllowsBattleCommands(this)) return false;
            if (!EnsureBattlefieldRules()) return false;
            ResolveManager();
            ArmyRuntimeState army = GetArmy(order.teamId);
            if (manager == null || army == null)
                return false;

            if (phase == WarSandboxBattlePhase.Setup &&
                !TryValidateTerrainDeployment(CaptureBattlefieldRules(), out string deploymentError)) return RejectCommand(deploymentError);
            if (order.type != ArmyOrderType.Attack && order.type != ArmyOrderType.Move &&
                order.type != ArmyOrderType.Hold && order.type != ArmyOrderType.Retreat) return false;
            if (order.type == ArmyOrderType.Retreat)
            { order.target = army.spawnCenter; order.hasTarget = true; }
            if (order.type == ArmyOrderType.Move || order.type == ArmyOrderType.Retreat)
            {
                if (!order.hasTarget) return RejectCommand("命令缺少目标。");
                if (!TryResolveMoveTarget(order.teamId, order.target, out order.target, out string error)) return RejectCommand(error);
                // Commit the authoritative target before changing navigation, route or order state.
                // A rejected target must leave every part of the previous command untouched.
                if (IsNavigableTeam(order.teamId) && !manager.TrySetFlowTargetOverride(order.teamId, order.target, out error))
                    return RejectCommand(error);
            }

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
                    break;

                case ArmyOrderType.Hold:
                    ClearFlowTarget(order.teamId);
                    ApplyTeamNavigation(order.teamId, false, false);
                    break;

                case ArmyOrderType.Retreat:
                    ApplyTeamNavigation(order.teamId, true, false);
                    break;

                default:
                    return false;
            }

            manager.NotifyMovementCommand(order.teamId);
            CommandError = null;
            army.currentOrder = order;
            army.hasOrder = true;
            bool firstOrder = phase == WarSandboxBattlePhase.Setup;
            manager.StartBattle();
            phase = WarSandboxBattlePhase.Running;
            FeedbackRequested?.Invoke(firstOrder ? WarSandboxSoundCue.Start : WarSandboxSoundCue.Command);
            return true;
        }

        public bool StartDefaultBattle()
        {
            if (IsTerminalPhase(phase) || !WarSandboxSceneSession.AllowsBattleCommands(this)) return false;
            if (!EnsureBattlefieldRules()) return false;
            if (!initialized)
                RebuildArmyStates();

            // A terrain control-point start is all-or-nothing; do not start one army and
            // report success while another army's unreachable default order was rejected.
            if (gameMode == WarSandboxGameMode.ControlPoint)
                for (int teamId = 0; teamId < armies.Length; teamId++)
                    if (armies[teamId].initialUnitCount > 0 &&
                        !TryResolveMoveTarget(teamId, controlPointCenter, out _, out string error)) return RejectCommand(error);
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
            if (battleResult.valid && battleResult.TryGetArmy(teamId, out var settled)) return settled.survivors;
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
            if (IsTerminalPhase(phase) || !WarSandboxSceneSession.AllowsBattleCommands(this)) return;
            if (!EnsureBattlefieldRules()) return;
            ResolveManager();
            if (manager == null)
                return;

            if (phase == WarSandboxBattlePhase.Setup &&
                !TryValidateTerrainDeployment(CaptureBattlefieldRules(), out string error)) { RejectCommand(error); return; }
            CommandError = null;
            bool firstStart = phase == WarSandboxBattlePhase.Setup;
            manager.StartBattle();
            phase = WarSandboxBattlePhase.Running;
            if (firstStart) FeedbackRequested?.Invoke(WarSandboxSoundCue.Start);
        }

        public bool EndBattle()
        {
            if ((phase != WarSandboxBattlePhase.Running && phase != WarSandboxBattlePhase.Paused) ||
                !WarSandboxSceneSession.AllowsBattleCommands(this) || manager == null) return false;
            CompleteBattle(WarSandboxBattlePhase.Ended, TelemetrySnapshot, WarSandboxVictoryReason.ManualEnd);
            return true;
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
            ResetBattleState(true);
        }

        internal void ResetForDeployment() => ResetBattleState(false);
        internal void ResetForDeployment(WarSandboxBattlefieldRules rules)
        {
            ApplyBattlefieldRules(rules);
            ResetBattleState(false);
        }
        internal void CommitDeploymentRules() => initialBattlefieldRules = CaptureBattlefieldRules().Copy();

        private void ResetBattleState(bool restoreRules)
        {
            if (!WarSandboxSceneSession.AllowsBattleCommands(this)) return;
            ResolveManager();
            if (manager == null || !EnsureBattlefieldRules())
                return;

            manager.PauseBattle();
            if (restoreRules && initialBattlefieldRules.HasValue) ApplyBattlefieldRules(initialBattlefieldRules.Value);
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
            CommandError = null;
            ResetControlPointState();
            initialized = false;
            RebuildArmyStates();
            ConfigureControlPointTelemetry();
            ApplyStaticObstacleSettings();
        }

        public void SetSimulationSpeed(float speed)
        {
            if (!WarSandboxSceneSession.AllowsBattleCommands(this)) return;
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
            EnsureControlPointZoneCounts();

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
                if (counts[teamId] > 0 && TryResolveGroundPoint(armies[teamId].spawnCenter, out var grounded, out _))
                    armies[teamId].spawnCenter = grounded;
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
            if (!snapshot.valid || snapshot.teams == null)
                return;

            int previousOwner = controlPointOwnerTeamId;
            bool wasContested = IsControlPointContested;
            EnsureControlPointZoneCounts();
            int fieldedTeamCount = 0;
            for (int teamId = 0; teamId < armies.Length; teamId++)
            {
                TeamSpatialTelemetry team = snapshot.GetTeam(teamId);
                controlPointZoneCounts[teamId] = team.valid ? Mathf.Max(0, team.observationZoneCount) : 0;
                if (armies[teamId].initialUnitCount > 0)
                    fieldedTeamCount++;
            }

            if (fieldedTeamCount < 2)
                return;

            WarSandboxControlPointState state = WarSandboxControlPoint.ResolveCapture(
                controlPointOwnerTeamId,
                controlPointCaptureProgress,
                controlPointZoneCounts,
                Time.deltaTime,
                controlPointCaptureSeconds);
            controlPointOwnerTeamId = state.ownerTeamId;
            controlPointCaptureProgress = state.progress;
            if (previousOwner != controlPointOwnerTeamId || wasContested != IsControlPointContested)
                FeedbackRequested?.Invoke(WarSandboxSoundCue.ControlPoint);

            if (!state.captured)
                return;

            WarSandboxBattlePhase resultPhase = state.ownerTeamId == 0 && fieldedTeamCount <= 2
                ? WarSandboxBattlePhase.AttackerVictory
                : state.ownerTeamId == 1 && fieldedTeamCount <= 2
                    ? WarSandboxBattlePhase.DefenderVictory
                    : WarSandboxBattlePhase.ArmyVictory;
            CompleteBattle(resultPhase, snapshot, WarSandboxVictoryReason.ControlPoint, state.ownerTeamId);
        }

        private void CompleteBattle(
            WarSandboxBattlePhase resultPhase,
            BattleTelemetrySnapshot snapshot,
            WarSandboxVictoryReason victoryReason,
            int winnerTeamId = -1)
        {
            if (battleResult.valid) return;
            phase = resultPhase;
            battleResult = WarSandboxBattleResult.Capture(phase, armies, snapshot, victoryReason, winnerTeamId);
            manager.PauseBattle();
            FeedbackRequested?.Invoke(WarSandboxSoundCue.Finish);
        }

        private void ResetControlPointState()
        {
            controlPointOwnerTeamId = -1;
            controlPointCaptureProgress = 0f;
            if (controlPointZoneCounts == null)
                return;

            for (int teamId = 0; teamId < controlPointZoneCounts.Length; teamId++)
                controlPointZoneCounts[teamId] = 0;
        }

        private void EnsureControlPointZoneCounts()
        {
            int required = armies != null ? armies.Length : MinimumArmyCount;
            if (controlPointZoneCounts == null || controlPointZoneCounts.Length != required)
                controlPointZoneCounts = new int[required];
        }

        private void ConfigureControlPointTelemetry()
        {
            ResolveManager();
            if (manager == null || manager.Telemetry == null)
                return;

            if (!TryResolveGroundPoint(controlPointCenter, out var center, out _)) return;
            manager.Telemetry.ConfigureObservationZone(
                center,
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

                if (IssueOrderInternal(ArmyOrder.Move(teamId, route[1]), false))
                    route.RemoveAt(0);
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
            if (manager != null && manager.TryGetTerrainContext(out var surface, out _, out _))
                obstaclePresenter.Sync(active, surface);
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
                   value == WarSandboxBattlePhase.Draw || value == WarSandboxBattlePhase.Ended;
        }
    }
}
