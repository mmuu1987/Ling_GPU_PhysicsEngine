using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using UnityEngine;

namespace MassEngine.Game
{
    [Serializable, DataContract]
    public sealed class WarSandboxPlanFile
    {
        [DataMember(IsRequired = true)]
        public int schemaVersion;
        [DataMember(IsRequired = true)]
        public string planId;
        [DataMember(IsRequired = true)]
        public string displayName;
        [DataMember(IsRequired = true)]
        public string battlefieldId;
        [DataMember(IsRequired = true)]
        public int battlefieldVersion;
        [DataMember(IsRequired = true)]
        public string terrainId;
        [DataMember(IsRequired = true)]
        public int terrainVersion;
        [DataMember(IsRequired = true)]
        public float worldWidth;
        [DataMember(IsRequired = true)]
        public float worldDepth;
        [DataMember(IsRequired = true)]
        public float boundaryPadding;
        [DataMember(IsRequired = true)]
        public WarSandboxPlanRules rules;
        [DataMember(IsRequired = true)]
        public WarSandboxPlanEntry[] entries;
        /// <summary>Optional local unit stat overrides (one set per template id). Absent in older plans;
        /// omitted when empty so plans without overrides stay byte-identical to schema 1.</summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false)]
        public WarSandboxTemplateStats[] statOverrides;
    }

    [Serializable, DataContract]
    public sealed class WarSandboxPlanRules
    {
        [DataMember(IsRequired = true)]
        public int valuesVersion;
        [DataMember(IsRequired = true)]
        public WarSandboxGameMode gameMode;
        [DataMember(IsRequired = true)]
        public WarSandboxPlanVector3 controlPointCenter;
        [DataMember(IsRequired = true)]
        public float controlPointRadius;
        [DataMember(IsRequired = true)]
        public float controlPointCaptureSeconds;
        [DataMember(IsRequired = true)]
        public bool staticObstaclesEnabled;
        [DataMember(IsRequired = true)]
        public float staticObstacleClearance;
        [DataMember(IsRequired = true)]
        public WarSandboxPlanObstacle[] staticObstacles;

        public static WarSandboxPlanRules From(WarSandboxBattlefieldRules source)
        {
            var obstacles = source.staticObstacles ?? new StaticObstacleRect[0];
            return new WarSandboxPlanRules
            {
                valuesVersion = 1, gameMode = source.gameMode,
                controlPointCenter = WarSandboxPlanVector3.From(source.controlPointCenter),
                controlPointRadius = source.controlPointRadius,
                controlPointCaptureSeconds = source.controlPointCaptureSeconds,
                staticObstaclesEnabled = source.staticObstaclesEnabled,
                staticObstacleClearance = source.staticObstacleClearance,
                staticObstacles = obstacles.Select(WarSandboxPlanObstacle.From).ToArray()
            };
        }

        public bool TryToRuntime(out WarSandboxBattlefieldRules result, out string error)
        {
            result = default;
            error = null;
            if (valuesVersion != 1) error = "方案中的战场规则版本不支持。";
            else if (controlPointCenter == null || controlPointCenter.valuesVersion != 1) error = "方案缺少有效的据点坐标。";
            else if (staticObstacles == null) error = "方案缺少障碍布局。";
            else if (staticObstacles.Length > StaticObstacleMath.MaxObstacleCount) error = "方案障碍数量超出上限。";
            else if (gameMode != WarSandboxGameMode.Annihilation && gameMode != WarSandboxGameMode.ControlPoint)
                error = "方案中的战斗模式无效。";
            if (error != null) return false;
            var obstacles = new StaticObstacleRect[staticObstacles.Length];
            for (int i = 0; i < obstacles.Length; i++)
            {
                if (staticObstacles[i] == null) { error = "方案包含空障碍。"; return false; }
                if (!staticObstacles[i].TryToRuntime(out obstacles[i], out error)) return false;
            }
            result = new WarSandboxBattlefieldRules
            {
                gameMode = gameMode, controlPointCenter = controlPointCenter.ToRuntime(),
                controlPointRadius = controlPointRadius, controlPointCaptureSeconds = controlPointCaptureSeconds,
                staticObstaclesEnabled = staticObstaclesEnabled, staticObstacleClearance = staticObstacleClearance,
                staticObstacles = obstacles
            };
            return result.TryValidate(out error);
        }
    }

