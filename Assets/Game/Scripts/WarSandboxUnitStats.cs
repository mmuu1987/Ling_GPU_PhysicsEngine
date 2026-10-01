using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Player-tunable unit statistics. Keys are persisted; never rename an existing key.</summary>
    public enum WarSandboxUnitStat
    {
        MaxHp, AttackDamage, AttackInterval, AttackRange, TargetAcquireRadius, MaxSpeed,
        ChargeDamageMultiplier, ChargeMinSpeedFraction, ProjectileRange, ProjectileSpeed, ProjectileSplashRadius
    }

    public enum WarSandboxStatRole { All, MeleeOnly, RangedOnly }
    public enum WarSandboxStatSource { Official, Global, Local }

    public sealed class WarSandboxStatDefinition
    {
        public WarSandboxUnitStat Stat { get; }
        public string Key { get; }
        public string Label { get; }
        public string Unit { get; }
        public string Group { get; }
        public float Min { get; }
        public float Max { get; }
        public bool Integer { get; }
        public WarSandboxStatRole Role { get; }
        public bool UsesMovement { get; }
        private readonly Func<CombatConfig, MovementConfig, float> read;
        private readonly Action<CombatConfig, MovementConfig, float> write;

        internal WarSandboxStatDefinition(WarSandboxUnitStat stat, string key, string label, string unit, string group,
            float min, float max, bool integer, WarSandboxStatRole role, bool usesMovement,
            Func<CombatConfig, MovementConfig, float> read, Action<CombatConfig, MovementConfig, float> write)
        {
            Stat = stat; Key = key; Label = label; Unit = unit; Group = group; Min = min; Max = max; Integer = integer;
            Role = role; UsesMovement = usesMovement; this.read = read; this.write = write;
        }

        /// <summary>Limits a player value to the supported range. Non-finite input collapses to Min.</summary>
        public float Clamp(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Min;
            value = Mathf.Clamp(value, Min, Max);
            return Integer ? Mathf.Round(value) : value;
        }

        /// <summary>Slider/keyboard precision: integers stay whole, small values keep two decimals.</summary>
        public float Step(float value)
        {
            if (Integer) return Clamp(Mathf.Round(value));
            float digits = Mathf.Abs(value) < 10 ? 100 : Mathf.Abs(value) < 100 ? 10 : 1;
            return Clamp(Mathf.Round(value * digits) / digits);
        }

        public string Format(float value) => Integer
            ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

        internal float Read(UnitTypeConfig template) => read(template.combatConfig, template.movementConfig);
        internal void Write(CombatConfig combat, MovementConfig movement, float value) => write(combat, movement, value);
    }

    /// <summary>Field table, applicability and runtime-copy construction for unit stat overrides.</summary>
    public static class WarSandboxUnitStats
    {
        public const string GroupCore = "生存与攻击";
        public const string GroupMove = "机动";
        public const string GroupCharge = "冲锋 · 近战";
        public const string GroupProjectile = "弹道 · 远程";

        public static readonly WarSandboxStatDefinition[] Definitions =
        {
            new WarSandboxStatDefinition(WarSandboxUnitStat.MaxHp, "maxHp", "生命值", "", GroupCore, 1, 100000, true, WarSandboxStatRole.All, false,
                (c, m) => c.maxHp, (c, m, v) => c.maxHp = Mathf.RoundToInt(v)),
            new WarSandboxStatDefinition(WarSandboxUnitStat.AttackDamage, "attackDamage", "攻击伤害", "", GroupCore, 1, 10000, true, WarSandboxStatRole.All, false,
                (c, m) => c.attackDamage, (c, m, v) => c.attackDamage = Mathf.RoundToInt(v)),
            new WarSandboxStatDefinition(WarSandboxUnitStat.AttackInterval, "attackInterval", "攻击间隔", "秒", GroupCore, 0.1f, 10, false, WarSandboxStatRole.All, false,
                (c, m) => c.attackInterval, (c, m, v) => c.attackInterval = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.AttackRange, "attackRange", "攻击距离", "米", GroupCore, 0.5f, 60, false, WarSandboxStatRole.All, false,
                (c, m) => c.attackRange, (c, m, v) => c.attackRange = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.TargetAcquireRadius, "targetAcquireRadius", "索敌半径", "米", GroupCore, 0.5f, 120, false, WarSandboxStatRole.All, false,
                (c, m) => c.targetAcquireRadius, (c, m, v) => c.targetAcquireRadius = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.MaxSpeed, "maxSpeed", "移动速度", "米/秒", GroupMove, 0.1f, 20, false, WarSandboxStatRole.All, true,
                (c, m) => m.maxSpeed, (c, m, v) => m.maxSpeed = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.ChargeDamageMultiplier, "chargeDamageMultiplier", "冲锋倍率", "×", GroupCharge, 1, 5, false, WarSandboxStatRole.MeleeOnly, false,
                (c, m) => c.chargeDamageMultiplier, (c, m, v) => c.chargeDamageMultiplier = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.ChargeMinSpeedFraction, "chargeMinSpeedFraction", "冲锋起速", "×移速", GroupCharge, 0.1f, 1.5f, false, WarSandboxStatRole.MeleeOnly, false,
                (c, m) => c.chargeMinSpeedFraction, (c, m, v) => c.chargeMinSpeedFraction = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.ProjectileRange, "projectileRange", "弹道射程", "米", GroupProjectile, 1, 80, false, WarSandboxStatRole.RangedOnly, false,
                (c, m) => c.projectileRange, (c, m, v) => c.projectileRange = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.ProjectileSpeed, "projectileSpeed", "弹道速度", "米/秒", GroupProjectile, 1, 100, false, WarSandboxStatRole.RangedOnly, false,
                (c, m) => c.projectileSpeed, (c, m, v) => c.projectileSpeed = v),
            new WarSandboxStatDefinition(WarSandboxUnitStat.ProjectileSplashRadius, "projectileSplashRadius", "溅射半径", "米", GroupProjectile, 0, 20, false, WarSandboxStatRole.RangedOnly, false,
                (c, m) => c.projectileSplashRadius, (c, m, v) => c.projectileSplashRadius = v),
        };

        public static WarSandboxStatDefinition Get(WarSandboxUnitStat stat) => Definitions[(int)stat];

        public static bool TryGet(string key, out WarSandboxStatDefinition definition)
        {
            foreach (var candidate in Definitions)
                if (string.Equals(candidate.Key, key, StringComparison.Ordinal)) { definition = candidate; return true; }
            definition = null; return false;
        }

        /// <summary>A unit is ranged when its authored projectileRange is positive (the engine's own switch).</summary>
        public static bool IsRanged(UnitTypeConfig template) =>
            template != null && template.combatConfig != null && template.combatConfig.projectileRange > 0;

        public static bool Applies(UnitTypeConfig template, WarSandboxStatDefinition definition)
        {
            if (template == null || definition == null) return false;
            if (definition.UsesMovement) return template.movementConfig != null;
            if (template.combatConfig == null) return false;
            bool ranged = IsRanged(template);
            return definition.Role == WarSandboxStatRole.All ||
                (definition.Role == WarSandboxStatRole.RangedOnly && ranged) ||
                (definition.Role == WarSandboxStatRole.MeleeOnly && !ranged);
        }

        public static bool TryGetOfficial(UnitTypeConfig template, WarSandboxStatDefinition definition, out float value)
        {
            value = 0;
            if (!Applies(template, definition)) return false;
            value = definition.Read(template); return true;
        }

        public static bool Same(float a, float b) => Mathf.Abs(a - b) <= 0.0001f * Mathf.Max(1f, Mathf.Abs(a), Mathf.Abs(b));
    }

    /// <summary>Sparse stat values for one template. Values are always stored clamped.</summary>
    public sealed class WarSandboxStatSet
    {
        private readonly SortedDictionary<WarSandboxUnitStat, float> values = new SortedDictionary<WarSandboxUnitStat, float>();
        public int Count => values.Count;
        public IEnumerable<KeyValuePair<WarSandboxUnitStat, float>> Values => values;
        public bool TryGet(WarSandboxUnitStat stat, out float value) => values.TryGetValue(stat, out value);
        public bool Contains(WarSandboxUnitStat stat) => values.ContainsKey(stat);

        public bool Set(WarSandboxUnitStat stat, float value)
        {
            value = WarSandboxUnitStats.Get(stat).Clamp(value);
            if (values.TryGetValue(stat, out float old) && old == value) return false;
            values[stat] = value; return true;
        }

        public bool Remove(WarSandboxUnitStat stat) => values.Remove(stat);

        public WarSandboxStatSet Clone()
        {
            var copy = new WarSandboxStatSet();
            foreach (var pair in values) copy.values.Add(pair.Key, pair.Value);
            return copy;
        }

        public bool SameAs(WarSandboxStatSet other)
        {
            if (other == null) return values.Count == 0;
            if (other.values.Count != values.Count) return false;
            foreach (var pair in values)
                if (!other.values.TryGetValue(pair.Key, out float value) || value != pair.Value) return false;
            return true;
        }

        public WarSandboxStatValue[] ToContract() =>
            values.Select(pair => new WarSandboxStatValue { stat = WarSandboxUnitStats.Get(pair.Key).Key, value = pair.Value }).ToArray();

        /// <summary>Unknown keys (from a newer game) and non-finite numbers are skipped, never fatal.</summary>
        public static WarSandboxStatSet FromContract(WarSandboxStatValue[] source, out int skipped)
        {
            var set = new WarSandboxStatSet(); skipped = 0;
            if (source == null) return set;
            foreach (var item in source)
            {
                if (item == null || !WarSandboxUnitStats.TryGet(item.stat, out var definition) ||
                    float.IsNaN(item.value) || float.IsInfinity(item.value)) { skipped++; continue; }
                set.Set(definition.Stat, item.value);
            }
            return set;
        }
    }

    /// <summary>Local (per plan/draft) overrides keyed by template asset. One set per template by design.</summary>
    public sealed class WarSandboxStatOverrides
    {
        private readonly Dictionary<UnitTypeConfig, WarSandboxStatSet> sets = new Dictionary<UnitTypeConfig, WarSandboxStatSet>();
        public IEnumerable<UnitTypeConfig> Templates => sets.Keys;
        public bool IsEmpty => sets.Count == 0;

        public WarSandboxStatSet Get(UnitTypeConfig template) =>
            template != null && sets.TryGetValue(template, out var set) ? set.Clone() : new WarSandboxStatSet();

        public bool TryGet(UnitTypeConfig template, WarSandboxUnitStat stat, out float value)
        {
            value = 0;
            return template != null && sets.TryGetValue(template, out var set) && set.TryGet(stat, out value);
        }

        public bool Set(UnitTypeConfig template, WarSandboxUnitStat stat, float value)
        {
            if (template == null) return false;
            if (!sets.TryGetValue(template, out var set)) sets[template] = set = new WarSandboxStatSet();
            return set.Set(stat, value);
        }

        public bool Remove(UnitTypeConfig template, WarSandboxUnitStat stat)
        {
            if (template == null || !sets.TryGetValue(template, out var set) || !set.Remove(stat)) return false;
            if (set.Count == 0) sets.Remove(template);
            return true;
        }

        public bool RemoveTemplate(UnitTypeConfig template) => template != null && sets.Remove(template);

        public void Replace(UnitTypeConfig template, WarSandboxStatSet set)
        {
            if (template == null) return;
            if (set == null || set.Count == 0) sets.Remove(template); else sets[template] = set.Clone();
        }

        public WarSandboxStatOverrides Clone()
        {
            var copy = new WarSandboxStatOverrides();
            foreach (var pair in sets) copy.sets.Add(pair.Key, pair.Value.Clone());
            return copy;
        }

        public bool SameAs(WarSandboxStatOverrides other)
        {
            if (other == null) return sets.Count == 0;
            if (other.sets.Count != sets.Count) return false;
            foreach (var pair in sets)
                if (!other.sets.TryGetValue(pair.Key, out var set) || !set.SameAs(pair.Value)) return false;
            return true;
        }

        /// <summary>Keeps only templates still present in the deployment (used before saving a plan).</summary>
        public WarSandboxStatOverrides For(IEnumerable<UnitTypeConfig> used)
        {
            var keep = new HashSet<UnitTypeConfig>(used.Where(t => t != null));
            var copy = new WarSandboxStatOverrides();
            foreach (var pair in sets) if (keep.Contains(pair.Key)) copy.sets.Add(pair.Key, pair.Value.Clone());
            return copy;
        }
    }

    /// <summary>Global overrides keyed by stable template id (shared by every battlefield and plan).</summary>
    public sealed class WarSandboxGlobalStats
    {
        private readonly SortedDictionary<string, WarSandboxStatSet> sets = new SortedDictionary<string, WarSandboxStatSet>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> revisions = new Dictionary<string, int>(StringComparer.Ordinal);
        public string LastChange { get; set; }
        public string LastChangeUtc { get; set; }
        public int SkippedValues { get; private set; }
        public IEnumerable<string> TemplateIds => sets.Keys;
        public bool IsEmpty => sets.Count == 0;
        public int Count => sets.Count;

        public WarSandboxStatSet Get(string templateId) =>
            templateId != null && sets.TryGetValue(templateId, out var set) ? set.Clone() : new WarSandboxStatSet();

        public bool TryGet(string templateId, WarSandboxUnitStat stat, out float value)
        {
            value = 0;
            return templateId != null && sets.TryGetValue(templateId, out var set) && set.TryGet(stat, out value);
        }

        public int RevisionOf(string templateId) => templateId != null && revisions.TryGetValue(templateId, out int r) ? r : 0;

        public void Replace(string templateId, int revision, WarSandboxStatSet set)
        {
            if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentException("templateId");
            if (set == null || set.Count == 0) { sets.Remove(templateId); revisions.Remove(templateId); return; }
            sets[templateId] = set.Clone(); revisions[templateId] = revision;
        }

        /// <summary>Writes only the given fields; other global fields of the template are kept.</summary>
        public void Merge(string templateId, int revision, WarSandboxStatSet changes)
        {
            var merged = Get(templateId);
            foreach (var pair in changes.Values) merged.Set(pair.Key, pair.Value);
            Replace(templateId, revision, merged);
        }

        public bool Remove(string templateId) { revisions.Remove(templateId ?? ""); return templateId != null && sets.Remove(templateId); }

        public WarSandboxGlobalStats Clone()
        {
            var copy = new WarSandboxGlobalStats { LastChange = LastChange, LastChangeUtc = LastChangeUtc };
            foreach (var pair in sets) copy.Replace(pair.Key, RevisionOf(pair.Key), pair.Value);
            return copy;
        }

        public WarSandboxUnitOverrideFile ToContract() => new WarSandboxUnitOverrideFile
        {
            schemaVersion = WarSandboxUnitOverrideFile.CurrentSchema,
            templates = sets.Select(pair => new WarSandboxTemplateStats
            {
                valuesVersion = 1, templateId = pair.Key, templateRevision = Mathf.Max(1, RevisionOf(pair.Key)), stats = pair.Value.ToContract()
            }).ToArray(),
            lastChange = LastChange, lastChangeUtc = LastChangeUtc
        };

        public static bool TryFromContract(WarSandboxUnitOverrideFile file, out WarSandboxGlobalStats stats, out string error)
        {
            stats = null; error = null;
            if (file == null) error = "全局兵种数值文件为空。";
            else if (file.schemaVersion != WarSandboxUnitOverrideFile.CurrentSchema) error = "全局兵种数值文件版本不支持：" + file.schemaVersion;
            else if (file.templates == null) error = "全局兵种数值文件缺少兵种列表。";
            else if (file.templates.Length > WarSandboxTemplateStats.MaxTemplates) error = "全局兵种数值文件兵种数量超出上限。";
            if (error != null) return false;
            var result = new WarSandboxGlobalStats { LastChange = file.lastChange, LastChangeUtc = file.lastChangeUtc };
            foreach (var item in file.templates)
            {
                if (!WarSandboxTemplateStats.TryValidate(item, out error)) return false;
                if (result.sets.ContainsKey(item.templateId)) { error = "全局兵种数值文件包含重复兵种：" + item.templateId; return false; }
                var set = WarSandboxStatSet.FromContract(item.stats, out int skipped);
                result.SkippedValues += skipped;
                result.Replace(item.templateId, item.templateRevision, set);
            }
            stats = result; return true;
        }
    }

    /// <summary>Resolves the effective value of each field: local (plan) &gt; global &gt; official, clamped per field.</summary>
    public sealed class WarSandboxStatResolver
    {
        public struct Value
        {
            public bool applies;
            public float official, effective;
            public bool hasGlobal, hasLocal;
            public float global, local;
            public WarSandboxStatSource source;
            public bool Customized => applies && !WarSandboxUnitStats.Same(effective, official);
        }

        private readonly WarSandboxBattlefieldCatalog catalog;
        public WarSandboxGlobalStats Global { get; }
        public WarSandboxStatOverrides Local { get; }

        public WarSandboxStatResolver(WarSandboxBattlefieldCatalog catalog, WarSandboxGlobalStats global, WarSandboxStatOverrides local)
        { this.catalog = catalog; Global = global; Local = local; }

        public string TemplateId(UnitTypeConfig template) =>
            catalog != null && catalog.TryGetTemplateId(template, out string id, out _) ? id : null;

        public Value Resolve(UnitTypeConfig template, WarSandboxStatDefinition definition)
        {
            var result = new Value();
            if (!WarSandboxUnitStats.TryGetOfficial(template, definition, out result.official)) return result;
            result.applies = true; result.effective = result.official; result.source = WarSandboxStatSource.Official;
            string id = Global != null ? TemplateId(template) : null;
            if (id != null && Global.TryGet(id, definition.Stat, out result.global))
            { result.hasGlobal = true; result.effective = definition.Clamp(result.global); result.source = WarSandboxStatSource.Global; }
            if (Local != null && Local.TryGet(template, definition.Stat, out result.local))
            { result.hasLocal = true; result.effective = definition.Clamp(result.local); result.source = WarSandboxStatSource.Local; }
            return result;
        }

        /// <summary>Fields whose effective value differs from the official asset. False = run the official asset untouched.</summary>
        public bool TryGetEffectiveChanges(UnitTypeConfig template, out WarSandboxStatSet changes)
        {
            changes = new WarSandboxStatSet();
            foreach (var definition in WarSandboxUnitStats.Definitions)
            {
                var value = Resolve(template, definition);
                if (value.Customized) changes.Set(definition.Stat, value.effective);
            }
            return changes.Count > 0;
        }

        public bool HasCustom(UnitTypeConfig template) => TryGetEffectiveChanges(template, out _);

        public bool HasLocal(UnitTypeConfig template) => Local != null && Local.Get(template).Count > 0;
    }

    [Serializable, DataContract]
    public sealed class WarSandboxStatValue
    {
        [DataMember(IsRequired = true)]
        public string stat;
        [DataMember(IsRequired = true)]
        public float value;
    }

    [Serializable, DataContract]
    public sealed class WarSandboxTemplateStats
    {
        public const int MaxTemplates = 256;
        public const int MaxStats = 64;
        [DataMember(IsRequired = true)]
        public int valuesVersion;
        [DataMember(IsRequired = true)]
        public string templateId;
        [DataMember(IsRequired = true)]
        public int templateRevision;
        [DataMember(IsRequired = true)]
        public WarSandboxStatValue[] stats;

        public static bool TryValidate(WarSandboxTemplateStats item, out string error)
        {
            error = null;
            if (item == null || item.valuesVersion != 1 || string.IsNullOrWhiteSpace(item.templateId) ||
                item.templateId != item.templateId.Trim() || item.templateId.Length > 128 || item.templateRevision <= 0 || item.stats == null)
                error = "兵种数值条目不完整。";
            else if (item.stats.Length > MaxStats) error = "兵种数值条目字段过多：" + item.templateId;
            return error == null;
        }
    }

    [Serializable, DataContract]
    public sealed class WarSandboxUnitOverrideFile
    {
        public const int CurrentSchema = 1;
        [DataMember(IsRequired = true)]
        public int schemaVersion;
        [DataMember(IsRequired = true)]
        public WarSandboxTemplateStats[] templates;
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public string lastChange;
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public string lastChangeUtc;
    }
}
