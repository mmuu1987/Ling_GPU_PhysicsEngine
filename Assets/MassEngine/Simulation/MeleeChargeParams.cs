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
        // Kaiju B (2026-10-15): one float per unit type, melee splash radius (0 = single target).
        public static readonly int SplashBufferId = Shader.PropertyToID("unitTypeMeleeSplash");

        private ComputeBuffer buffer;
        private Vector2[] uploaded;
        private ComputeBuffer splashBuffer;
        private float[] uploadedSplash;

        public ComputeBuffer Buffer => buffer;
        public bool IsValid => buffer != null && buffer.IsValid() && splashBuffer != null && splashBuffer.IsValid();

        public void Upload(Vector2[] values) => Upload(values, null);

        public void Upload(Vector2[] values, float[] splash)
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
                {
                    UploadSplash(splash, values.Length);
                    return;
                }
            }
            UploadSplash(splash, values.Length);
            buffer.SetData(values);
            uploaded = (Vector2[])values.Clone();
        }

        private void UploadSplash(float[] splash, int count)
        {
            if (splash == null || splash.Length != count) splash = new float[count];
            if (splashBuffer == null || splashBuffer.count != count)
            {
                splashBuffer?.Release();
                splashBuffer = new ComputeBuffer(count, sizeof(float));
                uploadedSplash = null;
            }
            if (uploadedSplash != null && uploadedSplash.Length == count)
            {
                bool same = true;
                for (int i = 0; i < count && same; i++)
                    same = uploadedSplash[i] == splash[i];
                if (same)
                    return;
            }
            splashBuffer.SetData(splash);
            uploadedSplash = (float[])splash.Clone();
        }

        public void Bind(ComputeShader shader, int kernel)
        {
            shader.EnableKeyword(Keyword);
            shader.SetBuffer(kernel, BufferId, buffer);
            shader.SetBuffer(kernel, SplashBufferId, splashBuffer);
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
            splashBuffer?.Release();
            splashBuffer = null;
            uploadedSplash = null;
        }
    }
}
