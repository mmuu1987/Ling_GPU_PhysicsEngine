using UnityEditor;
using UnityEngine;

public sealed class PaintedFlowFieldPainterWindow_Stage4 : EditorWindow
{
    private const string PainterComputePath = "Assets/MassGPUPhysics_Stage3/Shaders/PaintedFlowFieldPainter_Stage4.compute";

    private PaintedFlowFieldAsset_Stage4 asset;
    private PaintedFlowFieldAsset_Stage4 loadedAsset;
    private GPUInstancingManager_Stage3 manager;
    private ComputeShader painterCompute;
    private RenderTexture dataTexture;
    private RenderTexture previewTexture;
    private Texture2D assetUploadTexture;
    private bool paintEnabled = true;
    private bool eraseMode;
    private float brushRadiusPixels = 36f;
    private float brushSpeed = 1f;
    private float brushStrength = 0.7f;
    private int edgePaddingRadiusCells = 6;
    private float edgePaddingSpeed = 0.45f;
    private float edgePaddingWeight = 0.55f;
    private float presetAngleDegrees;
    private Vector2 presetConvergeTarget = Vector2.zero;
    private float presetSpeed = 1f;
    private float presetWeight = 1f;
    private float presetStopRadius = 1f;
    private Vector2 previousUV;
    private bool hasPreviousUV;
    private Rect canvasRect;

    private int clearKernel;
    private int paintKernel;
    private int visualizeKernel;

    [MenuItem("MassGPUPhysics/Stage4/Painted Flow Field Painter")]
    public static void ShowWindow()
    {
        GetWindow<PaintedFlowFieldPainterWindow_Stage4>("Painted Flow Field");
    }

    private void OnEnable()
    {
        minSize = new Vector2(520f, 520f);
        LoadComputeShader();
        TryAdoptSelection();
    }

    private void OnDisable()
    {
        ReleaseRenderTextures();
        DestroyImmediate(assetUploadTexture);
    }

