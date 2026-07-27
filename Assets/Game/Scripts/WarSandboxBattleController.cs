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

        private readonly ArmyRuntimeState[] armies =
        {
            new ArmyRuntimeState { teamId = 0, displayName = "攻方" },
            new ArmyRuntimeState { teamId = 1, displayName = "守方" }
        };

        private WarSandboxBattlePhase phase = WarSandboxBattlePhase.Setup;
        private float simulationSpeed = 1f;
        private bool initialized;

        public WarSandboxBattlePhase Phase { get { return phase; } }
        public float SimulationSpeed { get { return simulationSpeed; } }
        public ArmyRuntimeState SelectedArmy { get { return GetArmy(selectedTeam); } }

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

            EvaluateVictory();
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
            ResolveManager();
            ArmyRuntimeState army = GetArmy(order.teamId);
            if (manager == null || army == null)
                return false;

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
            }

            simulationSpeed = 1f;
            Time.timeScale = 1f;
            phase = WarSandboxBattlePhase.Setup;
            initialized = false;
            RebuildArmyStates();
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

            if (attackersDefeated && defendersDefeated)
                phase = WarSandboxBattlePhase.Draw;
            else
                phase = attackersDefeated
                    ? WarSandboxBattlePhase.DefenderVictory
                    : WarSandboxBattlePhase.AttackerVictory;

            manager.PauseBattle();
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
