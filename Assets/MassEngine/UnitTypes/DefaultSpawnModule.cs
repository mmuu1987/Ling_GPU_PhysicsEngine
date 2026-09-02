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
            int spawnedCount = Mathf.Max(0, end - offset);
            if (spawnedCount == 0)
                return;

            // A uniform random scatter starts a large fraction of the population in
            // overlap even at modest average density. Use a stable rectangular lattice
            // (the same broad formation model as UEBS-style ranks), then add only a
            // small deterministic interior jitter so the crowd does not look synthetic.
            float aspect = Mathf.Max(0.01f, size.z / Mathf.Max(0.01f, size.x));
            int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(spawnedCount * aspect)), 1, spawnedCount);
            int rows = Mathf.CeilToInt(spawnedCount / (float)columns);
            float stepX = rows > 1 ? size.x / (rows - 1) : 0f;
            float stepZ = columns > 1 ? size.z / (columns - 1) : 0f;
            float jitterX = stepX * 0.08f;
            float jitterZ = stepZ * 0.08f;

            for (int i = offset; i < end; i++)
            {
                int localIndex = i - offset;
                int row = localIndex / columns;
                int column = localIndex - row * columns;
                int entriesInRow = Mathf.Min(columns, spawnedCount - row * columns);

                float x = rows > 1 ? -halfSize.x + row * stepX : 0f;
                // Center a partial final rank instead of pinning it to one flank.
                float zStart = entriesInRow > 1 ? -stepZ * (entriesInRow - 1) * 0.5f : 0f;
                float z = zStart + column * stepZ;

                // Keep boundary ranks exact so the authored/resolved footprint remains
                // the formation contract. Only interior ranks receive deterministic
                // blue-noise-like jitter; no UnityEngine.Random global state is consumed.
                if (row > 0 && row < rows - 1)
                    x += SignedHash(localIndex, teamId, 0x68bc21ebu) * jitterX;
                if (column > 0 && column < entriesInRow - 1)
                    z += SignedHash(localIndex, teamId, 0x02e5be93u) * jitterZ;

                buffer[i] = new AgentData
                {
                    position = new Vector3(center.x + x, center.y, center.z + z),
                    rotation = Vector3.zero,
                    scale = Vector3.one,
                    velocity = Vector3.zero,
                    currentState = (int)AgentState.Idle,
                    currentAnimationTime = Hash01((uint)localIndex ^ ((uint)teamId * 0x9e3779b9u))
                };
            }
        }

        private static float SignedHash(int index, int teamId, uint salt)
        {
            uint seed = (uint)index ^ ((uint)teamId * 0x9e3779b9u) ^ salt;
            return Hash01(seed) * 2f - 1f;
        }

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00ffffffu) / 16777216f;
        }
    }
}
