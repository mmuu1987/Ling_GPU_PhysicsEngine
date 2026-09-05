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
        public const int EngagementSlotsPerTarget = 8;
        /// <summary>Per-team slot count inside teamSpatialStats: [count, minX, minZ, maxX, maxZ, reserved x3].</summary>
        public const int TeamStatsSlotsPerTeam = 8;
        /// <summary>Default team count; two-team combat (attacker vs defender) is the historical layout.</summary>
        public const int DefaultTeamCount = 2;

        public ComputeBuffer agentBuffer;
        public ComputeBuffer agentPositionReadBuffer;
        public ComputeBuffer agentPositionWriteBuffer;
        public ComputeBuffer gridCountsBuffer;
        public ComputeBuffer gridAgentIndicesBuffer;
        public ComputeBuffer teamGridCountsBuffer;
        public ComputeBuffer teamGridAgentIndicesBuffer;
        public ComputeBuffer flowFieldDirectionsBuffer;
        public ComputeBuffer defenderFlowFieldDirectionsBuffer;
        public ComputeBuffer runtimeAttackerTargetDensityBuffer;
        public ComputeBuffer runtimeAttackerFlowStatsBuffer;
        public ComputeBuffer runtimeAttackerFlowTargetsBuffer;
        public ComputeBuffer runtimeDefenderTargetDensityBuffer;
        public ComputeBuffer runtimeDefenderFlowStatsBuffer;
        public ComputeBuffer runtimeDefenderFlowTargetsBuffer;
        public ComputeBuffer unitTypeIndexBuffer;
        public ComputeBuffer unitTypeSettingsBuffer;
        public ComputeBuffer spatialHashStatsBuffer;
        public ComputeBuffer teamSpatialStatsBuffer;
        /// <summary>One stance per team (TeamStance values), indexed by raw teamId.</summary>
        public ComputeBuffer teamStanceBuffer;
        public RenderTexture runtimeAttackerFlowPreviewTexture;
        public RenderTexture runtimeDefenderFlowPreviewTexture;
        public RenderTexture densityMapTexture;
        public RenderTexture attackerDensityMapTexture;
        public RenderTexture defenderDensityMapTexture;

        private ComputeBuffer[] visibleIndexBuffers = System.Array.Empty<ComputeBuffer>();
        private ComputeBuffer[] drawArgsBuffers = System.Array.Empty<ComputeBuffer>();

        public readonly CombatBufferSet combatBuffers = new CombatBufferSet();

        public ComputeBuffer projectileBuffer;
        /// <summary>Append list of projectile pool slots the GPU still considers alive; the render path's only source of instance count.</summary>
        public ComputeBuffer activeProjectileIndexBuffer;
        public ComputeBuffer projectileDrawArgsBuffer;

        public int AgentCount { get; private set; }
        public int GridCellCount { get; private set; }
        public int MaxAgentsPerCell { get; private set; }
        public int UnitTypeCount { get; private set; }
        public int MaxProjectiles { get; private set; }
        /// <summary>How many teams the team-partitioned buffers were sized for. Kernels must clamp teamId to [0, TeamCount).</summary>
        public int TeamCount { get; private set; }
        /// <summary>Total int slots in teamSpatialStatsBuffer.</summary>
        public int TeamStatsSlotCount { get { return TeamCount * TeamStatsSlotsPerTeam; } }

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

        public void Allocate(int agentCount, int gridCellCount, int maxAgentsPerCell, int flowFieldResolutionX, int flowFieldResolutionZ, int unitTypeCount, int teamCount = DefaultTeamCount)
        {
            ReleaseAll();

            AgentCount = Mathf.Max(0, agentCount);
            GridCellCount = Mathf.Max(1, gridCellCount);
            MaxAgentsPerCell = Mathf.Max(1, maxAgentsPerCell);
            UnitTypeCount = Mathf.Max(0, unitTypeCount);
            MaxProjectiles = agentCount > 0 ? Mathf.Max(1, agentCount / 4) : 0;
            TeamCount = Mathf.Max(1, teamCount);
            int safeFlowResolutionX = Mathf.Max(1, flowFieldResolutionX);
            int safeFlowResolutionZ = Mathf.Max(1, flowFieldResolutionZ);
            int safeFlowCellCount = safeFlowResolutionX * safeFlowResolutionZ;

            if (AgentCount <= 0 || UnitTypeCount <= 0)
                return;

            int agentStride = Marshal.SizeOf(typeof(AgentData));
            if (agentStride != AgentStrideBytes)
            {
                Debug.LogError("MassEngine AgentData stride must remain 56 bytes. Actual: " + agentStride + " - refusing to allocate.");
                ReleaseAll();
                return;
            }

            int settingsStride = Marshal.SizeOf(typeof(UnitTypeGpuSettings));
            if (settingsStride != UnitTypeGpuSettings.StrideBytes)
            {
                Debug.LogError("MassEngine UnitTypeGpuSettings stride must remain " + UnitTypeGpuSettings.StrideBytes + " bytes. Actual: " + settingsStride + " - refusing to allocate.");
                ReleaseAll();
                return;
            }

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
            long teamGridIndexCapacity = gridIndexCapacity * TeamCount;
            if (teamGridIndexCapacity > int.MaxValue / sizeof(int))
            {
                Debug.LogError("MassEngine: team combat grid would need " + teamGridIndexCapacity + " entries; refusing to allocate. Shrink simulationWorldSize, raise cellSize, or lower maxAgentsPerCell.");
                ReleaseAll();
                return;
            }

            teamGridCountsBuffer = new ComputeBuffer(GridCellCount * TeamCount, sizeof(int));
            teamGridAgentIndicesBuffer = new ComputeBuffer((int)teamGridIndexCapacity, sizeof(int));
            flowFieldDirectionsBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            defenderFlowFieldDirectionsBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(float) * 2);
            runtimeAttackerTargetDensityBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(uint));
            runtimeAttackerFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
            runtimeAttackerFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);
            runtimeDefenderTargetDensityBuffer = new ComputeBuffer(safeFlowCellCount, sizeof(uint));
            runtimeDefenderFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
            runtimeDefenderFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);
            unitTypeIndexBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            unitTypeSettingsBuffer = new ComputeBuffer(UnitTypeCount, UnitTypeGpuSettings.StrideBytes);
            spatialHashStatsBuffer = new ComputeBuffer(4, sizeof(int));
            teamSpatialStatsBuffer = new ComputeBuffer(TeamStatsSlotCount, sizeof(int));
            teamStanceBuffer = new ComputeBuffer(TeamCount, sizeof(int));
            // stats[3] carries a sentinel no kernel ever writes: if a telemetry readback
            // sees it gone, GPU memory was wiped (device reset/TDR) and the manager
            // reinitializes. Slots 1-2 stay reserved; slot 0 is the overflow counter.
            spatialHashStatsBuffer.SetData(new[] { 0, 0, 0, DeviceResetSentinel });
            teamSpatialStatsBuffer.SetData(new int[TeamStatsSlotCount]);
            // Hold is 0, so a stance buffer nobody uploaded freezes every team. That is a
            // failure anyone spots in one frame, unlike defaulting to Advance, which would
            // silently march the teams that were meant to hold their ground.
            teamStanceBuffer.SetData(new int[TeamCount]);
            runtimeAttackerFlowPreviewTexture = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            runtimeDefenderFlowPreviewTexture = CreateFlowPreviewTexture(safeFlowResolutionX, safeFlowResolutionZ);
            densityMapTexture = CreateDensityMapTexture(safeFlowResolutionX, safeFlowResolutionZ);
            attackerDensityMapTexture = CreateDensityMapTexture(safeFlowResolutionX, safeFlowResolutionZ);
            defenderDensityMapTexture = CreateDensityMapTexture(safeFlowResolutionX, safeFlowResolutionZ);

            // GPU buffer contents are undefined after allocation; zero-fill everything a
            // kernel may read before its first producer runs.
            flowFieldDirectionsBuffer.SetData(new Vector2[safeFlowCellCount]);
            defenderFlowFieldDirectionsBuffer.SetData(new Vector2[safeFlowCellCount]);
            gridCountsBuffer.SetData(new int[GridCellCount]);
            teamGridCountsBuffer.SetData(new int[GridCellCount * TeamCount]);

            combatBuffers.teamIdBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.hpReadBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.hpWriteBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.targetAgentIndexBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.engagementSlotAssignmentBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.engagementSlotOccupancyBuffer = new ComputeBuffer(AgentCount * EngagementSlotsPerTarget, sizeof(uint));
            combatBuffers.attackCooldownBuffer = new ComputeBuffer(AgentCount, sizeof(float));
            combatBuffers.homePositionBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 3);
            combatBuffers.pendingDamageReadBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.pendingDamageWriteBuffer = new ComputeBuffer(AgentCount, sizeof(int));
            combatBuffers.launchRequestBuffer = new ComputeBuffer(AgentCount, sizeof(int));

            // 初始化 launchRequestBuffer 为 0（计数器模式）
            int[] initialLaunchRequests = new int[AgentCount];
            combatBuffers.launchRequestBuffer.SetData(initialLaunchRequests);

            if (MaxProjectiles > 0)
            {
                projectileBuffer = new ComputeBuffer(MaxProjectiles, 64);
                activeProjectileIndexBuffer = CreateAppendIndexBuffer(MaxProjectiles);
                projectileDrawArgsBuffer = CreateArgsBuffer();
            }

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
            int[] engagementAssignments = new int[agents.Length];
            float[] cooldowns = new float[agents.Length];
            int[] pendingDamage = new int[agents.Length];

            for (int i = 0; i < agents.Length; i++)
            {
                positions[i] = new Vector2(agents[i].position.x, agents[i].position.z);
                homePositions[i] = agents[i].position;
                targetIndices[i] = -1;
                engagementAssignments[i] = -1;
            }

            agentPositionReadBuffer.SetData(positions);
            agentPositionWriteBuffer.SetData(positions);
            combatBuffers.homePositionBuffer.SetData(homePositions);
            combatBuffers.targetAgentIndexBuffer.SetData(targetIndices);
            combatBuffers.engagementSlotAssignmentBuffer.SetData(engagementAssignments);
            combatBuffers.engagementSlotOccupancyBuffer.SetData(new uint[agents.Length * EngagementSlotsPerTarget]);
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

        /// <summary>
        /// Uploads one stance per team, indexed by raw teamId. Entries past TeamCount are
        /// ignored; an array shorter than TeamCount is rejected outright rather than
        /// partially applied, which would leave some teams on a stale stance. The caller
        /// owns the teamId-to-stance mapping, this only moves it to the GPU.
        /// </summary>
        public void UploadTeamStances(int[] stances)
        {
            if (teamStanceBuffer == null || stances == null || stances.Length < TeamCount)
                return;

            teamStanceBuffer.SetData(stances, 0, 0, TeamCount);
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

        public void ResetProjectileAppendCounter()
        {
            SetCounter(activeProjectileIndexBuffer);
        }

        public void CopyProjectileCountToArgs()
        {
            CopyCount(activeProjectileIndexBuffer, projectileDrawArgsBuffer);
        }

        /// <summary>
        /// Writes the projectile draw args for a mesh. This resets the instance count to
        /// zero, so it must run before the per-frame CopyProjectileCountToArgs - calling it
        /// mid-frame costs one frame of projectile visuals, never a stale instance count.
        /// </summary>
        public void ConfigureProjectileDrawArgs(Mesh mesh)
        {
            SetArgs(projectileDrawArgsBuffer, mesh);
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
            ReleaseBuffer(ref runtimeAttackerTargetDensityBuffer);
            ReleaseBuffer(ref runtimeAttackerFlowStatsBuffer);
            ReleaseBuffer(ref runtimeAttackerFlowTargetsBuffer);
            ReleaseBuffer(ref runtimeDefenderTargetDensityBuffer);
            ReleaseBuffer(ref runtimeDefenderFlowStatsBuffer);
            ReleaseBuffer(ref runtimeDefenderFlowTargetsBuffer);
            ReleaseBuffer(ref unitTypeIndexBuffer);
            ReleaseBuffer(ref unitTypeSettingsBuffer);
            ReleaseBuffer(ref spatialHashStatsBuffer);
            ReleaseBuffer(ref teamSpatialStatsBuffer);
            ReleaseBuffer(ref teamStanceBuffer);
            ReleaseBuffer(ref projectileBuffer);
            ReleaseBuffer(ref activeProjectileIndexBuffer);
            ReleaseBuffer(ref projectileDrawArgsBuffer);

            for (int i = 0; i < visibleIndexBuffers.Length; i++)
                ReleaseBuffer(ref visibleIndexBuffers[i]);
            for (int i = 0; i < drawArgsBuffers.Length; i++)
                ReleaseBuffer(ref drawArgsBuffers[i]);
            visibleIndexBuffers = System.Array.Empty<ComputeBuffer>();
            drawArgsBuffers = System.Array.Empty<ComputeBuffer>();

            ReleaseRenderTexture(ref runtimeAttackerFlowPreviewTexture);
            ReleaseRenderTexture(ref runtimeDefenderFlowPreviewTexture);
            ReleaseRenderTexture(ref densityMapTexture);
            ReleaseRenderTexture(ref attackerDensityMapTexture);
            ReleaseRenderTexture(ref defenderDensityMapTexture);

            AgentCount = 0;
            GridCellCount = 0;
            MaxAgentsPerCell = 0;
            UnitTypeCount = 0;
            MaxProjectiles = 0;
            TeamCount = 0;
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
