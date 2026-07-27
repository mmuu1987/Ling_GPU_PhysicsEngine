using System.Collections.Generic;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Result of the scenario physics evaluation: human-readable issues plus the
    /// derived parameter set that WOULD make the scenario physically consistent.
    /// Consumed by the runtime warning in MassEngineManager and by the editor
    /// Auto-Fit tool — one shared source of truth for the suggested numbers.
    /// </summary>
    public sealed class ScenarioPhysicsReport
    {
        public readonly List<string> Issues = new List<string>();
        public int TotalAgents;
        public Vector2 SuggestedWorldSize;
        public float SuggestedCellSize;
        public int SuggestedMaxAgentsPerCell;
        public int SuggestedFlowResolution;
        public float SuggestedFlowCellSize;
        public Vector2 SuggestedFlowOrigin;

        public bool HasIssues { get { return Issues.Count > 0; } }

        public string Describe()
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("MassEngine scenario physics check — ").Append(Issues.Count).Append(" issue(s) for ")
                .Append(TotalAgents).Append(" agents:\n");
            for (int i = 0; i < Issues.Count; i++)
                text.Append("  ").Append(i + 1).Append(". ").Append(Issues[i]).Append('\n');
            text.Append("Suggested fit: worldSize ").Append(SuggestedWorldSize.x).Append('x').Append(SuggestedWorldSize.y)
                .Append(", cellSize ").Append(SuggestedCellSize)
                .Append(", maxAgentsPerCell ").Append(SuggestedMaxAgentsPerCell)
                .Append(", flow ").Append(SuggestedFlowResolution).Append('@').Append(SuggestedFlowCellSize)
                .Append("m origin ").Append(SuggestedFlowOrigin)
                .Append(".  (Editor menu: MassEngine/Auto-Fit Scenario)");
            return text.ToString();
        }
    }

    /// <summary>
    /// Physical-consistency accounting for a scenario. The engine parameters are
    /// physically coupled (head count ↔ footprint ↔ world ↔ grid ↔ flow field);
    /// this class is the ledger that keeps designers out of impossible configurations
    /// — the failure mode is otherwise a silent, unexplained frame-rate collapse.
    /// </summary>
    public static class ScenarioPhysics
    {
        // Grid occupancy the neighborhood scans stay comfortable at.
        private const float TargetAgentsPerCell = 6f;
        private const float WorldMarginMeters = 40f;
        private const float WorldMarginFactor = 1.25f;

        public static ScenarioPhysicsReport Evaluate(UnitTypeConfig[] unitTypes, SimulationConfig simulation, RuntimeFlowConfig flow, LodConfig lod = null)
        {
            ScenarioPhysicsReport report = new ScenarioPhysicsReport();

            int totalAgents = 0;
            float maxSpawnDensity = 0f;
            float maxAbsExtent = 0f;
            bool anySpawn = false;
            bool hasAttackerTeam = false;
            bool hasDefenderTeam = false;
            var footprints = new List<(int teamId, string name, Rect rect)>();

            if (unitTypes != null)
            {
                for (int i = 0; i < unitTypes.Length; i++)
                {
                    SpawnConfig spawn = unitTypes[i] != null ? unitTypes[i].spawnConfig : null;
                    if (spawn == null)
                        continue;

                    anySpawn = true;
                    hasAttackerTeam |= unitTypes[i].teamId == 0;
                    hasDefenderTeam |= unitTypes[i].teamId == 1;
                    totalAgents += Mathf.Max(0, spawn.unitCount);
                    float density = spawn.ResolveDensity();
                    maxSpawnDensity = Mathf.Max(maxSpawnDensity, density);

                    Vector3 size = spawn.ResolveSpawnSize();
                    maxAbsExtent = Mathf.Max(maxAbsExtent,
                        Mathf.Abs(spawn.spawnCenter.x) + size.x * 0.5f,
                        Mathf.Abs(spawn.spawnCenter.z) + size.z * 0.5f);
                    footprints.Add((unitTypes[i].teamId, unitTypes[i].name,
                        new Rect(spawn.spawnCenter.x - size.x * 0.5f, spawn.spawnCenter.z - size.z * 0.5f, size.x, size.z)));

                    float agentRadius = unitTypes[i].flockingConfig != null
                        ? Mathf.Max(0.05f, unitTypes[i].flockingConfig.agentRadius)
                        : 0.45f;
                    float packingLimit = 1f / (4f * agentRadius * agentRadius); // square packing: one agent per (2r)^2
                    if (density > packingLimit)
                    {
                        report.Issues.Add(string.Format(
                            "'{0}' 出生密度 {1:F2} 人/m² 超过该兵种的物理堆积极限 {2:F2}（半径 {3:F2}m，{4} 人占 {5:F0}x{6:F0}m）——单位互相重叠、分离力爆炸。{7}",
                            unitTypes[i].name, density, packingLimit, agentRadius,
                            spawn.unitCount, size.x, size.z,
                            spawn.HasManualFootprint ? "增大 spawnSize 或清零改用自动脚印。" : "降低 formationDensity。"));
                    }
                }
            }

            report.TotalAgents = totalAgents;

            // Hostile spawn zones must not interpenetrate: overlapped armies start the
            // battle as one chaotic melee blob. (Same-team overlap is legal — mixed
            // formations are a designer tool.)
            for (int a = 0; a < footprints.Count; a++)
            {
                for (int b = a + 1; b < footprints.Count; b++)
                {
                    if (footprints[a].teamId == footprints[b].teamId)
                        continue;
                    if (footprints[a].rect.Overlaps(footprints[b].rect))
                    {
                        report.Issues.Add(string.Format(
                            "敌对出生区重叠：'{0}'(team {1}) 与 '{2}'(team {3}) 的阵地互相穿插——自动脚印随兵力变大后，spawnCenter 需要拉开（纵深的一半 + 交战间距）。",
                            footprints[a].name, footprints[a].teamId, footprints[b].name, footprints[b].teamId));
                    }
                }
            }

            // ---- suggested parameter set (always computed, issues or not) ----
            // The world must satisfy BOTH constraints: contain every spawn footprint
            // with maneuvering margin, AND keep the global density at a fightable
            // level (~0.35/m² leaves room to advance and flank).
            const float targetGlobalDensity = 0.35f;
            float extentSide = (maxAbsExtent * WorldMarginFactor + WorldMarginMeters) * 2f;
            float packingSide = Mathf.Sqrt(Mathf.Max(1, totalAgents) / targetGlobalDensity);
            float suggestedWorld = Mathf.Ceil(Mathf.Max(120f, extentSide, packingSide) / 20f) * 20f;
            report.SuggestedWorldSize = new Vector2(suggestedWorld, suggestedWorld);

            float planningDensity = Mathf.Max(0.2f, maxSpawnDensity);
            float cell = Mathf.Sqrt(TargetAgentsPerCell / planningDensity);
            report.SuggestedCellSize = Mathf.Clamp(Mathf.Round(cell), 2f, 8f);
            report.SuggestedMaxAgentsPerCell = Mathf.Clamp(
                Mathf.CeilToInt(planningDensity * report.SuggestedCellSize * report.SuggestedCellSize * 4f), 16, 64);

            report.SuggestedFlowResolution = suggestedWorld > 512f ? 256 : 128;
            report.SuggestedFlowCellSize = Mathf.Ceil(suggestedWorld / report.SuggestedFlowResolution);
            report.SuggestedFlowOrigin = new Vector2(-suggestedWorld * 0.5f, -suggestedWorld * 0.5f);

            if (simulation == null || !anySpawn)
                return report;

            // ---- issue detection against the CURRENT configuration ----
            Vector2 world = simulation.simulationWorldSize;
            float worldArea = Mathf.Max(1f, world.x * world.y);

            float globalDensity = totalAgents / worldArea;
            if (globalDensity > SpawnConfig.PackingLimitPerSquareMeter)
            {
                report.Issues.Add(string.Format(
                    "世界物理装不下：{0} 人 / {1:F0}x{2:F0}m = {3:F1} 人/m²，超过堆积极限 {4:F1}。需要世界 ≥ {5:F0}x{5:F0}m。",
                    totalAgents, world.x, world.y, globalDensity,
                    SpawnConfig.PackingLimitPerSquareMeter, report.SuggestedWorldSize.x));
            }

            float worldHalfX = world.x * 0.5f;
            if (maxAbsExtent > Mathf.Min(worldHalfX, world.y * 0.5f))
            {
                report.Issues.Add(string.Format(
                    "生成区超出世界边界（阵地延伸到 ±{0:F0}m，世界半宽只有 ±{1:F0}m）——越界单位会被压到边缘挤成一条线。",
                    maxAbsExtent, worldHalfX));
            }

            float cellSize = Mathf.Max(0.1f, simulation.cellSize);
            float expectedPerCell = maxSpawnDensity * cellSize * cellSize;
            if (expectedPerCell > simulation.maxAgentsPerCell * 0.75f)
            {
                report.Issues.Add(string.Format(
                    "空间哈希将溢出：出生密度 {0:F1} 人/m² × 格子 {1:F0}x{1:F0}m ≈ {2:F0} 人/格，逼近/超过 maxAgentsPerCell={3}（溢出者静默掉出邻域查询→穿插成团）。建议 cellSize {4} + maxAgentsPerCell {5}。",
                    maxSpawnDensity, cellSize, expectedPerCell, simulation.maxAgentsPerCell,
                    report.SuggestedCellSize, report.SuggestedMaxAgentsPerCell));
            }

            if (flow != null && (flow.flowFieldEnabled || flow.defenderFlowFieldEnabled))
            {
                float coverage = flow.flowFieldResolution * Mathf.Max(0.1f, flow.flowFieldCellSize);
                float neededCoverage = Mathf.Max(world.x, world.y);
                float worldHalfZ = world.y * 0.5f;
                Vector2 flowMax = flow.flowFieldOrigin + new Vector2(coverage, coverage);
                if (coverage < neededCoverage - 0.5f ||
                    flow.flowFieldOrigin.x > -worldHalfX + 0.5f || flowMax.x < worldHalfX - 0.5f ||
                    flow.flowFieldOrigin.y > -worldHalfZ + 0.5f || flowMax.y < worldHalfZ - 0.5f)
                {
                    report.Issues.Add(string.Format(
                        "流场未覆盖全世界（覆盖 {0:F0}m 起点 {1}，世界 {2:F0}x{3:F0}m 居中原点）——覆盖外的单位被钳到边缘格，导航错乱且密度统计被污染。建议 {4}@{5}m origin {6}。",
                        coverage, flow.flowFieldOrigin, world.x, world.y,
                        report.SuggestedFlowResolution, report.SuggestedFlowCellSize, report.SuggestedFlowOrigin));
                }
            }

            // Navigation-source accounting: dynamic targeting is subordinate to its
            // team's flow master switch — the off+on combination silently produces an
            // army that stands still forever.
            if (flow != null)
            {
                if (hasAttackerTeam && !flow.flowFieldEnabled && flow.runtimeDynamicAttackerFlowEnabled)
                    report.Issues.Add("攻方动态寻的被 flowFieldEnabled=false 短路：攻方开战后将原地待命（点击下令除外）。");
                if (hasDefenderTeam && !flow.defenderFlowFieldEnabled && flow.runtimeDynamicDefenderFlowEnabled)
                    report.Issues.Add("守方动态寻的被 defenderFlowFieldEnabled=false 短路（该开关同时选择守方条令 HOLD/FLOW_FIELD）。");
            }

            if (lod != null)
            {
                if (lod.nearLodRadius >= lod.midLodRadius)
                    report.Issues.Add(string.Format(
                        "LOD 半径失序：nearLodRadius {0:F0} >= midLodRadius {1:F0} —— mid 层永远为空，中景网格/降频档位全部失效。",
                        lod.nearLodRadius, lod.midLodRadius));
                if (lod.maxRenderDistance > 0f && lod.maxRenderDistance < lod.midLodRadius)
                    report.Issues.Add(string.Format(
                        "maxRenderDistance {0:F0} < midLodRadius {1:F0}：mid 层内的单位会在镜头附近凭空消失。",
                        lod.maxRenderDistance, lod.midLodRadius));
            }

            return report;
        }
    }
}
