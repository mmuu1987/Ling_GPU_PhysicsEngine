using UnityEngine;
using UnityEngine.Rendering;
using static MassGPUPhysics.Stage7.MassGpuShaderPropertyIds_Stage7;

namespace MassGPUPhysics.Stage7
{
    public sealed class MassGpuRenderDispatcher_Stage7
    {
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        public void Draw(UnitTypeRegistry registry, MassGpuBufferManager_Stage7 buffers, Bounds bounds)
        {
            if (registry == null || buffers == null || !buffers.IsAllocated)
                return;

            for (int i = 0; i < registry.RegisteredTypes.Count; i++)
            {
                IUnitType unitType = registry.RegisteredTypes[i];
                RenderConfig render = unitType.Config != null ? unitType.Config.renderConfig : null;
                if (render == null)
                    continue;

                bool isDefender = unitType.TeamId == 1;
                DrawLod(render, 0, render.nearMesh, render.nearMaterial, buffers.agentBuffer, isDefender ? buffers.nearDefenderAgentIndexBuffer : buffers.nearAttackerAgentIndexBuffer, isDefender ? buffers.nearDefenderArgsBuffer : buffers.nearAttackerArgsBuffer, bounds);
                DrawLod(render, 1, render.midMesh, render.midMaterial, buffers.agentBuffer, isDefender ? buffers.midDefenderAgentIndexBuffer : buffers.midAttackerAgentIndexBuffer, isDefender ? buffers.midDefenderArgsBuffer : buffers.midAttackerArgsBuffer, bounds);
                DrawLod(render, 2, render.farMesh, render.farMaterial, buffers.agentBuffer, isDefender ? buffers.farDefenderAgentIndexBuffer : buffers.farAttackerAgentIndexBuffer, isDefender ? buffers.farDefenderArgsBuffer : buffers.farAttackerArgsBuffer, bounds);
            }
        }

        private void DrawLod(RenderConfig render, int lodLevel, Mesh mesh, Material material, ComputeBuffer agentBuffer, ComputeBuffer visibleIndices, ComputeBuffer argsBuffer, Bounds bounds)
        {
            if (mesh == null || material == null || agentBuffer == null || visibleIndices == null || argsBuffer == null)
                return;

            propertyBlock.Clear();
            propertyBlock.SetBuffer(AgentBufferId, agentBuffer);
            propertyBlock.SetBuffer(VisibleAgentIndicesId, visibleIndices);
            SyncVatProfile(propertyBlock, render != null ? render.vatProfile : null, lodLevel);
            Graphics.DrawMeshInstancedIndirect(
                mesh,
                0,
                material,
                bounds,
                argsBuffer,
                0,
                propertyBlock,
                GetShadowCasting(render, lodLevel),
                GetReceiveShadows(render, lodLevel));
        }

        private static ShadowCastingMode GetShadowCasting(RenderConfig render, int lodLevel)
        {
            if (render == null)
                return lodLevel == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;

            if (lodLevel <= 0)
                return render.nearShadowCasting;
            if (lodLevel == 1)
                return render.midShadowCasting;
            return render.farShadowCasting;
        }

        private static bool GetReceiveShadows(RenderConfig render, int lodLevel)
        {
            if (render == null)
                return lodLevel == 0;

            if (lodLevel <= 0)
                return render.nearReceiveShadows;
            if (lodLevel == 1)
                return render.midReceiveShadows;
            return render.farReceiveShadows;
        }

        private static void SyncVatProfile(MaterialPropertyBlock block, ScriptableObject profile, int lodLevel)
        {
            if (block == null || profile == null || !MassGpuSystemManager_Stage7.TryReadVatProfile(profile, out MassGpuSystemManager_Stage7.VatProfileData profileData, out string ignoredError))
                return;

            GetVatLayoutForLod(profileData, lodLevel, out Texture positionTexture, out Texture normalTexture, out int textureWidth, out int textureHeight, out int rowsPerFrame);
            block.SetTexture(VATPosTexId, positionTexture);
            block.SetTexture(VATNormTexId, normalTexture);
            block.SetFloat(VATTexWidthId, Mathf.Max(1, textureWidth));
            block.SetFloat(VATTexHeightId, Mathf.Max(1, textureHeight));
            block.SetFloat(VATRowsPerFrameId, Mathf.Max(1, rowsPerFrame));
            block.SetFloat(VATFrameCountId, Mathf.Max(1, profileData.totalFrameCount));
            block.SetFloat(VATFrameRateId, Mathf.Max(1, profileData.frameRate));
            SyncClip(block, IdleClipStartFrameId, IdleClipFrameCountId, IdleClipFrameRateId, profileData.idle);
            SyncClip(block, MoveClipStartFrameId, MoveClipFrameCountId, MoveClipFrameRateId, profileData.move);
            SyncClip(block, AttackClipStartFrameId, AttackClipFrameCountId, AttackClipFrameRateId, profileData.attack);
            SyncClip(block, DeathClipStartFrameId, DeathClipFrameCountId, DeathClipFrameRateId, profileData.death);
        }

        private static void GetVatLayoutForLod(
            MassGpuSystemManager_Stage7.VatProfileData profileData,
            int lodLevel,
            out Texture positionTexture,
            out Texture normalTexture,
            out int textureWidth,
            out int textureHeight,
            out int rowsPerFrame)
        {
            if (lodLevel == 1 && profileData.hasMidLod)
            {
                positionTexture = profileData.midLodPositionTexture;
                normalTexture = profileData.midLodNormalTexture;
                textureWidth = profileData.midLodTextureWidth;
                textureHeight = profileData.midLodTextureHeight;
                rowsPerFrame = profileData.midLodRowsPerFrame;
                return;
            }

            if (lodLevel > 0 && profileData.hasLowLod)
            {
                positionTexture = profileData.lowLodPositionTexture;
                normalTexture = profileData.lowLodNormalTexture;
                textureWidth = profileData.lowLodTextureWidth;
                textureHeight = profileData.lowLodTextureHeight;
                rowsPerFrame = profileData.lowLodRowsPerFrame;
                return;
            }

            if (lodLevel > 1 && profileData.hasMidLod)
            {
                positionTexture = profileData.midLodPositionTexture;
                normalTexture = profileData.midLodNormalTexture;
                textureWidth = profileData.midLodTextureWidth;
                textureHeight = profileData.midLodTextureHeight;
                rowsPerFrame = profileData.midLodRowsPerFrame;
                return;
            }

            positionTexture = profileData.positionTexture;
            normalTexture = profileData.normalTexture;
            textureWidth = profileData.textureWidth;
            textureHeight = profileData.textureHeight;
            rowsPerFrame = profileData.rowsPerFrame;
        }

        private static void SyncClip(MaterialPropertyBlock block, int startFrameId, int frameCountId, int frameRateId, MassGpuSystemManager_Stage7.VatClipData clip)
        {
            block.SetFloat(startFrameId, Mathf.Max(0, clip.startFrame));
            block.SetFloat(frameCountId, Mathf.Max(1, clip.frameCount));
            block.SetFloat(frameRateId, Mathf.Max(1, clip.frameRate));
        }
    }
}
