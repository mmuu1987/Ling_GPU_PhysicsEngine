using System;
using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game
{
    [DisallowMultipleComponent]
    public sealed class WarSandboxRuntimeDeployment : MonoBehaviour
    {
        public WarSandboxBattleController controller;
        public WarSandboxBattlefieldCatalog battlefieldCatalog;
        [Tooltip("Optional battlefield-local selectable library. Null preserves the legacy Scenario-only roster.")]
        public WarSandboxRosterPolicy rosterPolicy;
        public bool UseTemplateSpawnDefaults => rosterPolicy != null && rosterPolicy.useTemplateSpawnDefaults;
        public bool IsEditing { get; private set; }
        public WarSandboxDeploymentDraft Draft { get; private set; }
        public string Error { get; private set; }
        public IReadOnlyList<UnitTypeConfig> Templates => templates;
        /// <summary>Deduplicated NEW-choice presentation only. Templates remains the full compatibility/validation set.</summary>
        public IReadOnlyList<UnitTypeConfig> ChoiceTemplates => choiceTemplates;
        public ScenarioConfig SourceScenario => sourceScenario;
        public Vector2 WorldSize => controller != null && controller.manager != null && controller.manager.systemConfig != null &&
            controller.manager.systemConfig.simulationConfig != null
            ? controller.manager.systemConfig.simulationConfig.simulationWorldSize : Vector2.zero;

        private readonly List<UnitTypeConfig> templates = new List<UnitTypeConfig>();
        private readonly List<UnitTypeConfig> choiceTemplates = new List<UnitTypeConfig>();
        private ScenarioConfig sourceScenario;
        private WarSandboxDeploymentEntry[] committed;
        private WarSandboxDeploymentInstance active;
        private bool dispatchBeforeEdit;
        private WarSandboxLocalPlanStore planStore;
        private WarSandboxUnitStatStore statStore;
        private WarSandboxStatOverrides committedStats = new WarSandboxStatOverrides();
        private readonly WarSandboxTerrainValidation terrainValidation = new WarSandboxTerrainValidation();
        /// <summary>Player-wide stat layer. Tests may substitute a store in a temporary directory.</summary>
        public WarSandboxUnitStatStore StatStore
        {
            get => statStore ?? (statStore = new WarSandboxUnitStatStore());
            set => statStore = value ?? throw new ArgumentNullException(nameof(value));
        }
        /// <summary>True while the running deployment uses at least one non-official unit stat.</summary>
        public bool HasCustomStats => active != null && active.HasCustomStats && controller != null &&
            controller.manager != null && controller.manager.scenarioConfig == active.Scenario;
        public int CustomTemplateCount => HasCustomStats ? active.CustomTemplateCount : 0;
        /// <summary>Local (plan) overrides of the applied deployment.</summary>
        public WarSandboxStatOverrides CommittedStats => committedStats.Clone();
        /// <summary>Resolver for the open draft: draft-local &gt; global &gt; official.</summary>
        public WarSandboxStatResolver DraftStats => new WarSandboxStatResolver(battlefieldCatalog, StatStore.Current, Draft != null ? Draft.Stats : committedStats);
        public WarSandboxLocalPlanStore PlanStore
        {
            get => planStore ?? (planStore = new WarSandboxLocalPlanStore());
            set => planStore = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static bool BlocksCommands(WarSandboxBattleController controller)
        {
            var editor = controller != null ? controller.GetComponent<WarSandboxRuntimeDeployment>() : null;
            return editor != null && editor.IsEditing;
        }

        private bool CanAccess()
        {
            var session = WarSandboxSceneSession.Instance;
            return controller != null && controller.manager != null &&
                (session == null || (!session.InputBlocked && session.Controller == controller));
        }

        public bool TryBeginEdit(bool confirmEndBattle, out string error)
        {
            error = null;
            if (!CanAccess()) return Reject("当前无法编辑布阵。", out error);
            if (IsEditing) return true;
            if (rosterPolicy != null && !rosterPolicy.TryValidateDefinition(out error)) { Error = error; return false; }
            if ((controller.Phase == WarSandboxBattlePhase.Running || controller.Phase == WarSandboxBattlePhase.Paused) && !confirmEndBattle)
                return Reject("请先确认结束当前战斗。", out error);
            var manager = controller.manager;
            if (!EnsureCommitted(out error)) { Error = error; return false; }
            if (controller.Phase != WarSandboxBattlePhase.Setup) controller.ResetBattle();
            if (controller.Phase != WarSandboxBattlePhase.Setup) return Reject("无法返回布阵阶段。", out error);
            manager.PauseBattle();
            StatStore.Invalidate();
            Draft = new WarSandboxDeploymentDraft(committed, controller.CaptureBattlefieldRules(), committedStats);
            dispatchBeforeEdit = manager.enableGpuDispatch;
            manager.enableGpuDispatch = false;
            IsEditing = true; Error = null;
            PlanState28.For(controller)?.Initialize(PlanSignature28(false));
            return true;
        }

        // The authored scenario is captured once, before any runtime copy replaces it on the manager.
        private bool EnsureCommitted(out string error)
        {
            error = null;
            if (committed != null) return true;
            var manager = controller.manager;
            if (!WarSandboxDeploymentDraft.TryCapture(manager.scenarioConfig, out var initial, out error)) return false;
            sourceScenario = manager.scenarioConfig;
            committed = initial.Snapshot();
            // Only the NEW-unit chooser is filtered; committed drafts and ID resolution remain intact.
            foreach (var entry in committed) if (Selectable(entry.template) && !templates.Contains(entry.template)) templates.Add(entry.template);
            if (rosterPolicy != null) foreach (var template in rosterPolicy.templates)
                if (Selectable(template) && !templates.Contains(template)) templates.Add(template);
            choiceTemplates.Clear(); choiceTemplates.AddRange(WarSandboxRosterChoices.Local(battlefieldCatalog, templates));
            return true;
        }

        private bool Selectable(UnitTypeConfig template) => battlefieldCatalog == null || battlefieldCatalog.IsSelectableTemplate(template);

        /// <summary>
        /// Called by the scene session while a catalog battlefield finishes loading (before input is
        /// enabled). With no applicable global override the authored scenario is left untouched. A
        /// failure never blocks the battlefield: it falls back to official values and reports why.
        /// </summary>
        public bool TryApplyGlobalStatsOnLoad(out string warning)
        {
            warning = null;
            if (controller == null || controller.manager == null || battlefieldCatalog == null) return false;
            var manager = controller.manager;
            if (controller.Phase != WarSandboxBattlePhase.Setup || manager.IsBattleRunning)
            { warning = "全局数值只能在进入战场或明确应用布阵时生效。"; return false; }
            var global = StatStore.Current;
            warning = StatStore.Warning;
            if (global.IsEmpty || manager.scenarioConfig == null || manager.scenarioConfig.unitTypes == null) return false;
            var resolver = new WarSandboxStatResolver(battlefieldCatalog, global, null);
            bool affected = false;
            foreach (var unit in manager.scenarioConfig.unitTypes) affected |= unit != null && resolver.HasCustom(unit);
            if (!affected) return false;
            if (!EnsureCommitted(out string error)) { warning = "全局兵种数值未生效：" + error; return false; }
            var previousScenario = manager.scenarioConfig;
            var previousClearance = manager.DeploymentRadiusClearance;
            var radiusDraft = new WarSandboxDeploymentDraft(committed, controller.CaptureBattlefieldRules());
            if (!WarSandboxRadiusPolicy.TryValidate(manager, radiusDraft, resolver, out var clearance, out var radiusError) ||
                (clearance.HasValue && !terrainValidation.TryValidate(manager, radiusDraft, out _, out radiusError, clearance)))
            { warning = "全局半径未生效，保留原部署：" + radiusError; return false; }
            WarSandboxDeploymentInstance candidate = null;
            try
            {
                candidate = new WarSandboxDeploymentInstance(committed, resolver);
                if (clearance.HasValue || previousClearance.HasValue)
                { manager.PauseBattle(); manager.Release(); manager.SetDeploymentRadiusClearance(clearance); }
                manager.scenarioConfig = candidate.Scenario;
                manager.ResetScenario(); manager.PauseBattle();
                if (Application.isPlaying && (manager.Buffers == null || !manager.Buffers.IsAllocated ||
                    manager.UnitTypes == null || manager.UnitTypes.UnitTypeCount != committed.Length))
                    throw new InvalidOperationException("GPU 部署初始化失败。");
                var previous = active; active = candidate; candidate = null;
                previous?.Dispose();
                return true;
            }
            catch (Exception exception)
            {
                manager.Release(); manager.SetDeploymentRadiusClearance(previousClearance); manager.scenarioConfig = previousScenario;
                try { manager.ResetScenario(); manager.PauseBattle(); } catch { }
                candidate?.Dispose();
                warning = "全局兵种数值未生效，已按官方数值运行：" + exception.Message;
                return false;
            }
        }

        public bool SetStat(UnitTypeConfig template, WarSandboxUnitStat stat, float value) =>
            IsEditing && Draft != null && templates.Contains(template) && Draft.SetStat(template, stat, value);

        /// <summary>
        /// Pushes this plan's overrides of one template to the global layer (by template id). Only the
        /// fields set locally are written; other global fields are kept. Optionally removes the
        /// pushed fields from the plan so the template follows the global value from now on.
        /// </summary>
        public bool TryPushStats(UnitTypeConfig template, bool clearLocal, out string error)
        {
            error = null;
            if (!IsEditing || Draft == null) return Reject("请先进入布阵阶段。", out error);
            if (battlefieldCatalog == null || !battlefieldCatalog.TryGetTemplateId(template, out string id, out int revision))
                return Reject("此兵种没有稳定的模板编号，不能推送到全局。", out error);
            var local = Draft.Stats.Get(template);
            if (local.Count == 0) return Reject("本局没有可推送的修改。", out error);
            var next = StatStore.Current.Clone();
            next.Merge(id, revision, local);
            if (!StatStore.TrySave(next, "推送 " + template.unitTypeName + "（" + id + "）" + local.Count + " 项", out error))
                return Reject(error, out error);
            if (clearLocal)
            {
                var fields = new List<WarSandboxUnitStat>();
                foreach (var pair in local.Values) fields.Add(pair.Key);
                Draft.ClearStats(template, fields);
            }
            Error = null; return true;
        }

        public bool TryUndoGlobalChange(out string error)
        {
            if (!StatStore.TryUndo(out error)) return Reject(error, out error);
            Error = null; return true;
        }

        public bool TryValidate(out string error)
        {
            error = null;
            if (!CanAccess() || !IsEditing || Draft == null || controller.Phase != WarSandboxBattlePhase.Setup)
            { error = "当前无法应用布阵。"; return false; }
            return ValidateDraft(Draft, out error);
        }

        /// <summary>One validated translation of a captured draft entry. Never applies/rebuilds the GPU deployment.</summary>
        public bool TryTranslateDraft(WarSandboxDeploymentDraft expectedDraft, int revision, int index,
            WarSandboxDeploymentEntry expectedEntry, Vector3 center, out string error)
        {
            error = null;
            if (!CanAccess() || !IsEditing || Draft == null || controller.Phase != WarSandboxBattlePhase.Setup ||
                !ReferenceEquals(Draft, expectedDraft) || Draft.Revision != revision || index < 0 || index >= Draft.Count || !Draft[index].Equals(expectedEntry))
            { error = "草稿或目标编成已变化，拖动已取消。"; return false; }
            var next = expectedEntry; next.center = new Vector3(center.x, expectedEntry.center.y, center.z);
            if (next.Equals(expectedEntry)) return true;
            var entries = Draft.Snapshot(); entries[index] = next;
            var candidate = new WarSandboxDeploymentDraft(entries, Draft.Rules, Draft.Stats);
            if (!ValidateDraft(candidate, out error)) return false;
            // Validation is CPU-only and synchronous. Recheck the token before the single history write.
            if (!ReferenceEquals(Draft, expectedDraft) || Draft.Revision != revision || !Draft[index].Equals(expectedEntry))
            { error = "草稿已变化，位置未提交。"; return false; }
            return Draft.Set(index, next);
        }

        public bool TryGetResizeRadius(WarSandboxDeploymentEntry entry,out float radius,out string error)
        {
            radius=0;error=null;var info=UnitPreviewRadius.Read(entry.template);
            if(!info.Available||entry.template.spawnConfig==null){error="自定义生成模块/实际半径尚未验证，不能调整尺寸。";return false;}
            var resolver=new WarSandboxStatResolver(battlefieldCatalog,StatStore.Current,Draft.Stats);
            var value=resolver.Resolve(entry.template,WarSandboxUnitStats.Get(WarSandboxUnitStat.AgentRadius));
            radius=value.applies?value.effective:info.BaseRadius;
            // DefaultSpawnModule generates scale=1. Do not import an unrelated rendering-size multiplier.
            return WarSandboxDeploymentResize.Finite(radius)&&radius>0;
        }
        public bool TryResizeDraft(WarSandboxDeploymentDraft expected,int revision,int index,WarSandboxDeploymentEntry original,
            WarSandboxDeploymentEntry proposed,out string error)
        {
            error=null;
            if(proposed.count!=original.count||proposed.template!=original.template||proposed.teamId!=original.teamId||proposed.center.y!=original.center.y)
            {error="尺寸拖拽不能改变人数、兵种、军团或高度。";return false;}
            return TryEditFormation(expected,revision,index,original,proposed,true,out error);
        }
        public bool TryEditFormation(WarSandboxDeploymentDraft expected,int revision,int index,WarSandboxDeploymentEntry original,
            WarSandboxDeploymentEntry next,bool shapeChanged,out string error)
        {
            error=null;
            if(!CanAccess()||!IsEditing||Draft==null||controller.Phase!=WarSandboxBattlePhase.Setup||!ReferenceEquals(Draft,expected)||
                Draft.Revision!=revision||index<0||index>=Draft.Count||!Draft[index].Equals(original)||next.template!=original.template||next.teamId!=original.teamId||next.center.y!=original.center.y)
            {error="草稿或目标编成已变化，未提交。";return false;}
            if(next.Equals(original))return true;
            if(shapeChanged)
            {
                if(!WarSandboxDeploymentResize.Finite(next.density)||next.density<.05f||next.density>SpawnConfig.PackingLimitPerSquareMeter||!WarSandboxDeploymentResize.Finite(next.aspect)||next.aspect<.1f||next.aspect>10){error="密度或比例无效。";return false;}
                if(!WarSandboxDeploymentResize.TryFromSize(next,next.center,new Vector2(next.Size.x,next.Size.z),out var converted,out error))return false;
                if(next.manualSize.x>0&&next.manualSize.z>0)next=converted;
                if(!TryGetResizeRadius(next,out var radius,out error)||!WarSandboxDeploymentResize.Fits(next,radius,next.template.spawnConfig.formationJitterFraction,out error))return false;
            }
            var entries=Draft.Snapshot();entries[index]=next;var candidate=new WarSandboxDeploymentDraft(entries,Draft.Rules,Draft.Stats);
            if(!ValidateDraft(candidate,out error))return false;
            if(!ReferenceEquals(Draft,expected)||Draft.Revision!=revision||!Draft[index].Equals(original)){error="草稿已变化。";return false;}
            return Draft.Set(index,next);
        }

        public bool SelectTemplate(int index, UnitTypeConfig template)
        {
            if (!IsEditing || Draft == null || index < 0 || index >= Draft.Count || !templates.Contains(template)) return false;
            var entry = Draft[index];
            if (rosterPolicy != null) entry = rosterPolicy.Select(entry, template);
            else entry.template = template;
            return Draft.Set(index, entry);
        }

        private bool ValidateDraft(WarSandboxDeploymentDraft draft, out string error)
        {
            error = null;
            for (int i = 0; i < draft.Count; i++)
                if (!templates.Contains(draft[i].template)) { error = "编成使用了未收录的兵种模板。"; return false; }
            var simulation = controller.manager.systemConfig != null ? controller.manager.systemConfig.simulationConfig : null;
            float padding = simulation != null ? simulation.boundaryPadding : 0;
            if (float.IsNaN(padding) || float.IsInfinity(padding) || padding < 0) { error = "战场边界留白无效。"; return false; }
            if (!draft.TryValidate(WorldSize - Vector2.one * (padding * 2), draft.Rules, out error)) return false;
            if (rosterPolicy != null && !rosterPolicy.TryValidate(draft, simulation, out error)) return false;
            var session = WarSandboxSceneSession.Instance;
            if (session != null && session.CurrentBattlefield != null &&
                !WarSandboxSceneSession.TryValidateTerrainProvider(controller.manager, session.CurrentBattlefield, out error)) return false;
            var resolver = new WarSandboxStatResolver(battlefieldCatalog, StatStore.Current, draft.Stats);
            if (!WarSandboxRadiusPolicy.TryValidate(controller.manager, draft, resolver, out var clearance, out error)) return false;
            return terrainValidation.TryValidate(controller.manager, draft, out _, out error, clearance);
        }

        // Presentation-only canonical state. Stable plan IDs, no persistence side effects.
        public string PlanSignature28(bool current)
        {
            var entry = WarSandboxSceneSession.Instance != null ? WarSandboxSceneSession.Instance.CurrentBattlefield : null;
            if (entry == null || committed == null || controller == null || controller.manager == null) return null;
            bool draft = current && IsEditing && Draft != null;
            var plan = WarSandboxLocalPlanStore.Create("state28", "state28", entry.id, entry.contentVersion,
                entry.terrainId, entry.terrainVersion, WorldSize, controller.manager.systemConfig.simulationConfig.boundaryPadding,
                draft ? Draft.Rules : controller.CaptureBattlefieldRules(), draft ? Draft.Snapshot() : committed,
                battlefieldCatalog, draft ? Draft.Stats : committedStats, out _);
            return PlanState28.Canonical(plan);
        }

        public bool TrySavePlan(string slot, string displayName, bool overwrite, out string error)
        {
            if (!TryValidate(out error)) return Reject(error, out error);
            if (!TryGetBattlefield(out var entry, out error)) return Reject(error, out error);
            if (!battlefieldCatalog.TryValidateTemplates(out error)) return Reject(error, out error);
            float padding = controller.manager.systemConfig.simulationConfig.boundaryPadding;
            var plan = WarSandboxLocalPlanStore.Create(slot, displayName, entry.id, entry.contentVersion,
                entry.terrainId, entry.terrainVersion, WorldSize, padding, Draft.Rules, Draft.Snapshot(), battlefieldCatalog, Draft.Stats, out error);
            if (plan == null || !PlanStore.TrySave(plan, overwrite, out error)) return Reject(error, out error);
            var state28 = PlanState28.For(controller);
            if (state28 != null && !state28.RecordStored(slot, out error)) return Reject("方案已写入，但读取核验失败：" + error, out error);
            Error = null; return true;
        }

        public bool TryLoadPlan(string slot, out string error)
        {
            error = null;
            if (!CanAccess() || !IsEditing || Draft == null) return Reject("请先进入布阵阶段。", out error);
            if (!TryGetBattlefield(out var entry, out error)) return Reject(error, out error);
            if (!PlanStore.TryLoad(slot, out var plan, out error)) return Reject(error, out error);
            float padding = controller.manager.systemConfig.simulationConfig.boundaryPadding;
            if (!WarSandboxLocalPlanStore.TryResolve(plan, entry, battlefieldCatalog, WorldSize, padding, out var candidate, out error))
                return Reject(error, out error);
            bool loaded = TryReplaceDraft(candidate, out error);
            if (loaded) PlanState28.For(controller)?.RecordStored(slot, out _);
            return loaded;
        }

        public bool TryReplaceDraft(WarSandboxDeploymentDraft candidate, out string error)
        {
            if (!CanAccess() || !IsEditing || Draft == null || candidate == null)
                return Reject("请先进入布阵阶段。", out error);
            if (!ValidateDraft(candidate, out error)) return Reject(error, out error);
            Draft.Replace(candidate.Snapshot(), candidate.Rules, candidate.Stats);
            Error = null; return true;
        }

        private bool TryGetBattlefield(out WarSandboxBattlefieldEntry entry, out string error)
        {
            entry = null; error = null;
            var session = WarSandboxSceneSession.Instance;
            if (session == null || battlefieldCatalog == null || string.IsNullOrEmpty(session.CurrentEntryId))
            { error = "此场景未关联战场目录，无法保存或载入本地方案。"; return false; }
            entry = session.CurrentBattlefield;
            if (entry == null) { error = "战场请求身份缺失。"; return false; }
            return WarSandboxSceneSession.TryValidateTerrainProvider(controller.manager, entry, out error);
        }

        public bool TryApply(out string error)
        {
            StatStore.Invalidate(); // Freeze one fresh global snapshot for validation AND runtime construction.
            if (!TryValidate(out error)) { Error = error; return false; }
            var manager = controller.manager;
            var previousScenario = manager.scenarioConfig;
            var previousRules = controller.CaptureBattlefieldRules();
            var previousClearance = manager.DeploymentRadiusClearance;
            var nextEntries = Draft.Snapshot();
            WarSandboxDeploymentInstance candidate = null;
            try
            {
                // Only runtime copies acquire terrain height; authored/saved XZ and Y stay intact.
                if (!manager.TryGetTerrainContext(out var surface, out _, out string terrainError))
                    throw new InvalidOperationException(terrainError);
                var runtimeEntries = (WarSandboxDeploymentEntry[])nextEntries.Clone();
                if (surface != null)
                    for (int i = 0; i < runtimeEntries.Length; i++)
                    {
                        var point = runtimeEntries[i].center;
                        if (!surface.TrySample(new Vector2(point.x, point.z), out var sample))
                            throw new InvalidOperationException("部署中心不在地表上。");
                        runtimeEntries[i].center.y = sample.Position.y;
                    }
                var nextStats = Draft.Stats;
                var resolver = new WarSandboxStatResolver(battlefieldCatalog, StatStore.Current, nextStats);
                if (!WarSandboxRadiusPolicy.TryValidate(manager, Draft, resolver, out var clearance, out var radiusError))
                    throw new InvalidOperationException(radiusError);
                candidate = new WarSandboxDeploymentInstance(runtimeEntries, resolver);
                if (clearance.HasValue || previousClearance.HasValue)
                { manager.PauseBattle(); manager.Release(); manager.SetDeploymentRadiusClearance(clearance); }
                manager.scenarioConfig = candidate.Scenario;
                manager.enableGpuDispatch = dispatchBeforeEdit;
                IsEditing = false;
                controller.ResetForDeployment(Draft.Rules);
                if (Application.isPlaying && (manager.Buffers == null || !manager.Buffers.IsAllocated ||
                    manager.UnitTypes == null || manager.UnitTypes.UnitTypeCount != nextEntries.Length))
                    throw new InvalidOperationException("GPU 部署初始化失败。");
                var previous = active; active = candidate; candidate = null;
                committed = nextEntries; committedStats = nextStats; Draft = null; Error = null;
                controller.CommitDeploymentRules();
                PlanState28.For(controller)?.MarkApplied();
                previous?.Dispose();
                for (int i = 0; i < controller.ArmyCount; i++)
                    if (controller.GetArmy(i).initialUnitCount > 0) { controller.SelectArmy(i); break; }
                return true;
            }
            catch (Exception exception)
            {
                // The manager must stop referencing a candidate before its owned configs die.
                manager.Release(); manager.SetDeploymentRadiusClearance(previousClearance); manager.scenarioConfig = previousScenario;
                manager.enableGpuDispatch = dispatchBeforeEdit;
                IsEditing = false;
                string rollbackError = null;
                try { controller.ResetForDeployment(previousRules); }
                catch (Exception rollback) { rollbackError = " 恢复失败：" + rollback.Message; }
                candidate?.Dispose();
                IsEditing = true; manager.enableGpuDispatch = false;
                return Reject("布阵未应用：" + exception.Message + rollbackError, out error);
            }
        }

        public bool CancelEditing()
        {
            if (!CanAccess() || !IsEditing) return false;
            CloseDraft(); return true;
        }

        private void CloseDraft()
        {
            if (IsEditing && controller != null && controller.manager != null)
                controller.manager.enableGpuDispatch = dispatchBeforeEdit;
            IsEditing = false; Draft = null; Error = null;
        }

        private bool Reject(string message, out string error) { error = message; Error = message; return false; }
        private void OnDisable() => CloseDraft();

        private void OnDestroy()
        {
            CloseDraft();
            if (active != null && controller != null && controller.manager != null && controller.manager.scenarioConfig == active.Scenario)
            {
                controller.manager.Release();
                if (controller.manager.DeploymentRadiusClearance.HasValue)
                { controller.manager.PauseBattle(); controller.manager.SetDeploymentRadiusClearance(null); }
                controller.manager.scenarioConfig = sourceScenario;
            }
            active?.Dispose(); active = null;
        }
    }
}



