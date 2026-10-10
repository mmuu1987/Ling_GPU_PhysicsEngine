using System;
using UnityEngine;

namespace MassEngine
{
    public sealed partial class MassEngineManager
    {
        [Header("Optional authored terrain (null is the legacy plane)")]
        public TerrainSurfaceAsset terrainSurfaceAsset;

        private TerrainSurface terrainSurface;
        private TerrainNavigationGrid terrainNavigation;
        private TerrainNavigationRuntime terrainRuntime;
        private string terrainError;
        private string terrainContextError;
        private int terrainContextHash;
        private bool terrainContextChecked;
        private Bounds terrainRenderBounds;
        // Runtime deployment can remove and re-add an initial template. Keep this scene's
        // largest supported radius across resets, rather than silently narrowing its nav mask.
        private float terrainClearanceFloor = .45f;

        private float? deploymentRadiusClearance;
        public float? DeploymentRadiusClearance => deploymentRadiusClearance;
        /// <summary>Opt-in radius-edit boundary only. Caller must release old buffers first;
        /// never changes a paused/live world's navigation in place. Null preserves legacy policy.</summary>
        public void SetDeploymentRadiusClearance(float? radius)
        {
            if (bufferManager != null || unitTypeRegistry != null || IsBattleRunning)
                throw new InvalidOperationException("Radius clearance can change only after releasing the previous deployment.");
            if (radius.HasValue && (float.IsNaN(radius.Value) || float.IsInfinity(radius.Value) || radius.Value < .45f))
                throw new ArgumentOutOfRangeException(nameof(radius));
            deploymentRadiusClearance = radius; terrainContextChecked = false;
        }

        public TerrainSurface TerrainSurface => terrainSurface;
        public TerrainNavigationGrid TerrainNavigation => terrainNavigation;
        public string TerrainError => terrainError ?? terrainContextError;
        public bool TerrainGpuAllocated => terrainRuntime != null && terrainRuntime.IsAllocated;
        public long TerrainGpuBytes => terrainRuntime?.GpuBytes ?? 0;
        public int TerrainCompletedFields => terrainRuntime?.CompletedFields ?? 0;
        public double TerrainTotalSolveMilliseconds => terrainRuntime?.TotalSolveMilliseconds ?? 0;
        public float TerrainMaxSolveMilliseconds => terrainRuntime?.MaxSolveMilliseconds ?? 0;
        public float TerrainMaxDynamicQueueWaitMilliseconds => terrainRuntime?.MaxDynamicQueueWaitMilliseconds ?? 0;

        // Authoring data is versioned and read-only during play. No height-array scan per frame.
        private int TerrainConfigurationHash()
        {
            if (terrainSurfaceAsset == null) return 0;
            unchecked
            {
                int hash = terrainSurfaceAsset.GetInstanceID();
                hash = hash * 31 + terrainSurfaceAsset.Version;
                hash = hash * 31 + (terrainSurfaceAsset.Id ?? "").GetHashCode();
                hash = hash * 31 + Simulation.simulationWorldSize.GetHashCode();
                hash = hash * 31 + Simulation.boundaryPadding.GetHashCode();
                hash = hash * 31 + Flow.flowFieldResolution;
                hash = hash * 31 + Flow.flowFieldOrigin.GetHashCode();
                hash = hash * 31 + Flow.flowFieldCellSize.GetHashCode();
                return hash * 31 + ResolveTerrainClearance().GetHashCode();
            }
        }

        private float ResolveTerrainClearance()
        {
            if (deploymentRadiusClearance.HasValue) return deploymentRadiusClearance.Value;
            float radius = .45f;
            if (scenarioConfig != null && scenarioConfig.unitTypes != null)
                foreach (var unit in scenarioConfig.unitTypes)
                    if (unit != null && unit.flockingConfig != null)
                        radius = Mathf.Max(radius, unit.flockingConfig.agentRadius);
            if (unitTypeRegistry != null)
                foreach (var unit in unitTypeRegistry.RegisteredTypes)
                    radius = Mathf.Max(radius, unit.BuildGpuSettings().agentRadius);
            terrainClearanceFloor = Mathf.Max(terrainClearanceFloor, radius);
            return terrainClearanceFloor;
        }

