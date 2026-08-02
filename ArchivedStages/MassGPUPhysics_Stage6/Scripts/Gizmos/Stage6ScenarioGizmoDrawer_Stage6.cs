using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class Stage6ScenarioGizmoDrawer_Stage6
{
    private const int CircleSegments = 64;
    private static Mesh unitDiscMesh;

    public static void DrawSpawnArea(
        Stage6ScenarioGizmoTeam_Stage6 team,
        float fillAlpha,
        float outlineAlpha,
        bool drawLabel,
        float labelYOffset)
    {
        if (!team.IsValid)
            return;

        Color fillColor = WithAlpha(team.TeamColor, fillAlpha);
        Color outlineColor = WithAlpha(team.TeamColor, outlineAlpha);
        if (team.SpawnShape == Stage6SpawnShape.Circle)
            DrawCircleSpawn(team.SpawnCenter, team.SpawnRadius, fillColor, outlineColor);
        else
            DrawRectSpawn(team.SpawnCenter, team.SpawnSize, fillColor, outlineColor);

        if (drawLabel)
            DrawTeamLabel(team, labelYOffset);
    }

    public static void DrawFlowField(
        Stage6ScenarioGizmoFlowField_Stage6 flowField,
        bool drawGrid,
        int gridStride,
        bool drawLabel,
        float labelYOffset)
    {
        if (!flowField.IsValid)
            return;

        Color outlineColor = WithAlpha(flowField.Color, 0.85f);
        Color fillColor = WithAlpha(flowField.Color, 0.05f);
        DrawRectSpawn(flowField.Center, flowField.Size, fillColor, outlineColor);

        if (drawGrid)
            DrawFlowGrid(flowField, Mathf.Max(1, gridStride));

        if (drawLabel)
            DrawLabel(
                flowField.Center + Vector3.up * labelYOffset,
                $"{flowField.Label}\n{flowField.ResolutionX}x{flowField.ResolutionZ}  cell {flowField.CellSize:0.##}",
                outlineColor);
    }

    private static void DrawRectSpawn(Vector3 center, Vector3 size, Color fillColor, Color outlineColor)
    {
        Vector3 safeSize = new Vector3(Mathf.Max(0.01f, size.x), 0.05f, Mathf.Max(0.01f, size.z));
        Gizmos.color = fillColor;
        Gizmos.DrawCube(new Vector3(center.x, center.y, center.z), safeSize);
        Gizmos.color = outlineColor;
        Gizmos.DrawWireCube(new Vector3(center.x, center.y, center.z), safeSize);
    }

    private static void DrawCircleSpawn(Vector3 center, float radius, Color fillColor, Color outlineColor)
    {
        radius = Mathf.Max(0.01f, radius);
        Mesh disc = GetUnitDiscMesh();
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(new Vector3(center.x, center.y, center.z), Quaternion.identity, new Vector3(radius, 1f, radius));
        Gizmos.color = fillColor;
        Gizmos.DrawMesh(disc);
        Gizmos.matrix = oldMatrix;

        Gizmos.color = outlineColor;
        DrawCircleOutline(center, radius);
    }

    private static void DrawCircleOutline(Vector3 center, float radius)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i / (float)CircleSegments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private static void DrawFlowGrid(Stage6ScenarioGizmoFlowField_Stage6 flowField, int stride)
    {
        Color gridColor = WithAlpha(flowField.Color, 0.2f);
        Gizmos.color = gridColor;

        int xStep = Mathf.Max(1, stride);
        int zStep = Mathf.Max(1, stride);
        for (int x = 0; x <= flowField.ResolutionX; x += xStep)
        {
            float worldX = flowField.Origin.x + Mathf.Min(x, flowField.ResolutionX) * flowField.CellSize;
            Gizmos.DrawLine(
                new Vector3(worldX, 0f, flowField.Origin.y),
                new Vector3(worldX, 0f, flowField.Origin.y + flowField.WorldSize.y));
        }

        for (int z = 0; z <= flowField.ResolutionZ; z += zStep)
        {
            float worldZ = flowField.Origin.y + Mathf.Min(z, flowField.ResolutionZ) * flowField.CellSize;
            Gizmos.DrawLine(
                new Vector3(flowField.Origin.x, 0f, worldZ),
                new Vector3(flowField.Origin.x + flowField.WorldSize.x, 0f, worldZ));
        }
    }

    private static void DrawTeamLabel(Stage6ScenarioGizmoTeam_Stage6 team, float labelYOffset)
    {
        string label =
            $"{team.RoleName}: {team.TeamName}\n" +
            $"{team.UnitName}  x{team.UnitCount:n0}\n" +
            $"{team.SpawnShape}";
        DrawLabel(team.SpawnCenter + Vector3.up * labelYOffset, label, team.TeamColor);
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

    private static Mesh GetUnitDiscMesh()
    {
        if (unitDiscMesh != null)
            return unitDiscMesh;

        var vertices = new Vector3[CircleSegments + 1];
        var triangles = new int[CircleSegments * 3];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < CircleSegments; i++)
        {
            float angle = i / (float)CircleSegments * Mathf.PI * 2f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        for (int i = 0; i < CircleSegments; i++)
        {
            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = i == CircleSegments - 1 ? 1 : i + 2;
        }

        unitDiscMesh = new Mesh
        {
            name = "Stage6 Scenario Gizmo Disc",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            triangles = triangles
        };
        unitDiscMesh.RecalculateBounds();
        return unitDiscMesh;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }
}