    [Serializable, DataContract]
    public sealed class WarSandboxPlanEntry
    {
        [DataMember(IsRequired = true)]
        public int valuesVersion;
        [DataMember(IsRequired = true)]
        public string templateId;
        [DataMember(IsRequired = true)]
        public int templateRevision;
        [DataMember(IsRequired = true)]
        public int teamId;
        [DataMember(IsRequired = true)]
        public int count;
        [DataMember(IsRequired = true)]
        public WarSandboxPlanVector3 center;
        [DataMember(IsRequired = true)]
        public float density;
        [DataMember(IsRequired = true)]
        public float aspect;
        [DataMember(IsRequired = true)]
        public WarSandboxPlanVector3 manualSize;
        /// <summary>Optional; omitted when 0 (auto) so older plans stay byte-identical and still load.</summary>
        [DataMember(EmitDefaultValue = false)]
        public int facing;

        public static WarSandboxPlanEntry From(WarSandboxDeploymentEntry source, string id, int revision)
        {
            return new WarSandboxPlanEntry
            {
                valuesVersion = 1, templateId = id, templateRevision = revision,
                teamId = source.teamId, count = source.count,
                center = WarSandboxPlanVector3.From(source.center), density = source.density,
                aspect = source.aspect, manualSize = WarSandboxPlanVector3.From(source.manualSize), facing = source.facing
            };
        }

        public bool TryToRuntime(UnitTypeConfig template, out WarSandboxDeploymentEntry result, out string error)
        {
            result = default; error = null;
            if (valuesVersion != 1) error = "方案中的编成数据版本不支持。";
            else if (string.IsNullOrWhiteSpace(templateId) || templateRevision <= 0) error = "方案中的兵种模板引用无效。";
            else if (center == null || manualSize == null) error = "方案中的部署坐标缺失。";
            else if (template == null) error = "方案引用的兵种模板不存在：" + templateId;
            if (error != null) return false;
            result = new WarSandboxDeploymentEntry
            {
                template = template, teamId = teamId, count = count, center = center.ToRuntime(),
                density = density, aspect = aspect, manualSize = manualSize.ToRuntime(), facing = facing
            };
            return true;
        }
    }

    [Serializable, DataContract]
    public sealed class WarSandboxPlanVector3
    {
        [DataMember(IsRequired = true)]
        public int valuesVersion;
        [DataMember(IsRequired = true)]
        public float x, y, z;
        public static WarSandboxPlanVector3 From(Vector3 value) => new WarSandboxPlanVector3
        { valuesVersion = 1, x = value.x, y = value.y, z = value.z };
        public Vector3 ToRuntime() => new Vector3(x, y, z);
    }

