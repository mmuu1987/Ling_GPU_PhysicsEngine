using System.Collections.Generic;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Optional per-vertex ambient occlusion for VAT units. The baker (MassEngine/顶点AO) writes AO into uv3.x of a COPY of a
    /// profile mesh (same vertices, same order, so the VAT textures still pair) and registers source → copy here. At unit
    /// resolve time <see cref="Substitute"/> swaps the copy in and the VAT shaders multiply ambient (and part of the direct
    /// light) by it. Original meshes and profiles are never modified; an empty or missing library, or the switch off, gives
    /// exactly the previous rendering. Takes effect when unit types are resolved (next battle load).
    /// </summary>
    public sealed class VertexAoLibrary : ScriptableObject
    {
        /// <summary>Resources path of the library asset.</summary>
        public const string ResourcePath = "MassEngineVertexAo";
        public const string PrefKey = "MassEngine.VertexAO";

        [System.Serializable]
        public struct Entry
        {
            public Mesh source;
            public Mesh baked;
            public string note;
        }

        [Tooltip("AO strength on ambient light (0 = off).")]
        [Range(0f, 1f)] public float strength = 1f;
        [Tooltip("How much of the AO also darkens direct sunlight (0 = ambient only).")]
        [Range(0f, 1f)] public float directStrength = 0f; // 2026-10-13 v2: AO darkens ambient only (on sunlight it read as dirt)
        public List<Entry> entries = new List<Entry>();

        private static VertexAoLibrary cached;
        private static bool loaded;
        private static int enabledCache = -1;

        /// <summary>Runtime switch (PlayerPrefs, default on). Editor menu: MassEngine/顶点AO.</summary>
        public static bool Enabled
        {
            get { if (enabledCache < 0) enabledCache = PlayerPrefs.GetInt(PrefKey, 1) != 0 ? 1 : 0; return enabledCache == 1; }
            set { enabledCache = value ? 1 : 0; PlayerPrefs.SetInt(PrefKey, enabledCache); PlayerPrefs.Save(); }
        }

        public static VertexAoLibrary Instance
        {
            get
            {
                if (!loaded) { cached = Resources.Load<VertexAoLibrary>(ResourcePath); loaded = true; }
                return cached;
            }
        }

        /// <summary>Forget the cached asset and switch (after a bake, or in tests).</summary>
        public static void ResetCache() { cached = null; loaded = false; enabledCache = -1; }

        /// <summary>
        /// The AO copy of <paramref name="mesh"/> when the switch is on and a matching copy is registered; otherwise the mesh
        /// itself with <paramref name="strength"/> = 0 (the shaders then ignore uv3 entirely).
        /// </summary>
        public static Mesh Substitute(Mesh mesh, out float strength, out float direct)
        {
            strength = 0f; direct = 0f;
            if (mesh == null || !Enabled) return mesh;
            var lib = Instance;
            if (lib == null || lib.entries == null) return mesh;
            for (int i = 0; i < lib.entries.Count; i++)
            {
                var e = lib.entries[i];
                if (e.source != mesh || e.baked == null) continue;
                if (e.baked.vertexCount != mesh.vertexCount) return mesh; // stale copy: never pair wrong vertices with the VAT
                strength = Mathf.Clamp01(lib.strength);
                direct = Mathf.Clamp01(lib.directStrength);
                return e.baked;
            }
            return mesh;
        }
    }
}
