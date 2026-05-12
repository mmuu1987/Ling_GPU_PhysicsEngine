using UnityEngine;

public static class MassAgentSpawnUtility_Stage6
{
    public struct CombatSpawnData
    {
        public GPUInstancingManager_Stage6.AgentData[] Agents;
        public Vector2[] AgentPositions;
        public int[] TeamIds;
        public int[] Hp;
        public int[] TargetAgentIndices;
        public float[] AttackCooldowns;
        public Vector3[] HomePositions;
        public int[] PendingDamage;
    }

    public static GPUInstancingManager_Stage6.AgentData[] BuildInitialAgents(
        int instanceCount,
        Vector3 spawnArea,
        bool spawnClusterForCollisionDemo,
        float clusteredSpawnRadius,
        float animationDuration)
    {
        var agents = new GPUInstancingManager_Stage6.AgentData[Mathf.Max(1, instanceCount)];

        for (int i = 0; i < agents.Length; i++)
        {
            Vector3 position = spawnClusterForCollisionDemo
                ? RandomClusterPosition(spawnArea, clusteredSpawnRadius)
                : RandomAreaPosition(spawnArea);

            agents[i] = new GPUInstancingManager_Stage6.AgentData
            {
                position = position,
                rotation = new Vector3(0f, Random.Range(0f, 360f), 0f),
                scale = Vector3.one,
                velocity = Random.insideUnitSphere * 0.1f,
                currentState = 0,
                currentAnimationTime = Random.Range(0f, animationDuration)
            };

            agents[i].velocity.y = 0f;
        }

        return agents;
    }

    public static CombatSpawnData BuildInitialCombatData(
        int instanceCount,
        bool enableTwoTeamCombat,
        int attackerCount,
        Vector3 fallbackSpawnArea,
        bool spawnClusterForCollisionDemo,
        float clusteredSpawnRadius,
        Vector3 attackerSpawnCenter,
        Vector3 attackerSpawnSize,
        Vector3 defenderSpawnCenter,
        Vector3 defenderSpawnSize,
        int attackerMaxHp,
        int defenderMaxHp,
        float animationDuration)
    {
        int safeCount = Mathf.Max(1, instanceCount);
        int safeAttackerCount = enableTwoTeamCombat ? Mathf.Clamp(attackerCount, 0, safeCount) : safeCount;
        int safeAttackerHp = Mathf.Max(1, attackerMaxHp);
        int safeDefenderHp = Mathf.Max(1, defenderMaxHp);

        var data = new CombatSpawnData
        {
            Agents = new GPUInstancingManager_Stage6.AgentData[safeCount],
            AgentPositions = new Vector2[safeCount],
            TeamIds = new int[safeCount],
            Hp = new int[safeCount],
            TargetAgentIndices = new int[safeCount],
            AttackCooldowns = new float[safeCount],
            HomePositions = new Vector3[safeCount],
            PendingDamage = new int[safeCount]
        };

        for (int i = 0; i < safeCount; i++)
        {
            bool isAttacker = i < safeAttackerCount;
            Vector3 position;
            float yaw;

            if (enableTwoTeamCombat)
            {
                int teamStartIndex = isAttacker ? 0 : safeAttackerCount;
                int teamCount = isAttacker ? safeAttackerCount : safeCount - safeAttackerCount;
                int teamLocalIndex = i - teamStartIndex;
                position = FormationGridPosition(
                    teamLocalIndex,
                    teamCount,
                    isAttacker ? attackerSpawnCenter : defenderSpawnCenter,
                    isAttacker ? attackerSpawnSize : defenderSpawnSize);
                yaw = isAttacker ? 90f : -90f;
            }
            else
            {
                position = spawnClusterForCollisionDemo
                    ? RandomClusterPosition(fallbackSpawnArea, clusteredSpawnRadius)
                    : RandomAreaPosition(fallbackSpawnArea);
                yaw = Random.Range(0f, 360f);
            }

            data.Agents[i] = new GPUInstancingManager_Stage6.AgentData
            {
                position = position,
                rotation = new Vector3(0f, yaw, 0f),
                scale = Vector3.one,
                velocity = enableTwoTeamCombat ? Vector3.zero : RandomFallbackVelocity(),
                currentState = 0,
                currentAnimationTime = Random.Range(0f, animationDuration)
            };

            data.TeamIds[i] = enableTwoTeamCombat && !isAttacker ? 1 : 0;
            data.AgentPositions[i] = new Vector2(position.x, position.z);
            data.Hp[i] = isAttacker ? safeAttackerHp : safeDefenderHp;
            data.TargetAgentIndices[i] = -1;
            data.AttackCooldowns[i] = Random.Range(0f, Mathf.Max(0.01f, animationDuration));
            data.HomePositions[i] = position;
            data.PendingDamage[i] = 0;
        }

        return data;
    }

    private static Vector3 RandomClusterPosition(Vector3 spawnArea, float clusteredSpawnRadius)
    {
        Vector2 p = Random.insideUnitCircle * Mathf.Max(0.01f, clusteredSpawnRadius);
        return new Vector3(p.x, Random.Range(-spawnArea.y, spawnArea.y), p.y);
    }

    private static Vector3 RandomAreaPosition(Vector3 spawnArea)
    {
        return new Vector3(
            Random.Range(-spawnArea.x, spawnArea.x),
            Random.Range(-spawnArea.y, spawnArea.y),
            Random.Range(-spawnArea.z, spawnArea.z));
    }

    private static Vector3 FormationGridPosition(int index, int count, Vector3 center, Vector3 size)
    {
        int safeCount = Mathf.Max(1, count);
        Vector3 safeSize = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0f, size.y), Mathf.Max(0.01f, size.z));
        float aspect = safeSize.x / safeSize.z;
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(safeCount * aspect)));
        int rows = Mathf.Max(1, Mathf.CeilToInt((float)safeCount / columns));
        int column = index % columns;
        int row = index / columns;

        float x = columns <= 1
            ? center.x
            : center.x - safeSize.x * 0.5f + safeSize.x * column / (columns - 1);
        float z = rows <= 1
            ? center.z
            : center.z - safeSize.z * 0.5f + safeSize.z * row / (rows - 1);

        return new Vector3(x, center.y, z);
    }

    private static Vector3 RandomFallbackVelocity()
    {
        Vector3 velocity = Random.insideUnitSphere * 0.1f;
        velocity.y = 0f;
        return velocity;
    }
}
