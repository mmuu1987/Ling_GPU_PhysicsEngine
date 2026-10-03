using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MassEngine.Projectiles
{
    /// <summary>
    /// Opt-in impact flash ring for splash projectiles (e.g. a dragon fireball). It exists only when the
    /// scene's ProjectileRenderConfig has an impactMaterial AND a registered unit type declares
    /// CombatConfig.projectileSplashRadius &gt; 0. Otherwise nothing is allocated, the projectile kernel
    /// keeps its legacy variant (keyword off) and no extra draw is issued.
    /// The kernel appends one entry per splash impact (wrapping ring); ProjectileImpact.shader fades
    /// entries by age, so no CPU readback or per-impact bookkeeping is involved.
    /// </summary>
    public sealed class ProjectileImpactFx : IDisposable
    {
        public const string Keyword = "MASS_PROJECTILE_IMPACT_FX";
        public const int DefaultCapacity = 128;
        public static readonly int RingId = Shader.PropertyToID("projectileImpactRing");
        public static readonly int CounterId = Shader.PropertyToID("projectileImpactCounter");
        public static readonly int CapacityId = Shader.PropertyToID("projectileImpactCapacity");
        public static readonly int NowId = Shader.PropertyToID("_ProjectileImpactNow");
        public static readonly int DurationId = Shader.PropertyToID("_ProjectileImpactDuration");

        /// <summary>Mirrors ProjectileImpactData in ProjectileSimulation.compute / ProjectileImpact.shader (32 bytes).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ImpactData
        {
            public Vector3 position;
            public float radius;
            public float time;
            public float groundY;
            public Vector2 padding;
        }

        public static int Stride => Marshal.SizeOf<ImpactData>();
        public ComputeBuffer Ring { get; private set; }
        public ComputeBuffer Counter { get; private set; }
        public int Capacity { get; }
        public bool IsValid => Ring != null && Counter != null;

        public ProjectileImpactFx(int capacity = DefaultCapacity)
        {
            Capacity = Mathf.Clamp(capacity, 1, 4096);
            Ring = new ComputeBuffer(Capacity, Stride);
            Ring.SetData(new ImpactData[Capacity]); // radius 0 = empty slot, never drawn
            Counter = new ComputeBuffer(1, sizeof(uint));
            Counter.SetData(new uint[1]);
        }

        /// <summary>Enables the impact variant of the simulate kernel and binds the ring.</summary>
        public void Bind(ComputeShader shader, int kernel)
        {
            if (shader == null || kernel < 0 || !IsValid) return;
            shader.EnableKeyword(Keyword);
            shader.SetBuffer(kernel, RingId, Ring);
            shader.SetBuffer(kernel, CounterId, Counter);
            shader.SetInt(CapacityId, Capacity);
        }

        /// <summary>Restores the legacy kernel variant (the shader asset is shared between scenes).</summary>
        public static void Unbind(ComputeShader shader)
        {
            if (shader != null) shader.DisableKeyword(Keyword);
        }

        public void Dispose()
        {
            Ring?.Release();
            Counter?.Release();
            Ring = null;
            Counter = null;
        }
    }
}
