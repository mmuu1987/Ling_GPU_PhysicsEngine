using UnityEngine;

namespace MassEngine
{
    public sealed partial class MassEngineManager
    {
        // 炸飞 (2026-10-17, render only): see AgentKnockFx.
        private AgentKnockFx agentKnock;
        private float[] knockHeightCache;

        /// <summary>
        /// Allocates the knock buffer only while some unit type knocks bodies flying AND the splash
        /// impact ring exists (the kernel reads the blasts from it). Otherwise releases it.
        /// </summary>
        private void SyncAgentKnock()
        {
            int typeCount = gpuSettingsCache != null ? gpuSettingsCache.Length : 0;
            if (knockHeightCache == null || knockHeightCache.Length != typeCount)
                knockHeightCache = new float[typeCount];
            bool wanted = typeCount > 0 && projectileImpactFx != null && unitTypeRegistry != null
                && unitTypeRegistry.FillKnockFlyHeights(knockHeightCache);
            int agents = unitTypeRegistry != null ? unitTypeRegistry.TotalAgentCount : 0;
            if (wanted && (agentKnock == null || agentKnock.AgentCount != Mathf.Max(1, agents) || agentKnock.TypeCount != typeCount))
            {
                agentKnock?.Dispose();
                agentKnock = new AgentKnockFx(agents, typeCount);
            }
            else if (!wanted && agentKnock != null)
            {
                agentKnock.Dispose();
                agentKnock = null;
            }
            if (agentKnock != null)
                agentKnock.UploadHeights(knockHeightCache);
            if (pipelineOrchestrator != null)
                pipelineOrchestrator.AgentKnock = agentKnock;
            AgentKnockFx.ApplyGlobal(agentKnock);
        }

        private void ReleaseAgentKnock()
        {
            if (agentKnock != null)
                agentKnock.Dispose();
            agentKnock = null;
            knockHeightCache = null;
            AgentKnockFx.ApplyGlobal(null);
        }
    }
}