    [Serializable, DataContract]
    public sealed class WarSandboxPlanObstacle
    {
        [DataMember(IsRequired = true)]
        public int valuesVersion;
        [DataMember(IsRequired = true)]
        public float centerX, centerY, sizeX, sizeY;
        public static WarSandboxPlanObstacle From(StaticObstacleRect value) => new WarSandboxPlanObstacle
        { valuesVersion = 1, centerX = value.center.x, centerY = value.center.y, sizeX = value.size.x, sizeY = value.size.y };
        public bool TryToRuntime(out StaticObstacleRect result, out string error)
        {
            result = default; error = null;
            if (valuesVersion != 1) error = "方案中的障碍数据版本不支持。";
            else if (!Finite(centerX) || !Finite(centerY) || !Finite(sizeX) || !Finite(sizeY) || sizeX <= 0.01f || sizeY <= 0.01f)
                error = "方案中的障碍尺寸无效。";
            result = new StaticObstacleRect(new Vector2(centerX, centerY), new Vector2(sizeX, sizeY));
            return error == null;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class WarSandboxLocalPlanStore
    {
        public const int SchemaVersion = 1;
        public const int MaxPlanBytes = 2 * 1024 * 1024;
        public const int MaxSlotLength = 32;
        private readonly string directory;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public WarSandboxLocalPlanStore() : this(Path.Combine(Application.persistentDataPath, "WarSandboxPlans")) { }
        public WarSandboxLocalPlanStore(string directoryPath) { directory = directoryPath; }
        public string DirectoryPath => directory;

        public bool TrySave(WarSandboxPlanFile plan, bool overwrite, out string error)
        {
            error = null;
            if (!TryValidateSlot(plan != null ? plan.planId : null, out error)) return false;
            if (!TryValidatePlan(plan, out error)) return false;
            string temp = null;
            try
            {
                string path = GetPath(plan.planId);
                if (File.Exists(path) && !overwrite) { error = "方案已存在，请确认覆盖：" + plan.planId; return false; }
                temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(directory);
                byte[] bytes;
                using (var json = new MemoryStream())
                { new DataContractJsonSerializer(typeof(WarSandboxPlanFile)).WriteObject(json, plan); bytes = json.ToArray(); }
                if (bytes.Length > MaxPlanBytes) { error = "方案文件超过大小限制。"; return false; }
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (overwrite && File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (Exception exception) { error = "方案保存失败：" + exception.Message; return false; }
            finally { TryDelete(temp); }
        }

        public bool TryLoad(string slot, out WarSandboxPlanFile plan, out string error)
        {
            plan = null; error = null;
            if (!TryValidateSlot(slot, out error)) return false;
            try
            {
                string path = GetPath(slot);
                if (!File.Exists(path)) { error = "找不到本地方案：" + slot; return false; }
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length <= 0 || file.Length > MaxPlanBytes) { error = "方案文件大小无效：" + slot; return false; }
                    var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 16, MaxStringContentLength = MaxPlanBytes,
                        MaxArrayLength = MaxPlanBytes, MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384 };
                    using (var reader = JsonReaderWriterFactory.CreateJsonReader(file, Utf8, quotas, null))
                    {
                        plan = (WarSandboxPlanFile)new DataContractJsonSerializer(typeof(WarSandboxPlanFile)).ReadObject(reader);
                    }
                }
                if (!TryValidatePlan(plan, out error)) { plan = null; return false; }
                if (!string.Equals(plan.planId, slot, StringComparison.OrdinalIgnoreCase))
                { plan = null; error = "方案编号与文件名不一致。"; return false; }
                return true;
            }
            catch (Exception exception) { plan = null; error = "方案读取失败：" + exception.Message; return false; }
        }

        public string[] ListSlots()
        {
            TryListSlots(out var slots, out _); return slots;
        }

        public bool TryListSlots(out string[] slots, out string error)
        {
            slots = new string[0]; error = null;
            try
            {
                if (File.Exists(directory)) throw new IOException("方案目录被同名文件占用。");
                if (!Directory.Exists(directory)) return true;
                slots = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension).Where(IsValidSlot).OrderBy(value => value, StringComparer.Ordinal).ToArray();
                return true;
            }
            catch (Exception exception) { error = "方案目录读取失败：" + exception.Message; return false; }
        }

        public bool Exists(string slot)
        { try { return TryValidateSlot(slot, out _) && File.Exists(GetPath(slot)); } catch { return false; } }

        public static WarSandboxPlanFile Create(string slot, string displayName, string battlefieldId, int battlefieldVersion,
            string terrainId, int terrainVersion, Vector2 worldSize, float boundaryPadding,
            WarSandboxBattlefieldRules rules, WarSandboxDeploymentEntry[] entries, WarSandboxBattlefieldCatalog catalog, out string error) =>
            Create(slot, displayName, battlefieldId, battlefieldVersion, terrainId, terrainVersion, worldSize, boundaryPadding,
                rules, entries, catalog, null, out error);

