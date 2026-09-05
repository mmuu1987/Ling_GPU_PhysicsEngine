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
        Draw = 5,
        /// <summary>
        /// One army out of three or more is left standing; which one is in
        /// WarSandboxBattleResult.winnerTeamId. Two-army battles keep reporting
        /// AttackerVictory/DefenderVictory so existing HUD and saves read the same as before.
        /// </summary>
        ArmyVictory = 6
    }

    public enum WarSandboxGameMode
    {
        Annihilation = 0,
        ControlPoint = 1
    }

    public enum WarSandboxVictoryReason
    {
        Annihilation = 0,
        ControlPoint = 1
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
        public WarSandboxVictoryReason victoryReason;
        /// <summary>Winning teamId, or -1 for a draw. The only way to name a winner past two armies.</summary>
        public int winnerTeamId;
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
            BattleTelemetrySnapshot telemetry,
            WarSandboxVictoryReason victoryReason = WarSandboxVictoryReason.Annihilation,
            int winnerTeamId = -1)
        {
            // Left at -1 by a two-army caller, the winner follows from the phase itself. Only an
            // ArmyVictory has to name it, because there the phase alone does not.
            if (winnerTeamId < 0)
            {
                if (phase == WarSandboxBattlePhase.AttackerVictory)
                    winnerTeamId = 0;
                else if (phase == WarSandboxBattlePhase.DefenderVictory)
                    winnerTeamId = 1;
            }

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
                victoryReason = victoryReason,
                winnerTeamId = winnerTeamId,
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

    public static class WarSandboxMoveRoute
    {
        public static bool HasReached(Vector3 armyCenter, Vector3 waypoint, float arrivalRadius)
        {
            Vector2 delta = new Vector2(armyCenter.x - waypoint.x, armyCenter.z - waypoint.z);
            float radius = Mathf.Max(0.1f, arrivalRadius);
            return delta.sqrMagnitude <= radius * radius;
        }
    }

    public static class WarSandboxVictory
    {
        /// <summary>
        /// The annihilation rule, generalized past two armies: among the armies that actually
        /// fielded units, count how many still have survivors. Two or more still standing means
        /// the battle goes on (returns false). Exactly one means that one won. Zero means both
        /// sides emptied inside the same sample, which is a draw.
        ///
        /// A slot that never fielded a unit is not a defeated army - an unused teamId in the
        /// middle of the range must not hand anyone a victory.
        /// </summary>
        public static bool TryResolveAnnihilation(
            int[] initialCounts,
            int[] aliveCounts,
            out WarSandboxBattlePhase phase,
            out int winnerTeamId)
        {
            phase = WarSandboxBattlePhase.Running;
            winnerTeamId = -1;

            if (initialCounts == null)
                return false;

            int engaged = 0;
            int standing = 0;
            for (int teamId = 0; teamId < initialCounts.Length; teamId++)
            {
                if (initialCounts[teamId] <= 0)
                    continue;

                engaged++;
                int alive = aliveCounts != null && teamId < aliveCounts.Length ? aliveCounts[teamId] : 0;
                if (alive <= 0)
                    continue;

                standing++;
                winnerTeamId = teamId;
            }

            // One army alone on the field has no battle to win, so its solitude never ends one.
            if (engaged < 2 || standing >= 2)
            {
                winnerTeamId = -1;
                return false;
            }

            if (standing == 0)
                phase = WarSandboxBattlePhase.Draw;
            else if (engaged <= 2 && winnerTeamId == 0)
                phase = WarSandboxBattlePhase.AttackerVictory;
            else if (engaged <= 2 && winnerTeamId == 1)
                phase = WarSandboxBattlePhase.DefenderVictory;
            else
                // Past two armies - or when the survivor is neither team 0 nor team 1 - the old
                // phases cannot name the winner, so winnerTeamId carries it instead.
                phase = WarSandboxBattlePhase.ArmyVictory;

            return true;
        }
    }

    public static class WarSandboxControlPoint
    {
        public static float ResolveProgress(
            float current,
            int attackersInZone,
            int defendersInZone,
            float deltaTime,
            float captureSeconds)
        {
            float step = Mathf.Max(0f, deltaTime) / Mathf.Max(1f, captureSeconds);
            if (attackersInZone > 0 && defendersInZone <= 0)
                return Mathf.Clamp(current + step, -1f, 1f);
            if (defendersInZone > 0 && attackersInZone <= 0)
                return Mathf.Clamp(current - step, -1f, 1f);
            if (attackersInZone <= 0 && defendersInZone <= 0)
                return Mathf.MoveTowards(current, 0f, step * 0.5f);
            return Mathf.Clamp(current, -1f, 1f);
        }
    }
}
