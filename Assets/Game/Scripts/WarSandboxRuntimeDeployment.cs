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
        public ScenarioConfig SourceScenario => sourceScenario;
        public Vector2 WorldSize => controller != null && controller.manager != null && controller.manager.systemConfig != null &&
            controller.manager.systemConfig.simulationConfig != null
            ? controller.manager.systemConfig.simulationConfig.simulationWorldSize : Vector2.zero;

        private readonly List<UnitTypeConfig> templates = new List<UnitTypeConfig>();
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
            var global = StatStore.Current;
            warning = StatStore.Warning;
            if (global.IsEmpty || manager.scenarioConfig == null || manager.scenarioConfig.unitTypes == null) return false;
            var resolver = new WarSandboxStatResolver(battlefieldCatalog, global, null);
            bool affected = false;
            foreach (var unit in manager.scenarioConfig.unitTypes) affected |= unit != null && resolver.HasCustom(unit);
            if (!affected) return false;
            if (!EnsureCommitted(out string error)) { warning = "全局兵种数值未生效：" + error; return false; }
            var previousScenario = manager.scenarioConfig;
            WarSandboxDeploymentInstance candidate = null;
            try
            {
                candidate = new WarSandboxDeploymentInstance(committed, resolver);
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
                manager.Release(); manager.scenarioConfig = previousScenario;
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
            return terrainValidation.TryValidate(controller.manager, draft, out _, out error);
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
            return TryReplaceDraft(candidate, out error);
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
            if (!TryValidate(out error)) { Error = error; return false; }
            var manager = controller.manager;
            var previousScenario = manager.scenarioConfig;
            var previousRules = controller.CaptureBattlefieldRules();
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
                StatStore.Invalidate();
                var nextStats = Draft.Stats;
                candidate = new WarSandboxDeploymentInstance(runtimeEntries,
                    new WarSandboxStatResolver(battlefieldCatalog, StatStore.Current, nextStats));
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
                previous?.Dispose();
                for (int i = 0; i < controller.ArmyCount; i++)
                    if (controller.GetArmy(i).initialUnitCount > 0) { controller.SelectArmy(i); break; }
                return true;
            }
            catch (Exception exception)
            {
                // The manager must stop referencing a candidate before its owned configs die.
                manager.Release(); manager.scenarioConfig = previousScenario;
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
                controller.manager.scenarioConfig = sourceScenario;
            }
            active?.Dispose(); active = null;
        }
    }
}
