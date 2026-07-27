using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GPUInstancingManager_Stage3))]
public sealed class GPUInstancingManagerStage3Editor : Editor
{
    private const float PreviewHeight = 360f;
    private const float LegendSwatchSize = 12f;
    private const float BlockedPreviewCost = 255f;
    private Texture2D cachedPreviewTexture;
    private int cachedResolutionX;
    private int cachedResolutionZ;
    private int cachedDirectionsHash;
    private int cachedCostsHash;
    private bool showTuningGuide = true;

    private void OnDisable()
    {
        if (cachedPreviewTexture == null)
            return;

        DestroyImmediate(cachedPreviewTexture);
        cachedPreviewTexture = null;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var manager = (GPUInstancingManager_Stage3)target;
        DrawTuningGuide();
        if (!manager.showFlowFieldPreview)
            return;

        EditorGUILayout.Space(10f);
        DrawFlowFieldPreview(manager);
    }

    private void DrawTuningGuide()
    {
        EditorGUILayout.Space(8f);
        showTuningGuide = EditorGUILayout.Foldout(showTuningGuide, "人群/手绘流场调参指南", true);
        if (!showTuningGuide)
            return;

        /*
        EditorGUILayout.HelpBox(
            "Agent Radius：个体占地半径。调大后更早互相避让，拥挤更容易散开；太大会显得队伍膨胀、通道变窄。拥挤分不开时可试 0.55~0.7。\n\n" +
            "Separation Strength：分离/排挤强度。解决“挤成一团”的主参数；越大推开越快，过高会抖动或弹飞。建议从 30、45、60 逐步试。\n\n" +
            "Velocity Damping：速度阻尼，类似摩擦力。越大越稳，但也会吃掉分离推力；太大时个体可能原地扭动、挤不开。拥挤时可试 2~3，抖动时再略微加大。\n\n" +
            "Flow Field Weight：流场控制权重。越大越贴你画的线，但也越容易把人群压到同一条线上；拥挤或打转时可降到 0.5~0.75。\n\n" +
            "Flow Field Responsiveness：跟随流场的反应速度。越大越快转向；过高时在边界、急弯、方向冲突处容易原地打转。打转时优先降到 2~3。\n\n" +
            "Edge Padding：在 Painter 里生成的边界回流。它只负责把跑出路径边缘的个体温柔拉回有效流场；太强会抢主流方向，建议 Speed/Weight 先用 0.25~0.55。",
            MessageType.Info);
        */
        EditorGUILayout.HelpBox(
            "Tuning guide: larger Agent Radius and Separation Strength spread crowds sooner, while Velocity Damping stabilizes motion. If a painted flow feels too strict or agents spin near sharp turns, lower Flow Field Weight or Responsiveness. Edge Padding should gently pull agents back to valid flow without overpowering the main direction.",
            MessageType.Info);
    }

    private void DrawFlowFieldPreview(GPUInstancingManager_Stage3 manager)
    {
        EditorGUILayout.LabelField("Flow Field Preview", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(Application.isPlaying ? "Rebuild Flow Field" : "Preview/Rebuild Flow Field"))
            {
                if (Application.isPlaying)
                    manager.RebuildFlowField();
                else
                    manager.RebuildFlowFieldPreview();

                Repaint();
            }

            if (GUILayout.Button("Open Painter"))
                PaintedFlowFieldPainterWindow_Stage4.ShowWindow();
        }

        GPUInstancingManager_Stage3.FlowFieldPreviewSnapshot preview = manager.FlowFieldPreview;
        if (preview == null || !preview.isValid)
        {
            EditorGUILayout.HelpBox("No flow field preview yet. Click Preview/Rebuild Flow Field.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Status", preview.status);
        EditorGUILayout.LabelField("Resolution", $"{preview.resolutionX} x {preview.resolutionZ}");
        EditorGUILayout.LabelField("Cell Size", preview.cellSize.ToString("0.###"));
        EditorGUILayout.LabelField("Origin", preview.origin.ToString("F2"));
        EditorGUILayout.LabelField("Target", preview.target.ToString("F2"));
        EditorGUILayout.LabelField("Blocked Cells", preview.blockedCellCount.ToString());

        if (!preview.isEnabled)
        {
            EditorGUILayout.HelpBox(preview.status, MessageType.Warning);
            return;
        }

        if (!HasValidPreviewArrays(preview))
        {
            EditorGUILayout.HelpBox("Preview data is incomplete. Rebuild the flow field preview.", MessageType.Warning);
            return;
        }

        DrawLegend();

        Rect rect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
        DrawPreviewMap(rect, preview);
    }

    private static bool HasValidPreviewArrays(GPUInstancingManager_Stage3.FlowFieldPreviewSnapshot preview)
    {
        int expectedCount = Mathf.Max(1, preview.resolutionX * preview.resolutionZ);
        return preview.directions != null &&
               preview.costs != null &&
               preview.directions.Length >= expectedCount &&
               preview.costs.Length >= expectedCount;
    }

