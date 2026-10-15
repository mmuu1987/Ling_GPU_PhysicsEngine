#if UNITY_EDITOR
// Editor-only fix for P4 (2026-10-12, F1a). In the Editor Unity compiles a compute-shader variant synchronously the
// first time it is dispatched. Each local-order kernel (SimulateLocal{Attack,Move,Hold}{Melee,Ranged}) is a full copy
// of the combat simulation, so the first Move/Hold/Attack froze the frame for 23-33 s (Logs/CommandLatency).
// This compiles exactly the variants the local-order path will dispatch as soon as a battle is entered (Setup phase),
// so the first player command never freezes. Results live in Library/ShaderCache; a cache hit costs milliseconds.
// Shader edits invalidate the cache, so the next battle entry compiles again (once). Player builds are unaffected:
// every variant is compiled at build time. Toggle: Tools/战场 1024/进战场时预热局部指令着色器.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MassEngine.Game.Editor
{
    [InitializeOnLoad]
    public static class LocalOrderShaderPrewarm
    {
        const string ComputePath = "Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute";
        const string PrefKey = "WarSandbox.LocalOrderShaderPrewarm";
        const string ToggleMenu = "Tools/战场 1024/进战场时预热局部指令着色器";
        const string Terrain = "MASS_TERRAIN_ENABLED", Charge = "MASS_MELEE_CHARGE", Orders = "MASS_LOCAL_ORDERS";
        // Index = orchestrator role (MassGpuShaderSet.LocalKernel); role % 2 selects the melee(1)/ranged(2) type bit.
        static readonly string[] RoleKernels =
        {
            "SimulateLocalAttackMelee", "SimulateLocalAttackRanged", "SimulateLocalMoveMelee",
            "SimulateLocalMoveRanged", "SimulateLocalHoldMelee", "SimulateLocalHoldRanged"
        };

        static int lastSignature = -1;
        static double nextCheck;

        static LocalOrderShaderPrewarm()
        {
            EditorApplication.update += Poll;
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        [MenuItem(ToggleMenu, false, 44)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem(ToggleMenu, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, Enabled);
            return true;
        }

        static void Poll()
        {
            if (!EditorApplication.isPlaying || !Enabled) { lastSignature = -1; return; }
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 0.5;
            var session = WarSandboxSceneSession.Instance;
            if (session == null || session.IsLoading || session.State != WarSandboxEntryState.Battle || session.Controller == null) return;
            var manager = session.Controller.manager;
            if (manager == null || manager.Buffers == null || !manager.Buffers.IsAllocated) return;
            int mask = PartitionTypeMask(manager);
            if (mask == 0) return; // unit types not uploaded yet (or the partition path is unavailable)
            bool charge = ChargeActive(manager);
            int signature = mask | (charge ? 4 : 0);
            if (signature == lastSignature) return;
            lastSignature = signature;
            Warm(mask, charge, "battle entry");
        }

        static int PartitionTypeMask(MassEngineManager manager)
        {
            var p = typeof(MassGpuBufferManager).GetProperty("LocalPartitionTypeMask", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return p != null ? (int)p.GetValue(manager.Buffers) : 3;
        }

        static bool ChargeActive(MassEngineManager manager)
        {
            var f = typeof(MassEngineManager).GetField("meleeCharge", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) return true;
            var charge = f.GetValue(manager) as MeleeChargeParams;
            return charge != null && charge.IsValid;
        }

        /// <summary>
        /// Compiles the variants dispatched by the local-order path for the given melee(1)/ranged(2) type mask:
        /// the partition kernels run with {terrain, charge?} (MASS_LOCAL_ORDERS is disabled on that path) and the
        /// observation kernel runs with {terrain, local orders} after the combat dispatch (charge already unbound).
        /// </summary>
        public static string Warm(int mask, bool charge, string reason)
        {
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            if (shader == null) return Report("compute asset not found: " + ComputePath, true);
            var util = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ShaderUtil");
            var compile = util?.GetMethod("CompileComputeShaderVariant", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (compile == null) return Report("UnityEditor.ShaderUtil.CompileComputeShaderVariant not available; skipped", true);
            var api = SystemInfo.graphicsDeviceType;
            var target = EditorUserBuildSettings.activeBuildTarget;

            var work = new List<KeyValuePair<string, string[]>>();
            for (int role = 0; role < RoleKernels.Length; role++)
                if ((mask & (1 << (role % 2))) != 0)
                    work.Add(new KeyValuePair<string, string[]>(RoleKernels[role], charge ? new[] { Terrain, Charge } : new[] { Terrain }));
            work.Add(new KeyValuePair<string, string[]>("ObserveLocalOrders", new[] { Terrain, Orders }));

            var log = new StringBuilder();
            var total = Stopwatch.StartNew();
            int compiled = 0, failed = 0;
            try
            {
                for (int i = 0; i < work.Count; i++)
                {
                    string kernelName = work[i].Key;
                    if (!shader.HasKernel(kernelName)) { log.Append(' ').Append(kernelName).Append("=missing"); failed++; continue; }
                    EditorUtility.DisplayProgressBar("预热局部指令着色器（仅编辑器）",
                        kernelName + "（" + (i + 1) + "/" + work.Count + "）首次编译每个约 20–30 秒；已有缓存则立即完成",
                        i / (float)work.Count);
                    var timer = Stopwatch.StartNew();
                    bool ok;
                    try
                    {
                        object info = compile.Invoke(null, new object[] { shader, shader.FindKernel(kernelName), work[i].Value, api, target });
                        var success = info?.GetType().GetProperty("Success", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        ok = info != null && (success == null || (bool)success.GetValue(info));
                    }
                    catch (Exception e) { ok = false; Debug.LogWarning("[LocalOrderPrewarm] " + kernelName + ": " + (e.InnerException ?? e).Message); }
                    timer.Stop();
                    if (!ok) failed++;
                    if (timer.Elapsed.TotalMilliseconds > 1000) compiled++;
                    log.Append(' ').Append(kernelName).Append('=').Append(ok ? timer.Elapsed.TotalMilliseconds.ToString("F0") + "ms" : "FAILED");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return Report(reason + " mask=" + mask + " charge=" + charge + " api=" + api + " variants=" + work.Count +
                          " compiled=" + compiled + " failed=" + failed + " total=" + total.Elapsed.TotalSeconds.ToString("F1") + "s |" + log, failed > 0);
        }

        static string Report(string message, bool warn)
        {
            if (warn) Debug.LogWarning("[LocalOrderPrewarm] " + message);
            else Debug.Log("[LocalOrderPrewarm] " + message);
            return message;
        }
    }
}
#endif
