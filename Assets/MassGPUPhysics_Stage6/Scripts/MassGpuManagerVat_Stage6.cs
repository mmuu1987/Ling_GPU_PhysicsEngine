using UnityEngine;
using UnityEngine.Rendering;

public partial class GPUInstancingManager_Stage6
{
    private void SyncVatClipWindows(Material material)
    {
        if (material == null)
            return;

        material.SetFloat(IdleClipStartFrameId, Mathf.Max(0f, idleClipFrameRange.x));
        material.SetFloat(IdleClipFrameCountId, Mathf.Max(1f, idleClipFrameRange.y));
        material.SetFloat(IdleClipFrameRateId, Mathf.Max(1f, idleClipFrameRate));
        material.SetFloat(MoveClipStartFrameId, Mathf.Max(0f, moveClipFrameRange.x));
        material.SetFloat(MoveClipFrameCountId, Mathf.Max(1f, moveClipFrameRange.y));
        material.SetFloat(MoveClipFrameRateId, Mathf.Max(1f, moveClipFrameRate));
        material.SetFloat(AttackClipStartFrameId, Mathf.Max(0f, attackClipFrameRange.x));
        material.SetFloat(AttackClipFrameCountId, Mathf.Max(1f, attackClipFrameRange.y));
        material.SetFloat(AttackClipFrameRateId, Mathf.Max(1f, attackClipFrameRate));
        material.SetFloat(DeathClipStartFrameId, Mathf.Max(0f, deathClipFrameRange.x));
        material.SetFloat(DeathClipFrameCountId, Mathf.Max(1f, deathClipFrameRange.y));
        material.SetFloat(DeathClipFrameRateId, Mathf.Max(1f, deathClipFrameRate));
    }

    public bool TryApplyVatProfile(bool logWarnings = true)
    {
        if (!TryApplyAttackerVatProfile(logWarnings))
            return false;

        return TryApplyDefenderVatProfile(logWarnings);
    }

    private bool TryApplyAttackerVatProfile(bool logWarnings)
    {
        if (vatProfile == null)
            return true;

        if (!vatProfile.IsValid(out string error))
        {
            if (logWarnings)
                Debug.LogError($"[GPUInstancingManager_Stage6] VAT Profile '{vatProfile.name}' is invalid: {error}", this);

            return false;
        }

        instanceMesh = vatProfile.cleanMesh;
        if (vatProfile.HasLowLod)
            midInstanceMesh = vatProfile.lowLodMesh;

        vatFrameCount = Mathf.Max(1, vatProfile.totalFrameCount);
        vatFrameRate = Mathf.Max(1, vatProfile.frameRate);
        idleClipFrameRange = vatProfile.idle.ToRange();
        moveClipFrameRange = vatProfile.move.ToRange();
        attackClipFrameRange = vatProfile.attack.ToRange();
        deathClipFrameRange = vatProfile.death.ToRange();
        idleClipFrameRate = Mathf.Max(1, vatProfile.idle.frameRate);
        moveClipFrameRate = Mathf.Max(1, vatProfile.move.frameRate);
        attackClipFrameRate = Mathf.Max(1, vatProfile.attack.frameRate);
        deathClipFrameRate = Mathf.Max(1, vatProfile.death.frameRate);
        return true;
    }

    private bool TryApplyDefenderVatProfile(bool logWarnings)
    {
        if (defenderVatProfile == null)
            return true;

        if (!defenderVatProfile.IsValid(out string error))
        {
            if (logWarnings)
                Debug.LogError($"[GPUInstancingManager_Stage6] Defender VAT Profile '{defenderVatProfile.name}' is invalid: {error}", this);

            return false;
        }

        defenderInstanceMesh = defenderVatProfile.cleanMesh;
        if (defenderVatProfile.HasLowLod)
            defenderMidInstanceMesh = defenderVatProfile.lowLodMesh;

        return true;
    }

