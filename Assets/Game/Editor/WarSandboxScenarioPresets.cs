using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    public enum WarSandboxScalePreset
    {
        Standard10K = 0,
        Large50K = 1,
        Huge100K = 2,
        Stress200K = 3,
        Custom = 4
    }

    public readonly struct WarSandboxPresetDefinition
    {
        public readonly int unitsPerTeam;
        public readonly string performanceNote;
        public readonly MessageType messageType;

        public WarSandboxPresetDefinition(int unitsPerTeam, string performanceNote, MessageType messageType)
        {
            this.unitsPerTeam = unitsPerTeam;
            this.performanceNote = performanceNote;
            this.messageType = messageType;
        }
    }

    /// <summary>
    /// Product-scale presets for the current two-team sandbox. A preset is authoring
    /// intent: applying it writes SpawnConfig assets with Undo, then the editor window
    /// invokes ScenarioAutoFit to keep deployment and engine dimensions consistent.
    /// </summary>
    public static class WarSandboxScenarioPresets
    {
        public static readonly string[] DisplayNames =
        {
            "标准：1万 vs 1万",
            "大型：5万 vs 5万",
            "超大型：10万 vs 10万",
            "压力测试：20万 vs 20万",
            "自定义"
        };

        public static WarSandboxPresetDefinition GetDefinition(WarSandboxScalePreset preset, int customUnitsPerTeam)
        {
            switch (preset)
            {
                case WarSandboxScalePreset.Large50K:
                    return new WarSandboxPresetDefinition(50000, "实测约 30 FPS；可能出现局部 GRID OVERFLOW。", MessageType.Warning);
                case WarSandboxScalePreset.Huge100K:
                    return new WarSandboxPresetDefinition(100000, "实测约 18 FPS；属于压力展示档。", MessageType.Warning);
                case WarSandboxScalePreset.Stress200K:
                    return new WarSandboxPresetDefinition(200000, "实测约 12 FPS；仅用于容量/极限展示。", MessageType.Error);
                case WarSandboxScalePreset.Custom:
                    return new WarSandboxPresetDefinition(
                        Mathf.Max(1, customUnitsPerTeam),
                        "自定义规模：性能和空间哈希精度取决于实际配置。",
                        MessageType.Info);
                default:
                    return new WarSandboxPresetDefinition(10000, "实测约 113 FPS；默认流畅展示档。", MessageType.Info);
            }
        }

        public static void ApplyPerTeamUnitCount(ScenarioConfig scenario, int unitsPerTeam)
        {
            if (scenario == null || scenario.unitTypes == null)
                return;

            unitsPerTeam = Mathf.Max(1, unitsPerTeam);
            ApplyTeamUnitCount(CollectTeamSpawns(scenario, 0), unitsPerTeam);
            ApplyTeamUnitCount(CollectTeamSpawns(scenario, 1), unitsPerTeam);
        }

        public static int ResolveTeamUnitCount(ScenarioConfig scenario, int teamId)
        {
            List<SpawnConfig> spawns = CollectTeamSpawns(scenario, teamId);
            int total = 0;
            for (int i = 0; i < spawns.Count; i++)
                total += Mathf.Max(0, spawns[i].unitCount);
            return total;
        }

        private static List<SpawnConfig> CollectTeamSpawns(ScenarioConfig scenario, int teamId)
        {
            var result = new List<SpawnConfig>();
            var unique = new HashSet<SpawnConfig>();
            if (scenario == null || scenario.unitTypes == null)
                return result;

            for (int i = 0; i < scenario.unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = scenario.unitTypes[i];
                SpawnConfig spawn = unitType != null ? unitType.spawnConfig : null;
                if (spawn != null && unitType.teamId == teamId && unique.Add(spawn))
                    result.Add(spawn);
            }

            return result;
        }

        private static void ApplyTeamUnitCount(List<SpawnConfig> spawns, int targetTotal)
        {
            if (spawns.Count == 0)
                return;

            int previousTotal = 0;
            for (int i = 0; i < spawns.Count; i++)
                previousTotal += Mathf.Max(0, spawns[i].unitCount);

            int remaining = targetTotal;
            for (int i = 0; i < spawns.Count; i++)
            {
                int allocation;
                if (i == spawns.Count - 1)
                {
                    allocation = remaining;
                }
                else if (previousTotal > 0)
                {
                    float share = Mathf.Max(0, spawns[i].unitCount) / (float)previousTotal;
                    allocation = Mathf.Clamp(Mathf.RoundToInt(targetTotal * share), 0, remaining);
                }
                else
                {
                    allocation = remaining / (spawns.Count - i);
                }

                Undo.RecordObject(spawns[i], "Apply War Sandbox Scale Preset");
                spawns[i].unitCount = allocation;
                EditorUtility.SetDirty(spawns[i]);
                remaining -= allocation;
            }
        }
    }
}
