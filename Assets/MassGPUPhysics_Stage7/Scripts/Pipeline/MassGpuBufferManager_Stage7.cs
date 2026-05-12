using System.Runtime.InteropServices;
using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class MassGpuBufferManager_Stage7
    {
        public const int AgentStrideBytes = 56;

        public ComputeBuffer agentBuffer;
        public ComputeBuffer agentPositionReadBuffer;
        public ComputeBuffer agentPositionWriteBuffer;
        public ComputeBuffer gridCountsBuffer;
        public ComputeBuffer gridAgentIndicesBuffer;
        public ComputeBuffer flowFieldDirectionsBuffer;
        public ComputeBuffer defenderFlowFieldDirectionsBuffer;
        public ComputeBuffer runtimeAttackerTargetDensityBuffer;
        public ComputeBuffer runtimeAttackerFlowStatsBuffer;
        public ComputeBuffer runtimeAttackerFlowTargetsBuffer;
        public ComputeBuffer runtimeDefenderTargetDensityBuffer;
        public ComputeBuffer runtimeDefenderFlowStatsBuffer;
        public ComputeBuffer runtimeDefenderFlowTargetsBuffer;
        public ComputeBuffer nearAttackerAgentIndexBuffer;
        public ComputeBuffer midAttackerAgentIndexBuffer;
        public ComputeBuffer farAttackerAgentIndexBuffer;
        public ComputeBuffer nearDefenderAgentIndexBuffer;
        public ComputeBuffer midDefenderAgentIndexBuffer;
        public ComputeBuffer farDefenderAgentIndexBuffer;
        public ComputeBuffer nearAttackerArgsBuffer;
        public ComputeBuffer midAttackerArgsBuffer;
        public ComputeBuffer farAttackerArgsBuffer;
        public ComputeBuffer nearDefenderArgsBuffer;
        public ComputeBuffer midDefenderArgsBuffer;
        public ComputeBuffer farDefenderArgsBuffer;
        public RenderTexture runtimeAttackerFlowPreviewTexture;
        public RenderTexture runtimeDefenderFlowPreviewTexture;
        public RenderTexture densityMapTexture;

        public readonly CombatBufferSet combatBuffers = new CombatBufferSet();

        public int AgentCount { get; private set; }
        public int GridCellCount { get; private set; }
        public int MaxAgentsPerCell { get; private set; }

        public bool IsAllocated { get { return agentBuffer != null && AgentCount > 0; } }

        public void Allocate(int agentCount, int gridCellCount, int maxAgentsPerCell, int flowFieldResolutionX, int flowFieldResolutionZ)
        {
            ReleaseAll();

            AgentCount = Mathf.Max(0, agentCount);
            GridCellCount = Mathf.Max(1, gridCellCount);
            MaxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);
            int safeFlowResolutionX = Mathf.Max(1, flowFieldResolutionX);
            int safeFlowResolutionZ = Mathf.Max(1, flowFieldResolutionZ);
            int safeFlowCellCount = safeFlowResolutionX * safeFlowResolutionZ;

            if (AgentCount <= 0)
                return;

            int agentStride = Marshal.SizeOf(typeof(AgentData));
            if (agentStride != AgentStrideBytes)
                Debug.LogError("Stage7 AgentData stride must remain 56 bytes. Actual: " + agentStride);

            agentBuffer = new ComputeBuffer(AgentCount, AgentStrideBytes);
            agentPositionReadBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 2);
            agentPositionWriteBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 2);
            gridCountsBuffer = new ComputeBuffer(GridCellCount, sizeof(int));
            gridAgentIndicesBuffer = new ComputeBuffer(GridCellCount * MaxAgentsPerCell, sizeof(int));
            flowFieldDirectionsBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            defenderFlowFieldDirectionsBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            runtimeAttackerTargetDensityBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(uint));
            runtimeAttackerFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
            runtimeAttackerFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);
            runtimeDefenderTargetDensityBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(uint));
            runtimeDefenderFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
            runtimeDefenderFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);
            runtimeAttackerFlowPreviewTexture = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            runtimeDefenderFlowPreviewTexture = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            densityMapTexture = CreateDensityMapTexture(safeFlowResolutionX, safeFlowResolutionZ);

            combatBuffers.teamIdBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.hpBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.targetAgentIndexBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.attackCooldownBuffer = new ComputeBuffer(AgentCount, sizeof(float));
            combatBuffers.homePositionBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 3);
            combatBuffers.pendingDamageReadBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.pendingDamageWriteBuffer = new ComputeBuffer(AgentCount, sizeof(int));

            nearAttackerAgentIndexBuffer = CreateAppendIndexBuffer(AgentCount);
            midAttackerAgentIndexBuffer = CreateAppendIndexBuffer(AgentCount);
            farAttackerAgentIndexBuffer = CreateAppendIndexBuffer(AgentCount);
            nearDefenderAgentIndexBuffer = CreateAppendIndexBuffer(AgentCount);
            midDefenderAgentIndexBuffer = CreateAppendIndexBuffer(AgentCount);
            farDefenderAgentIndexBuffer = CreateAppendIndexBuffer(AgentCount);

            nearAttackerArgsBuffer = CreateArgsBuffer();
            midAttackerArgsBuffer = CreateArgsBuffer();
            farAttackerArgsBuffer = CreateArgsBuffer();
            nearDefenderArgsBuffer = CreateArgsBuffer();
            midDefenderArgsBuffer = CreateArgsBuffer();
            farDefenderArgsBuffer = CreateArgsBuffer();
        }

        public void UploadInitialData(AgentData[] agents, int[] teamIds, int[] hpValues)
        {
            if (!IsAllocated || agents == null)
                return;

            agentBuffer.SetData(agents);

            Vector2[] positions = new Vector2[agents.Length];
            Vector3[] homePositions = new Vector3[agents.Length];
            int[] targetIndices = new int[agents.Length];
            float[] cooldowns = new float[agents.Length];
            int[] pendingDamage = new int[agents.Length];

            for (int i = 0; i < agents.Length; i++)
            {
                positions[i] = new Vector2(agents[i].position.x, agents[i].position.z);
                homePositions[i] = agents[i].position;
                targetIndices[i] = -1;
            }

            agentPositionReadBuffer.SetData(positions);
            agentPositionWriteBuffer.SetData(positions);
            combatBuffers.homePositionBuffer.SetData(homePositions);
            combatBuffers.targetAgentIndexBuffer.SetData(targetIndices);
            combatBuffers.attackCooldownBuffer.SetData(cooldowns);
            combatBuffers.pendingDamageReadBuffer.SetData(pendingDamage);
            combatBuffers.pendingDamageWriteBuffer.SetData(pendingDamage);

            if (teamIds != null)
                combatBuffers.teamIdBuffer.SetData(teamIds);
            if (hpValues != null)
                combatBuffers.hpBuffer.SetData(hpValues);
        }

        public void ResetAppendCounters()
        {
            SetCounter(nearAttackerAgentIndexBuffer);
            SetCounter(midAttackerAgentIndexBuffer);
            SetCounter(farAttackerAgentIndexBuffer);
            SetCounter(nearDefenderAgentIndexBuffer);
            SetCounter(midDefenderAgentIndexBuffer);
            SetCounter(farDefenderAgentIndexBuffer);
        }

        public void CopyVisibleCountsToArgs()
        {
            CopyCount(nearAttackerAgentIndexBuffer, nearAttackerArgsBuffer);
            CopyCount(midAttackerAgentIndexBuffer, midAttackerArgsBuffer);
            CopyCount(farAttackerAgentIndexBuffer, farAttackerArgsBuffer);
            CopyCount(nearDefenderAgentIndexBuffer, nearDefenderArgsBuffer);
            CopyCount(midDefenderAgentIndexBuffer, midDefenderArgsBuffer);
            CopyCount(farDefenderAgentIndexBuffer, farDefenderArgsBuffer);
        }

        public void ConfigureDrawArgs(RenderConfig attackerRender, RenderConfig defenderRender)
        {
            SetArgs(nearAttackerArgsBuffer, attackerRender != null ? attackerRender.nearMesh : null);
            SetArgs(midAttackerArgsBuffer, attackerRender != null ? attackerRender.midMesh : null);
            SetArgs(farAttackerArgsBuffer, attackerRender != null ? attackerRender.farMesh : null);
            SetArgs(nearDefenderArgsBuffer, defenderRender != null ? defenderRender.nearMesh : null);
            SetArgs(midDefenderArgsBuffer, defenderRender != null ? defenderRender.midMesh : null);
            SetArgs(farDefenderArgsBuffer, defenderRender != null ? defenderRender.farMesh : null);
        }

        public void SwapSimulationBuffers()
        {
            ComputeBuffer positionTemp = agentPositionReadBuffer;
            agentPositionReadBuffer = agentPositionWriteBuffer;
            agentPositionWriteBuffer = positionTemp;
            combatBuffers.SwapPendingDamage();
        }

        public void ReleaseAll()
        {
            ReleaseBuffer(ref agentBuffer);
            ReleaseBuffer(ref agentPositionReadBuffer);
            ReleaseBuffer(ref agentPositionWriteBuffer);
            ReleaseBuffer(ref gridCountsBuffer);
            ReleaseBuffer(ref gridAgentIndicesBuffer);
            ReleaseBuffer(ref flowFieldDirectionsBuffer);
            ReleaseBuffer(ref defenderFlowFieldDirectionsBuffer);
            ReleaseBuffer(ref runtimeAttackerTargetDensityBuffer);
            ReleaseBuffer(ref runtimeAttackerFlowStatsBuffer);
            ReleaseBuffer(ref runtimeAttackerFlowTargetsBuffer);
            ReleaseBuffer(ref runtimeDefenderTargetDensityBuffer);
            ReleaseBuffer(ref runtimeDefenderFlowStatsBuffer);
            ReleaseBuffer(ref runtimeDefenderFlowTargetsBuffer);
            ReleaseBuffer(ref nearAttackerAgentIndexBuffer);
            ReleaseBuffer(ref midAttackerAgentIndexBuffer);
            ReleaseBuffer(ref farAttackerAgentIndexBuffer);
            ReleaseBuffer(ref nearDefenderAgentIndexBuffer);
            ReleaseBuffer(ref midDefenderAgentIndexBuffer);
            ReleaseBuffer(ref farDefenderAgentIndexBuffer);
            ReleaseBuffer(ref nearAttackerArgsBuffer);
            ReleaseBuffer(ref midAttackerArgsBuffer);
            ReleaseBuffer(ref farAttackerArgsBuffer);
            ReleaseBuffer(ref nearDefenderArgsBuffer);
            ReleaseBuffer(ref midDefenderArgsBuffer);
            ReleaseBuffer(ref farDefenderArgsBuffer);
            ReleaseRenderTexture(ref runtimeAttackerFlowPreviewTexture);
            ReleaseRenderTexture(ref runtimeDefenderFlowPreviewTexture);
            ReleaseRenderTexture(ref densityMapTexture);
            combatBuffers.ReleaseAll();

            AgentCount = 0;
            GridCellCount = 0;
            MaxAgentsPerCell = 0;
        }

        public static void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
                return;

            buffer.Release();
            buffer = null;
        }

        private static ComputeBuffer CreateAppendIndexBuffer(int count)
        {
            ComputeBuffer buffer = new ComputeBuffer(Mathf.Max(1, count), sizeof(uint), ComputeBufferType.Append);
            buffer.SetCounterValue(0);
            return buffer;
        }

        private static ComputeBuffer CreateArgsBuffer()
        {
            return new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        private static RenderTexture CreateFlowPreviewTexture(int width, int height)
        {
            RenderTexture texture = new RenderTexture(Mathf.Max(1, width), Mathf.Max(1, height), 0, RenderTextureFormat.ARGB32);
            texture.enableRandomWrite = true;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Create();
            return texture;
        }

        private static RenderTexture CreateDensityMapTexture(int width, int height)
        {
            RenderTexture texture = new RenderTexture(Mathf.Max(1, width), Mathf.Max(1, height), 0, RenderTextureFormat.RInt);
            texture.enableRandomWrite = true;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Create();
            return texture;
        }

        private static void SetCounter(ComputeBuffer buffer)
        {
            if (buffer != null)
                buffer.SetCounterValue(0);
        }

        private static void CopyCount(ComputeBuffer appendBuffer, ComputeBuffer argsBuffer)
        {
            if (appendBuffer != null && argsBuffer != null)
                ComputeBuffer.CopyCount(appendBuffer, argsBuffer, sizeof(uint));
        }

        private static void SetArgs(ComputeBuffer argsBuffer, Mesh mesh)
        {
            if (argsBuffer == null)
                return;

            uint[] args =
            {
                mesh != null ? mesh.GetIndexCount(0) : 0u,
                0u,
                mesh != null ? mesh.GetIndexStart(0) : 0u,
                mesh != null ? (uint)mesh.GetBaseVertex(0) : 0u,
                0u
            };
            argsBuffer.SetData(args);
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            Object.Destroy(texture);
            texture = null;
        }
    }
}
