using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class DefaultCombatModule : ICombatModule
    {
        public CombatConfig Config { get; private set; }

        public DefaultCombatModule(CombatConfig config)
        {
            Config = config;
        }

        public int FindNearestEnemy(int agentIndex, int agentTeamId, SpatialHashQuery query, int[] teamIds, int[] hpValues)
        {
            if (query.positions == null || query.candidateIndices == null || teamIds == null || hpValues == null)
                return -1;
            if (agentIndex < 0 || agentIndex >= query.positions.Length)
                return -1;

            Vector3 agentPosition = query.positions[agentIndex];
            float bestDistanceSq = float.MaxValue;
            int bestIndex = -1;

            for (int i = 0; i < query.candidateIndices.Count; i++)
            {
                int candidate = query.candidateIndices[i];
                if (candidate == agentIndex || candidate < 0 || candidate >= query.positions.Length)
                    continue;
                if (candidate >= teamIds.Length || candidate >= hpValues.Length)
                    continue;
                if (teamIds[candidate] == agentTeamId || hpValues[candidate] <= 0)
                    continue;

                Vector3 delta = query.positions[candidate] - agentPosition;
                delta.y = 0f;
                float distanceSq = delta.sqrMagnitude;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestIndex = candidate;
                }
            }

            return bestIndex;
        }

        public bool IsInAttackRange(Vector3 agentPosition, Vector3 targetPosition, float attackRange)
        {
            Vector3 delta = targetPosition - agentPosition;
            delta.y = 0f;
            float range = Mathf.Max(0f, attackRange);
            return delta.sqrMagnitude <= range * range;
        }

        public int ComputeDamage(float cooldownRemaining, float attackInterval, int attackDamage)
        {
            if (cooldownRemaining > 0f)
                return 0;

            return Mathf.Max(0, attackDamage);
        }

        public AgentState ResolvePostDamageState(int hp)
        {
            return hp <= 0 ? AgentState.Dead : AgentState.Attack;
        }
    }
}
