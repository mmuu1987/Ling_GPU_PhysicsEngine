using UnityEngine;

using DefenderMovementMode = GPUInstancingManager_Stage6.DefenderMovementMode;
using FlowFieldPreviewSnapshot = GPUInstancingManager_Stage6.FlowFieldPreviewSnapshot;

public sealed partial class MassGpuRuntime_Stage6
{
    private void BuildAndUploadFlowField()
    {
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.flowFieldDirectionsBuffer);
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.defenderFlowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation)
        {
            CreateEmptyAttackerFlowFieldBuffer();
            CreateEmptyDefenderFlowFieldBuffer();
            CacheDisabledFlowFieldPreview();
            return;
        }

        ConfigureRuntimeAttackerFlowFieldGrid();
        UploadRuntimeAttackerFlowField();

        if (defenderMovementMode != DefenderMovementMode.UseDefenderFlowField)
        {
            UploadEmptyDefenderFlowField("Defender movement mode is Hold Position No Separation.");
        }
        else if (enableRuntimeDynamicDefenderFlowField)
        {
            ConfigureRuntimeDefenderFlowFieldGrid();
            UploadRuntimeDefenderFlowField();
        }
        else if (defenderPaintedFlowFieldAsset != null)
        {
            UploadDefenderPaintedFlowField();
        }
        else
        {
            UploadEmptyDefenderFlowField("No defender painted flow field asset assigned; defenders fall back to holding position.");
        }
    }

    public void RebuildFlowFieldPreview()
    {
        RecalculateGridSettings();

        if (!enableFlowFieldNavigation)
        {
            CacheDisabledFlowFieldPreview();
            return;
        }

        activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        if (paintedFlowFieldAsset == null && !autoSizeRuntimeAttackerFlowField)
        {
            CacheMissingPaintedFlowFieldPreview();
            return;
        }

        if (Application.isPlaying && autoSizeRuntimeAttackerFlowField)
        {
            ConfigureRuntimeAttackerFlowFieldGrid();
            CacheRuntimeInitialFlowFieldPreview("Runtime auto-sized flow field preview rebuilt.");
        }
        else
        {
            CachePaintedFlowFieldPreview(Application.isPlaying ? "Runtime painted flow field preview rebuilt." : "Editor painted flow field preview rebuilt.");
        }
    }

    private void UploadPaintedFlowField()
    {
        paintedFlowFieldAsset.EnsureCellArray();
        Vector2[] flowVectors = paintedFlowFieldAsset.BuildFlowVectors();
        flowFieldResolutionX = paintedFlowFieldAsset.resolutionX;
        flowFieldResolutionZ = paintedFlowFieldAsset.resolutionZ;
        flowFieldOrigin = paintedFlowFieldAsset.origin;
        activeFlowFieldCellSize = paintedFlowFieldAsset.cellSize;

        flowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(flowVectors);
        CachePaintedFlowFieldPreview("Runtime painted flow field uploaded.");

        Debug.Log($"[GPUInstancingManager_Stage6] Stage6 painted flow field {flowFieldResolutionX}x{flowFieldResolutionZ}, asset {paintedFlowFieldAsset.name}.");
    }

    private void ConfigureRuntimeAttackerFlowFieldGrid()
    {
        if (!autoSizeRuntimeAttackerFlowField && paintedFlowFieldAsset != null)
        {
            flowFieldResolutionX = paintedFlowFieldAsset.resolutionX;
            flowFieldResolutionZ = paintedFlowFieldAsset.resolutionZ;
            flowFieldOrigin = paintedFlowFieldAsset.origin;
            activeFlowFieldCellSize = paintedFlowFieldAsset.cellSize;
            return;
        }

        float padding = Mathf.Max(0f, runtimeFlowFieldPadding);
        Vector2 worldSize = new Vector2(
            Mathf.Max(0.25f, activeWorldSize.x + padding * 2f),
            Mathf.Max(0.25f, activeWorldSize.y + padding * 2f));
        flowFieldOrigin = gridOrigin - new Vector2(padding, padding);

        float requestedCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        int maxResolution = Mathf.Max(16, runtimeFlowFieldMaxResolution);
        float resolutionCellSize = Mathf.Max(worldSize.x / maxResolution, worldSize.y / maxResolution);
        activeFlowFieldCellSize = Mathf.Max(requestedCellSize, resolutionCellSize);
        flowFieldResolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / activeFlowFieldCellSize));
        flowFieldResolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / activeFlowFieldCellSize));
    }

    private void ConfigureRuntimeDefenderFlowFieldGrid()
    {
        if (!autoSizeRuntimeDefenderFlowField && defenderPaintedFlowFieldAsset != null)
        {
            defenderFlowFieldResolutionX = defenderPaintedFlowFieldAsset.resolutionX;
            defenderFlowFieldResolutionZ = defenderPaintedFlowFieldAsset.resolutionZ;
            defenderFlowFieldOrigin = defenderPaintedFlowFieldAsset.origin;
            activeDefenderFlowFieldCellSize = defenderPaintedFlowFieldAsset.cellSize;
            return;
        }

        float padding = Mathf.Max(0f, runtimeDefenderFlowFieldPadding);
        Vector2 worldSize = new Vector2(
            Mathf.Max(0.25f, activeWorldSize.x + padding * 2f),
            Mathf.Max(0.25f, activeWorldSize.y + padding * 2f));
        defenderFlowFieldOrigin = gridOrigin - new Vector2(padding, padding);

        float requestedCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        int maxResolution = Mathf.Max(16, runtimeDefenderFlowFieldMaxResolution);
        float resolutionCellSize = Mathf.Max(worldSize.x / maxResolution, worldSize.y / maxResolution);
        activeDefenderFlowFieldCellSize = Mathf.Max(requestedCellSize, resolutionCellSize);
        defenderFlowFieldResolutionX = Mathf.Max(1, Mathf.CeilToInt(worldSize.x / activeDefenderFlowFieldCellSize));
        defenderFlowFieldResolutionZ = Mathf.Max(1, Mathf.CeilToInt(worldSize.y / activeDefenderFlowFieldCellSize));
    }

    private Bounds CalculateCombatSpawnBounds()
    {
        Bounds bounds = CreateXZBounds(attackerSettings.spawnCenter, attackerSettings.spawnSize);
        bounds.Encapsulate(CreateXZBounds(defenderSettings.spawnCenter, defenderSettings.spawnSize));

        if (!enableTwoTeamCombat)
            bounds.Encapsulate(CreateXZBounds(Vector3.zero, spawnArea));

        return bounds;
    }

    private static Bounds CreateXZBounds(Vector3 center, Vector3 size)
    {
        Vector3 safeSize = new Vector3(Mathf.Max(0.01f, size.x), 1f, Mathf.Max(0.01f, size.z));
        return new Bounds(new Vector3(center.x, 0f, center.z), safeSize);
    }

    private void UploadRuntimeAttackerFlowField()
    {
        Vector2[] flowVectors = BuildRuntimeInitialAttackerFlowVectors();
        flowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(flowVectors);
        CacheRuntimeInitialFlowFieldPreview("Runtime attacker flow grid uploaded.", flowVectors);

        Debug.Log($"[GPUInstancingManager_Stage6] Stage6 runtime attacker flow field {flowFieldResolutionX}x{flowFieldResolutionZ}, cell {activeFlowFieldCellSize:0.###}, origin {flowFieldOrigin}.");
    }

    private void UploadRuntimeDefenderFlowField()
    {
        Vector2[] flowVectors = BuildRuntimeInitialDefenderFlowVectors();
        defenderFlowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        defenderFlowFieldDirectionsBuffer.SetData(flowVectors);

        Debug.Log($"[GPUInstancingManager_Stage6] Stage6 runtime defender flow field {defenderFlowFieldResolutionX}x{defenderFlowFieldResolutionZ}, cell {activeDefenderFlowFieldCellSize:0.###}, origin {defenderFlowFieldOrigin}.");
    }

    private Vector2[] BuildRuntimeInitialAttackerFlowVectors()
    {
        int count = Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ);
        Vector2[] vectors = new Vector2[count];
        Vector2 fallbackTarget = new Vector2(defenderSettings.spawnCenter.x, defenderSettings.spawnCenter.z);
        Vector2[] paintedVectors = null;

        if (paintedFlowFieldAsset != null)
        {
            paintedFlowFieldAsset.EnsureCellArray();
            paintedVectors = paintedFlowFieldAsset.BuildFlowVectors();
        }

        for (int z = 0; z < flowFieldResolutionZ; z++)
        {
            for (int x = 0; x < flowFieldResolutionX; x++)
            {
                int index = z * flowFieldResolutionX + x;
                Vector2 center = RuntimeFlowCellCenter(x, z);
                Vector2 direction = paintedVectors != null
                    ? SamplePaintedFlowDirection(center, paintedVectors)
                    : Vector2.zero;

                if (direction.sqrMagnitude <= 0.0001f)
                {
                    Vector2 toTarget = fallbackTarget - center;
                    direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;
                }

                vectors[index] = direction;
            }
        }

        return vectors;
    }

    private Vector2[] BuildRuntimeInitialDefenderFlowVectors()
    {
        int count = Mathf.Max(1, defenderFlowFieldResolutionX * defenderFlowFieldResolutionZ);
        Vector2[] vectors = new Vector2[count];
        Vector2 fallbackTarget = new Vector2(attackerSettings.spawnCenter.x, attackerSettings.spawnCenter.z);
        Vector2[] paintedVectors = null;

        if (defenderPaintedFlowFieldAsset != null)
        {
            defenderPaintedFlowFieldAsset.EnsureCellArray();
            paintedVectors = defenderPaintedFlowFieldAsset.BuildFlowVectors();
        }

        for (int z = 0; z < defenderFlowFieldResolutionZ; z++)
        {
            for (int x = 0; x < defenderFlowFieldResolutionX; x++)
            {
                int index = z * defenderFlowFieldResolutionX + x;
                Vector2 center = RuntimeDefenderFlowCellCenter(x, z);
                Vector2 direction = paintedVectors != null
                    ? SampleDefenderPaintedFlowDirection(center, paintedVectors)
                    : Vector2.zero;

                if (direction.sqrMagnitude <= 0.0001f)
                {
                    Vector2 toTarget = fallbackTarget - center;
                    direction = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.zero;
                }

                vectors[index] = direction;
            }
        }

        return vectors;
    }

    private Vector2 RuntimeFlowCellCenter(int x, int z)
    {
        return flowFieldOrigin + new Vector2((x + 0.5f) * activeFlowFieldCellSize, (z + 0.5f) * activeFlowFieldCellSize);
    }

    private Vector2 RuntimeDefenderFlowCellCenter(int x, int z)
    {
        return defenderFlowFieldOrigin + new Vector2((x + 0.5f) * activeDefenderFlowFieldCellSize, (z + 0.5f) * activeDefenderFlowFieldCellSize);
    }

    private Vector2 SamplePaintedFlowDirection(Vector2 world, Vector2[] paintedVectors)
    {
        if (paintedFlowFieldAsset == null || paintedVectors == null || paintedVectors.Length == 0)
            return Vector2.zero;

        Vector2 local = world - paintedFlowFieldAsset.origin;
        float cellSize = Mathf.Max(0.0001f, paintedFlowFieldAsset.cellSize);
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.y / cellSize);
        if (x < 0 || z < 0 || x >= paintedFlowFieldAsset.resolutionX || z >= paintedFlowFieldAsset.resolutionZ)
            return Vector2.zero;

        return paintedVectors[z * paintedFlowFieldAsset.resolutionX + x];
    }

    private Vector2 SampleDefenderPaintedFlowDirection(Vector2 world, Vector2[] paintedVectors)
    {
        if (defenderPaintedFlowFieldAsset == null || paintedVectors == null || paintedVectors.Length == 0)
            return Vector2.zero;

        Vector2 local = world - defenderPaintedFlowFieldAsset.origin;
        float cellSize = Mathf.Max(0.0001f, defenderPaintedFlowFieldAsset.cellSize);
        int x = Mathf.FloorToInt(local.x / cellSize);
        int z = Mathf.FloorToInt(local.y / cellSize);
        if (x < 0 || z < 0 || x >= defenderPaintedFlowFieldAsset.resolutionX || z >= defenderPaintedFlowFieldAsset.resolutionZ)
            return Vector2.zero;

        return paintedVectors[z * defenderPaintedFlowFieldAsset.resolutionX + x];
    }

    private void UploadDefenderPaintedFlowField()
    {
        defenderPaintedFlowFieldAsset.EnsureCellArray();
        Vector2[] flowVectors = defenderPaintedFlowFieldAsset.BuildFlowVectors();
        defenderFlowFieldResolutionX = defenderPaintedFlowFieldAsset.resolutionX;
        defenderFlowFieldResolutionZ = defenderPaintedFlowFieldAsset.resolutionZ;
        defenderFlowFieldOrigin = defenderPaintedFlowFieldAsset.origin;
        activeDefenderFlowFieldCellSize = defenderPaintedFlowFieldAsset.cellSize;

        defenderFlowFieldDirectionsBuffer = new ComputeBuffer(flowVectors.Length, sizeof(float) * 2);
        defenderFlowFieldDirectionsBuffer.SetData(flowVectors);

        Debug.Log($"[GPUInstancingManager_Stage6] Stage6 defender painted flow field {defenderFlowFieldResolutionX}x{defenderFlowFieldResolutionZ}, asset {defenderPaintedFlowFieldAsset.name}.");
    }

    private void UploadEmptyFlowField(string status)
    {
        CreateEmptyAttackerFlowFieldBuffer();
        CacheMissingPaintedFlowFieldPreview(status);
        Debug.LogWarning($"[GPUInstancingManager_Stage6] Stage6 painted flow field disabled: {status}");
    }

    private void UploadEmptyDefenderFlowField(string status)
    {
        CreateEmptyDefenderFlowFieldBuffer();
        if (defenderMovementMode == DefenderMovementMode.UseDefenderFlowField)
            Debug.LogWarning($"[GPUInstancingManager_Stage6] Stage6 defender painted flow field disabled: {status}");
    }

    private void CreateEmptyAttackerFlowFieldBuffer()
    {
        flowFieldResolutionX = 1;
        flowFieldResolutionZ = 1;
        flowFieldOrigin = gridOrigin;
        activeFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        flowFieldDirectionsBuffer = new ComputeBuffer(1, sizeof(float) * 2);
        flowFieldDirectionsBuffer.SetData(new[] { Vector2.zero });
    }

    private void CreateEmptyDefenderFlowFieldBuffer()
    {
        defenderFlowFieldResolutionX = 1;
        defenderFlowFieldResolutionZ = 1;
        defenderFlowFieldOrigin = gridOrigin;
        activeDefenderFlowFieldCellSize = Mathf.Max(0.25f, flowFieldCellSize);
        defenderFlowFieldDirectionsBuffer = new ComputeBuffer(1, sizeof(float) * 2);
        defenderFlowFieldDirectionsBuffer.SetData(new[] { Vector2.zero });
    }

    private void CreateRuntimeDynamicFlowResources()
    {
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.runtimeAttackerTargetDensityBuffer);
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.runtimeAttackerFlowStatsBuffer);
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.runtimeAttackerFlowTargetsBuffer);
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.runtimeDefenderTargetDensityBuffer);
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.runtimeDefenderFlowStatsBuffer);
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.runtimeDefenderFlowTargetsBuffer);
        ReleaseRuntimeFlowPreviewTextures();

        int attackerCellCount = Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ);
        runtimeAttackerTargetDensityBuffer = new ComputeBuffer(attackerCellCount, sizeof(uint));
        runtimeAttackerFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
        runtimeAttackerFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);

        runtimeAttackerFlowPreviewTexture = new RenderTexture(flowFieldResolutionX, flowFieldResolutionZ, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "RuntimeDynamicAttackerFlowPreview_Stage6"
        };
        runtimeAttackerFlowPreviewTexture.Create();

        int defenderCellCount = Mathf.Max(1, defenderFlowFieldResolutionX * defenderFlowFieldResolutionZ);
        runtimeDefenderTargetDensityBuffer = new ComputeBuffer(defenderCellCount, sizeof(uint));
        runtimeDefenderFlowStatsBuffer = new ComputeBuffer(4, sizeof(int));
        runtimeDefenderFlowTargetsBuffer = new ComputeBuffer(8, sizeof(float) * 4);

        runtimeDefenderFlowPreviewTexture = new RenderTexture(defenderFlowFieldResolutionX, defenderFlowFieldResolutionZ, 0, RenderTextureFormat.ARGBFloat)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "RuntimeDynamicDefenderFlowPreview_Stage6"
        };
        runtimeDefenderFlowPreviewTexture.Create();

        FlowFieldPreview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void ReleaseRuntimeFlowPreviewTextures()
    {
        ReleaseRuntimeFlowPreviewTexture(ref buffers.runtimeAttackerFlowPreviewTexture);
        ReleaseRuntimeFlowPreviewTexture(ref buffers.runtimeDefenderFlowPreviewTexture);
    }

    private void ReleaseRuntimeFlowPreviewTexture(ref RenderTexture texture)
    {
        if (texture == null)
            return;

        texture.Release();
        if (Application.isPlaying)
            Object.Destroy(texture);
        else
            Object.DestroyImmediate(texture);
        texture = null;
    }

    private void CachePaintedFlowFieldPreview(string status)
    {
        if (paintedFlowFieldAsset == null)
            return;

        paintedFlowFieldAsset.EnsureCellArray();
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = true;
        preview.resolutionX = paintedFlowFieldAsset.resolutionX;
        preview.resolutionZ = paintedFlowFieldAsset.resolutionZ;
        preview.origin = paintedFlowFieldAsset.origin;
        preview.worldSize = paintedFlowFieldAsset.worldSize;
        preview.cellSize = paintedFlowFieldAsset.cellSize;
        preview.target = GetPaintedFlowFieldCenter();
        preview.blockedCellCount = 0;
        preview.directions = paintedFlowFieldAsset.BuildFlowVectors();
        preview.costs = paintedFlowFieldAsset.BuildPreviewCosts();
        preview.status = status;
        preview.source = "Painted";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void CacheRuntimeInitialFlowFieldPreview(string status, Vector2[] directions = null)
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = true;
        preview.resolutionX = flowFieldResolutionX;
        preview.resolutionZ = flowFieldResolutionZ;
        preview.origin = flowFieldOrigin;
        preview.worldSize = new Vector2(flowFieldResolutionX * activeFlowFieldCellSize, flowFieldResolutionZ * activeFlowFieldCellSize);
        preview.cellSize = activeFlowFieldCellSize;
        preview.target = new Vector2(defenderSettings.spawnCenter.x, defenderSettings.spawnCenter.z);
        preview.blockedCellCount = 0;
        preview.directions = directions ?? BuildRuntimeInitialAttackerFlowVectors();
        preview.costs = BuildRuntimeFlowPreviewCosts();
        preview.status = status;
        preview.source = autoSizeRuntimeAttackerFlowField ? "Runtime Auto Sized" : "Painted";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private float[] BuildRuntimeFlowPreviewCosts()
    {
        int count = Mathf.Max(1, flowFieldResolutionX * flowFieldResolutionZ);
        float[] costs = new float[count];
        for (int i = 0; i < costs.Length; i++)
            costs[i] = 1f;

        return costs;
    }

    private void CacheMissingPaintedFlowFieldPreview(string status = "No painted flow field asset assigned.")
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = false;
        preview.resolutionX = 1;
        preview.resolutionZ = 1;
        preview.origin = gridOrigin;
        preview.worldSize = activeWorldSize;
        preview.cellSize = Mathf.Max(0.25f, flowFieldCellSize);
        preview.target = Vector2.zero;
        preview.blockedCellCount = 0;
        preview.directions = new[] { Vector2.zero };
        preview.costs = new[] { 0f };
        preview.status = status;
        preview.source = "Fallback";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void CacheDisabledFlowFieldPreview()
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = false;
        preview.resolutionX = 1;
        preview.resolutionZ = 1;
        preview.origin = gridOrigin;
        preview.worldSize = activeWorldSize;
        preview.cellSize = Mathf.Max(0.25f, flowFieldCellSize);
        preview.target = Vector2.zero;
        preview.blockedCellCount = 0;
        preview.directions = new[] { Vector2.zero };
        preview.costs = new[] { 0f };
        preview.status = "Flow field navigation is disabled.";
        preview.source = "Fallback";
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private Vector2 GetPaintedFlowFieldCenter()
    {
        if (paintedFlowFieldAsset == null)
            return Vector2.zero;

        return paintedFlowFieldAsset.origin + paintedFlowFieldAsset.worldSize * 0.5f;
    }

    public void RebuildFlowField()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("[GPUInstancingManager_Stage6] Flow field is uploaded when Play Mode starts.");
            return;
        }

        if (agentBuffer == null || !kernels.IsValid)
            return;

        runtimeDynamicAttackerFlowActive = false;
        runtimeDynamicDefenderFlowActive = false;
        BuildAndUploadFlowField();
        CreateRuntimeDynamicFlowResources();
        nextDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        nextDefenderDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.ClearRuntimeAttackerFlowResources);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.BuildRuntimeAttackerTargetDensity);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.SelectRuntimeAttackerFlowTargets);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.GenerateRuntimeAttackerFlowField);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.ClearRuntimeDefenderFlowResources);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.BuildRuntimeDefenderTargetDensity);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.SelectRuntimeDefenderFlowTargets);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.GenerateRuntimeDefenderFlowField);
        BindComputeBuffers(kernels.CombatSimulationShader, kernels.SimulateCombatAndAccumulateDamage);
    }

    private bool ShouldUseRuntimeDynamicAttackerFlowField()
    {
        return Application.isPlaying &&
               enableRuntimeDynamicAttackerFlowField &&
               enableFlowFieldNavigation &&
               enableTwoTeamCombat &&
               battleStarted &&
               runtimeAttackerTargetDensityBuffer != null &&
               runtimeAttackerFlowStatsBuffer != null &&
               runtimeAttackerFlowTargetsBuffer != null &&
               runtimeAttackerFlowPreviewTexture != null;
    }

    private bool ShouldUseRuntimeDynamicDefenderFlowField()
    {
        return Application.isPlaying &&
               enableRuntimeDynamicDefenderFlowField &&
               enableFlowFieldNavigation &&
               enableTwoTeamCombat &&
               battleStarted &&
               defenderMovementMode == DefenderMovementMode.UseDefenderFlowField &&
               defenderFlowFieldDirectionsBuffer != null &&
               runtimeDefenderTargetDensityBuffer != null &&
               runtimeDefenderFlowStatsBuffer != null &&
               runtimeDefenderFlowTargetsBuffer != null &&
               runtimeDefenderFlowPreviewTexture != null;
    }

    private bool ConsumeRuntimeDynamicAttackerFlowRebuildRequest()
    {
        FlowFieldPreview.isWaitingForRuntimeReadback = false;

        if (!ShouldUseRuntimeDynamicAttackerFlowField())
        {
            if (runtimeDynamicAttackerFlowActive)
            {
                runtimeDynamicAttackerFlowActive = false;
                RestorePaintedAttackerFlowField("Runtime dynamic attacker flow disabled; restored painted fallback.");
            }
            return false;
        }

        if (Time.time < nextDynamicFlowUpdateTime)
            return false;

        runtimeDynamicAttackerFlowActive = true;
        lastRuntimeDynamicFlowUpdateTime = Time.time;
        nextDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicFlowUpdateInterval);
        CacheRuntimeGpuFlowPreview(
            "Runtime GPU Sector Flow",
            $"Runtime GPU attacker flow rebuilt every {dynamicFlowUpdateInterval:0.###}s. Preview is rendered on GPU.");
        return true;
    }

    private bool ConsumeRuntimeDynamicDefenderFlowRebuildRequest()
    {
        if (!ShouldUseRuntimeDynamicDefenderFlowField())
        {
            if (runtimeDynamicDefenderFlowActive)
            {
                runtimeDynamicDefenderFlowActive = false;
                RestorePaintedDefenderFlowField("Runtime dynamic defender flow disabled; restored painted fallback.");
            }
            return false;
        }

        if (Time.time < nextDefenderDynamicFlowUpdateTime)
            return false;

        runtimeDynamicDefenderFlowActive = true;
        lastRuntimeDynamicDefenderFlowUpdateTime = Time.time;
        nextDefenderDynamicFlowUpdateTime = Time.time + Mathf.Max(0.1f, dynamicDefenderFlowUpdateInterval);
        return true;
    }

    private void CacheRuntimeGpuFlowPreview(string source, string status)
    {
        FlowFieldPreviewSnapshot preview = FlowFieldPreview;
        preview.isValid = true;
        preview.isEnabled = enableFlowFieldNavigation && flowFieldDirectionsBuffer != null;
        preview.resolutionX = flowFieldResolutionX;
        preview.resolutionZ = flowFieldResolutionZ;
        preview.origin = flowFieldOrigin;
        preview.worldSize = new Vector2(flowFieldResolutionX * activeFlowFieldCellSize, flowFieldResolutionZ * activeFlowFieldCellSize);
        preview.cellSize = activeFlowFieldCellSize;
        preview.target = Vector2.zero;
        preview.blockedCellCount = 0;
        preview.status = status;
        preview.source = source;
        preview.dynamicTargetCount = 0;
        preview.dynamicTargets = new Vector2[0];
        preview.aliveDefenderCount = 0;
        preview.lastRuntimeUpdateTime = lastRuntimeDynamicFlowUpdateTime;
        preview.isWaitingForRuntimeReadback = false;
        preview.runtimePreviewTexture = runtimeAttackerFlowPreviewTexture;
    }

    private void RestorePaintedAttackerFlowField(string status)
    {
        if (!Application.isPlaying || !kernels.IsValid || agentBuffer == null)
            return;

        runtimeDynamicAttackerFlowActive = false;
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.flowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation)
        {
            CreateEmptyAttackerFlowFieldBuffer();
            CacheDisabledFlowFieldPreview();
        }
        else if (paintedFlowFieldAsset != null)
        {
            ConfigureRuntimeAttackerFlowFieldGrid();
            UploadRuntimeAttackerFlowField();
            FlowFieldPreview.status = status;
        }
        else
        {
            UploadEmptyFlowField(status);
            FlowFieldPreview.source = "Fallback";
        }

        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.SelectRuntimeAttackerFlowTargets);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.GenerateRuntimeAttackerFlowField);
        BindComputeBuffers(kernels.CombatSimulationShader, kernels.SimulateCombatAndAccumulateDamage);
    }

    private void RestorePaintedDefenderFlowField(string status)
    {
        if (!Application.isPlaying || !kernels.IsValid || agentBuffer == null)
            return;

        runtimeDynamicDefenderFlowActive = false;
        MassGpuBufferSet_Stage6.ReleaseBuffer(ref buffers.defenderFlowFieldDirectionsBuffer);

        if (!enableFlowFieldNavigation || defenderMovementMode != DefenderMovementMode.UseDefenderFlowField)
        {
            CreateEmptyDefenderFlowFieldBuffer();
        }
        else if (enableRuntimeDynamicDefenderFlowField)
        {
            ConfigureRuntimeDefenderFlowFieldGrid();
            UploadRuntimeDefenderFlowField();
        }
        else if (defenderPaintedFlowFieldAsset != null)
        {
            UploadDefenderPaintedFlowField();
        }
        else
        {
            UploadEmptyDefenderFlowField(status);
        }

        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.SelectRuntimeDefenderFlowTargets);
        BindComputeBuffers(kernels.RuntimeFlowShader, kernels.GenerateRuntimeDefenderFlowField);
        BindComputeBuffers(kernels.CombatSimulationShader, kernels.SimulateCombatAndAccumulateDamage);
    }
}
