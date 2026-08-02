using System.Runtime.InteropServices;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Per-unit-type simulation parameters uploaded to the GPU as one
    /// StructuredBuffer element per registered unit type. Field order and size (112
    /// bytes) must match struct UnitTypeSettings in AgentDataCommon.hlsl exactly.
    /// This is THE channel through which unit-type configuration reaches the compute
    /// pipeline; no per-unit-type scalar uniforms exist anymore.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitTypeGpuSettings
    {
        public const int StrideBytes = 112;

        public float agentRadius;
        public float separationStrength;
        public float velocityDamping;
        public float maxSpeed;
        public float targetAcquireRadius;
        public float attackRange;
        public float attackInterval;
        public int attackDamage;
        public float densityAvoidanceStrength;
        public float densityPressureRangePerSqm;
        public float densitySpeedPenalty;
        public float speedVariation;
        public float laneBiasStrength;
        public float flowFieldWeight;
        public float flowFieldResponsiveness;
        public float attractionStrength;
        public float idleClipDuration;
        public float moveClipDuration;
        public float attackClipDuration;
        public float deathClipDuration;
        public float moveAnimationSpeedMin;
        public float moveAnimationSpeedMax;
        public float densityComfortPerSqm;
        public int padding3;
        public int teamId;
        public int padding0;
        public int padding1;
        public int padding2;

        public static UnitTypeGpuSettings CreateDefaults(int teamId)
        {
            return new UnitTypeGpuSettings
            {
                agentRadius = 0.45f,
                separationStrength = 18f,
                velocityDamping = 5f,
                maxSpeed = 6f,
                targetAcquireRadius = 8f,
                attackRange = 1.35f,
                attackInterval = 0.8f,
                attackDamage = 10,
                densityAvoidanceStrength = 2f,
                densityPressureRangePerSqm = 1.2f,
                densitySpeedPenalty = 0.45f,
                speedVariation = 0.08f,
                laneBiasStrength = 0.25f,
                flowFieldWeight = 1f,
                flowFieldResponsiveness = 6f,
                attractionStrength = 1f,
                idleClipDuration = 1f,
                moveClipDuration = 1f,
                attackClipDuration = 1f,
                deathClipDuration = 1.5f,
                moveAnimationSpeedMin = 0.85f,
                moveAnimationSpeedMax = 1.15f,
                densityComfortPerSqm = 0.6f,
                teamId = teamId
            };
        }

        /// <summary>
        /// Convenience path used by tests and tooling: defaults plus the contributions of
        /// the default module set. UnitTypeBase-built units go through their module
        /// instances instead, so custom modules can override any of this.
        /// </summary>
        public static UnitTypeGpuSettings FromConfig(UnitTypeConfig config)
        {
            UnitTypeGpuSettings settings = CreateDefaults(config != null ? config.teamId : 0);
            new DefaultMovementModule(config != null ? config.movementConfig : null).Contribute(ref settings);
            new DefaultFlockingModule(config != null ? config.flockingConfig : null).Contribute(ref settings);
            new DefaultCombatModule(config != null ? config.combatConfig : null).Contribute(ref settings);
            new DefaultAnimationModule(config != null ? config.animationConfig : null).Contribute(ref settings);
            return settings;
        }
    }
}