        public static WarSandboxPlanFile Create(string slot, string displayName, string battlefieldId, int battlefieldVersion,
            string terrainId, int terrainVersion, Vector2 worldSize, float boundaryPadding,
            WarSandboxBattlefieldRules rules, WarSandboxDeploymentEntry[] entries, WarSandboxBattlefieldCatalog catalog,
            WarSandboxStatOverrides stats, out string error)
        {
            error = null;
            if (!TryValidateSlot(slot, out error)) return null;
            if (catalog == null || catalog.templates == null) { error = "兵种模板目录缺失。"; return null; }
            var saved = new WarSandboxPlanEntry[entries != null ? entries.Length : 0];
            for (int i = 0; i < saved.Length; i++)
            {
                if (!catalog.TryGetTemplateId(entries[i].template, out string id, out int revision))
                { error = "兵种模板未注册稳定编号：" + (entries[i].template != null ? entries[i].template.name : "Missing"); return null; }
                saved[i] = WarSandboxPlanEntry.From(entries[i], id, revision);
            }
            WarSandboxTemplateStats[] savedStats = null;
            if (stats != null && entries != null)
            {
                var used = stats.For(entries.Select(e => e.template));
                var list = new System.Collections.Generic.List<WarSandboxTemplateStats>();
                foreach (var template in used.Templates.OrderBy(t => t.name, StringComparer.Ordinal))
                {
                    var set = used.Get(template);
                    if (set.Count == 0 || !catalog.TryGetTemplateId(template, out string id, out int revision)) continue;
                    list.Add(new WarSandboxTemplateStats { valuesVersion = 1, templateId = id, templateRevision = revision, stats = set.ToContract() });
                }
                if (list.Count > 0) savedStats = list.OrderBy(t => t.templateId, StringComparer.Ordinal).ToArray();
            }
            return new WarSandboxPlanFile
            {
                schemaVersion = SchemaVersion, planId = slot, displayName = string.IsNullOrWhiteSpace(displayName) ? slot : displayName.Trim(),
                battlefieldId = battlefieldId, battlefieldVersion = battlefieldVersion, terrainId = terrainId,
                terrainVersion = terrainVersion, worldWidth = worldSize.x, worldDepth = worldSize.y,
                boundaryPadding = boundaryPadding, rules = WarSandboxPlanRules.From(rules), entries = saved,
                statOverrides = savedStats
            };
        }

        public static bool TryValidatePlan(WarSandboxPlanFile plan, out string error)
        {
            error = null;
            if (plan == null) error = "方案内容为空。";
            else if (plan.schemaVersion != SchemaVersion) error = "方案格式版本不支持：" + plan.schemaVersion;
            else if (!IsValidSlot(plan.planId)) error = "方案编号无效。";
            else if (string.IsNullOrWhiteSpace(plan.displayName) || plan.displayName.Length > 80) error = "方案名称无效。";
            else if (string.IsNullOrWhiteSpace(plan.battlefieldId) || plan.battlefieldVersion <= 0) error = "方案战场引用无效。";
            else if (string.IsNullOrWhiteSpace(plan.terrainId) || plan.terrainVersion <= 0) error = "方案地形引用无效。";
            else if (!Finite(plan.worldWidth) || !Finite(plan.worldDepth) || plan.worldWidth <= 0 || plan.worldDepth <= 0 ||
                !Finite(plan.boundaryPadding) || plan.boundaryPadding < 0) error = "方案空间契约无效。";
            else if (plan.rules == null) error = "方案缺少战场规则。";
            else if (!plan.rules.TryToRuntime(out _, out error)) return false;
            else if (plan.entries == null || plan.entries.Length < 1 || plan.entries.Length > WarSandboxDeploymentDraft.MaxCompositions)
                error = "方案编成数量无效。";
            if (error != null) return false;
            foreach (var entry in plan.entries)
            {
                if (entry == null || entry.valuesVersion != 1 || string.IsNullOrWhiteSpace(entry.templateId) || entry.templateRevision <= 0 ||
                    entry.center == null || entry.manualSize == null || entry.center.valuesVersion != 1 || entry.manualSize.valuesVersion != 1)
                { error = "方案包含不完整的编成数据。"; return false; }
                if (entry.teamId < 0 || entry.teamId > ConfigValidator.MaxTeamId || entry.count < 1 || entry.count > WarSandboxDeploymentDraft.MaxTotalUnits ||
                    !Finite(entry.center.ToRuntime()) || !Finite(entry.manualSize.ToRuntime()) ||
                    !Finite(entry.density) || entry.density < 0.05f || entry.density > SpawnConfig.PackingLimitPerSquareMeter ||
                    !Finite(entry.aspect) || entry.aspect < 0.1f || entry.aspect > 10f ||
                    entry.manualSize.x < 0 || entry.manualSize.y < 0 || entry.manualSize.z < 0 ||
                    ((entry.manualSize.x > 0) != (entry.manualSize.z > 0)) || !WarSandboxDeploymentFacing.IsValid(entry.facing))
                { error = "方案编成包含非法数值。"; return false; }
            }
            if (plan.entries.Sum(entry => (long)entry.count) > WarSandboxDeploymentDraft.MaxTotalUnits)
            { error = "方案总人数超出上限。"; return false; }
            if (plan.statOverrides != null)
            {
                if (plan.statOverrides.Length > WarSandboxDeploymentDraft.MaxCompositions) { error = "方案兵种数值条目过多。"; return false; }
                var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                foreach (var item in plan.statOverrides)
                {
                    if (!WarSandboxTemplateStats.TryValidate(item, out error)) { error = "方案" + error; return false; }
                    if (!ids.Add(item.templateId)) { error = "方案兵种数值重复：" + item.templateId; return false; }
                }
            }
            return true;
        }

