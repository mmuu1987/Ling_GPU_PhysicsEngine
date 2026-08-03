using System;
using UnityEngine;

namespace MassEngine.Game
{
    public enum ArmyOrderType
    {
        None = 0,
        Attack = 1,
        Move = 2,
        Hold = 3,
        Retreat = 4
    }

    public enum WarSandboxBattlePhase
    {
        Setup = 0,
        Running = 1,
        Paused = 2,
        AttackerVictory = 3,
        DefenderVictory = 4,
        Draw = 5
    }

    [Serializable]
    public struct WarSandboxBattleResult
    {
        public WarSandboxBattlePhase phase;
        public int attackerInitial;
        public int defenderInitial;
        public int attackerSurvivors;
        public int defenderSurvivors;
        public float battleSeconds;
        public int attackerFlowRebuilds;
        public int defenderFlowRebuilds;
        public int peakGridOverflowPerFrame;
        public bool valid;

        public int AttackerCasualties
        {
            get { return Mathf.Max(0, attackerInitial - attackerSurvivors); }
        }

        public int DefenderCasualties
        {
            get { return Mathf.Max(0, defenderInitial - defenderSurvivors); }
        }

        public static WarSandboxBattleResult Capture(
            WarSandboxBattlePhase phase,
            int attackerInitial,
            int defenderInitial,
            BattleTelemetrySnapshot telemetry)
        {
            return new WarSandboxBattleResult
            {
                phase = phase,
                attackerInitial = Mathf.Max(0, attackerInitial),
                defenderInitial = Mathf.Max(0, defenderInitial),
                attackerSurvivors = Mathf.Clamp(telemetry.aliveAttackers, 0, Mathf.Max(0, attackerInitial)),
                defenderSurvivors = Mathf.Clamp(telemetry.aliveDefenders, 0, Mathf.Max(0, defenderInitial)),
                battleSeconds = Mathf.Max(0f, telemetry.battleSeconds),
                attackerFlowRebuilds = Mathf.Max(0, telemetry.attackerFlowRebuilds),
                defenderFlowRebuilds = Mathf.Max(0, telemetry.defenderFlowRebuilds),
                peakGridOverflowPerFrame = Mathf.Max(0, telemetry.peakGridOverflowPerFrame),
                valid = true
            };
        }
    }

    [Serializable]
    public struct ArmyOrder
    {
        public int teamId;
        public ArmyOrderType type;
        public Vector3 target;
        public bool hasTarget;

        public static ArmyOrder Attack(int teamId)
        {
            return new ArmyOrder { teamId = teamId, type = ArmyOrderType.Attack };
        }

        public static ArmyOrder Move(int teamId, Vector3 target)
        {
            return new ArmyOrder { teamId = teamId, type = ArmyOrderType.Move, target = target, hasTarget = true };
        }

        public static ArmyOrder Hold(int teamId)
        {
            return new ArmyOrder { teamId = teamId, type = ArmyOrderType.Hold };
        }

        public static ArmyOrder Retreat(int teamId)
        {
            return new ArmyOrder { teamId = teamId, type = ArmyOrderType.Retreat };
        }
    }

    [Serializable]
    public sealed class ArmyRuntimeState
    {
        public int teamId;
        public string displayName;
        public int initialUnitCount;
        public Vector3 spawnCenter;
        public ArmyOrder currentOrder;
        public bool hasOrder;
    }
}
