using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Player camera preferences (2026-10-10 feedback: camera tuning belongs in Settings, not only the Inspector).
    /// Speeds are multipliers on the adaptive camera; pitch/framing drive army focus; follow auto-zoom can be off.
    /// </summary>
    [Serializable]
    public struct WarSandboxCameraSettings
    {
        public int version;
        public float panSpeed, zoomSpeed, lookSpeed;
        public float focusPitch;
        public float framing;
        public bool followAutoZoom;

        public static WarSandboxCameraSettings Default => new WarSandboxCameraSettings
        { version = 1, panSpeed = 1f, zoomSpeed = 1f, lookSpeed = 1f, focusPitch = 50f, framing = .5f, followAutoZoom = true };

        private static bool In(float v, float lo, float hi) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= lo && v <= hi;
        public bool IsValid => version == 1 && In(panSpeed, .5f, 2f) && In(zoomSpeed, .5f, 2f) && In(lookSpeed, .5f, 2f) &&
                               In(focusPitch, 30f, 75f) && In(framing, 0f, 1f);
        /// <summary>Share of the view the army may fill: loose 0.70 … tight 1.00 (default 0.85).</summary>
        public float FrameMargin => Mathf.Lerp(.7f, 1f, framing);
        public string FramingLabel => framing < .34f ? "宽松" : framing > .66f ? "紧凑" : "适中";

        public static bool Same(WarSandboxCameraSettings a, WarSandboxCameraSettings b) =>
            a.version == b.version && a.followAutoZoom == b.followAutoZoom &&
            Mathf.Abs(a.panSpeed - b.panSpeed) < .0001f && Mathf.Abs(a.zoomSpeed - b.zoomSpeed) < .0001f &&
            Mathf.Abs(a.lookSpeed - b.lookSpeed) < .0001f && Mathf.Abs(a.focusPitch - b.focusPitch) < .0001f &&
            Mathf.Abs(a.framing - b.framing) < .0001f;

        // Sliders are 0..1; multipliers map logarithmically so the middle is x1.00 (x0.5 … x2).
        public static float MultiplierToSlider(float m) => Mathf.InverseLerp(-1f, 1f, Mathf.Log(Mathf.Clamp(m, .5f, 2f), 2f));
        public static float SliderToMultiplier(float s) => Mathf.Clamp(Mathf.Round(Mathf.Pow(2f, Mathf.Lerp(-1f, 1f, Mathf.Clamp01(s))) * 20f) / 20f, .5f, 2f);
    }

    /// <summary>Process-wide camera preferences, stored as WarSandboxCameraSettings.json next to the audio settings file.</summary>
    public static class WarSandboxCameraPrefs
    {
        private static bool loaded;
        private static string path;
        private static WarSandboxCameraSettings current = WarSandboxCameraSettings.Default;
        public static string Error { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { loaded = false; path = null; current = WarSandboxCameraSettings.Default; Error = null; }

        public static WarSandboxCameraSettings Current { get { EnsureLoaded(); return current; } }
        public static string FilePath { get { EnsureLoaded(); return path; } }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            string directory = null;
            var audio = WarSandboxAudio.Instance != null || !Application.isPlaying ? WarSandboxAudio.Instance : WarSandboxAudio.Ensure();
            if (audio != null && !string.IsNullOrEmpty(audio.SettingsPath)) directory = Path.GetDirectoryName(audio.SettingsPath);
            if (string.IsNullOrEmpty(directory)) directory = Application.persistentDataPath;
            path = Path.Combine(directory, "WarSandboxCameraSettings.json");
            current = WarSandboxCameraSettings.Default; Error = null;
            try
            {
                if (!File.Exists(path)) return;
                var stored = JsonUtility.FromJson<WarSandboxCameraSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (stored.IsValid) current = stored;
                else Error = "镜头设置内容无效，暂时使用默认镜头设置。";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { Error = "无法读取镜头设置，暂时使用默认镜头设置。"; }
        }

        public static void Set(WarSandboxCameraSettings value) { EnsureLoaded(); if (value.IsValid) current = value; }
        public static void RestoreDefaults() { EnsureLoaded(); current = WarSandboxCameraSettings.Default; }

        public static bool Save(out string error)
        {
            EnsureLoaded(); error = null;
            if (!current.IsValid) { error = "镜头设置超出范围。"; return false; }
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, JsonUtility.ToJson(current, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                var back = JsonUtility.FromJson<WarSandboxCameraSettings>(File.ReadAllText(path, Encoding.UTF8));
                if (!WarSandboxCameraSettings.Same(back, current)) { error = "镜头设置已写入，但读回核验未通过。请重试。"; return false; }
                Error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { error = "无法保存镜头设置，请检查磁盘空间和目录权限。"; return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