    public bool ApplyVatProfileToAssignedMaterials(bool logWarnings = true)
    {
        if (!TryApplyVatProfile(logWarnings))
            return false;

        if (vatProfile != null && vatProfile.IsValid(out string ignoredError))
        {
            MassGpuDrawUtility_Stage6.SyncVatMaterial(instanceMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
            MassGpuDrawUtility_Stage6.SyncVatMaterial(midInstanceMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
            MassGpuDrawUtility_Stage6.SyncVatMaterial(farInstanceMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
            SyncVatClipWindows(instanceMaterial);
            SyncVatClipWindows(midInstanceMaterial);
            SyncVatClipWindows(farInstanceMaterial);

            SyncVatMaterialLayout(instanceMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
            if (vatProfile.HasLowLod)
            {
                SyncVatMaterialLayout(midInstanceMaterial, vatProfile.lowLodPositionTexture, vatProfile.lowLodNormalTexture, vatProfile.lowLodTextureWidth, vatProfile.lowLodTextureHeight, vatProfile.lowLodRowsPerFrame);
                SyncVatMaterialLayout(farInstanceMaterial, vatProfile.lowLodPositionTexture, vatProfile.lowLodNormalTexture, vatProfile.lowLodTextureWidth, vatProfile.lowLodTextureHeight, vatProfile.lowLodRowsPerFrame);
            }
            else
            {
                SyncVatMaterialLayout(midInstanceMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
                SyncVatMaterialLayout(farInstanceMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
            }
        }

        if (defenderVatProfile != null && defenderVatProfile.IsValid(out string ignoredDefenderError))
        {
            SyncVatMaterialProfileMetadata(defenderInstanceMaterial, defenderVatProfile);
            SyncVatMaterialProfileMetadata(defenderMidInstanceMaterial, defenderVatProfile);
            SyncVatMaterialProfileMetadata(defenderFarInstanceMaterial, defenderVatProfile);
            SyncVatMaterialLayout(defenderInstanceMaterial, defenderVatProfile.positionTexture, defenderVatProfile.normalTexture, defenderVatProfile.textureWidth, defenderVatProfile.textureHeight, defenderVatProfile.rowsPerFrame);
            if (defenderVatProfile.HasLowLod)
            {
                SyncVatMaterialLayout(defenderMidInstanceMaterial, defenderVatProfile.lowLodPositionTexture, defenderVatProfile.lowLodNormalTexture, defenderVatProfile.lowLodTextureWidth, defenderVatProfile.lowLodTextureHeight, defenderVatProfile.lowLodRowsPerFrame);
                SyncVatMaterialLayout(defenderFarInstanceMaterial, defenderVatProfile.lowLodPositionTexture, defenderVatProfile.lowLodNormalTexture, defenderVatProfile.lowLodTextureWidth, defenderVatProfile.lowLodTextureHeight, defenderVatProfile.lowLodRowsPerFrame);
            }
            else
            {
                SyncVatMaterialLayout(defenderMidInstanceMaterial, defenderVatProfile.positionTexture, defenderVatProfile.normalTexture, defenderVatProfile.textureWidth, defenderVatProfile.textureHeight, defenderVatProfile.rowsPerFrame);
                SyncVatMaterialLayout(defenderFarInstanceMaterial, defenderVatProfile.positionTexture, defenderVatProfile.normalTexture, defenderVatProfile.textureWidth, defenderVatProfile.textureHeight, defenderVatProfile.rowsPerFrame);
            }
        }

        return true;
    }

    public string GetVatProfileStatus()
    {
        string attackerStatus = vatProfile == null
            ? "No VAT Profile assigned. Manual VAT fields are used for attacker/default rendering."
            : vatProfile.IsValid(out string error)
            ? $"VAT Profile ready: {vatProfile.name}"
            : $"VAT Profile invalid: {error}";

        if (defenderVatProfile == null)
            return attackerStatus + "\nDefender uses attacker/default rendering.";

        string defenderStatus = defenderVatProfile.IsValid(out string defenderError)
            ? $"Defender VAT Profile ready: {defenderVatProfile.name}"
            : $"Defender VAT Profile invalid: {defenderError}";
        return attackerStatus + "\n" + defenderStatus;
    }

    private static void EnableInstancing(Material material)
    {
        if (material != null)
            material.enableInstancing = true;
    }

    private void SyncRuntimeVatBindings()
    {
        SyncVatMaterialGroup(runtimeAttackerNearMaterial, runtimeAttackerMidMaterial, runtimeAttackerFarMaterial);
        SyncVatMaterialGroup(runtimeDefenderNearMaterial, runtimeDefenderMidMaterial, runtimeDefenderFarMaterial);

        SyncVatClipWindows(runtimeAttackerNearMaterial);
        SyncVatClipWindows(runtimeAttackerMidMaterial);
        SyncVatClipWindows(runtimeAttackerFarMaterial);
        SyncVatClipWindows(runtimeDefenderNearMaterial);
        SyncVatClipWindows(runtimeDefenderMidMaterial);
        SyncVatClipWindows(runtimeDefenderFarMaterial);

        SyncVatProfileToMaterials();
        SyncVatProfileToPropertyBlocks();
        SyncDefenderVatProfileToMaterials();
        SyncDefenderVatProfileToPropertyBlocks();
    }

    private void SyncVatMaterialGroup(Material nearMaterial, Material midMaterial, Material farMaterial)
    {
        MassGpuDrawUtility_Stage6.SyncVatMaterial(nearMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
        MassGpuDrawUtility_Stage6.SyncVatMaterial(midMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
        MassGpuDrawUtility_Stage6.SyncVatMaterial(farMaterial, vatFrameCount, vatFrameRate, VATFrameCountId, VATFrameRateId);
    }

    private void SyncVatProfileToMaterials()
    {
        if (vatProfile == null || !vatProfile.IsValid(out string ignoredError))
            return;

        SyncVatMaterialLayout(runtimeAttackerNearMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
        SyncVatMaterialLayout(runtimeAttackerMidMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
        SyncVatMaterialLayout(runtimeAttackerFarMaterial, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
    }

    private void SyncVatProfileToPropertyBlocks()
    {
        if (vatProfile == null || !vatProfile.IsValid(out string ignoredError))
            return;

        SyncVatPropertyBlock(nearAttackerPropertyBlock, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
        SyncVatPropertyBlockProfileMetadata(nearAttackerPropertyBlock, vatProfile);

        if (vatProfile.HasLowLod)
        {
            SyncVatPropertyBlock(midAttackerPropertyBlock, vatProfile.lowLodPositionTexture, vatProfile.lowLodNormalTexture, vatProfile.lowLodTextureWidth, vatProfile.lowLodTextureHeight, vatProfile.lowLodRowsPerFrame);
            SyncVatPropertyBlock(farAttackerPropertyBlock, vatProfile.lowLodPositionTexture, vatProfile.lowLodNormalTexture, vatProfile.lowLodTextureWidth, vatProfile.lowLodTextureHeight, vatProfile.lowLodRowsPerFrame);
        }
        else
        {
            SyncVatPropertyBlock(midAttackerPropertyBlock, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
            SyncVatPropertyBlock(farAttackerPropertyBlock, vatProfile.positionTexture, vatProfile.normalTexture, vatProfile.textureWidth, vatProfile.textureHeight, vatProfile.rowsPerFrame);
        }

        SyncVatPropertyBlockProfileMetadata(midAttackerPropertyBlock, vatProfile);
        SyncVatPropertyBlockProfileMetadata(farAttackerPropertyBlock, vatProfile);
    }

    private void SyncDefenderVatProfileToMaterials()
    {
        VATProfile_Stage5 profile = defenderVatProfile != null ? defenderVatProfile : vatProfile;
        if (profile == null || !profile.IsValid(out string ignoredError))
            return;

        SyncVatMaterialProfileMetadata(runtimeDefenderNearMaterial, profile);
        SyncVatMaterialProfileMetadata(runtimeDefenderMidMaterial, profile);
        SyncVatMaterialProfileMetadata(runtimeDefenderFarMaterial, profile);
        SyncVatMaterialLayout(runtimeDefenderNearMaterial, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
        SyncVatMaterialLayout(runtimeDefenderMidMaterial, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
        SyncVatMaterialLayout(runtimeDefenderFarMaterial, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
    }

    private void SyncDefenderVatProfileToPropertyBlocks()
    {
        VATProfile_Stage5 profile = defenderVatProfile != null ? defenderVatProfile : vatProfile;
        if (profile == null || !profile.IsValid(out string ignoredError))
            return;

        SyncVatPropertyBlock(nearDefenderPropertyBlock, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
        SyncVatPropertyBlockProfileMetadata(nearDefenderPropertyBlock, profile);

        if (profile.HasLowLod)
        {
            SyncVatPropertyBlock(midDefenderPropertyBlock, profile.lowLodPositionTexture, profile.lowLodNormalTexture, profile.lowLodTextureWidth, profile.lowLodTextureHeight, profile.lowLodRowsPerFrame);
            SyncVatPropertyBlock(farDefenderPropertyBlock, profile.lowLodPositionTexture, profile.lowLodNormalTexture, profile.lowLodTextureWidth, profile.lowLodTextureHeight, profile.lowLodRowsPerFrame);
        }
        else
        {
            SyncVatPropertyBlock(midDefenderPropertyBlock, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
            SyncVatPropertyBlock(farDefenderPropertyBlock, profile.positionTexture, profile.normalTexture, profile.textureWidth, profile.textureHeight, profile.rowsPerFrame);
        }

        SyncVatPropertyBlockProfileMetadata(midDefenderPropertyBlock, profile);
        SyncVatPropertyBlockProfileMetadata(farDefenderPropertyBlock, profile);
    }

    private static void SyncVatPropertyBlockProfileMetadata(MaterialPropertyBlock block, VATProfile_Stage5 profile)
    {
        if (block == null || profile == null)
            return;

        block.SetFloat(VATFrameCountId, Mathf.Max(1, profile.totalFrameCount));
        block.SetFloat(VATFrameRateId, Mathf.Max(1, profile.frameRate));
        SyncVatPropertyBlockClipWindow(block, IdleClipStartFrameId, IdleClipFrameCountId, IdleClipFrameRateId, profile.idle);
        SyncVatPropertyBlockClipWindow(block, MoveClipStartFrameId, MoveClipFrameCountId, MoveClipFrameRateId, profile.move);
        SyncVatPropertyBlockClipWindow(block, AttackClipStartFrameId, AttackClipFrameCountId, AttackClipFrameRateId, profile.attack);
        SyncVatPropertyBlockClipWindow(block, DeathClipStartFrameId, DeathClipFrameCountId, DeathClipFrameRateId, profile.death);
    }

    private static void SyncVatMaterialProfileMetadata(Material material, VATProfile_Stage5 profile)
    {
        if (material == null || profile == null)
            return;

        material.SetFloat(VATFrameCountId, Mathf.Max(1, profile.totalFrameCount));
        material.SetFloat(VATFrameRateId, Mathf.Max(1, profile.frameRate));
        SyncVatMaterialClipWindow(material, IdleClipStartFrameId, IdleClipFrameCountId, IdleClipFrameRateId, profile.idle);
        SyncVatMaterialClipWindow(material, MoveClipStartFrameId, MoveClipFrameCountId, MoveClipFrameRateId, profile.move);
        SyncVatMaterialClipWindow(material, AttackClipStartFrameId, AttackClipFrameCountId, AttackClipFrameRateId, profile.attack);
        SyncVatMaterialClipWindow(material, DeathClipStartFrameId, DeathClipFrameCountId, DeathClipFrameRateId, profile.death);
    }

    private static void SyncVatMaterialClipWindow(
        Material material,
        int startFrameId,
        int frameCountId,
        int frameRateId,
        VATProfile_Stage5.VATClipWindow clip)
    {
        material.SetFloat(startFrameId, Mathf.Max(0, clip.startFrame));
        material.SetFloat(frameCountId, Mathf.Max(1, clip.frameCount));
        material.SetFloat(frameRateId, Mathf.Max(1, clip.frameRate));
    }

    private static void SyncVatPropertyBlockClipWindow(
        MaterialPropertyBlock block,
        int startFrameId,
        int frameCountId,
        int frameRateId,
        VATProfile_Stage5.VATClipWindow clip)
    {
        block.SetFloat(startFrameId, Mathf.Max(0, clip.startFrame));
        block.SetFloat(frameCountId, Mathf.Max(1, clip.frameCount));
        block.SetFloat(frameRateId, Mathf.Max(1, clip.frameRate));
    }

    private static void SyncVatMaterialLayout(Material material, Texture positionTexture, Texture normalTexture, int textureWidth, int textureHeight, int rowsPerFrame)
    {
        if (material == null)
            return;

        material.SetTexture(VATPosTexId, positionTexture);
        material.SetTexture(VATNormTexId, normalTexture);
        material.SetFloat(VATTexWidthId, Mathf.Max(1, textureWidth));
        material.SetFloat(VATTexHeightId, Mathf.Max(1, textureHeight));
        material.SetFloat(VATRowsPerFrameId, Mathf.Max(1, rowsPerFrame));
    }

    private static void SyncVatPropertyBlock(MaterialPropertyBlock block, Texture positionTexture, Texture normalTexture, int textureWidth, int textureHeight, int rowsPerFrame)
    {
        if (block == null)
            return;

        block.SetTexture(VATPosTexId, positionTexture);
        block.SetTexture(VATNormTexId, normalTexture);
        block.SetFloat(VATTexWidthId, Mathf.Max(1, textureWidth));
        block.SetFloat(VATTexHeightId, Mathf.Max(1, textureHeight));
        block.SetFloat(VATRowsPerFrameId, Mathf.Max(1, rowsPerFrame));
    }
}
