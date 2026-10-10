#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Explicit editor/build preparation for the P9 local-order compute variant.
    /// This deliberately does not run on scene load, play-mode entry, or first command:
    /// Unity's internal variant compiler is synchronous and took about six seconds on
    /// the validation machine. The generated data lives only in Library/ShaderCache.
    /// </summary>
    public static class P9LocalCommandShaderWarmup
    {
        const string ComputePath = "Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute";
        const string KernelName = "SimulateLocalAttackMelee";
        const string MarkerPath = "Library/WarSandbox/P9ShaderWarmup-D3D11.json";
        static readonly string[] Keywords = { "MASS_TERRAIN_ENABLED", "MASS_LOCAL_ORDERS" };

        [Serializable] sealed class CacheEntry { public string path; public string sha256; public long bytes; }
        [Serializable] sealed class Marker
        {
            public string format = "p9-local-command-shader-warmup-v1";
            public string sourceFingerprint, unityVersion, api, buildTarget, kernel, keywords;
            public double compileMilliseconds;
            public CacheEntry[] cacheEntries;
        }

        [MenuItem("Tools/WarSandbox/Performance/Precompile P9 local-command shader (D3D11)")]
        public static void PrecompileMenu() => PrecompileForP9Verification();

        // Also callable explicitly by a developer/build preparation step.
        public static void PrecompileForP9Verification()
        {
            string result = null;
            try { result = Run(); UnityEngine.Debug.Log("P9 local-command shader preparation: " + result); }
            catch (Exception ex) { UnityEngine.Debug.LogError("P9 local-command shader preparation failed safely: " + ex); throw; }
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("P9 Shader Preparation", result, "OK");
        }

        static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run this explicit one-time preparation outside Play Mode; never on the first-command path.");
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Wait for script compilation to finish before preparing the shader.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("This measured cache entry is specific to Editor D3D11 / StandaloneWindows64. No other backend is assumed.");

            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            if (shader == null) throw new FileNotFoundException("Compute asset not found", ComputePath);
            int kernel = shader.FindKernel(KernelName);
            string fingerprint = Fingerprint();
            string markerPath = Path.GetFullPath(MarkerPath);
            if (TrySkip(markerPath, fingerprint, out string reason)) return "Already prepared; " + reason;

            Type util = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ShaderUtil", throwOnError: true);
            MethodInfo compile = util.GetMethod("CompileComputeShaderVariant", BindingFlags.Static | BindingFlags.NonPublic);
            if (compile == null) throw new MissingMethodException("UnityEditor.ShaderUtil.CompileComputeShaderVariant");

            string cacheRoot = Path.GetFullPath(Path.Combine("Library", "ShaderCache", "compute"));
            Dictionary<string, CacheEntry> before = Snapshot(cacheRoot);
            var timer = Stopwatch.StartNew();
            object info;
            try
            {
                info = compile.Invoke(null, new object[]
                {
                    shader, kernel, Keywords, GraphicsDeviceType.Direct3D11, BuildTarget.StandaloneWindows64
                });
            }
            catch (TargetInvocationException e) { throw new InvalidOperationException("Unity's compute compiler threw", e.InnerException ?? e); }
            timer.Stop();
            if (info == null) throw new InvalidOperationException("Unity returned no variant compile information.");
            Type infoType = info.GetType();
            bool success = (bool)infoType.GetProperty("Success", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(info);
            if (!success) throw new InvalidOperationException("Unity did not compile the requested local-command variant.");
            var data = infoType.GetProperty("ShaderData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(info) as byte[];
            if (data == null || data.Length == 0) throw new InvalidOperationException("Compiler returned an empty D3D11 program.");

            Dictionary<string, CacheEntry> after = Snapshot(cacheRoot);
            CacheEntry[] entries = after.Values.OrderBy(x => x.path, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0) throw new InvalidOperationException("Unity reported success but no compute cache entry exists; do not claim prewarming.");
            var marker = new Marker
            {
                sourceFingerprint = fingerprint, unityVersion = Application.unityVersion,
                api = GraphicsDeviceType.Direct3D11.ToString(), buildTarget = BuildTarget.StandaloneWindows64.ToString(),
                kernel = KernelName, keywords = string.Join(";", Keywords),
                compileMilliseconds = timer.Elapsed.TotalMilliseconds, cacheEntries = entries
            };
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
            string temp = markerPath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(marker, true), new UTF8Encoding(false));
            if (File.Exists(markerPath)) File.Delete(markerPath);
            File.Move(temp, markerPath);
            int added = after.Keys.Except(before.Keys).Count();
            return string.Format("Compiled {0} (ShaderData {1:N0} bytes) in {2:N0} ms; compute-cache entries={3}, new={4}. This was an explicit preparation step; no scene/command code ran.", KernelName, data.Length, timer.Elapsed.TotalMilliseconds, entries.Length, added);
        }

        static bool TrySkip(string markerPath, string fingerprint, out string reason)
        {
            reason = null;
            if (!File.Exists(markerPath)) return false;
            try
            {
                var marker = JsonUtility.FromJson<Marker>(File.ReadAllText(markerPath));
                if (marker == null || marker.format != "p9-local-command-shader-warmup-v1" ||
                    marker.sourceFingerprint != fingerprint || marker.unityVersion != Application.unityVersion ||
                    marker.api != GraphicsDeviceType.Direct3D11.ToString() || marker.buildTarget != BuildTarget.StandaloneWindows64.ToString() ||
                    marker.kernel != KernelName || marker.keywords != string.Join(";", Keywords) || marker.cacheEntries == null || marker.cacheEntries.Length == 0)
                    return false;
                string root = Path.GetFullPath(Path.Combine("Library", "ShaderCache", "compute"));
                foreach (var row in marker.cacheEntries)
                {
                    string file = Path.Combine(root, row.path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(file) || HashFile(file) != row.sha256) return false;
                }
                reason = "Unity version, sources, backend, keyword set, and cached bytes match the marker.";
                return true;
            }
            catch { return false; }
        }

        static string Fingerprint()
        {
            var paths = AssetDatabase.GetDependencies(ComputePath, true)
                .Where(p => p.EndsWith(".compute", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            using (var sha = SHA256.Create())
            {
                var text = new StringBuilder(Application.unityVersion).Append('|').Append(ComputePath).Append('|')
                    .Append(GraphicsDeviceType.Direct3D11).Append('|').Append(BuildTarget.StandaloneWindows64).Append('|')
                    .Append(KernelName).Append('|').Append(string.Join(";", Keywords)).Append('\n');
                foreach (string path in paths)
                {
                    string absolute = Path.GetFullPath(path);
                    if (!File.Exists(absolute)) throw new FileNotFoundException("Compute dependency missing", path);
                    text.Append(path).Append('|').Append(HashFile(absolute)).Append('\n');
                }
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }

        static Dictionary<string, CacheEntry> Snapshot(string root)
        {
            var result = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            if (!Directory.Exists(root)) return result;
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                result[relative] = new CacheEntry { path = relative, sha256 = HashFile(file), bytes = new FileInfo(file).Length };
            }
            return result;
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
