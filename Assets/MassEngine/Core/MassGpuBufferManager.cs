using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Owns every ComputeBuffer / RenderTexture of the MassEngine pipeline. Visible-index and
    /// indirect-args buffers are allocated per unit type x LOD (3 LODs), so buffer layout
    /// scales with the scenario instead of being hard-wired to two teams.
    /// </summary>
    public sealed class MassGpuBufferManager
    {
        public const int AgentStrideBytes = 56;
        public const int LodLevels = 3;
        public const int MaxEngagementSlotsPerTarget = 16;

        public ComputeBuffer agentBuffer;
        public ComputeBuffer agentPositionReadBuffer;
        public ComputeBuffer agentPositionWriteBuffer;
        public ComputeBuffer gridCountsBuffer;
        public ComputeBuffer gridAgentIndicesBuffer;
        public ComputeBuffer teamGridCountsBuffer;
        public ComputeBuffer teamGridAgentIndicesBuffer;
        public ComputeBuffer flowFieldDirectionsBuffer;
        public ComputeBuffer defenderFlowFieldDirectionsBuffer;
        public ComputeBuffer[] flowFieldDirectionsBuffers;
        public ComputeBuffer runtimeTargetDensityBuffer;
        public ComputeBuffer runtimeFlowStatsBuffer;
        public ComputeBuffer runtimeFlowTargetsBuffer;
        public ComputeBuffer unitTypeIndexBuffer;
        public ComputeBuffer unitTypeSettingsBuffer;
        public ComputeBuffer spatialHashStatsBuffer;
        public ComputeBuffer teamSpatialStatsBuffer;
        public RenderTexture runtimeAttackerFlowPreviewTexture;
        public RenderTexture runtimeDefenderFlowPreviewTexture;
        public RenderTexture[] runtimeFlowPreviewTextures;
        public RenderTexture densityMapTexture;

        public int teamCount = 2;

        private ComputeBuffer[] visibleIndexBuffers = System.Array.Empty<ComputeBuffer>();
        private ComputeBuffer[] drawArgsBuffers = System.Array.Empty<ComputeBuffer>();

        public readonly CombatBufferSet combatBuffers = new CombatBufferSet();

        public int AgentCount { get; private set; }
        public int GridCellCount { get; private set; }
        public int MaxAgentsPerCell { get; private set; }
        public int UnitTypeCount { get; private set; }

        public bool IsAllocated { get { return agentBuffer != null && AgentCount > 0; } }

        /// <summary>Written into spatialHashStats[3] at allocation; vanishing means a GPU device reset wiped buffer memory.</summary>
        public const int DeviceResetSentinel = 0x4D455631;

        public ComputeBuffer GetVisibleIndexBuffer(int unitTypeIndex, int lodLevel)
        {
            int index = unitTypeIndex * LodLevels + lodLevel;
            return index >= 0 && index < visibleIndexBuffers.Length ? visibleIndexBuffers[index] : null;
        }

        public ComputeBuffer GetDrawArgsBuffer(int unitTypeIndex, int lodLevel)
        {
            int index = unitTypeIndex * LodLevels + lodLevel;
            return index >= 0 && index < drawArgsBuffers.Length ? drawArgsBuffers[index] : null;
        }

        public void Allocate(int agentCount, int gridCellCount, int maxAgentsPerCell, int flowFieldResolutionX, int flowFieldResolutionZ, int unitTypeCount)
        {
            ReleaseAll();

            AgentCount = Mathf.Max(0, agentCount);
            GridCellCount = Mathf.Max(1, gridCellCount);
            MaxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);
            UnitTypeCount = Mathf.Max(0, unitTypeCount);
            int safeFlowResolutionX = Mathf.Max(1, flowFieldResolutionX);
            int safeFlowResolutionZ = Mathf.Max(1, flowFieldResolutionZ);
            int safeFlowCellCount = safeFlowResolutionX * safeFlowResolutionZ;

            if (AgentCount <= 0 || UnitTypeCount <= 0)
                return;

            int agentStride = Marshal.SizeOf(typeof(AgentData));
            if (agentStride != AgentStrideBytes)
                Debug.LogError("MassEngine AgentData stride must remain 56 bytes. Actual: " + agentStride);

            int settingsStride = Marshal.SizeOf(typeof(UnitTypeGpuSettings));
            if (settingsStride != UnitTypeGpuSettings.StrideBytes)
                Debug.LogError("MassEngine UnitTypeGpuSettings stride must remain " + UnitTypeGpuSettings.StrideBytes + " bytes. Actual: " + settingsStride);

            agentBuffer = new ComputeBuffer(AgentCount, AgentStrideBytes);
            agentPositionReadBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 2);
            agentPositionWriteBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 2);
            gridCountsBuffer = new ComputeBuffer(GridCellCount, sizeof(int));
            long gridIndexCapacity = (long)GridCellCount * MaxAgentsPerCell;
            if (gridIndexCapacity > int.MaxValue / sizeof(int))
            {
                // A mis-scaled world (huge size / tiny cell) must fail loudly here, not
                // as an int-overflow ArgumentException deep inside ComputeBuffer.
                Debug.LogError("MassEngine: grid index buffer would need " + gridIndexCapacity + " entries (cells " + GridCellCount + " x perCell " + MaxAgentsPerCell + "); refusing to allocate. Shrink simulationWorldSize or raise cellSize.");
                ReleaseAll();
                return;
            }

            gridAgentIndicesBuffer = new ComputeBuffer(GridCellCount * MaxAgentsPerCell, sizeof(int));
            long teamGridIndexCapacity = gridIndexCapacity * 2L;
            if (teamGridIndexCapacity > int.MaxValue / sizeof(int))
            {
                Debug.LogError("MassEngine: team combat grid would need " + teamGridIndexCapacity + " entries; refusing to allocate. Shrink simulationWorldSize, raise cellSize, or lower maxAgentsPerCell.");
                ReleaseAll();
                return;
            }

            teamGridCountsBuffer = new ComputeBuffer(GridCellCount * 2, sizeof(int));
            teamGridAgentIndicesBuffer = new ComputeBuffer((int)teamGridIndexCapacity, sizeof(int));
            flowFieldDirectionsBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            defenderFlowFieldDirectionsBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            flowFieldDirectionsBuffers = new ComputeBuffer[teamCount];
            for (int i = 0; i < teamCount; i++)
            {
                flowFieldDirectionsBuffers[i] = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            }
            runtimeTargetDensityBuffer = new ComputeBuffer(safeFlowCellCount * teamCount, sizeof(uint));
            runtimeFlowStatsBuffer = new ComputeBuffer(4 * teamCount, sizeof(int));
            runtimeFlowTargetsBuffer = new ComputeBuffer(8 * teamCount, sizeof(float) * 4);
            unitTypeIndexBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            unitTypeSettingsBuffer = new ComputeBuffer(UnitTypeCount, UnitTypeGpuSettings.StrideBytes);
            spatialHashStatsBuffer = new ComputeBuffer(4, sizeof(int));
            teamSpatialStatsBuffer = new ComputeBuffer(16, sizeof(int));
            // stats[3] carries a sentinel no kernel ever writes: if a telemetry readback
            // sees it gone, GPU memory was wiped (device reset/TDR) and the manager
            // reinitializes. Slots 1-2 stay reserved; slot 0 is the overflow counter.
            spatialHashStatsBuffer.SetData(new[] { 0, 0, 0, DeviceResetSentinel });
            teamSpatialStatsBuffer.SetData(new int[16]);
            runtimeAttackerFlowPreviewTexture = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            runtimeDefenderFlowPreviewTexture = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            runtimeFlowPreviewTextures = new RenderTexture[teamCount];
            for (int i = 0; i < teamCount; i++)
            {
                runtimeFlowPreviewTextures[i] = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            }
            densityMapTexture = CreateDensityMapTexture(safeFlowResolutionX, safeFlowResolutionZ);

            // GPU buffer contents are undefined after allocation; zero-fill everything a
            // kernel may read before its first producer runs.
            flowFieldDirectionsBuffer.SetData(new Vector2[safeFlowCellCount]);
            defenderFlowFieldDirectionsBuffer.SetData(new Vector2[safeFlowCellCount]);
            Vector2[] zeroFlow = new Vector2[safeFlowCellCount];
            for (int i = 0; i < teamCount; i++)
            {
                flowFieldDirectionsBuffers[i].SetData(zeroFlow);
            }
            gridCountsBuffer.SetData(new int[GridCellCount]);
            teamGridCountsBuffer.SetData(new int[GridCellCount * 2]);

            combatBuffers.teamIdBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.hpReadBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.hpWriteBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.targetAgentIndexBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.engagementSlotAssignmentBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.engagementSlotOccupancyBuffer = new ComputeBuffer(AgentCount * MaxEngagementSlotsPerTarget, sizeof(uint));
            combatBuffers.attackCooldownBuffer = new ComputeBuffer(AgentCount, sizeof(float));
            combatBuffers.homePositionBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 3);
            combatBuffers.pendingDamageReadBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.pendingDamageWriteBuffer = new ComputeBuffer(AgentCount, sizeof(int));

            int bucketCount = UnitTypeCount * LodLevels;
            visibleIndexBuffers = new ComputeBuffer[bucketCount];
            drawArgsBuffers = new ComputeBuffer[bucketCount];
            for (int i = 0; i < bucketCount; i++)
            {
                visibleIndexBuffers[i] = CreateAppendIndexBuffer(AgentCount);
                drawArgsBuffers[i] = CreateArgsBuffer();
            }
        }

        public void UploadInitialData(AgentData[] agents, int[] teamIds, int[] hpValues, int[] unitTypeIndices)
        {
            if (!IsAllocated || agents == null)
                return;

            agentBuffer.SetData(agents);

            Vector2[] positions = new Vector2[agents.Length];
            Vector3[] homePositions = new Vector3[agents.Length];
            int[] targetIndices = new int[agents.Length];
            int[] slotAssignments = new int[agents.Length];
            float[] cooldowns = new float[agents.Length];
            int[] pendingDamage = new int[agents.Length];

            for (int i = 0; i < agents.Length; i++)
            {
                positions[i] = new Vector2(agents[i].position.x, agents[i].position.z);
                homePositions[i] = agents[i].position;
                targetIndices[i] = -1;
                slotAssignments[i] = -1;
            }

            agentPositionReadBuffer.SetData(positions);
            agentPositionWriteBuffer.SetData(positions);
            combatBuffers.homePositionBuffer.SetData(homePositions);
            combatBuffers.targetAgentIndexBuffer.SetData(targetIndices);
            combatBuffers.engagementSlotAssignmentBuffer.SetData(slotAssignments);
            combatBuffers.attackCooldownBuffer.SetData(cooldowns);
            combatBuffers.pendingDamageReadBuffer.SetData(pendingDamage);
            combatBuffers.pendingDamageWriteBuffer.SetData(pendingDamage);

            if (teamIds != null)
                combatBuffers.teamIdBuffer.SetData(teamIds);
            if (hpValues != null)
            {
                combatBuffers.hpReadBuffer.SetData(hpValues);
                combatBuffers.hpWriteBuffer.SetData(hpValues);
            }
            if (unitTypeIndices != null)
                unitTypeIndexBuffer.SetData(unitTypeIndices);
        }

        public void UploadUnitTypeSettings(UnitTypeGpuSettings[] settings)
        {
            if (unitTypeSettingsBuffer == null || settings == null || settings.Length != UnitTypeCount)
                return;

            unitTypeSettingsBuffer.SetData(settings);
        }

        public void ResetAppendCounters(int unitTypeIndex)
        {
            for (int lod = 0; lod < LodLevels; lod++)
                SetCounter(GetVisibleIndexBuffer(unitTypeIndex, lod));
        }

        public void CopyVisibleCountsToArgs(int unitTypeIndex)
        {
            for (int lod = 0; lod < LodLevels; lod++)
                CopyCount(GetVisibleIndexBuffer(unitTypeIndex, lod), GetDrawArgsBuffer(unitTypeIndex, lod));
        }

        public void ConfigureDrawArgs(IReadOnlyList<IUnitType> unitTypes)
        {
            if (unitTypes == null)
                return;

            for (int i = 0; i < unitTypes.Count && i < UnitTypeCount; i++)
            {
                ResolvedUnitTypeRuntime runtime = unitTypes[i].RenderRuntime;
                for (int lod = 0; lod < LodLevels; lod++)
                    SetArgs(GetDrawArgsBuffer(i, lod), runtime != null ? runtime.GetMesh(lod) : null);
            }
        }

        public void SwapSimulationBuffers()
        {
            ComputeBuffer positionTemp = agentPositionReadBuffer;
            agentPositionReadBuffer = agentPositionWriteBuffer;
            agentPositionWriteBuffer = positionTemp;
            combatBuffers.SwapPendingDamage();
            combatBuffers.SwapHp();
        }

        public void ReleaseAll()
        {
            // Combat buffers first: if anything below throws, the plain buffers are the
            // least likely to leak (they are all released through the same helper).
            combatBuffers.ReleaseAll();

            ReleaseBuffer(ref agentBuffer);
            ReleaseBuffer(ref agentPositionReadBuffer);
            ReleaseBuffer(ref agentPositionWriteBuffer);
            ReleaseBuffer(ref gridCountsBuffer);
            ReleaseBuffer(ref gridAgentIndicesBuffer);
            ReleaseBuffer(ref teamGridCountsBuffer);
            ReleaseBuffer(ref teamGridAgentIndicesBuffer);
            ReleaseBuffer(ref flowFieldDirectionsBuffer);
            ReleaseBuffer(ref defenderFlowFieldDirectionsBuffer);
            if (flowFieldDirectionsBuffers != null)
            {
                for (int i = 0; i < flowFieldDirectionsBuffers.Length; i++)
                    ReleaseBuffer(ref flowFieldDirectionsBuffers[i]);
                flowFieldDirectionsBuffers = null;
            }
            ReleaseBuffer(ref runtimeTargetDensityBuffer);
            ReleaseBuffer(ref runtimeFlowStatsBuffer);
            ReleaseBuffer(ref runtimeFlowTargetsBuffer);
            ReleaseBuffer(ref unitTypeIndexBuffer);
            ReleaseBuffer(ref unitTypeSettingsBuffer);
            ReleaseBuffer(ref spatialHashStatsBuffer);
            ReleaseBuffer(ref teamSpatialStatsBuffer);

            for (int i = 0; i < visibleIndexBuffers.Length; i++)
                ReleaseBuffer(ref visibleIndexBuffers[i]);
            for (int i = 0; i < drawArgsBuffers.Length; i++)
                ReleaseBuffer(ref drawArgsBuffers[i]);
            visibleIndexBuffers = System.Array.Empty<ComputeBuffer>();
            drawArgsBuffers = System.Array.Empty<ComputeBuffer>();

            ReleaseRenderTexture(ref runtimeAttackerFlowPreviewTexture);
            ReleaseRenderTexture(ref runtimeDefenderFlowPreviewTexture);
            if (runtimeFlowPreviewTextures != null)
            {
                for (int i = 0; i < runtimeFlowPreviewTextures.Length; i++)
                    ReleaseRenderTexture(ref runtimeFlowPreviewTextures[i]);
                runtimeFlowPreviewTextures = null;
            }
            ReleaseRenderTexture(ref densityMapTexture);

            AgentCount = 0;
            GridCellCount = 0;
            MaxAgentsPerCell = 0;
            UnitTypeCount = 0;
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

            // Fresh RT contents are driver-defined; clear to transparent black so
            // preview overlays (gizmos/HUD) show nothing until a kernel writes.
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
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
            if (Application.isPlaying)
                Object.Destroy(texture);
            else
                Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
