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
        private readonly WarSandboxTerrainValidation terrainValidation = new WarSandboxTerrainValidation();
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
            if (committed == null)
            {
                if (!WarSandboxDeploymentDraft.TryCapture(manager.scenarioConfig, out var initial, out error)) { Error = error; return false; }
                sourceScenario = manager.scenarioConfig;
                committed = initial.Snapshot();
                foreach (var entry in committed) if (!templates.Contains(entry.template)) templates.Add(entry.template);
                if (rosterPolicy != null) foreach (var template in rosterPolicy.templates)
                    if (!templates.Contains(template)) templates.Add(template);
            }
            if (controller.Phase != WarSandboxBattlePhase.Setup) controller.ResetBattle();
            if (controller.Phase != WarSandboxBattlePhase.Setup) return Reject("无法返回布阵阶段。", out error);
            manager.PauseBattle();
            Draft = new WarSandboxDeploymentDraft(committed, controller.CaptureBattlefieldRules());
            dispatchBeforeEdit = manager.enableGpuDispatch;
            manager.enableGpuDispatch = false;
            IsEditing = true; Error = null;
            return true;
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
                entry.terrainId, entry.terrainVersion, WorldSize, padding, Draft.Rules, Draft.Snapshot(), battlefieldCatalog, out error);
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
            Draft.Replace(candidate.Snapshot(), candidate.Rules);
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
                candidate = new WarSandboxDeploymentInstance(runtimeEntries);
                manager.scenarioConfig = candidate.Scenario;
                manager.enableGpuDispatch = dispatchBeforeEdit;
                IsEditing = false;
                controller.ResetForDeployment(Draft.Rules);
                if (Application.isPlaying && (manager.Buffers == null || !manager.Buffers.IsAllocated ||
                    manager.UnitTypes == null || manager.UnitTypes.UnitTypeCount != nextEntries.Length))
                    throw new InvalidOperationException("GPU 部署初始化失败。");
                var previous = active; active = candidate; candidate = null;
                committed = nextEntries; Draft = null; Error = null;
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
