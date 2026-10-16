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
        // Flight (2026-10-15): one float per unit type, flight altitude in metres (0 = ground). Also bound to the
        // projectile kernel under ProjectileFlightKeyword while any type flies.
        public static readonly int FlightBufferId = Shader.PropertyToID("unitTypeFlight");
        public const string ProjectileFlightKeyword = "MASS_PROJECTILE_FLIGHT";

        private ComputeBuffer buffer;
        private Vector2[] uploaded;
        private ComputeBuffer splashBuffer;
        private float[] uploadedSplash;
        private ComputeBuffer flightBuffer;
        private float[] uploadedFlight;

        public ComputeBuffer FlightBuffer => flightBuffer;
        public bool AnyFlight { get; private set; }

        public ComputeBuffer Buffer => buffer;
        public bool IsValid => buffer != null && buffer.IsValid() && splashBuffer != null && splashBuffer.IsValid() &&
            flightBuffer != null && flightBuffer.IsValid();

        public void Upload(Vector2[] values) => Upload(values, null);

        public void Upload(Vector2[] values, float[] splash) => Upload(values, splash, null);

        public void Upload(Vector2[] values, float[] splash, float[] flight)
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
            UploadFlight(flight, values.Length);
                    return;
                }
            }
            UploadSplash(splash, values.Length);
            UploadFlight(flight, values.Length);
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

        private void UploadFlight(float[] flight, int count)
        {
            if (flight == null || flight.Length != count) flight = new float[count];
            bool any = false;
            for (int i = 0; i < count; i++) any |= flight[i] > 0f;
            AnyFlight = any;
            if (flightBuffer == null || flightBuffer.count != count)
            {
                flightBuffer?.Release();
                flightBuffer = new ComputeBuffer(count, sizeof(float));
                uploadedFlight = null;
            }
            if (uploadedFlight != null && uploadedFlight.Length == count)
            {
                bool same = true;
                for (int i = 0; i < count && same; i++)
                    same = uploadedFlight[i] == flight[i];
                if (same)
                    return;
            }
            flightBuffer.SetData(flight);
            uploadedFlight = (float[])flight.Clone();
        }

        /// <summary>Projectile kernel: flight-aware unit capsules and splash layers while any type flies.</summary>
        public void BindProjectile(ComputeShader shader, int kernel)
        {
            if (shader == null || kernel < 0) return;
            if (AnyFlight && flightBuffer != null && flightBuffer.IsValid())
            {
                shader.EnableKeyword(ProjectileFlightKeyword);
                shader.SetBuffer(kernel, FlightBufferId, flightBuffer);
            }
            else
                shader.DisableKeyword(ProjectileFlightKeyword);
        }

        public static void UnbindProjectile(ComputeShader shader)
        {
            if (shader != null) shader.DisableKeyword(ProjectileFlightKeyword);
        }

        public void Bind(ComputeShader shader, int kernel)
        {
            shader.EnableKeyword(Keyword);
            shader.SetBuffer(kernel, BufferId, buffer);
            shader.SetBuffer(kernel, SplashBufferId, splashBuffer);
            shader.SetBuffer(kernel, FlightBufferId, flightBuffer);
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
            flightBuffer?.Release();
            flightBuffer = null;
            uploadedFlight = null;
            AnyFlight = false;
        }
    }
}
