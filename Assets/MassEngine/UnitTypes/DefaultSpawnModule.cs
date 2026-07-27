using UnityEngine;

namespace MassEngine
{
    public sealed class DefaultSpawnModule : ISpawnModule
    {
        public SpawnConfig Config { get; private set; }

        public DefaultSpawnModule(SpawnConfig config)
        {
            Config = config;
        }

        public void GenerateAgents(AgentData[] buffer, int offset, int count, int teamId)
        {
            if (buffer == null)
                return;

            Vector3 center = Config != null ? Config.spawnCenter : Vector3.zero;
            Vector3 size = Config != null ? Config.ResolveSpawnSize() : new Vector3(35f, 0f, 80f);
            Vector3 halfSize = size * 0.5f;
            int end = Mathf.Min(buffer.Length, offset + Mathf.Max(0, count));

            for (int i = offset; i < end; i++)
            {
                buffer[i] = new AgentData
                {
                    position = new Vector3(
                        center.x + Random.Range(-halfSize.x, halfSize.x),
                        center.y,
                        center.z + Random.Range(-halfSize.z, halfSize.z)),
                    rotation = Vector3.zero,
                    scale = Vector3.one,
                    velocity = Vector3.zero,
                    currentState = (int)AgentState.Idle,
                    currentAnimationTime = Random.Range(0f, 1f)
                };
            }
        }
    }
}
