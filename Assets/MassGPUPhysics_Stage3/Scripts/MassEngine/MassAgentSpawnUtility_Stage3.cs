using UnityEngine;

public static class MassAgentSpawnUtility_Stage3
{
    public static GPUInstancingManager_Stage3.AgentData[] BuildInitialAgents(
        int instanceCount,
        Vector3 spawnArea,
        bool spawnClusterForCollisionDemo,
        float clusteredSpawnRadius,
        float animationDuration)
    {
        var agents = new GPUInstancingManager_Stage3.AgentData[Mathf.Max(1, instanceCount)];

        for (int i = 0; i < agents.Length; i++)
        {
            Vector3 position = spawnClusterForCollisionDemo
                ? RandomClusterPosition(spawnArea, clusteredSpawnRadius)
                : RandomAreaPosition(spawnArea);

            agents[i] = new GPUInstancingManager_Stage3.AgentData
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
}