        /// <summary>CPU-only context, also usable by editor validation. Never substitutes a plane for bad terrain.</summary>
        public bool TryGetTerrainContext(out TerrainSurface surface, out TerrainNavigationGrid navigation, out string error)
        {
            int hash = unchecked(TerrainConfigurationHash() * 31 + lastStaticObstacleHash);
            if (!terrainContextChecked || hash != terrainContextHash)
            {
                terrainContextChecked = true; terrainContextHash = hash;
                terrainSurface = null; terrainNavigation = null; terrainError = null; terrainContextError = null;
                if (terrainSurfaceAsset != null)
                {
                    try
                    {
                        if (!terrainSurfaceAsset.TryCreateSurface(out var candidate, out string invalid))
                            throw new InvalidOperationException(invalid);
                        if (candidate.Id == "flat-ground") throw new InvalidOperationException("flat-ground is reserved for the explicit legacy plane.");
                        Vector2 world = Simulation.simulationWorldSize;
                        if ((candidate.Origin + world * .5f).sqrMagnitude > 1e-6f || (candidate.Size - world).sqrMagnitude > 1e-6f)
                            throw new InvalidOperationException("Terrain origin/size do not match the simulation world; no automatic recentering is allowed.");
                        Vector2 flowEnd = Flow.flowFieldOrigin + Vector2.one * (Flow.flowFieldResolution * Flow.flowFieldCellSize);
                        if (Flow.flowFieldOrigin.x > candidate.Origin.x || Flow.flowFieldOrigin.y > candidate.Origin.y ||
                            flowEnd.x < candidate.Origin.x + world.x || flowEnd.y < candidate.Origin.y + world.y)
                            throw new InvalidOperationException("Terrain navigation grid does not cover the simulation world.");
                        terrainSurface = candidate;
                        terrainNavigation = CreateTerrainNavigationCore(CurrentTerrainObstacles(), activeStaticObstaclePadding);
                        float min = float.PositiveInfinity, max = float.NegativeInfinity;
                        for (int z = 0; z < candidate.Depth; z++)
                            for (int x = 0; x < candidate.Width; x++)
                            { float y = candidate.HeightAtVertex(x, z); min = Mathf.Min(min, y); max = Mathf.Max(max, y); }
                        terrainRenderBounds = new Bounds(new Vector3(0, (min + max) * .5f, 0), new Vector3(world.x + 40, max - min + 120, world.y + 40));
                    }
                    catch (Exception exception)
                    { terrainSurface = null; terrainNavigation = null; terrainContextError = exception.Message; }
                }
            }
            surface = terrainSurface; navigation = terrainNavigation; error = terrainContextError;
            return error == null;
        }

        public TerrainNavigationGrid CreateTerrainNavigation(StaticObstacleRect[] obstacles, float padding, float? candidateClearance = null)
        {
            if (!TryGetTerrainContext(out var surface, out _, out string error)) throw new InvalidOperationException(error);
            if (candidateClearance.HasValue && (float.IsNaN(candidateClearance.Value) || float.IsInfinity(candidateClearance.Value) || candidateClearance.Value < .45f))
                throw new ArgumentOutOfRangeException(nameof(candidateClearance));
            return surface == null ? null : CreateTerrainNavigationCore(obstacles, padding, candidateClearance);
        }

        private TerrainNavigationGrid CreateTerrainNavigationCore(StaticObstacleRect[] obstacles, float padding, float? candidateClearance = null) =>
            new TerrainNavigationGrid(terrainSurface, Flow.flowFieldOrigin, Flow.flowFieldCellSize,
                Flow.flowFieldResolution, Flow.flowFieldResolution, candidateClearance ?? ResolveTerrainClearance(), Simulation.boundaryPadding, obstacles, padding);

