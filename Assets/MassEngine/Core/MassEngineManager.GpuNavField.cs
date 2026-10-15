using System;
using UnityEngine;

namespace MassEngine
{
    public sealed partial class MassEngineManager
    {
        private GpuNavField gpuNavField;
        private ComputeShader gpuNavFieldRequestedShader; // developer admission (TryEnableGpuNavField)
        private string gpuNavFieldLastError;
        private TerrainNavigationGrid gpuNavFieldFailedFor;
        /// <summary>GPU nav field (null = off). Docs/GPU-NAVFIELD-N0-20261013.md.</summary>
        public GpuNavField GpuNavField => gpuNavField;
        public string GpuNavFieldError => gpuNavFieldLastError;

        /// <summary>
        /// Developer admission: builds the fine (half cell, double resolution) grid with the coarse grid's clearance,
        /// boundary and obstacles now, and keeps it in step with later navigation rebuilds until DisableGpuNavField.
        /// </summary>
        public bool TryEnableGpuNavField(ComputeShader shader, out string error)
        {
            gpuNavFieldRequestedShader = shader;
            gpuNavFieldFailedFor = null;
            bool ok = BuildGpuNavField(shader, out error);
            if (!ok) gpuNavFieldRequestedShader = null;
            return ok;
        }

        public void DisableGpuNavField()
        {
            gpuNavFieldRequestedShader = null;
            ReleaseGpuNavField();
        }

        private void ReleaseGpuNavField()
        {
            if (pipelineOrchestrator != null && pipelineOrchestrator.FineNavigation == gpuNavField) pipelineOrchestrator.FineNavigation = null;
            if (terrainRuntime != null) terrainRuntime.GpuCoversTeam = null;
            gpuNavField?.Dispose(); gpuNavField = null;
        }

        /// <summary>
        /// Called from EnsureTerrainResources (load, navigation rebuilds, every frame): reference compares only unless the
        /// field has to be (re)built. Wanted = developer admission, or RuntimeFlowConfig.gpuNavField with a shader.
        /// </summary>
        /// <summary>Per-frame config mirror: N3 steering and N4 crowd cost (all default off).</summary>
        private Func<int, bool> gpuNavCoversTeam;
        /// <summary>N5d final: team steers by a valid GPU layer for its dynamic (Density) goal.</summary>
        private bool GpuNavCoversTeam(int team)
        {
            var f = gpuNavField;
            return f != null && !f.IsDisposed && f.SteeringEnabled && team >= 0 && team < 32 && team < f.TeamCount
                && ((f.ValidMask >> team) & 1u) != 0u && f.LastGoal(team).mode == GpuNavGoal.Density;
        }

        private void ApplyGpuNavConfig(GpuNavField field)
        {
            if (terrainRuntime != null) terrainRuntime.GpuCoversTeam = gpuNavCoversTeam ??= GpuNavCoversTeam;
            field.SteeringEnabled = Flow != null && Flow.gpuNavSteering;
            field.CrowdEnabled = Flow != null && Flow.gpuNavCrowd;
            field.LocalEnabled = Flow != null && Flow.gpuNavLocalOrders;
            field.FormationLineEnabled = Flow != null && Flow.gpuNavFormationLine;
            field.ArrivalFollowEnabled = Flow != null && Flow.gpuNavArrivalFollow;
            if (Flow != null) field.ArrivalZoneScale = Mathf.Max(0f, Flow.gpuNavArrivalZoneScale);
            if (field.TeamSpatial == null) field.TeamSpatial = GpuNavTeamSpatial;
            if (Flow != null)
            {
                field.LineMinWidth = Mathf.Max(2f, Flow.gpuNavLineMinWidth);
                field.LineMaxWidth = Mathf.Max(field.LineMinWidth, Flow.gpuNavLineMaxWidth);
                field.LineMaxOffset = Mathf.Max(0f, Flow.gpuNavLineMaxOffset);
                field.LinePackingArea = Mathf.Max(0f, Flow.gpuNavLinePackingArea);
            }
            // N5a: local Move/Retreat groups get GPU layers (commit hook); only slots still referenced are relaxed.
            if (localOrders != null && !localOrders.IsDisposed)
            {
                if (localOrders.SlotCommitted == null) localOrders.SlotCommitted = OnLocalOrderSlotCommitted;
                field.SetLocalActiveMask(localOrders.UsedSlotMask);
            }
            else field.SetLocalActiveMask(0);
            if (Flow == null) return;
            float area = field.Graph.CellSize * field.Graph.CellSize;
            field.CrowdGain = Mathf.Max(0f, Flow.gpuNavCrowdGain);
            field.CrowdFullPerCell = Mathf.Max(0.01f, Flow.gpuNavCrowdFullDensity * area);
            field.CrowdCap = Mathf.Max(0f, Flow.gpuNavCrowdCap);
            field.CrowdTimeConstant = Mathf.Max(0.01f, Flow.gpuNavCrowdTimeConstant);
            field.CrowdFreeSpeed = Mathf.Max(0f, Flow.gpuNavCrowdFreeSpeed);
        }