    private void OnSelectionChange()
    {
        TryAdoptSelection();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawCanvas();
        HandleCanvasInput();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.LabelField("Painted Flow Field Canvas", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Paint on the 2D canvas. Stroke direction becomes flow direction; speed and strength are stored in a high precision field, while color is only the preview.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        manager = (GPUInstancingManager_Stage3)EditorGUILayout.ObjectField("Manager", manager, typeof(GPUInstancingManager_Stage3), true);
        asset = (PaintedFlowFieldAsset_Stage4)EditorGUILayout.ObjectField("Flow Field Asset", asset, typeof(PaintedFlowFieldAsset_Stage4), false);
        if (EditorGUI.EndChangeCheck())
            EnsureCanvasFromAsset(false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Asset"))
                CreateAsset();

            using (new EditorGUI.DisabledScope(manager == null || asset == null))
            {
                if (GUILayout.Button("Fit To Manager"))
                    FitAssetToManager();
                if (GUILayout.Button("Assign To Manager"))
                    AssignToManager();
            }

            using (new EditorGUI.DisabledScope(asset == null))
            {
                if (GUILayout.Button("Load Asset"))
                    LoadAssetToCanvas();
                if (GUILayout.Button("Save Canvas"))
                    SaveCanvasToAsset();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            paintEnabled = EditorGUILayout.Toggle("Paint", paintEnabled, GUILayout.Width(160f));
            eraseMode = EditorGUILayout.Toggle("Erase", eraseMode, GUILayout.Width(160f));
        }

        brushRadiusPixels = EditorGUILayout.Slider("Brush Radius (px)", brushRadiusPixels, 1f, 256f);
        brushSpeed = EditorGUILayout.Slider("Brush Speed", brushSpeed, 0f, 1f);
        brushStrength = EditorGUILayout.Slider("Brush Strength", brushStrength, 0f, 1f);
        edgePaddingRadiusCells = EditorGUILayout.IntSlider("Edge Padding Radius", edgePaddingRadiusCells, 1, 32);
        edgePaddingSpeed = EditorGUILayout.Slider("Edge Padding Speed", edgePaddingSpeed, 0f, 1f);
        edgePaddingWeight = EditorGUILayout.Slider("Edge Padding Weight", edgePaddingWeight, 0f, 1f);
        /*
        EditorGUILayout.HelpBox(
            "Edge Padding 会在你画好的流场外围生成一圈“回流区”：空白格子会指向最近的有效流场格子，用来把跑到边缘的个体拉回路径。\n" +
            "Radius 控制回流区厚度；Speed 控制拉回速度；Weight 控制拉回控制力。个体卡边界时加大 Radius；如果回流抢主方向或打转，就降低 Speed/Weight。",
            MessageType.None);
        */
        EditorGUILayout.HelpBox(
            "Edge Padding fills empty cells around the painted path so agents near the path can be pulled back toward valid flow. Increase Radius for a wider recovery band; lower Speed/Weight if it overpowers the main direction.",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(dataTexture == null))
            {
                if (GUILayout.Button("Clear Canvas"))
                    ClearCanvas();
                if (GUILayout.Button("Visualize"))
                    DispatchVisualize();
                if (GUILayout.Button("Generate Edge Padding"))
                    GenerateEdgePadding();
            }
        }

        DrawPresetControls();

        if (asset != null)
            EditorGUILayout.LabelField("Asset", $"{asset.resolutionX} x {asset.resolutionZ}, cell {asset.cellSize:0.###}, origin {asset.origin:F2}");
    }

    private void DrawPresetControls()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Flow Field Presets", EditorStyles.boldLabel);
        presetSpeed = EditorGUILayout.Slider("Preset Speed", presetSpeed, 0f, 1f);
        presetWeight = EditorGUILayout.Slider("Preset Weight", presetWeight, 0f, 1f);

        using (new EditorGUI.DisabledScope(asset == null))
        {
            presetAngleDegrees = EditorGUILayout.Slider("Uniform Angle", presetAngleDegrees, 0f, 360f);
            if (GUILayout.Button("Generate Uniform Direction"))
                GenerateUniformDirectionPreset();

            presetConvergeTarget = EditorGUILayout.Vector2Field("Converge Target XZ", presetConvergeTarget);
            presetStopRadius = EditorGUILayout.FloatField("Converge Stop Radius", presetStopRadius);
            if (GUILayout.Button("Generate Converge To Point"))
                GenerateConvergeToPointPreset();
        }
    }

