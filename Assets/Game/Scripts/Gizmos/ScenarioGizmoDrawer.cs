using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MassEngine.Game
{
    internal static class ScenarioGizmoDrawer
    {
        private const int CircleSegments = 48;
        private static Mesh previewQuadMesh;
        private static Material previewMaterial;

        public static void DrawSimulationBounds(
            ScenarioGizmoSimulation simulation,
            bool drawGrid,
            int gridStride,
            bool drawLabel,
            float labelYOffset)
        {
            if (!simulation.IsValid)
                return;

            DrawRect(simulation.Center, simulation.Size, WithAlpha(simulation.Color, 0.02f), WithAlpha(simulation.Color, 0.45f), 0.02f);
            if (drawGrid)
                DrawGrid(simulation.Origin, simulation.WorldSize, simulation.CellSize, simulation.ResolutionX, simulation.ResolutionZ, Mathf.Max(1, gridStride), WithAlpha(simulation.Color, 0.12f));
            if (drawLabel)
                DrawLabel(simulation.Center + Vector3.up * labelYOffset, $"Simulation\n{simulation.WorldSize.x:0.#} x {simulation.WorldSize.y:0.#}", simulation.Color);
        }

        public static void DrawSpawnArea(ScenarioGizmoUnit unit, float fillAlpha, float outlineAlpha, bool drawLabel, float labelYOffset)
        {
            if (!unit.IsValid)
                return;

            DrawRect(unit.SpawnCenter, unit.SpawnSize, WithAlpha(unit.Color, fillAlpha), WithAlpha(unit.Color, outlineAlpha), 0.05f);
            if (drawLabel)
            {
                string label = $"{unit.RoleName}: {unit.UnitName}\nx{unit.UnitCount:n0}";
                DrawLabel(unit.SpawnCenter + Vector3.up * labelYOffset, label, unit.Color);
            }
        }

        public static void DrawFlowField(
            ScenarioGizmoFlowField flowField,
            bool drawGrid,
            int gridStride,
            bool drawLabel,
            float labelYOffset)
        {
            if (!flowField.IsValid)
                return;

            DrawRect(flowField.Center, flowField.Size, WithAlpha(flowField.Color, 0.04f), WithAlpha(flowField.Color, 0.8f), 0.03f);
            if (drawGrid)
                DrawGrid(flowField.Origin, flowField.WorldSize, flowField.CellSize, flowField.ResolutionX, flowField.ResolutionZ, Mathf.Max(1, gridStride), WithAlpha(flowField.Color, 0.22f));
            if (drawLabel)
            {
                string label = $"{flowField.Label}\n{flowField.ResolutionX} x {flowField.ResolutionZ}  cell {flowField.CellSize:0.##}";
                DrawLabel(flowField.Center + Vector3.up * labelYOffset, label, flowField.Color);
            }
        }

        public static void DrawFlowPreviewTexture(ScenarioGizmoFlowField flowField, Texture texture, float alpha, float yOffset)
        {
            if (!flowField.IsValid || texture == null)
                return;

            Material material = GetPreviewMaterial();
            Mesh mesh = GetPreviewQuadMesh();
            if (material == null || mesh == null)
                return;

            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
            material.SetPass(0);

            Matrix4x4 matrix = Matrix4x4.TRS(
                flowField.Center + Vector3.up * yOffset,
                Quaternion.identity,
                new Vector3(flowField.WorldSize.x, 1f, flowField.WorldSize.y));
            Graphics.DrawMeshNow(mesh, matrix);
        }

        public static void DrawConfiguredTarget(ScenarioGizmoUnit unit, float labelYOffset)
        {
            if (!unit.IsValid || unit.Movement == null || !unit.Movement.useConfiguredFlowTarget)
                return;

            Color color = WithAlpha(unit.Color, 0.95f);
            if (unit.Movement.flowTargetMode == FlowFieldTargetMode.Area)
            {
                DrawRect(unit.Movement.targetAreaCenter, unit.Movement.targetAreaSize, WithAlpha(unit.Color, 0.08f), color, 0.08f);
                DrawLabel(unit.Movement.targetAreaCenter + Vector3.up * labelYOffset, $"{unit.RoleName} Target Area", color);
                return;
            }

            Vector3 point = unit.Movement.targetPoint;
            Gizmos.color = color;
            Gizmos.DrawSphere(point, 0.75f);
            DrawCircleOutline(point, 2f, color);
            DrawLabel(point + Vector3.up * labelYOffset, $"{unit.RoleName} Target Point", color);
        }

        private static void DrawRect(Vector3 center, Vector3 size, Color fillColor, Color outlineColor, float height)
        {
            Vector3 safeSize = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, height), Mathf.Max(0.01f, size.z));
            Vector3 safeCenter = new Vector3(center.x, center.y, center.z);
            Gizmos.color = fillColor;
            Gizmos.DrawCube(safeCenter, safeSize);
            Gizmos.color = outlineColor;
            Gizmos.DrawWireCube(safeCenter, safeSize);
        }

        private static void DrawGrid(Vector2 origin, Vector2 worldSize, float cellSize, int resolutionX, int resolutionZ, int stride, Color color)
        {
            Gizmos.color = color;
            int xStep = Mathf.Max(1, stride);
            int zStep = Mathf.Max(1, stride);

            for (int x = 0; x <= resolutionX; x += xStep)
            {
                float worldX = origin.x + Mathf.Min(x, resolutionX) * cellSize;
                Gizmos.DrawLine(new Vector3(worldX, 0f, origin.y), new Vector3(worldX, 0f, origin.y + worldSize.y));
            }

            for (int z = 0; z <= resolutionZ; z += zStep)
            {
                float worldZ = origin.y + Mathf.Min(z, resolutionZ) * cellSize;
                Gizmos.DrawLine(new Vector3(origin.x, 0f, worldZ), new Vector3(origin.x + worldSize.x, 0f, worldZ));
            }
        }

        private static void DrawCircleOutline(Vector3 center, float radius, Color color)
        {
            Gizmos.color = color;
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i / (float)CircleSegments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        private static void DrawLabel(Vector3 position, string text, Color color)
        {
#if UNITY_EDITOR
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color }
            };
            Handles.Label(position, text, style);
#endif
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static Mesh GetPreviewQuadMesh()
        {
            if (previewQuadMesh != null)
                return previewQuadMesh;

            previewQuadMesh = new Mesh
            {
                name = "MassEngine Flow Preview Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(-0.5f, 0f, 0.5f)
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            previewQuadMesh.RecalculateBounds();
            return previewQuadMesh;
        }

        private static Material GetPreviewMaterial()
        {
            if (previewMaterial != null)
                return previewMaterial;

            Shader shader = Shader.Find("MassEngine/FlowFieldPreviewOverlay");
            if (shader == null)
                return null;

            previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return previewMaterial;
        }
    }
}
