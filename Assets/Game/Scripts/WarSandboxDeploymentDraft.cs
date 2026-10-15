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
        /// <summary>0 = auto (face nearest enemy formation); 1..4 = manual quarter, see WarSandboxDeploymentFacing.</summary>
        public int facing;
        /// <summary>Brush deployment (2026-10-15): painted footprint, null = rectangle. Size/Bounds follow its cell rectangle;
        /// the mask is immutable, so entries stay plain values for snapshots and Undo.</summary>
        [NonSerialized] public WarSandboxBrushMask brush;

        public static WarSandboxDeploymentEntry From(UnitTypeConfig unit) => new WarSandboxDeploymentEntry
        {
            template = unit, teamId = unit.teamId, count = unit.spawnConfig.unitCount,
            center = unit.spawnConfig.spawnCenter, density = unit.spawnConfig.formationDensity,
            aspect = unit.spawnConfig.formationAspect, manualSize = unit.spawnConfig.spawnSize
        };

        // C4 (2026-10-12): large bodies (dragons, giants, cavalry; radius above 1.5 m, half the
        // default 3 m spatial cell) start as a small block instead of the 1000-strong crowd default.
        public const float LargeBodyRadius = 1.5f;
        public const int CrowdDefaultCount = 1000, LargeDefaultCount = 24;
        // Auto-sized blocks are thinned to this fraction of square packing (one body per (2r)^2).
        public const float AutoPackingFraction = .8f;
        public static bool IsLargeTemplate(UnitTypeConfig unit) =>
            unit != null && unit.flockingConfig != null && unit.flockingConfig.agentRadius > LargeBodyRadius;
        public static int DefaultCount(UnitTypeConfig unit) => IsLargeTemplate(unit) ? LargeDefaultCount : CrowdDefaultCount;

        /// <summary>For auto-sized blocks only: thins a block the roster packing check would reject
        /// (e.g. a dragon given the crowd's spacing). Never packs tighter or edits a manual size.
        /// Auto sizing cannot go below SpawnConfig's 0.05 /m2 floor (Draft validation, HUD input and
        /// saved plans all enforce it), which is still too dense for any body over ~2.2 m; such a
        /// block gets an explicit footprint of the same aspect at the thinned density instead.</summary>
        public void FitDensityToBody()
        {
            if (template == null || template.flockingConfig == null || manualSize.x > 0 || manualSize.z > 0) return;
            float limit = BodyDensityLimit(template.flockingConfig.agentRadius);
            float effective = Mathf.Clamp(density, AutoDensityFloor, SpawnConfig.PackingLimitPerSquareMeter);
            if (effective <= limit) return; // also false for NaN density, which falls through and is replaced
            if (limit >= AutoDensityFloor) { density = limit; return; }
            float shape = Mathf.Clamp(float.IsNaN(aspect) ? 1f : aspect, .1f, 10f);
            float depth = Mathf.Sqrt(Mathf.Max(1, count) / limit / shape); // X, as SpawnConfig.ResolveSpawnSize
            manualSize = new Vector3(depth, 0f, depth * shape);
            GrowToLattice();
        }

        /// <summary>Explicit footprints of large bodies: the body density limit ignores row/column rounding and jitter, so
        /// some counts land a little under 2r and count-lock B rejects them. Grow the footprint just enough (keeps aspect).</summary>
        public void GrowToLattice()
        {
            if (template == null || template.flockingConfig == null || template.spawnConfig == null || manualSize.x <= 0 || manualSize.z <= 0) return;
            float k = WarSandboxDeploymentResize.LatticeGrowth(this, template.flockingConfig.agentRadius, template.spawnConfig.formationJitterFraction);
            if (k > 1f) manualSize = new Vector3(manualSize.x * k, manualSize.y, manualSize.z * k);
        }

        // Lattice growth adds at most ~13.4% area, so the sparsest allowed density sits this far below the body limit.
        public const float LatticeAreaSlack = 1.15f;

        // SpawnConfig.ResolveSpawnSize clamps auto density to this floor.
        public const float AutoDensityFloor = .05f;

        /// <summary>Densest spacing the body allows: AutoPackingFraction of square packing, one body per (2r)^2.</summary>
        public static float BodyDensityLimit(float radius)
        {
            float r = Mathf.Max(.05f, radius);
            return AutoPackingFraction / (4f * r * r);
        }

        /// <summary>Sparsest density an edit may produce: the 0.05 /m2 floor, or the body limit when the body is so
        /// large that even the floor packs it too tightly (dragons). Below 0.05 the footprint stays explicit.</summary>
        public static float MinimumDensity(UnitTypeConfig unit) =>
            unit != null && unit.flockingConfig != null ? Mathf.Min(AutoDensityFloor, BodyDensityLimit(unit.flockingConfig.agentRadius) / LatticeAreaSlack) : AutoDensityFloor;

        /// <summary>Densest density a count edit targets: the 1.5 /m2 packing limit, or the body limit if tighter.</summary>
        public static float MaximumDensity(UnitTypeConfig unit) =>
            unit != null && unit.flockingConfig != null ? Mathf.Min(SpawnConfig.PackingLimitPerSquareMeter, BodyDensityLimit(unit.flockingConfig.agentRadius)) : SpawnConfig.PackingLimitPerSquareMeter;

        public Vector3 Size => brush != null ? new Vector3(brush.Size.x, 0f, brush.Size.y) : SpawnConfig.ResolveSpawnSize(count, density, aspect, manualSize);
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
            public WarSandboxStatOverrides stats;
        }
        private readonly List<State> undo = new List<State>();
        private readonly List<State> redo = new List<State>();
        private WarSandboxBattlefieldRules rules;
        private WarSandboxStatOverrides stats;
        // Consecutive edits of one field (slider drag, repeated typing) collapse into one Undo step.
        private UnitTypeConfig coalesceTemplate;
        private WarSandboxUnitStat coalesceStat;
        public WarSandboxBattlefieldRules Rules => rules.Copy();
        /// <summary>Local (this plan) unit stat overrides; a copy, edit through SetStat/ClearStat.</summary>
        public WarSandboxStatOverrides Stats => stats.Clone();
        public int Count => entries.Count;
        public int Revision { get; private set; }
        /// <summary>2048 maps: outward x offset for compositions added from template defaults (see
        /// WarSandboxRuntimeDeployment.authoredSpawnOutwardOffset). 0 = unchanged.</summary>
        public float SpawnOutwardOffset { get; set; }
        public static WarSandboxDeploymentEntry ShiftOutward(WarSandboxDeploymentEntry e, float offset)
        {
            if (offset != 0f && e.center.x != 0f) e.center.x += e.center.x < 0f ? -offset : offset;
            return e;
        }
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public const string LockedMessage = "挑战模式：敌方编成已锁定，只能编辑你指挥的军团。";
        /// <summary>Challenge mode (2026-10-14): entries of armies the player does not command are read-only.</summary>
        public bool IsLocked(int index) => index >= 0 && index < entries.Count && rules.Locks(entries[index].teamId);
        public bool IsTeamLocked(int teamId) => rules.Locks(teamId);
        private bool TemplateLocked(UnitTypeConfig template)
        {
            if (!rules.challengeEnabled) return false;
            for (int i = 0; i < entries.Count; i++) if (entries[i].template == template && rules.Locks(entries[i].teamId)) return true;
            return false;
        }
        /// <summary>Budget battle (2026-10-14): default budget for every army; one Undo step.</summary>
        public bool SetBudget(bool enabled, int points)
        {
            if (points < 0 || points > WarSandboxUnitCost.MaxBudget) return false;
            if (rules.budgetEnabled == enabled && rules.budgetPoints == points) return false;
            Remember(); rules.budgetEnabled = enabled; rules.budgetPoints = points; return true;
        }
        /// <summary>Per-army budget; 0 = follow the default budget. One Undo step.</summary>
        public bool SetTeamBudget(int teamId, int points)
        {
            if (teamId < 0 || teamId > ConfigValidator.MaxTeamId || points < 0 || points > WarSandboxUnitCost.MaxBudget) return false;
            var old = rules.budgetTeamPoints;
            int current = old != null && teamId < old.Length ? old[teamId] : 0;
            if (current == points) return false;
            var next = new int[ConfigValidator.MaxTeamId + 1];
            if (old != null) Array.Copy(old, next, Mathf.Min(old.Length, next.Length));
            next[teamId] = points;
            Remember(); rules.budgetTeamPoints = next; return true;
        }
        /// <summary>Turns challenge mode on for teamId, or off. One Undo step; never blocked by the lock itself.</summary>
        public bool SetChallenge(bool enabled, int teamId)
        {
            if (teamId < 0 || teamId > ConfigValidator.MaxTeamId) return false;
            if (rules.challengeEnabled == enabled && (!enabled || rules.challengeTeam == teamId)) return false;
            Remember(); rules.challengeEnabled = enabled; rules.challengeTeam = enabled ? teamId : 0; return true;
        }
        public WarSandboxDeploymentEntry this[int index] => entries[index];

        public WarSandboxDeploymentDraft(IEnumerable<WarSandboxDeploymentEntry> source, WarSandboxBattlefieldRules? initialRules = null,
            WarSandboxStatOverrides initialStats = null)
        {
            entries = new List<WarSandboxDeploymentEntry>(source);
            rules = (initialRules ?? WarSandboxBattlefieldRules.Default).Copy();
            stats = initialStats != null ? initialStats.Clone() : new WarSandboxStatOverrides();
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

        /// <summary>Loading a plan replaces entries, rules and local stat overrides as one Undo step.</summary>
        public void Replace(WarSandboxDeploymentEntry[] source, WarSandboxBattlefieldRules newRules, WarSandboxStatOverrides newStats)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Remember(); entries.Clear(); entries.AddRange(source); rules = newRules.Copy();
            stats = newStats != null ? newStats.Clone() : new WarSandboxStatOverrides();
        }

        public bool TryGetStat(UnitTypeConfig template, WarSandboxUnitStat stat, out float value) => stats.TryGet(template, stat, out value);

        public bool SetStat(UnitTypeConfig template, WarSandboxUnitStat stat, float value)
        {
            if (template == null || !WarSandboxUnitStats.Applies(template, WarSandboxUnitStats.Get(stat))) return false;
            if (TemplateLocked(template)) return false;
            var probe = stats.Clone();
            if (!probe.Set(template, stat, value)) return false;
            bool coalesce = undo.Count > 0 && redo.Count == 0 && coalesceTemplate == template && coalesceStat == stat;
            if (coalesce) Revision++; else Remember();
            stats = probe; coalesceTemplate = template; coalesceStat = stat;
            return true;
        }

        public bool ClearStat(UnitTypeConfig template, WarSandboxUnitStat stat)
        {
            if (!stats.TryGet(template, stat, out _) || TemplateLocked(template)) return false;
            Remember(); stats.Remove(template, stat); return true;
        }

        public bool ClearTemplateStats(UnitTypeConfig template)
        {
            if (stats.Get(template).Count == 0 || TemplateLocked(template)) return false;
            Remember(); stats.RemoveTemplate(template); return true;
        }

        /// <summary>Removes only the given fields of a template (after they were pushed to the global layer).</summary>
        public bool ClearStats(UnitTypeConfig template, IEnumerable<WarSandboxUnitStat> fields)
        {
            var next = stats.Clone(); bool changed = false;
            foreach (var field in fields) changed |= next.Remove(template, field);
            if (!changed) return false;
            Remember(); stats = next; return true;
        }

        public bool Set(int index, WarSandboxDeploymentEntry value)
        {
            if (index < 0 || index >= Count || entries[index].Equals(value)) return false;
            if (rules.Locks(entries[index].teamId) || rules.Locks(value.teamId)) return false;
            Remember(); entries[index] = value; return true;
        }

        public bool Add(UnitTypeConfig template, int teamId, bool templateDefaults = false)
        {
            if (Count >= MaxCompositions || template == null || template.spawnConfig == null ||
                teamId < 0 || teamId > ConfigValidator.MaxTeamId || rules.Locks(teamId)) return false;
            var value = ShiftOutward(WarSandboxDeploymentEntry.From(template), SpawnOutwardOffset);
            value.teamId = teamId; if (!templateDefaults) { value.count = WarSandboxDeploymentEntry.DefaultCount(template); value.manualSize = Vector3.zero; }
            value.FitDensityToBody();
            Remember(); entries.Add(value); return true;
        }

        public bool Remove(int index)
        {
            if (index < 0 || index >= Count || rules.Locks(entries[index].teamId)) return false;
            Remember(); entries.RemoveAt(index); return true;
        }

        public bool RemoveArmy(int teamId)
        {
            if (rules.Locks(teamId) || !entries.Exists(e => e.teamId == teamId)) return false;
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
            entries.Clear(); entries.AddRange(snapshot.entries); rules = snapshot.rules.Copy(); stats = snapshot.stats.Clone();
            coalesceTemplate = null; Revision++; return true;
        }

        private State Capture() => new State { entries = Snapshot(), rules = rules.Copy(), stats = stats.Clone() };
        private void Remember() { Push(undo, Capture()); redo.Clear(); coalesceTemplate = null; Revision++; }
        private static void Push(List<State> history, State snapshot)
        {
            if (history.Count == HistoryLimit) history.RemoveAt(0);
            history.Add(snapshot);
        }

        /// <summary>spatial=false (2026-10-12, deployment B) checks only what an edit must never break: representable
        /// values, team/facing, counts and totals. Footprint bounds, crowding, obstacles and overlaps are skipped so an
        /// edit may leave the draft temporarily invalid; apply/start/save always run the full (spatial) check.</summary>
        public bool TryValidate(Vector2 worldSize, WarSandboxBattlefieldRules rules, out string error, bool spatial = true)
        {
            error = null;
            if (Count < 1 || Count > MaxCompositions) error = "需要 1 到 " + MaxCompositions + " 个编成。";
            else if (!Finite(worldSize.x) || !Finite(worldSize.y) || worldSize.x <= 0 || worldSize.y <= 0) error = "战场尺寸无效。";
            else if (!rules.TryValidate(out error)) return false;
            if (error != null) return false;
            if (rules.challengeEnabled)
            {
                bool own = false;
                for (int i = 0; i < Count; i++) own |= entries[i].teamId == rules.challengeTeam;
                if (!own) { error = "挑战模式：你指挥的" + WarSandboxBattleController.DefaultArmyName(rules.challengeTeam) + "没有编成，请重新选择指挥的军团。"; return false; }
            }
            long total = 0;
            for (int i = 0; i < Count; i++)
            {
                var e = entries[i];
                string prefix = "编成 " + (i + 1) + "：";
                var validation = ConfigValidator.Validate(e.template);
                if (!validation.IsValid) { error = prefix + string.Join("\n", validation.Errors); return false; }
                if (e.teamId < 0 || e.teamId > ConfigValidator.MaxTeamId) error = prefix + "军团无效。";
                else if (!WarSandboxDeploymentFacing.IsValid(e.facing)) error = prefix + "朝向无效。";
                else if (e.count <= 0 || e.count > MaxTotalUnits) error = prefix + "人数必须为 1 到 " + MaxTotalUnits + "。";
                else if (!Finite(e.center) || !Finite(e.manualSize) || e.manualSize.x < 0 || e.manualSize.y < 0 || e.manualSize.z < 0)
                    error = prefix + "位置和阵型尺寸必须为有效数值。";
                else if ((e.manualSize.x > 0) != (e.manualSize.z > 0)) error = prefix + "手动阵型的宽、深必须同时大于零。";
                else if (!Finite(e.density) || e.density < 0.05f || e.density > SpawnConfig.PackingLimitPerSquareMeter ||
                    !Finite(e.aspect) || e.aspect < 0.1f || e.aspect > 10f) error = prefix + "密度或宽深比超出范围。";
                if (error != null) return false;
                total += e.count;
                if (total > MaxTotalUnits) { error = "总兵力不能超过 " + MaxTotalUnits + "。"; return false; }
                if (!spatial) continue;
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
                        if (WarSandboxBrushFootprint.Covers(e, wall)) { error = prefix + "部署脚印与障碍相交。"; return false; }
                    }
                for (int j = 0; j < i; j++)
                    if (WarSandboxBrushFootprint.Overlaps(e, entries[j])) { error = prefix + "与编成 " + (j + 1) + " 的部署脚印重叠。"; return false; }
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
        /// <summary>Number of templates running with at least one non-official stat.</summary>
        public int CustomTemplateCount { get; private set; }
        public bool HasCustomStats => CustomTemplateCount > 0;
        public bool HasRadiusChanges { get; private set; }
        private struct TunedConfigs { public CombatConfig combat; public MovementConfig movement; public FlockingConfig flocking; }
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        public WarSandboxDeploymentInstance(WarSandboxDeploymentEntry[] entries) : this(entries, null) { }

        /// <summary>
        /// stats == null or no effective change keeps the template's authored Combat/Movement assets
        /// (byte-for-byte legacy behaviour). Otherwise those two sub-configs are cloned once per
        /// template and receive the resolved values; source assets are never written.
        /// </summary>
        public WarSandboxDeploymentInstance(WarSandboxDeploymentEntry[] entries, WarSandboxStatResolver stats)
        {
            var tuned = new Dictionary<UnitTypeConfig, TunedConfigs>();
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
                    if (stats != null)
                    {
                        if (!tuned.TryGetValue(e.template, out var configs))
                        {
                            configs = default;
                            if (stats.TryGetEffectiveChanges(e.template, out var changes)) { configs = Tune(e.template, changes); CustomTemplateCount++; }
                            tuned.Add(e.template, configs);
                        }
                        if (configs.combat != null) unit.combatConfig = configs.combat;
                        if (configs.movement != null) unit.movementConfig = configs.movement;
                        if (configs.flocking != null) unit.flockingConfig = configs.flocking;
                    }
                    spawn.unitCount = e.count; spawn.spawnCenter = e.center;
                    spawn.formationDensity = e.density; spawn.formationAspect = e.aspect; spawn.spawnSize = e.manualSize;
                    spawn.facingYawDegrees = WarSandboxDeploymentFacing.ResolveYaw(entries, i);
                    if (e.brush != null)
                    {
                        // Brush deployment: explicit positions inside the painted cells (front first, 2r clearance kept).
                        float radius = unit.flockingConfig != null ? Mathf.Max(.05f, unit.flockingConfig.agentRadius) : .45f;
                        spawn.spawnSize = e.Size;
                        spawn.customSpawnOffsets = e.brush.Offsets(e.count, spawn.facingYawDegrees, WarSandboxBrushMask.Spacing(e.density, radius), radius, i * 7919 + e.teamId * 104729);
                    }
                    Scenario.unitTypes[i] = unit;
                }
            }
            catch { Dispose(); throw; }
        }

        private TunedConfigs Tune(UnitTypeConfig template, WarSandboxStatSet changes)
        {
            CombatConfig combat = null; MovementConfig movement = null; FlockingConfig flocking = null;
            foreach (var pair in changes.Values)
            {
                var definition = WarSandboxUnitStats.Get(pair.Key);
                if (definition.UsesFlocking)
                {
                    if (flocking == null)
                    { flocking = Own(UnityEngine.Object.Instantiate(template.flockingConfig)); flocking.name = template.flockingConfig.name + " (Tuned radius)"; }
                    HasRadiusChanges = true;
                }
                else if (definition.UsesMovement)
                {
                    if (movement == null)
                    {
                        movement = Own(UnityEngine.Object.Instantiate(template.movementConfig));
                        movement.name = template.movementConfig.name + " (Tuned)";
                    }
                }
                else if (combat == null)
                {
                    combat = Own(UnityEngine.Object.Instantiate(template.combatConfig));
                    combat.name = template.combatConfig.name + " (Tuned)";
                }
                definition.Write(combat, movement, pair.Value, flocking);
            }
            return new TunedConfigs { combat = combat, movement = movement, flocking = flocking };
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