    private void DrawCanvas()
    {
        if (asset == null)
        {
            EditorGUILayout.HelpBox("Create or assign a Painted Flow Field Asset first.", MessageType.Warning);
            return;
        }

        if (painterCompute == null)
        {
            EditorGUILayout.HelpBox($"Missing compute shader: {PainterComputePath}", MessageType.Error);
            if (GUILayout.Button("Reload Compute Shader"))
                LoadComputeShader();
            return;
        }

        EnsureCanvasFromAsset(false);
        if (previewTexture == null)
            return;

        float availableWidth = Mathf.Max(200f, position.width - 24f);
        float aspect = asset.resolutionZ > 0 ? (float)asset.resolutionX / asset.resolutionZ : 1f;
        float height = availableWidth / Mathf.Max(0.0001f, aspect);
        float maxHeight = Mathf.Max(200f, position.height - 250f);
        if (height > maxHeight)
        {
            height = maxHeight;
            availableWidth = height * aspect;
        }

        canvasRect = GUILayoutUtility.GetRect(availableWidth, height, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        EditorGUI.DrawRect(new Rect(canvasRect.x - 1f, canvasRect.y - 1f, canvasRect.width + 2f, canvasRect.height + 2f), new Color(0.08f, 0.08f, 0.08f));
        GUI.DrawTexture(canvasRect, previewTexture, ScaleMode.StretchToFill, false);
        DrawBrushOverlay();
    }

    private void HandleCanvasInput()
    {
        if (!paintEnabled || asset == null || dataTexture == null || painterCompute == null)
        {
            hasPreviousUV = false;
            return;
        }

        Event current = Event.current;
        if (current == null)
            return;

        bool inside = canvasRect.Contains(current.mousePosition);
        if (!inside && current.type != EventType.MouseUp)
        {
            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
                hasPreviousUV = false;
            return;
        }

        if ((current.type == EventType.MouseDown || current.type == EventType.MouseDrag) && current.button == 0)
        {
            Vector2 uv = MouseToUV(current.mousePosition);
            if (!hasPreviousUV)
                previousUV = uv;

            PaintSegment(previousUV, uv);
            previousUV = uv;
            hasPreviousUV = true;
            current.Use();
            Repaint();
        }
        else if (current.type == EventType.MouseUp || current.type == EventType.Ignore)
        {
            hasPreviousUV = false;
        }
    }

    private void DrawBrushOverlay()
    {
        Event current = Event.current;
        if (current == null || !canvasRect.Contains(current.mousePosition))
            return;

        Handles.BeginGUI();
        Handles.color = eraseMode ? new Color(1f, 0.2f, 0.1f, 0.95f) : new Color(0.1f, 0.9f, 1f, 0.95f);
        Handles.DrawWireDisc(current.mousePosition, Vector3.forward, brushRadiusPixels * CanvasPixelScale());
        Handles.EndGUI();
        Repaint();
    }

    private float CanvasPixelScale()
    {
        if (asset == null || asset.resolutionX <= 0)
            return 1f;

        return canvasRect.width / asset.resolutionX;
    }

    private Vector2 MouseToUV(Vector2 mousePosition)
    {
        float u = Mathf.InverseLerp(canvasRect.xMin, canvasRect.xMax, mousePosition.x);
        float v = 1f - Mathf.InverseLerp(canvasRect.yMin, canvasRect.yMax, mousePosition.y);
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    private void PaintSegment(Vector2 from, Vector2 to)
    {
        float radiusUV = brushRadiusPixels / Mathf.Max(1f, Mathf.Max(asset.resolutionX, asset.resolutionZ));
        float distancePixels = Vector2.Distance(from, to) * Mathf.Max(asset.resolutionX, asset.resolutionZ);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distancePixels / Mathf.Max(brushRadiusPixels * 0.35f, 1f)));
        Vector2 previous = from;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 current = Vector2.Lerp(from, to, i / (float)steps);
            DispatchPaint(previous, current, radiusUV);
            previous = current;
        }

