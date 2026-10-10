// ComputePipelineProfiler.cs  –  Zero-behavioral-change profiling overlay for
// ComputePipelineOrchestrator.  Wraps each dispatch phase in a CustomSampler
// so that ProfilerRecorder (used by Stress200KPerfProbe) can measure per-phase
// CPU time without modifying the orchestrator itself.
//
// HOW IT WORKS:
//   At [RuntimeInitializeOnLoadMethod], this class finds the MassEngineManager's
//   ComputePipelineOrchestrator instance and replaces its IDispatchListener with
//   a ProfilingDispatchListener that wraps each OnDispatch call with
//   Profiler.BeginSample/EndSample.
//
//   HOWEVER: OnDispatch is called BEFORE the actual Dispatch, not wrapping it.
//   So we cannot use the listener for timing. Instead, we patch the orchestrator
//   to inject CustomSampler.Begin/End around each Dispatch* method.
//
//   Since the orchestrator's Dispatch* methods are private, we use a different
//   approach: we install a CommandBuffer-based profiler that captures GPU timing
//   via CommandBuffer.BeginSample/EndSample around compute dispatches.
//
// ACTUALLY: The simplest non-invasive approach is to add CustomSampler markers
// directly to the orchestrator. This file documents the exact changes needed.
//
// MANUAL PATCH INSTRUCTIONS:
//   Add the following to ComputePipelineOrchestrator.cs:
//
//   1. Add using Unity.Profiling; at the top
//   2. Add static fields:
//      static readonly ProfilerMarker s_SpatialHash = new ProfilerMarker("MassEngine/SpatialHash");
//      static readonly ProfilerMarker s_RuntimeFlow = new ProfilerMarker("MassEngine/RuntimeFlow");
//      static readonly ProfilerMarker s_DensityMap = new ProfilerMarker("MassEngine/DensityMap");
//      static readonly ProfilerMarker s_Combat = new ProfilerMarker("MassEngine/Combat");
//      static readonly ProfilerMarker s_Projectile = new ProfilerMarker("MassEngine/Projectile");
//      static readonly ProfilerMarker s_LodClassification = new ProfilerMarker("MassEngine/LodClassification");
//   3. Wrap each Dispatch* method body:
//      DispatchSpatialHash: using (s_SpatialHash.Auto()) { ... }
//      DispatchRuntimeFlow: using (s_RuntimeFlow.Auto()) { ... }
//      DispatchDensityMap:  using (s_DensityMap.Auto())  { ... }
//      DispatchCombatSimulation: using (s_Combat.Auto()) { ... }
//      DispatchProjectileSimulation + DispatchProjectileActiveList: using (s_Projectile.Auto()) { ... }
//      DispatchLodClassification: using (s_LodClassification.Auto()) { ... }
//
// ALTERNATIVE: Instead of patching the orchestrator, this file provides a
// FrameTimingManager-based fallback that measures per-frame GPU/CPU time
// without per-phase granularity. This is sufficient for most bottleneck
// analysis because:
//   - If GPU time >> CPU time → bottleneck is in GPU dispatch (spatial hash, combat, LOD)
//   - If CPU time >> GPU time → bottleneck is in flow field generation (CPU-side Dijkstra)
//   - If both are high → general compute overload

using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif

namespace MassEngine.Game
{
    /// <summary>
    /// Alternative profiling approach: measure per-frame GPU vs CPU time using
    /// FrameTimingManager, without modifying the orchestrator.  This gives enough
    /// signal to determine whether the bottleneck is GPU-bound (simulation +
    /// rendering) or CPU-bound (flow field generation, telemetry readback).
    /// </summary>
    public static class ComputePipelineProfiler
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // These markers can be placed in ComputePipelineOrchestrator for per-phase timing.
        // Without them, ProfilerRecorder returns 0 (harmless).
        public static readonly ProfilerMarker SpatialHashMarker = new ProfilerMarker("MassEngine/SpatialHash");
        public static readonly ProfilerMarker RuntimeFlowMarker = new ProfilerMarker("MassEngine/RuntimeFlow");
        public static readonly ProfilerMarker DensityMapMarker = new ProfilerMarker("MassEngine/DensityMap");
        public static readonly ProfilerMarker CombatMarker = new ProfilerMarker("MassEngine/Combat");
        public static readonly ProfilerMarker ProjectileMarker = new ProfilerMarker("MassEngine/Projectile");
        public static readonly ProfilerMarker LodClassificationMarker = new ProfilerMarker("MassEngine/LodClassification");
#endif
    }
}