        private StaticObstacleRect[] CurrentTerrainObstacles()
        {
            var result = new StaticObstacleRect[activeStaticObstacleCount];
            Array.Copy(activeStaticObstacles, result, result.Length);
            return result;
        }

        private bool ValidateTerrainSpawns()
        {
            if (!TryGetTerrainContext(out var surface, out var navigation, out _)) return false;
            if (surface == null) return true;
            if (scenarioConfig != null && scenarioConfig.unitTypes != null)
                foreach (var unit in scenarioConfig.unitTypes)
                    if (unit != null && unit.spawnConfig != null && unit.spawnConfig.unitCount > 0)
                    {
                        Vector3 center = unit.spawnConfig.spawnCenter, size = unit.spawnConfig.ResolveSpawnSize();
                        if (!navigation.IsFootprintWalkable(new Vector2(center.x, center.z), new Vector2(size.x, size.z)))
                        { terrainError = "Spawn footprint intersects terrain exclusion or insufficient clearance: " + unit.name; return false; }
                    }
            return true;
        }

        private bool EnsureTerrainResources()
        {
            if (!TryGetTerrainContext(out var surface, out var navigation, out _)) return false;
            if (surface == null)
            { terrainRuntime?.Dispose(); terrainRuntime = null; return true; }
            if (terrainRuntime == null || terrainRuntime.Navigation != navigation || terrainRuntime.LaneApproach33 != Flow.terrainLaneApproach33)
            {
                terrainRuntime?.Dispose(); terrainRuntime = null;
                try { terrainRuntime = new TerrainNavigationRuntime(navigation, bufferManager.TeamCount, Flow.terrainLaneApproach33); }
                catch (Exception exception) { terrainError = exception.Message; return false; }
                MarkAllFlowFieldsDirty();
            }
            return true;
        }

        private void GroundInitialAgents(AgentData[] agents)
        {
            if (terrainSurface == null) return;
            for (int i = 0; i < agents.Length; i++)
            {
                Vector3 p = agents[i].position;
                if (!terrainNavigation.IsWalkable(new Vector2(p.x, p.z)) || !terrainSurface.TrySample(new Vector2(p.x, p.z), out var sample))
                    throw new InvalidOperationException("Generated spawn is not on navigable terrain (no position projection fallback).");
                p.y = sample.Position.y; agents[i].position = p;
            }
        }

        /// <summary>Validates before changing the previous target. All formations in the selected army must be connected.</summary>
        public bool TrySetFlowTargetOverride(int teamId, Vector3 point, out string error)
        {
            error = null;
            if (teamId < 0 || teamId >= flowTargetOverrides.Length) { error = "Invalid army."; return false; }
            if (!TryGetTerrainContext(out var surface, out var navigation, out error)) return false;
            if (surface != null)
            {
                var target = new Vector2(point.x, point.z);
                if (!navigation.IsWalkable(target) || !surface.TrySample(target, out var sample))
                { error = "目标位于禁行地形、陡坡或边界留白内。"; return false; }
                bool found = false;
                if (scenarioConfig != null && scenarioConfig.unitTypes != null)
                    foreach (var unit in scenarioConfig.unitTypes)
                        if (unit != null && unit.teamId == teamId && unit.spawnConfig != null && unit.spawnConfig.unitCount > 0)
                        {
                            found = true; Vector3 start = unit.spawnConfig.spawnCenter;
                            if (!navigation.AreConnected(new Vector2(start.x, start.z), target))
                            { error = "该军团有编成无法到达目标区域。"; return false; }
                        }
                if (!found) { error = "该军团没有已部署编成。"; return false; }
                point = sample.Position;
            }
            else point = ResolvePointOutsideStaticObstacles(point);
            flowTargetOverrides[teamId] = new FlowTargetOverride { active = true, point = point };
            flowFieldDirty[teamId] = true;
            return true;
        }

        private void ReleaseTerrain()
        {
            terrainRuntime?.Dispose(); terrainRuntime = null;
            terrainSurface = null; terrainNavigation = null; terrainError = null; terrainContextError = null; terrainContextChecked = false;
        }
    }
}

