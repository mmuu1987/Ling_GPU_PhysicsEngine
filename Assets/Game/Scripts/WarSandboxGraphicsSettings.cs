using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MassEngine.Game
{
    /// <summary>
    /// Player graphics preferences (2026-10-12): main-light shadow distance, 50–250 m in 10 m steps, default 100 m.
    /// Receivers fade shadows out before this distance (Docs/Rendering-Shadows.md), so any value is artefact-free;
    /// longer only spreads the same 4096 shadow map over more ground (softer near shadows) and adds prop casters.
    /// </summary>
    [Serializable]
    public struct WarSandboxGraphicsSettings
    {
        public const float MinShadowDistance = 50f, MaxShadowDistance = 250f, ShadowDistanceStep = 10f, DefaultShadowDistance = 100f;

        public int version;
        public float shadowDistance;

        public static WarSandboxGraphicsSettings Default => new WarSandboxGraphicsSettings { version = 1, shadowDistance = DefaultShadowDistance };

        public bool IsValid => version == 1 && !float.IsNaN(shadowDistance) && !float.IsInfinity(shadowDistance) &&
                               shadowDistance >= MinShadowDistance && shadowDistance <= MaxShadowDistance;

        public static bool Same(WarSandboxGraphicsSettings a, WarSandboxGraphicsSettings b) =>
            a.version == b.version && Mathf.Abs(a.shadowDistance - b.shadowDistance) < .001f;

        public static float DistanceToSlider(float d) => Mathf.InverseLerp(MinShadowDistance, MaxShadowDistance, d);
        public static float SliderToDistance(float s) =>
            Mathf.Clamp(Mathf.Round(Mathf.Lerp(MinShadowDistance, MaxShadowDistance, Mathf.Clamp01(s)) / ShadowDistanceStep) * ShadowDistanceStep,
                MinShadowDistance, MaxShadowDistance);
    }

    /// <summary>Process-wide graphics preferences, stored as WarSandboxGraphicsSettings.json next to the audio settings
    /// file, applied to the active URP asset. In the Editor the asset values are restored when Play mode ends, so trying
    /// settings never rewrites the project's quality assets.</summary>
    public static class WarSandboxGraphicsPrefs
    {
        private static bool loaded;
        private static string path;
        private static WarSandboxGraphicsSettings current = WarSandboxGraphicsSettings.Default;
        private static readonly Dictionary<UniversalRenderPipelineAsset, float> originals = new Dictionary<UniversalRenderPipelineAsset, float>();
        public static string Error { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { RestoreAssets(); loaded = false; path = null; current = WarSandboxGraphicsSettings.Default; Error = null; }

        public static WarSandboxGraphicsSettings Current { get { EnsureLoaded(); return current; } }
        public static string FilePath { get { EnsureLoaded(); return path; } }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            string directory = null;
            var audio = WarSandboxAudio.Instance != null || !Application.isPlaying ? WarSandboxAudio.Instance : WarSandboxAudio.Ensure();
            if (audio != null && !string.IsNullOrEmpty(audio.SettingsPath)) directory = Path.GetDirectoryName(audio.SettingsPath);
            if (string.IsNullOrEmpty(directory)) directory = Application.persistentDataPath;
            path = Path.Combine(directory, "WarSandboxGraphicsSettings.json");
            current = WarSandboxGraphicsSettings.Default; Error = null;
            try
            {
                if (!File.Exists(path)) return;
                var stored = JsonUtility.FromJson<WarSandboxGraphicsSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (stored.IsValid) current = stored;
                else Error = "画面设置内容无效，暂时使用默认画面设置。";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { Error = "无法读取画面设置，暂时使用默认画面设置。"; }
        }

        /// <summary>Pushes the current values to the active pipeline asset. Called at session start and on every change.</summary>
        public static void Apply()
        {
            EnsureLoaded();
            if (!Application.isPlaying) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            if (!originals.ContainsKey(urp)) originals[urp] = urp.shadowDistance;
            if (!Mathf.Approximately(urp.shadowDistance, current.shadowDistance)) urp.shadowDistance = current.shadowDistance;
        }

        public static void Set(WarSandboxGraphicsSettings value) { EnsureLoaded(); if (value.IsValid) { current = value; Apply(); } }
        public static void RestoreDefaults() { EnsureLoaded(); current = WarSandboxGraphicsSettings.Default; Apply(); }

        public static bool Save(out string error)
        {
            EnsureLoaded(); error = null;
            if (!current.IsValid) { error = "画面设置超出范围。"; return false; }
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, JsonUtility.ToJson(current, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                var back = JsonUtility.FromJson<WarSandboxGraphicsSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (!WarSandboxGraphicsSettings.Same(back, current)) { error = "画面设置已写入，但读回核验未通过。请重试。"; return false; }
                Error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { error = "无法保存画面设置，请检查磁盘空间和目录权限。"; return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        private static void RestoreAssets()
        {
            foreach (var pair in originals) if (pair.Key != null) pair.Key.shadowDistance = pair.Value;
            originals.Clear();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void HookPlayModeExit() =>
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            { if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) RestoreAssets(); };
#endif
    }
}
