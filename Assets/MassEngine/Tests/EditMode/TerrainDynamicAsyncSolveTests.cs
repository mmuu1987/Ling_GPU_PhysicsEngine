using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MassEngine.Tests {
 /// <summary>
 /// P3-CPU-01: the scheduled dynamic-enemy solve (goal extraction + Dijkstra + LaneApproach33 in one Burst job)
 /// must upload exactly the bits of the synchronous path for the same density snapshot, one or more Ticks later.
 /// Explicit commands stay immediate and cancel the same team's in-flight result; disposal with a job in flight is safe.
 /// Real ComputeBuffers + AsyncGPUReadback, same fixture style as BurstDependencyRepairRuntimeTests.
 /// </summary>
 public sealed class TerrainDynamicAsyncSolveTests {
  const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
  static int Count(string name) => (int)typeof(TerrainNavigationRuntime).Assembly.GetType("MassEngine.TerrainNavigationBurstWorkspace").GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null);
  static bool Blocked => !TerrainNavigationRuntime.BurstReserveRequested || !Unity.Burst.BurstCompiler.IsEnabled ||
      Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously || Array.Exists(Environment.GetCommandLineArgs(), a => a == "--burst-force-sync-compilation" || a == "--war-sandbox-nav-sync");

  sealed class World : IDisposable {
   public TerrainNavigationGrid nav; public TerrainNavigationRuntime runtime; public MassGpuBufferManager buffers; public PipelineFrameContext context; public uint[] data; public int n;
   public World(string layout, int width, int depth, int seed, double fill) {
    nav = (TerrainNavigationGrid)typeof(TerrainCardinalRouteCacheTests).GetMethod("Grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { layout, width, depth });
    n = nav.CellCount; runtime = new TerrainNavigationRuntime(nav, 2, true, true);
    buffers = new MassGpuBufferManager { runtimeFlowTargetDensityBuffer = new ComputeBuffer(2 * n, 4), flowFieldDirectionsBuffer = new ComputeBuffer(2 * n, 8) };
    buffers.flowFieldDirectionsBuffer.SetData(new Vector2[2 * n]);
    var random = new System.Random(seed); data = new uint[2 * n];
    for (int i = 0; i < 2 * n; i++) if (random.NextDouble() < fill) data[i] = i % 7 == 0 ? uint.MaxValue : (uint)random.Next(1, 40);
    buffers.runtimeFlowTargetDensityBuffer.SetData(data);
    context = new PipelineFrameContext { battleStarted = false, teamFlows = new[] { new TeamFlowFrameSettings { enabled = true, dynamicFlowEnabled = true }, new TeamFlowFrameSettings { enabled = true, dynamicFlowEnabled = true } } };
   }
   public IEnumerator WaitReady() {
    double deadline = Time.realtimeSinceStartupAsDouble + 60;
    runtime.Tick(buffers, context);
    while (runtime.PreparationState == "Waiting" && Time.realtimeSinceStartupAsDouble < deadline) { yield return null; runtime.Tick(buffers, context); }
    Assert.AreEqual(Blocked ? "Unavailable" : "Ready", runtime.PreparationState);
   }
   public void RequestBoth() {
    context.battleStarted = true;
    for (int i = 0; i < 2; i++) { context.teamFlows[i].targetMode = 0; runtime.Rebuild(buffers, context, i); }
    foreach (var r in (UnityEngine.Rendering.AsyncGPUReadbackRequest[])typeof(TerrainNavigationRuntime).GetField("requests", F).GetValue(runtime)) { r.WaitForCompletion(); Assert.IsFalse(r.hasError); }
   }
   public Vector2[] Expected(int team) {
    var goals = new List<Vector2>();
    for (int i = 0; i < n; i++) if (data[team * n + i] > 0 && nav.IsWalkable(nav.CellCenter(i))) goals.Add(nav.CellCenter(i));
    var expected = nav.CreateFlowField(goals, 0); TerrainLaneApproach33.Apply(nav, goals, expected, 0); return expected;
   }
   public Vector2[] Uploaded(int team) { var actual = new Vector2[n]; buffers.flowFieldDirectionsBuffer.GetData(actual, 0, team * n, n); return actual; }
   public IEnumerator TickUntil(int completed) {
    for (int frames = 0; runtime.CompletedFields < completed && frames < TerrainNavigationRuntime.MaxAsyncTicks + 2; frames++) { yield return null; runtime.Tick(buffers, context); }
    Assert.AreEqual(completed, runtime.CompletedFields);
   }
   public void Dispose() { runtime?.Dispose(); buffers?.runtimeFlowTargetDensityBuffer?.Release(); buffers?.flowFieldDirectionsBuffer?.Release(); }
  }

  static void Exact(Vector2[] a, Vector2[] b, string label) {
   Assert.AreEqual(a.Length, b.Length);
   for (int i = 0; i < a.Length; i++) {
    Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].x), BitConverter.SingleToInt32Bits(b[i].x), label + " x " + i);
    Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i].y), BitConverter.SingleToInt32Bits(b[i].y), label + " y " + i);
   }
  }

  static IEnumerator BothTeamsExact(string layout, int width, int depth, int seed, double fill) {
   Assert.IsTrue(SystemInfo.supportsAsyncGPUReadback);
   using (var w = new World(layout, width, depth, seed, fill)) {
    { var e = w.WaitReady(); while (e.MoveNext()) yield return e.Current; }
    Assert.IsTrue(w.runtime.AsyncDynamicSolve || Array.Exists(Environment.GetCommandLineArgs(), a => a == "--war-sandbox-nav-sync"));
    int before = w.runtime.CompletedFields;
    w.RequestBoth(); w.runtime.Tick(w.buffers, w.context);
    if (Blocked || !w.runtime.AsyncDynamicSolve) { Assert.AreEqual(before + 1, w.runtime.CompletedFields); Assert.IsFalse(w.runtime.AsyncSolveInFlight); }
    else { Assert.AreEqual(before, w.runtime.CompletedFields, "async: nothing uploaded on the scheduling Tick"); Assert.IsTrue(w.runtime.AsyncSolveInFlight); }
    { var e = w.TickUntil(before + 1); while (e.MoveNext()) yield return e.Current; } Exact(w.Expected(0), w.Uploaded(0), layout + " team0");
    { var e = w.TickUntil(before + 2); while (e.MoveNext()) yield return e.Current; } Exact(w.Expected(1), w.Uploaded(1), layout + " team1");
    if (!Blocked && w.runtime.AsyncDynamicSolve) { Assert.AreEqual(2, w.runtime.AsyncSolveCount); Assert.GreaterOrEqual(w.runtime.BurstSolveCount, 2); Assert.AreEqual(0, w.runtime.ManagedNativeSolveCount); Assert.AreEqual(0, w.runtime.AsyncDiscardedCount); }
    // Second round with changed density: the per-team copy is refreshed, not stale.
    for (int i = 0; i < w.data.Length; i++) w.data[i] = w.data[i] == 0 ? (i % 11 == 0 ? 3u : 0u) : (i % 3 == 0 ? 0u : w.data[i]);
    w.buffers.runtimeFlowTargetDensityBuffer.SetData(w.data);
    w.RequestBoth(); w.runtime.Tick(w.buffers, w.context);
    { var e = w.TickUntil(before + 3); while (e.MoveNext()) yield return e.Current; } Exact(w.Expected(0), w.Uploaded(0), layout + " team0 round2");
    { var e = w.TickUntil(before + 4); while (e.MoveNext()) yield return e.Current; } Exact(w.Expected(1), w.Uploaded(1), layout + " team1 round2");
   }
   Assert.AreEqual(0, Count("ActiveWorkspaces"));
  }

  [UnityEngine.TestTools.UnityTest] public IEnumerator AsyncMatchesSyncHoles16() => BothTeamsExact("holes", 16, 16, 1, .08);
  [UnityEngine.TestTools.UnityTest] public IEnumerator AsyncMatchesSyncHills64x48() => BothTeamsExact("hills", 64, 48, 2, .03);
  [UnityEngine.TestTools.UnityTest] public IEnumerator AsyncMatchesSyncWallGap41x23() => BothTeamsExact("wall-gap", 41, 23, 3, .05);
  [UnityEngine.TestTools.UnityTest] public IEnumerator AsyncMatchesSyncFractional37x29() => BothTeamsExact("fractional", 37, 29, 4, .2);
  [UnityEngine.TestTools.UnityTest] public IEnumerator AsyncMatchesSyncClearance48() => BothTeamsExact("clearance", 48, 48, 5, .01);

  [UnityEngine.TestTools.UnityTest] public IEnumerator ExplicitCommandCancelsSameTeamAndDisposeIsSafe() {
   Assert.IsTrue(SystemInfo.supportsAsyncGPUReadback);
   int active0 = Count("ActiveWorkspaces");
   using (var w = new World("hills", 64, 48, 9, .05)) {
    { var e = w.WaitReady(); while (e.MoveNext()) yield return e.Current; }
    if (Blocked || !w.runtime.AsyncDynamicSolve) yield break;
    int before = w.runtime.CompletedFields;
    w.RequestBoth(); w.runtime.Tick(w.buffers, w.context);
    Assert.IsTrue(w.runtime.AsyncSolveInFlight);
    // Explicit point command for team 0 while team 0's dynamic job is in flight: immediate upload, dynamic result discarded.
    int cell = w.n / 2; while (!w.nav.IsWalkable(w.nav.CellCenter(cell))) cell++;
    Vector2 p = w.nav.CellCenter(cell);
    w.context.teamFlows[0].targetMode = 1; w.context.teamFlows[0].targetPoint = new Vector3(p.x, 0, p.y);
    w.runtime.Rebuild(w.buffers, w.context, 0);
    Assert.IsFalse(w.runtime.AsyncSolveInFlight);
    Assert.AreEqual(before + 1, w.runtime.CompletedFields); Assert.AreEqual(1, w.runtime.AsyncDiscardedCount);
    Exact(w.nav.CreateFlowField(new List<Vector2> { p }, 0), w.Uploaded(0), "explicit");
    for (int frames = 0; frames < TerrainNavigationRuntime.MaxAsyncTicks + 2; frames++) { yield return null; w.runtime.Tick(w.buffers, w.context); }
    Exact(w.nav.CreateFlowField(new List<Vector2> { p }, 0), w.Uploaded(0), "explicit kept");
    Exact(w.Expected(1), w.Uploaded(1), "team1 after explicit");
    // Dispose while a job is in flight.
    w.context.teamFlows[0].targetMode = 0; w.RequestBoth(); w.runtime.Tick(w.buffers, w.context);
    Assert.IsTrue(w.runtime.AsyncSolveInFlight);
    w.runtime.Dispose(); w.runtime.Dispose();
    Assert.IsFalse(w.runtime.AsyncSolveInFlight);
   }
   Assert.AreEqual(active0, Count("ActiveWorkspaces"));
  }

  [Test] public void DirectConstructionKeepsSynchronousDefault() {
   var nav = (TerrainNavigationGrid)typeof(TerrainCardinalRouteCacheTests).GetMethod("Grid", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { "flat", 6, 4 });
   using (var runtime = new TerrainNavigationRuntime(nav, 2, true)) Assert.IsFalse(runtime.AsyncDynamicSolve);
  }
 }
}
