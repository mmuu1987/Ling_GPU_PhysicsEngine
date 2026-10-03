using System;
using System.IO;
using UnityEngine;

namespace MassEngine.Game
{
    public enum WarSandboxSoundCue { Command, Rejected, Start, ControlPoint, Finish }

    /// <summary>One shared 2D source for aggregate battle feedback; no per-agent audio objects.</summary>
    public sealed class WarSandboxAudio : MonoBehaviour
    {
        public static WarSandboxAudio Instance { get; private set; }
        public WarSandboxAudioSettings Settings { get; private set; }
        public string SettingsError { get; private set; }
        public string SettingsPath => store.FilePath;
        public int PlayedCues { get; private set; }
        public AudioSource Source => source;
        private WarSandboxSettingsStore store;
        private AudioSource source;
        private readonly AudioClip[] clips = new AudioClip[5];
        private readonly int[] requests = new int[5];
        private readonly float[] nextCue = new float[5];
        private float importantCueUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
        public static WarSandboxAudio Ensure()
        {
            if (Instance == null) new GameObject("Sandbox Audio").AddComponent<WarSandboxAudio>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject);
            string path = Path.Combine(Application.persistentDataPath, "WarSandboxSettings.json");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--war-sandbox-settings-file=", StringComparison.Ordinal))
                {
                    var candidate = argument.Substring("--war-sandbox-settings-file=".Length);
                    if (!Path.IsPathFullyQualified(candidate)) throw new ArgumentException("Settings override must be absolute.");
                    path = candidate;
                }
#endif
            store = new WarSandboxSettingsStore(path);
            store.TryLoad(out var settings, out var error); Settings = settings; SettingsError = error;
            source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0;
            source.ignoreListenerPause = true; ApplySettings();
            for (int i = 0; i < clips.Length; i++) clips[i] = WarSandboxSoundSynthesis.Create((WarSandboxSoundCue)i);
        }

        public void SetVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            var settings = Settings; settings.volume = Mathf.Clamp01(value); Settings = settings; ApplySettings();
        }
        public void SetMuted(bool value) { var settings = Settings; settings.muted = value; Settings = settings; ApplySettings(); }
        public void RestoreDefaults() { Settings = WarSandboxAudioSettings.Default; ApplySettings(); }
        private void ApplySettings()
        {
            source.volume = Settings.volume; source.mute = Settings.muted;
            if (Settings.muted || Settings.volume <= 0) source.Stop();
        }
        public bool SaveSettings()
        {
            bool saved = store.TrySave(Settings, out var error); SettingsError = error; return saved;
        }
        public int RequestCount(WarSandboxSoundCue cue) => requests[(int)cue];
        public void Play(WarSandboxSoundCue cue)
        {
            int index = (int)cue; requests[index]++;
            float now = Time.unscaledTime;
            bool minor = cue == WarSandboxSoundCue.Command || cue == WarSandboxSoundCue.Rejected;
            if (Settings.muted || Settings.volume <= 0 || now < nextCue[index] || (minor && now < importantCueUntil)) return;
            nextCue[index] = now + (cue == WarSandboxSoundCue.ControlPoint ? .6f : .12f);
            if (!minor) { importantCueUntil = now + .3f; source.Stop(); }
            source.PlayOneShot(clips[index]); PlayedCues++;
        }
        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            foreach (var clip in clips) if (clip != null) Destroy(clip);
        }
    }

    /// <summary>Original short tonal cues, generated locally without third-party audio assets.</summary>
    public static class WarSandboxSoundSynthesis
    {
        public static AudioClip Create(WarSandboxSoundCue cue)
        {
            float[] notes;
            switch (cue)
            {
                case WarSandboxSoundCue.Rejected: notes = new[] { 220f, 146.8f }; break;
                case WarSandboxSoundCue.Start: notes = new[] { 330f, 440f, 660f }; break;
                case WarSandboxSoundCue.ControlPoint: notes = new[] { 523.3f, 659.3f }; break;
                case WarSandboxSoundCue.Finish: notes = new[] { 392f, 493.9f, 587.3f, 784f }; break;
                default: notes = new[] { 480f, 640f }; break;
            }
            const int rate = 24000;
            int samplesPerNote = cue == WarSandboxSoundCue.Command ? 1440 : 3120;
            var data = new float[samplesPerNote * notes.Length];
            for (int n = 0; n < notes.Length; n++) for (int i = 0; i < samplesPerNote; i++)
            {
                float envelope = Mathf.Pow(Mathf.Sin(Mathf.PI * i / (samplesPerNote - 1)), 2);
                float phase = 2 * Mathf.PI * notes[n] * i / rate;
                data[n * samplesPerNote + i] = .22f * envelope * (Mathf.Sin(phase) + .2f * Mathf.Sin(phase * 2));
            }
            var clip = AudioClip.Create("Sandbox " + cue, data.Length, 1, rate, false);
            clip.SetData(data, 0); return clip;
        }
    }
}
