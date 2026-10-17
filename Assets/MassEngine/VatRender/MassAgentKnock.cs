using System;
using UnityEngine;
using MassEngine.Projectiles;

namespace MassEngine
{
    /// <summary>
    /// 炸飞 / knock-fly (2026-10-17, render only). Exists only while some unit type has
    /// CombatConfig.knockFlyHeight &gt; 0 AND the splash impact ring (ProjectileImpactFx) exists.
    /// The LOD classification kernel latches once per death whether the body died inside such a
    /// blast (variant MASS_AGENT_KNOCK) and writes one float4 per agent; the agent shaders read it
    /// through the global _MassKnock buffer. Nothing in agentBuffer or the combat kernels changes.
    /// </summary>
    public sealed class AgentKnockFx : IDisposable
    {
        public const string Keyword = "MASS_AGENT_KNOCK";
        public static readonly int KnockId = Shader.PropertyToID("_MassKnock");
        public static readonly int KnockOnId = Shader.PropertyToID("_MassKnockOn");
        public static readonly int HeightsId = Shader.PropertyToID("_MassKnockHeights");
        public static readonly int TypeCountId = Shader.PropertyToID("_MassKnockTypeCount");

        public ComputeBuffer Knock { get; private set; }
        public ComputeBuffer Heights { get; private set; }
        public int AgentCount { get; }
        public int TypeCount { get; }
        public bool IsValid => Knock != null && Heights != null;

        public AgentKnockFx(int agentCount, int typeCount)
        {
            AgentCount = Mathf.Max(1, agentCount);
            TypeCount = Mathf.Max(1, typeCount);
            Knock = new ComputeBuffer(AgentCount, sizeof(float) * 4);
            Knock.SetData(new Vector4[AgentCount]); // w = 0: not latched yet
            Heights = new ComputeBuffer(TypeCount, sizeof(float));
            Heights.SetData(new float[TypeCount]);
        }

        public void UploadHeights(float[] heights)
        {
            if (Heights != null && heights != null && heights.Length == TypeCount)
                Heights.SetData(heights);
        }

        /// <summary>Enables the knock variant of the classify kernel and binds the ring + tables.</summary>
        public void Bind(ComputeShader shader, int kernel, ProjectileImpactFx impacts)
        {
            if (shader == null || kernel < 0 || !IsValid || impacts == null || !impacts.IsValid)
            {
                Unbind(shader);
                return;
            }
            shader.EnableKeyword(Keyword);
            shader.SetBuffer(kernel, KnockId, Knock);
            shader.SetBuffer(kernel, HeightsId, Heights);
            shader.SetInt(TypeCountId, TypeCount);
            shader.SetBuffer(kernel, ProjectileImpactFx.RingId, impacts.Ring);
            shader.SetInt(ProjectileImpactFx.CapacityId, impacts.Capacity);
        }

        /// <summary>Restores the legacy classify variant (the compute asset is shared with kernel tests).</summary>
        public static void Unbind(ComputeShader shader)
        {
            if (shader != null) shader.DisableKeyword(Keyword);
        }

        /// <summary>Publishes the per-agent knock buffer to the agent shaders (null = off).</summary>
        public static void ApplyGlobal(AgentKnockFx knock)
        {
            if (knock != null && knock.IsValid)
            {
                Shader.SetGlobalBuffer(KnockId, knock.Knock);
                Shader.SetGlobalFloat(KnockOnId, 1f);
            }
            else
            {
                Shader.SetGlobalFloat(KnockOnId, 0f);
            }
        }

        public void Dispose()
        {
            Knock?.Release();
            Heights?.Release();
            Knock = null;
            Heights = null;
        }
    }
}
