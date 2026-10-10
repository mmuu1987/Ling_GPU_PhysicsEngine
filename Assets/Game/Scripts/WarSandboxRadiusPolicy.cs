using System;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Editing admission, NOT a replacement for any simulation formula.</summary>
    public static class WarSandboxRadiusPolicy
    {
        // Covers today's complete authored range; below .05 existing consumers disagree on minima.
        public const float Min = .05f, Max = 4.8f;
        public static bool TryValidate(MassEngineManager manager, WarSandboxDeploymentDraft draft, WarSandboxStatResolver resolver,
            out float? clearance, out string error)
        {
            clearance = null; error = null;
            var definition = WarSandboxUnitStats.Get(WarSandboxUnitStat.AgentRadius);
            bool changed = false;
            for (int i = 0; i < draft.Count; i++) changed |= resolver.Resolve(draft[i].template, definition).Customized;
            if (!changed && !manager.DeploymentRadiusClearance.HasValue) return true; // untouched legacy path
            var simulation = manager.systemConfig != null ? manager.systemConfig.simulationConfig : null;
            if (simulation == null) { error = "半径校验缺少战场网格设置。"; return false; }
            float maximum = .45f;
            var radii = new float[draft.Count];
            for (int i = 0; i < draft.Count; i++)
            {
                var entry = draft[i]; var value = resolver.Resolve(entry.template, definition);
                var info = UnitPreviewRadius.Read(entry.template);
                if (!info.Available) { error = "编成 " + (i + 1) + "：自定义模块的半径/生成比例尚未验证。"; return false; }
                float radius = value.applies ? value.effective : info.BaseRadius;
                if (!definition.Accepts(radius)) { error = "编成 " + (i + 1) + "：半径必须为 0.05–4.8 米。"; return false; }
                radii[i] = radius; maximum = Mathf.Max(maximum, radius);
            }
            // Once the radius workflow has been used, restoration also recalculates clearance,
            // rather than retaining a larger historical value. Ordinary ResetBattle keeps it.
            clearance = maximum;
            if (!changed) return true;
            if (float.IsNaN(simulation.cellSize) || simulation.cellSize < maximum * 2)
            { error = "半径未应用：当前邻域网格不支持该体积（最大直径 " + (maximum * 2).ToString("0.##") + " 米，网格 " + simulation.cellSize.ToString("0.##") + " 米）。请减小半径；不会自动改网格。"; return false; }
            var half = simulation.simulationWorldSize * .5f - Vector2.one * simulation.boundaryPadding;
            for (int i = 0; i < draft.Count; i++)
            {
                var entry = draft[i]; float r = radii[i]; string prefix = "编成 " + (i + 1) + "：";
                if (entry.count <= 0 || entry.template.spawnConfig == null) { error = prefix + "半径校验缺少有效生成配置。"; return false; }
                var size = entry.Size;
                // Same rows/columns as DefaultSpawnModule. Bound deterministic interior jitter
                // conservatively; a partial last row cannot defeat the inter-row X separation.
                float aspect = Mathf.Max(.01f, size.z / Mathf.Max(.01f, size.x));
                int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(entry.count * aspect)), 1, entry.count);
                int rows = Mathf.CeilToInt(entry.count / (float)columns);
                float jitter = Mathf.Clamp(entry.template.spawnConfig.formationJitterFraction, 0, .08f);
                if ((rows > 1 && size.x / (rows - 1) * (1 - 2 * jitter) + .0001f < 2 * r) ||
                    (columns > 1 && size.z / (columns - 1) * (1 - 2 * jitter) + .0001f < 2 * r))
                { error = prefix + "半径与实际行列间距/抖动不相容。请降低密度或扩大占地；不会自动减人数或缩模型。"; return false; }
                var bounds = Expanded(entry.Bounds, r);
                if (bounds.xMin < -half.x || bounds.xMax > half.x || bounds.yMin < -half.y || bounds.yMax > half.y)
                { error = prefix + "半径扩展后的占地越出战场留白。"; return false; }
                for (int j = 0; j < i; j++)
                    if (bounds.Overlaps(Expanded(draft[j].Bounds, radii[j])))
                    { error = prefix + "半径扩展后的占地可能与编成 " + (j + 1) + " 相交。请调整位置。"; return false; }
            }
            return true;
        }
        private static Rect Expanded(Rect rect, float radius)
        { rect.xMin -= radius; rect.xMax += radius; rect.yMin -= radius; rect.yMax += radius; return rect; }
    }
}