        DispatchVisualize();
    }

    private void DispatchPaint(Vector2 previous, Vector2 current, float radiusUV)
    {
        painterCompute.SetTexture(paintKernel, "FlowFieldData", dataTexture);
        painterCompute.SetInts("TextureSize", dataTexture.width, dataTexture.height);
        painterCompute.SetVector("PreviousUV", new Vector4(previous.x, previous.y, 0f, 0f));
        painterCompute.SetVector("CurrentUV", new Vector4(current.x, current.y, 0f, 0f));
        painterCompute.SetFloat("BrushRadiusUV", radiusUV);
        painterCompute.SetFloat("BrushSpeed", brushSpeed);
        painterCompute.SetFloat("BrushStrength", brushStrength);
        painterCompute.SetInt("EraseMode", eraseMode ? 1 : 0);
        Dispatch(paintKernel, dataTexture.width, dataTexture.height);
    }

    private void DispatchVisualize()
    {
        if (painterCompute == null || dataTexture == null || previewTexture == null)
            return;

        painterCompute.SetTexture(visualizeKernel, "FlowFieldData", dataTexture);
        painterCompute.SetTexture(visualizeKernel, "FlowFieldPreview", previewTexture);
        painterCompute.SetInts("TextureSize", dataTexture.width, dataTexture.height);
        Dispatch(visualizeKernel, dataTexture.width, dataTexture.height);
    }

    private void ClearCanvas()
    {
        if (painterCompute == null || dataTexture == null)
            return;

        painterCompute.SetTexture(clearKernel, "FlowFieldData", dataTexture);
        painterCompute.SetInts("TextureSize", dataTexture.width, dataTexture.height);
        Dispatch(clearKernel, dataTexture.width, dataTexture.height);
        DispatchVisualize();
        Repaint();
    }

    private void Dispatch(int kernel, int width, int height)
    {
        painterCompute.Dispatch(kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
    }

    private void CreateAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Painted Flow Field",
            "PaintedFlowField_Stage4",
            "asset",
            "Choose where to save the painted flow field asset.");

        if (string.IsNullOrEmpty(path))
            return;

        var created = CreateInstance<PaintedFlowFieldAsset_Stage4>();
        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssets();
        asset = created;

        if (manager != null)
        {
            FitAssetToManager();
            AssignToManager();
        }

        EnsureCanvasFromAsset(true);
    }

    private void FitAssetToManager()
    {
        if (manager == null || asset == null)
            return;

        Vector2 worldSize = manager.simulationWorldSize;
        if (worldSize.x <= 0f || worldSize.y <= 0f)
        {
            worldSize = new Vector2(
                manager.spawnArea.x * 2f + manager.boundaryPadding * 2f,
                manager.spawnArea.z * 2f + manager.boundaryPadding * 2f);
        }

        worldSize.x = Mathf.Max(worldSize.x, manager.flowFieldCellSize);
        worldSize.y = Mathf.Max(worldSize.y, manager.flowFieldCellSize);
        Vector2 origin = worldSize * -0.5f;

        Undo.RecordObject(asset, "Fit Painted Flow Field To Manager");
        asset.ConfigureFromWorld(origin, worldSize, Mathf.Max(0.25f, manager.flowFieldCellSize));
        EditorUtility.SetDirty(asset);
        EnsureCanvasFromAsset(true);
    }

    private void AssignToManager()
    {
        if (manager == null || asset == null)
            return;

        Undo.RecordObject(manager, "Assign Painted Flow Field");
        manager.paintedFlowFieldAsset = asset;
        EditorUtility.SetDirty(manager);
        RefreshManagerFlowField();
    }

    private void SaveCanvasToAsset()
    {
        if (asset == null || dataTexture == null)
            return;

        SaveCanvasToAssetData();
        AssetDatabase.SaveAssets();

        if (manager != null)
            RefreshManagerFlowField();
    }

    private void SaveCanvasToAssetData()
    {
        Texture2D readable = ReadRenderTexture(dataTexture, TextureFormat.RGBAFloat);
        Color[] pixels = readable.GetPixels();
        DestroyImmediate(readable);

        Undo.RecordObject(asset, "Save Painted Flow Field Canvas");
        asset.EnsureCellArray();
        Vector4[] cells = asset.Cells;
        for (int i = 0; i < cells.Length && i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            cells[i] = new Vector4(pixel.r, pixel.g, pixel.b, pixel.a);
        }

        EditorUtility.SetDirty(asset);
    }

    private void GenerateEdgePadding()
    {
        if (asset == null || dataTexture == null)
            return;

        SaveCanvasToAssetData();
        Undo.RecordObject(asset, "Generate Flow Field Edge Padding");
        asset.GenerateEdgePadding(edgePaddingRadiusCells, edgePaddingSpeed, edgePaddingWeight);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        EnsureCanvasFromAsset(true);
        if (manager != null)
            RefreshManagerFlowField();
    }

    private void GenerateUniformDirectionPreset()
    {
        if (asset == null)
            return;

        Undo.RecordObject(asset, "Generate Uniform Flow Field");
        asset.GenerateUniformDirection(presetAngleDegrees, presetSpeed, presetWeight);
        SaveGeneratedPreset();
    }

    private void GenerateConvergeToPointPreset()
    {
        if (asset == null)
            return;

        Undo.RecordObject(asset, "Generate Converging Flow Field");
        asset.GenerateConvergeToPoint(presetConvergeTarget, presetSpeed, presetWeight, Mathf.Max(0f, presetStopRadius));
        SaveGeneratedPreset();
    }

    private void SaveGeneratedPreset()
    {
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        EnsureCanvasFromAsset(true);

        if (manager != null)
            RefreshManagerFlowField();
    }

    private void RefreshManagerFlowField()
    {
        if (manager == null)
            return;

        if (Application.isPlaying)
            manager.RebuildFlowField();
        else
            manager.RebuildFlowFieldPreview();
    }

    private void LoadAssetToCanvas()
    {
        EnsureCanvasFromAsset(true);
    }

    private void EnsureCanvasFromAsset(bool forceReload)
    {
        if (asset == null || painterCompute == null)
            return;

        asset.EnsureCellArray();
        bool needsRecreate = dataTexture == null ||
                             dataTexture.width != asset.resolutionX ||
                             dataTexture.height != asset.resolutionZ;
        if (loadedAsset != asset)
            forceReload = true;

        if (needsRecreate)
        {
            ReleaseRenderTextures();
            dataTexture = CreateRenderTexture(asset.resolutionX, asset.resolutionZ, RenderTextureFormat.ARGBFloat, "Painted Flow Field Data");
            previewTexture = CreateRenderTexture(asset.resolutionX, asset.resolutionZ, RenderTextureFormat.ARGB32, "Painted Flow Field Preview");
            forceReload = true;
        }

        if (forceReload)
        {
            UploadAssetToDataTexture();
            DispatchVisualize();
            loadedAsset = asset;
        }
    }

    private void UploadAssetToDataTexture()
    {
        if (asset == null || dataTexture == null)
            return;

        DestroyImmediate(assetUploadTexture);
        assetUploadTexture = new Texture2D(asset.resolutionX, asset.resolutionZ, TextureFormat.RGBAFloat, false, true);
        Vector4[] cells = asset.Cells;
        var pixels = new Color[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            Vector4 cell = cells[i];
            pixels[i] = new Color(cell.x, cell.y, cell.z, cell.w);
        }

        assetUploadTexture.SetPixels(pixels);
        assetUploadTexture.Apply(false, false);
        Graphics.Blit(assetUploadTexture, dataTexture);
    }

    private Texture2D ReadRenderTexture(RenderTexture source, TextureFormat format)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var texture = new Texture2D(source.width, source.height, format, false, true);
        texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        texture.Apply(false, false);
        RenderTexture.active = previous;
        return texture;
    }

    private RenderTexture CreateRenderTexture(int width, int height, RenderTextureFormat format, string textureName)
    {
        var texture = new RenderTexture(width, height, 0, format)
        {
            name = textureName,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        return texture;
    }

    private void ReleaseRenderTextures()
    {
        if (dataTexture != null)
        {
            dataTexture.Release();
            DestroyImmediate(dataTexture);
            dataTexture = null;
        }

        if (previewTexture != null)
        {
            previewTexture.Release();
            DestroyImmediate(previewTexture);
            previewTexture = null;
        }

        loadedAsset = null;
    }

    private void LoadComputeShader()
    {
        painterCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(PainterComputePath);
        if (painterCompute == null)
            return;

        clearKernel = painterCompute.FindKernel("Clear");
        paintKernel = painterCompute.FindKernel("Paint");
        visualizeKernel = painterCompute.FindKernel("Visualize");
    }

    private void TryAdoptSelection()
    {
        if (Selection.activeGameObject == null)
            return;

        var selectedManager = Selection.activeGameObject.GetComponent<GPUInstancingManager_Stage3>();
        if (selectedManager == null)
            return;

        manager = selectedManager;
        presetConvergeTarget = manager.paintedFlowFieldAsset != null
            ? manager.paintedFlowFieldAsset.origin + manager.paintedFlowFieldAsset.worldSize * 0.5f
            : Vector2.zero;
        if (manager.paintedFlowFieldAsset != null)
        {
            asset = manager.paintedFlowFieldAsset;
            EnsureCanvasFromAsset(true);
        }
    }
}