    private static void DrawLegend()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawLegendItem(new Color(0.85f, 0.18f, 0.12f, 1f), "Blocked");
            DrawLegendItem(new Color(0.16f, 0.16f, 0.16f, 1f), "No Direction");
            DrawLegendItem(new Color(0.43f, 0.43f, 0.43f, 1f), "Walkable");
            DrawLegendItem(new Color(0.15f, 0.78f, 1f, 1f), "Direction");
        }
    }

    private static void DrawLegendItem(Color color, string label)
    {
        Rect swatch = GUILayoutUtility.GetRect(LegendSwatchSize, LegendSwatchSize, GUILayout.Width(LegendSwatchSize), GUILayout.Height(LegendSwatchSize));
        EditorGUI.DrawRect(swatch, color);
        GUILayout.Label(label, GUILayout.Width(84f));
    }

    private void DrawPreviewMap(Rect rect, GPUInstancingManager_Stage3.FlowFieldPreviewSnapshot preview)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f, 1f));

        float aspect = preview.resolutionZ > 0 ? (float)preview.resolutionX / preview.resolutionZ : 1f;
        Rect mapRect = FitAspect(rect, aspect);
        EditorGUI.DrawRect(mapRect, new Color(0.12f, 0.12f, 0.12f, 1f));
        Texture2D texture = GetOrBuildPreviewTexture(preview);
        if (texture != null)
            GUI.DrawTexture(mapRect, texture, ScaleMode.StretchToFill, false);

        GUI.Box(mapRect, GUIContent.none);
    }

    private Texture2D GetOrBuildPreviewTexture(GPUInstancingManager_Stage3.FlowFieldPreviewSnapshot preview)
    {
        int directionsHash = GetArrayHash(preview.directions);
        int costsHash = GetArrayHash(preview.costs);
        bool dirty = cachedPreviewTexture == null ||
                     cachedResolutionX != preview.resolutionX ||
                     cachedResolutionZ != preview.resolutionZ ||
                     cachedDirectionsHash != directionsHash ||
                     cachedCostsHash != costsHash;

        if (!dirty)
            return cachedPreviewTexture;

        if (cachedPreviewTexture == null ||
            cachedPreviewTexture.width != preview.resolutionX ||
            cachedPreviewTexture.height != preview.resolutionZ)
        {
            if (cachedPreviewTexture != null)
                DestroyImmediate(cachedPreviewTexture);

            cachedPreviewTexture = new Texture2D(preview.resolutionX, preview.resolutionZ, TextureFormat.RGBA32, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        var pixels = new Color32[preview.resolutionX * preview.resolutionZ];
        for (int z = 0; z < preview.resolutionZ; z++)
        {
            for (int x = 0; x < preview.resolutionX; x++)
            {
                int index = z * preview.resolutionX + x;
                Vector2 direction = preview.directions[index];
                bool blocked = preview.costs[index] >= BlockedPreviewCost;
                bool hasDirection = direction.sqrMagnitude > 0.0001f;

                Color color = blocked
                    ? new Color(0.72f, 0.09f, 0.06f, 1f)
                    : hasDirection
                        ? DirectionToColor(direction)
                        : new Color(0.03f, 0.03f, 0.03f, 1f);
                pixels[index] = color;
            }
        }

        cachedPreviewTexture.SetPixels32(pixels);
        cachedPreviewTexture.Apply(false, false);
        cachedResolutionX = preview.resolutionX;
        cachedResolutionZ = preview.resolutionZ;
        cachedDirectionsHash = directionsHash;
        cachedCostsHash = costsHash;
        return cachedPreviewTexture;
    }

    private static int GetArrayHash(Vector2[] values)
    {
        unchecked
        {
            int hash = values != null ? values.Length : 0;
            if (values == null)
                return hash;

            int step = Mathf.Max(1, values.Length / 256);
            for (int i = 0; i < values.Length; i += step)
            {
                hash = hash * 31 + values[i].x.GetHashCode();
                hash = hash * 31 + values[i].y.GetHashCode();
            }

            return hash;
        }
    }

    private static int GetArrayHash(float[] values)
    {
        unchecked
        {
            int hash = values != null ? values.Length : 0;
            if (values == null)
                return hash;

            int step = Mathf.Max(1, values.Length / 256);
            for (int i = 0; i < values.Length; i += step)
                hash = hash * 31 + values[i].GetHashCode();

            return hash;
        }
    }

    private static Rect FitAspect(Rect rect, float aspect)
    {
        aspect = Mathf.Max(0.0001f, aspect);
        float width = rect.width;
        float height = width / aspect;

        if (height > rect.height)
        {
            height = rect.height;
            width = height * aspect;
        }

        return new Rect(
            rect.x + (rect.width - width) * 0.5f,
            rect.y + (rect.height - height) * 0.5f,
            width,
            height);
    }

    private static Color DirectionToColor(Vector2 direction)
    {
        float magnitude = Mathf.Clamp01(direction.magnitude);
        Vector2 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float hue = Mathf.Atan2(safeDirection.y, safeDirection.x) / (Mathf.PI * 2f);
        if (hue < 0f)
            hue += 1f;

        return Color.HSVToRGB(hue, Mathf.Lerp(0.35f, 0.9f, magnitude), Mathf.Lerp(0.28f, 0.88f, magnitude));
    }

}