        /// <summary>N5b: the latest team spatial telemetry (centroid + bounds), default (invalid) when none.</summary>
        private TeamSpatialTelemetry GpuNavTeamSpatial(int team)
        {
            if (telemetry == null) return default;
            var snapshot = telemetry.Snapshot;
            return snapshot.valid ? snapshot.GetTeam(team) : default;
        }

        private void OnLocalOrderSlotCommitted(int slot, int team, LocalOrderKind kind, LocalOrderPlan plan)
        {
            if (gpuNavField != null && !gpuNavField.IsDisposed) gpuNavField.CommitLocalSlot(slot, team, kind, plan);
        }

        private void SyncGpuNavField()
        {
            ComputeShader shader = gpuNavFieldRequestedShader;
            if (shader == null && Flow != null && Flow.gpuNavField && shaderConfig != null) shader = shaderConfig.gpuNavFieldShader;
            if (shader == null || terrainRuntime == null)
            {
                if (gpuNavField != null) ReleaseGpuNavField();
                return;
            }
            var navigation = terrainRuntime.Navigation;
            if (gpuNavField != null && !gpuNavField.IsDisposed && gpuNavField.Coarse == navigation)
            {
                if (Flow != null) gpuNavField.PassesPerFrame = Mathf.Clamp(Flow.gpuNavPassesPerFrame, 1, 64);
                ApplyGpuNavConfig(gpuNavField);
                return;
            }
            if (gpuNavFieldFailedFor == navigation) return; // do not retry a failed build every frame
            if (!BuildGpuNavField(shader, out string error))
            {
                gpuNavFieldFailedFor = navigation;
                Debug.LogWarning("MassEngine: GPU nav field disabled: " + error, this);
            }
        }

        private bool BuildGpuNavField(ComputeShader shader, out string error)
        {
            error = null;
            ReleaseGpuNavField();
            if (shader == null) error = "GpuNavField compute shader missing.";
            else if (!SystemInfo.supportsComputeShaders) error = "Compute shaders unsupported.";
            else if (bufferManager == null || !bufferManager.IsAllocated || terrainRuntime == null || terrainSurface == null || pipelineOrchestrator == null)
                error = "GPU nav field requires initialized authored terrain.";
            if (error != null) { gpuNavFieldLastError = error; return false; }
            try
            {
                var coarse = terrainRuntime.Navigation;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var fine = new TerrainNavigationGrid(terrainSurface, coarse.Origin, coarse.CellSize * .5f,
                    coarse.ResolutionX * 2, coarse.ResolutionZ * 2, coarse.Clearance, Simulation.boundaryPadding,
                    CurrentTerrainObstacles(), activeStaticObstaclePadding);
                double gridMs = watch.Elapsed.TotalMilliseconds;
                var graph = GpuNavFieldGraph.FromGrids(fine, coarse);
                double graphMs = watch.Elapsed.TotalMilliseconds - gridMs;
                gpuNavField = new GpuNavField(shader, graph, bufferManager.TeamCount, coarse)
                { GridBuildMilliseconds = gridMs, GraphBuildMilliseconds = graphMs };
                if (Flow != null) gpuNavField.PassesPerFrame = Mathf.Clamp(Flow.gpuNavPassesPerFrame, 1, 64);
                ApplyGpuNavConfig(gpuNavField);
                pipelineOrchestrator.FineNavigation = gpuNavField;
                gpuNavFieldLastError = null;
                return true;
            }
            catch (Exception exception)
            {
                ReleaseGpuNavField();
                error = gpuNavFieldLastError = exception.Message;
                return false;
            }
        }
    }
}
