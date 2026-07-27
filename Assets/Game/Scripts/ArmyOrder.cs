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