        public static bool TryResolve(WarSandboxPlanFile plan, WarSandboxBattlefieldEntry battlefield,
            WarSandboxBattlefieldCatalog catalog, Vector2 world, float padding, out WarSandboxDeploymentDraft draft, out string error)
        {
            draft = null;
            if (!TryValidatePlan(plan, out error)) return false;
            if (battlefield == null || plan.battlefieldId != battlefield.id)
            { error = "请先进入此方案所属战场：" + plan.battlefieldId; return false; }
            if (plan.battlefieldVersion != battlefield.contentVersion)
            { error = "方案战场版本不兼容，当前布阵未改变。"; return false; }
            if (plan.terrainId != battlefield.terrainId || plan.terrainVersion != battlefield.terrainVersion)
            { error = "方案地形缺失或版本不兼容，不能替换为平面。"; return false; }
            if (plan.worldWidth != world.x || plan.worldDepth != world.y || plan.boundaryPadding != padding)
            { error = "方案战场尺寸或边界已改变，当前布阵未改变。"; return false; }
            if (!battlefield.TryValidateTerrain(out error)) return false;
            if (battlefield.terrainSurface != null)
            {
                if (!battlefield.terrainSurface.TryCreateSurface(out var surface, out error)) return false;
                if (surface.Origin != -world * .5f || surface.Size != world)
                { error = "方案地形提供者空间契约与战场不匹配。"; return false; }
            }
            if (catalog == null) { error = "兵种模板目录缺失。"; return false; }
            var entries = new WarSandboxDeploymentEntry[plan.entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var saved = plan.entries[i];
                if (!catalog.TryResolveTemplate(saved.templateId, saved.templateRevision, out var template, out error)) return false;
                if (!saved.TryToRuntime(template, out entries[i], out error)) return false;
            }
            if (!plan.rules.TryToRuntime(out var rules, out error)) return false;
            var stats = new WarSandboxStatOverrides();
            if (plan.statOverrides != null)
                foreach (var item in plan.statOverrides)
                {
                    // Overrides follow the template id; a template not deployed by this plan is ignored.
                    UnitTypeConfig template = null;
                    foreach (var entry in entries)
                        if (catalog.TryGetTemplateId(entry.template, out string id, out _) && id == item.templateId) { template = entry.template; break; }
                    if (template == null) continue;
                    foreach (var pair in WarSandboxStatSet.FromContract(item.stats, out _).Values)
                        if (WarSandboxUnitStats.Applies(template, WarSandboxUnitStats.Get(pair.Key))) stats.Set(template, pair.Key, pair.Value);
                }
            var candidate = new WarSandboxDeploymentDraft(entries, rules, stats);
            if (!candidate.TryValidate(world - Vector2.one * (padding * 2), rules, out error)) return false;
            draft = candidate; return true;
        }

        public static bool IsValidSlot(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxSlotLength || value[0] == '.' ||
                value[0] == '-' || value[value.Length - 1] == '.') return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            }
            string upper = value.ToUpperInvariant();
            if (upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL" ||
                (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) &&
                upper[3] >= '0' && upper[3] <= '9')) return false;
            return true;
        }

        private static bool TryValidateSlot(string value, out string error)
        { error = IsValidSlot(value) ? null : "方案编号只能使用字母、数字、下划线和短横线。"; return error == null; }
        private string GetPath(string slot) => Path.Combine(directory, slot + ".json");
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}
