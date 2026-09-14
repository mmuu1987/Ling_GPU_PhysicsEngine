using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    [Serializable]
    public struct WarSandboxDeploymentEntry
    {
        public UnitTypeConfig template;
        public int teamId;
        public int count;
        public Vector3 center;
        public float density;
        public float aspect;
        public Vector3 manualSize;

        public static WarSandboxDeploymentEntry From(UnitTypeConfig unit) => new WarSandboxDeploymentEntry
        {
            template = unit, teamId = unit.teamId, count = unit.spawnConfig.unitCount,
            center = unit.spawnConfig.spawnCenter, density = unit.spawnConfig.formationDensity,
            aspect = unit.spawnConfig.formationAspect, manualSize = unit.spawnConfig.spawnSize
        };

        public Vector3 Size => SpawnConfig.ResolveSpawnSize(count, density, aspect, manualSize);
        public Rect Bounds => new Rect(center.x - Size.x * 0.5f, center.z - Size.z * 0.5f, Size.x, Size.z);
        public string Name => template != null ? template.unitTypeName : "Missing template";
    }

    // Value snapshots keep editing and Undo independent of both assets and live GPU state.
    public sealed class WarSandboxDeploymentDraft
    {
        public const int MaxCompositions = 32;
        public const int MaxTotalUnits = 400000;
        private const int HistoryLimit = 32;
        private readonly List<WarSandboxDeploymentEntry> entries;
        private sealed class State
        {
            public WarSandboxDeploymentEntry[] entries;
            public WarSandboxBattlefieldRules rules;
        }
        private readonly List<State> undo = new List<State>();
        private readonly List<State> redo = new List<State>();
        private WarSandboxBattlefieldRules rules;
        public WarSandboxBattlefieldRules Rules => rules.Copy();
        public int Count => entries.Count;
        public int Revision { get; private set; }
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public WarSandboxDeploymentEntry this[int index] => entries[index];

        public WarSandboxDeploymentDraft(IEnumerable<WarSandboxDeploymentEntry> source, WarSandboxBattlefieldRules? initialRules = null)
        {
            entries = new List<WarSandboxDeploymentEntry>(source);
            rules = (initialRules ?? WarSandboxBattlefieldRules.Default).Copy();
        }

        public static bool TryCapture(ScenarioConfig scenario, out WarSandboxDeploymentDraft draft, out string error)
        {
            draft = null; error = null;
            if (scenario == null || scenario.unitTypes == null) { error = "Deployment is missing."; return false; }
            var values = new List<WarSandboxDeploymentEntry>();
            foreach (var unit in scenario.unitTypes)
            {
                var validation = ConfigValidator.Validate(unit);
                if (!validation.IsValid) { error = string.Join("\n", validation.Errors); return false; }
                values.Add(WarSandboxDeploymentEntry.From(unit));
            }
            draft = new WarSandboxDeploymentDraft(values);
            return true;
        }

        public WarSandboxDeploymentEntry[] Snapshot() => entries.ToArray();

        public void Replace(WarSandboxDeploymentEntry[] source, WarSandboxBattlefieldRules newRules)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Remember(); entries.Clear(); entries.AddRange(source); rules = newRules.Copy();
        }

        public bool Set(int index, WarSandboxDeploymentEntry value)
        {
            if (index < 0 || index >= Count || entries[index].Equals(value)) return false;
            Remember(); entries[index] = value; return true;
        }

        public bool Add(UnitTypeConfig template, int teamId)
        {
            if (Count >= MaxCompositions || template == null || template.spawnConfig == null ||
                teamId < 0 || teamId > ConfigValidator.MaxTeamId) return false;
            var value = WarSandboxDeploymentEntry.From(template);
            value.teamId = teamId; value.count = 1000; value.manualSize = Vector3.zero;
            Remember(); entries.Add(value); return true;
        }

        public bool Remove(int index)
        {
            if (index < 0 || index >= Count) return false;
            Remember(); entries.RemoveAt(index); return true;
        }

        public bool RemoveArmy(int teamId)
        {
            if (!entries.Exists(e => e.teamId == teamId)) return false;
            Remember(); entries.RemoveAll(e => e.teamId == teamId); return true;
        }

        public int NextArmyId()
        {
            for (int id = 0; id <= ConfigValidator.MaxTeamId; id++)
                if (!entries.Exists(e => e.teamId == id)) return id;
            return -1;
        }

        public bool Undo() => Restore(undo, redo);
        public bool Redo() => Restore(redo, undo);

        private bool Restore(List<State> from, List<State> to)
        {
            if (from.Count == 0) return false;
            Push(to, Capture()); var snapshot = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            entries.Clear(); entries.AddRange(snapshot.entries); rules = snapshot.rules.Copy(); Revision++; return true;
        }

        private State Capture() => new State { entries = Snapshot(), rules = rules.Copy() };
        private void Remember() { Push(undo, Capture()); redo.Clear(); Revision++; }
        private static void Push(List<State> history, State snapshot)
        {
            if (history.Count == HistoryLimit) history.RemoveAt(0);
            history.Add(snapshot);
        }

        public bool TryValidate(Vector2 worldSize, WarSandboxBattlefieldRules rules, out string error)
        {
            error = null;
            if (Count < 1 || Count > MaxCompositions) error = "需要 1 到 " + MaxCompositions + " 个编成。";
            else if (!Finite(worldSize.x) || !Finite(worldSize.y) || worldSize.x <= 0 || worldSize.y <= 0) error = "战场尺寸无效。";
            else if (!rules.TryValidate(out error)) return false;
            if (error != null) return false;
            long total = 0;
            for (int i = 0; i < Count; i++)
            {
                var e = entries[i];
                string prefix = "编成 " + (i + 1) + "：";
                var validation = ConfigValidator.Validate(e.template);
                if (!validation.IsValid) { error = prefix + string.Join("\n", validation.Errors); return false; }
                if (e.teamId < 0 || e.teamId > ConfigValidator.MaxTeamId) error = prefix + "军团无效。";
                else if (e.count <= 0 || e.count > MaxTotalUnits) error = prefix + "人数必须为 1 到 " + MaxTotalUnits + "。";
                else if (!Finite(e.center) || !Finite(e.manualSize) || e.manualSize.x < 0 || e.manualSize.y < 0 || e.manualSize.z < 0)
                    error = prefix + "位置和阵型尺寸必须为有效数值。";
                else if ((e.manualSize.x > 0) != (e.manualSize.z > 0)) error = prefix + "手动阵型的宽、深必须同时大于零。";
                else if (!Finite(e.density) || e.density < 0.05f || e.density > SpawnConfig.PackingLimitPerSquareMeter ||
                    !Finite(e.aspect) || e.aspect < 0.1f || e.aspect > 10f) error = prefix + "密度或宽深比超出范围。";
                if (error != null) return false;
                total += e.count;
                if (total > MaxTotalUnits) { error = "总兵力不能超过 " + MaxTotalUnits + "。"; return false; }
                Rect bounds = e.Bounds;
                if (!Finite(e.Size) || bounds.xMin < -worldSize.x * 0.5f || bounds.xMax > worldSize.x * 0.5f ||
                    bounds.yMin < -worldSize.y * 0.5f || bounds.yMax > worldSize.y * 0.5f)
                    error = prefix + "部署脚印越出战场。";
                else if (e.count / Mathf.Max(0.001f, bounds.width * bounds.height) > SpawnConfig.PackingLimitPerSquareMeter + 0.001f)
                    error = prefix + "手动阵型过于拥挤。";
                if (error != null) return false;
                if (rules.staticObstaclesEnabled && rules.staticObstacles != null)
                    foreach (var obstacle in rules.staticObstacles)
                    {
                        var wall = obstacle.Bounds;
                        wall.xMin -= rules.staticObstacleClearance; wall.xMax += rules.staticObstacleClearance;
                        wall.yMin -= rules.staticObstacleClearance; wall.yMax += rules.staticObstacleClearance;
                        if (Overlaps(bounds, wall)) { error = prefix + "部署脚印与障碍相交。"; return false; }
                    }
                for (int j = 0; j < i; j++)
                    if (Overlaps(bounds, entries[j].Bounds)) { error = prefix + "与编成 " + (j + 1) + " 的部署脚印重叠。"; return false; }
            }
            return true;
        }

        private static bool Overlaps(Rect a, Rect b) => Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.01f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.01f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    public sealed class WarSandboxDeploymentInstance : IDisposable
    {
        public ScenarioConfig Scenario { get; private set; }
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public WarSandboxDeploymentInstance(WarSandboxDeploymentEntry[] entries)
        {
            try
            {
                Scenario = Own(ScriptableObject.CreateInstance<ScenarioConfig>());
                Scenario.name = "War Sandbox Deployment (Runtime)";
                Scenario.unitTypes = new UnitTypeConfig[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    var unit = Own(UnityEngine.Object.Instantiate(e.template));
                    var spawn = Own(UnityEngine.Object.Instantiate(e.template.spawnConfig));
                    unit.name = e.template.name + " (Runtime)";
                    spawn.name = e.template.spawnConfig.name + " (Runtime)";
                    unit.teamId = e.teamId; unit.spawnConfig = spawn;
                    spawn.unitCount = e.count; spawn.spawnCenter = e.center;
                    spawn.formationDensity = e.density; spawn.formationAspect = e.aspect; spawn.spawnSize = e.manualSize;
                    Scenario.unitTypes[i] = unit;
                }
            }
            catch { Dispose(); throw; }
        }

        private T Own<T>(T value) where T : UnityEngine.Object
        {
            value.hideFlags = HideFlags.DontSave; owned.Add(value); return value;
        }

        public void Dispose()
        {
            foreach (var value in owned)
                if (value != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(value);
                    else UnityEngine.Object.DestroyImmediate(value);
                }
            owned.Clear(); Scenario = null;
        }
    }
}
