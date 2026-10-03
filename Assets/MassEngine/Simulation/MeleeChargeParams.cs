using System;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Opt-in melee charge table (CombatConfig.chargeDamageMultiplier &gt; 1). One float2 per unit type:
    /// x = damage multiplier of the first melee hit on arrival, y = minimum approach speed / maxSpeed.
    /// Allocated only while a unit type charges; otherwise the combat kernel keeps its legacy variant.
    /// </summary>
    public sealed class MeleeChargeParams : IDisposable
    {
        public const string Keyword = "MASS_MELEE_CHARGE";
        public static readonly int BufferId = Shader.PropertyToID("unitTypeMeleeCharge");

        private ComputeBuffer buffer;
        private Vector2[] uploaded;

        public ComputeBuffer Buffer => buffer;
        public bool IsValid => buffer != null && buffer.IsValid();

        public void Upload(Vector2[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("Melee charge table needs one entry per unit type.", nameof(values));
            if (buffer == null || buffer.count != values.Length)
            {
                buffer?.Release();
                buffer = new ComputeBuffer(values.Length, sizeof(float) * 2);
                uploaded = null;
            }
            if (uploaded != null && uploaded.Length == values.Length)
            {
                bool same = true;
                for (int i = 0; i < values.Length && same; i++)
                    same = uploaded[i] == values[i];
                if (same)
                    return;
            }
            buffer.SetData(values);
            uploaded = (Vector2[])values.Clone();
        }

        public void Bind(ComputeShader shader, int kernel)
        {
            shader.EnableKeyword(Keyword);
            shader.SetBuffer(kernel, BufferId, buffer);
        }

        public static void Unbind(ComputeShader shader)
        {
            shader.DisableKeyword(Keyword);
        }

        public void Dispose()
        {
            buffer?.Release();
            buffer = null;
            uploaded = null;
        }
    }
}
