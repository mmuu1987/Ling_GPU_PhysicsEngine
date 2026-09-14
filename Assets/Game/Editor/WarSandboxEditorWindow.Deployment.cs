using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public sealed partial class WarSandboxEditorWindow
    {
        [SerializeField] private bool manualDeployment;
        [SerializeField] private bool showManualFootprint;

        private void DrawDeploymentPage()
        {
            Text(pageContent, "先看部队占地，再选择自动排阵或手动摆放。预览会随参数更新。", "lead");
            string problem = SetupProblem();
            if (problem == null)
            {
                DrawBattlefieldRules();
                problem = RosterProblem();
            }
            if (problem != null)
            {
                Notice(pageContent, problem, true);
                ActionButton(pageContent, "去检查配置 →", () => Navigate(SetupProblem() != null ? 0 : 1)); return;
            }
            DrawDeploymentPreview();
            var tabs = Element(pageContent, "actions");
            ActionButton(tabs, "自动布阵（建议先用）", () => { manualDeployment = false; RefreshPage(); }, "auto-tab", primary: !manualDeployment);
            ActionButton(tabs, "手动摆放 / 高级占地", () => { manualDeployment = true; RefreshPage(); }, "manual-tab", primary: manualDeployment);
            if (manualDeployment) { DrawManualDeployment(); return; }
            var fit = Card(pageContent, "按现有兵力排列战场");
            FloatControl(fit, "两军阵前间距（米）", engagementGap, 0f, 100000f, value => engagementGap = value, "engagement-gap");
            Text(fit, "推荐 50 米。指两军最前排边缘之间的空地，不是两个出生中心的距离。", "field-help");
            Notice(fit, "执行后：军团 0、1 会重新排成对阵队形；同团编成按清单顺序前后排列。军团 2 及以上保留手动位置。所有军团都会计入战场尺寸。");
            ActionButton(fit, "按当前兵力自动布阵", () => RunAutoFit(false), "auto-fit", primary: true, writable: true);
            Text(fit, "保留人数，调整两方位置与战场容量，并保存配置资产。可用一次 Ctrl+Z 撤销；已经手工摆好的两方可跳过此步。", "field-help");
            var scale = new Foldout { text = "想整体改变战斗规模？展开选择预设", value = false }; pageContent.Add(scale);
            var choices = new List<string> { "轻量 · 每方 1 万（建议首次使用）", "大型 · 每方 5 万", "压力 · 每方 10 万", "极限 · 每方 20 万", "自定义每方人数" };
            var preset = new PopupField<string>("两方规模", choices, (int)scalePreset) { name = "scale-preset" };
            preset.AddToClassList("stacked-field"); preset.SetEnabled(!ReadOnly); scale.Add(preset);
            var custom = IntegerControl(scale, "军团 0、1 各有多少人", customUnitsPerTeam, value => customUnitsPerTeam = value, "custom-scale");
            custom.style.display = scalePreset == WarSandboxScalePreset.Custom ? DisplayStyle.Flex : DisplayStyle.None;
            var note = Text(scale, "", "notice");
            System.Action updateNote = () =>
            {
                note.text = scalePreset == WarSandboxScalePreset.Standard10K ? "适合先验证操作。选择预设不会立即修改，点击下方按钮才生效。" :
                    "大规模会增加负载。该预设只改军团 0、1 的总兵力，按原有兵种比例分配；额外军团人数保持原值。";
            };
            updateNote();
            preset.RegisterValueChangedCallback(evt =>
            {
                scalePreset = (WarSandboxScalePreset)preset.index;
                custom.style.display = scalePreset == WarSandboxScalePreset.Custom ? DisplayStyle.Flex : DisplayStyle.None;
                updateNote();
            });
            ActionButton(scale, "应用所选人数，并自动布阵", () => RunAutoFit(true), "apply-scale", writable: true);
        }

        private void DrawManualDeployment()
        {
            if (!SelectArmy(pageContent)) return;
            UnitTypeConfig unit = Scenario.unitTypes[selectedArmy];
            if (unit == null || unit.spawnConfig == null) return;
            SpawnConfig spawn = unit.spawnConfig;
            var card = Card(pageContent, "出生中心：这批士兵从哪里出现");
            Text(card, "X 控制左右，Z 控制前后。俯视预览上方是 +Z，右方是 +X；默认两军沿 X 轴交战。", "field-help");
            FloatControl(card, "左右位置 X（米）", spawn.spawnCenter.x, -100000f, 100000f, value =>
            {
                Edit(spawn, "Move Formation", () => { var center = spawn.spawnCenter; center.x = value; spawn.spawnCenter = center; }); RefreshPage();
            }, "spawn-x");
            FloatControl(card, "前后位置 Z（米）", spawn.spawnCenter.z, -100000f, 100000f, value =>
            {
                Edit(spawn, "Move Formation", () => { var center = spawn.spawnCenter; center.z = value; spawn.spawnCenter = center; }); RefreshPage();
            }, "spawn-z");
            Text(card, "例如 X = -100 在中心左侧 100 米；Z = 100 在中心上方 100 米。第三支军团必须手动放到不重叠的区域。", "field-help");
            ActionButton(card, "在 Scene 中看当前编成", () => FrameDeployment(spawn));
            var advanced = new Foldout { text = "高度与手动占地（通常保持默认）", value = showManualFootprint }; pageContent.Add(advanced);
            advanced.RegisterValueChangedCallback(evt => { if (evt.target == advanced) showManualFootprint = evt.newValue; });
            FloatControl(advanced, "高度 Y（米）", spawn.spawnCenter.y, -100000f, 100000f, value =>
                Edit(spawn, "Change Spawn Height", () => { var center = spawn.spawnCenter; center.y = value; spawn.spawnCenter = center; }), "spawn-y");
            Notice(advanced, "手动占地的 X、Z 必须同时大于 0 才生效，启用后不再按密度计算面积。新手建议用自动推导。");
            FloatControl(advanced, "手动纵深 X（米；0 = 自动）", spawn.spawnSize.x, 0f, 100000f, value =>
            {
                Edit(spawn, "Change Manual Footprint", () => { var size = spawn.spawnSize; size.x = value; spawn.spawnSize = size; }); RefreshPage();
            }, "footprint-x");
            FloatControl(advanced, "手动阵线宽 Z（米；0 = 自动）", spawn.spawnSize.z, 0f, 100000f, value =>
            {
                Edit(spawn, "Change Manual Footprint", () => { var size = spawn.spawnSize; size.z = value; spawn.spawnSize = size; }); RefreshPage();
            }, "footprint-z");
            ActionButton(advanced, "恢复自动推导占地", () =>
            {
                Edit(spawn, "Reset Manual Footprint", () => spawn.spawnSize = Vector3.zero); RefreshPage();
            }, writable: true);
        }

        private void DrawDeploymentPreview()
        {
            var card = Card(pageContent, "俯视占地预览 · 上 +Z / 右 +X");
            Text(card, "色块代表实际出生范围，数字对应编成清单。点击色块可手动编辑；重叠的色块需要检查。", "field-help");
            var map = Element(card, "deployment-map"); map.name = "deployment-preview";
            UnitTypeConfig[] roster = Scenario.unitTypes;
            Bounds bounds = DeploymentBounds();
            map.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                map.Clear();
                float width = evt.newRect.width, height = evt.newRect.height;
                float scale = Mathf.Min((width - 28f) / Mathf.Max(1f, bounds.size.x), (height - 28f) / Mathf.Max(1f, bounds.size.z));
                if (scale <= 0f) return;
                for (int i = 0; i < roster.Length; i++)
                {
                    var unit = roster[i]; if (unit == null || unit.spawnConfig == null) continue;
                    int index = i; var spawn = unit.spawnConfig; Vector3 size = spawn.ResolveSpawnSize();
                    var block = new Button(() => { selectedArmy = index; manualDeployment = true; RefreshPage(); }) { text = (i + 1).ToString(), tooltip = DescribeArmy(unit) };
                    block.AddToClassList("deployment-block");
                    block.style.left = (width - bounds.size.x * scale) * 0.5f + (spawn.spawnCenter.x - size.x * 0.5f - bounds.min.x) * scale;
                    block.style.top = (height - bounds.size.z * scale) * 0.5f + (bounds.max.z - spawn.spawnCenter.z - size.z * 0.5f) * scale;
                    block.style.width = Mathf.Max(6, size.x * scale); block.style.height = Mathf.Max(6, size.z * scale);
                    Color teamColor = WarSandboxTeamPalette.Resolve(unit.teamId);
                    block.style.backgroundColor = new Color(teamColor.r * 0.65f, teamColor.g * 0.65f, teamColor.b * 0.65f, 1f);
                    map.Add(block);
                }
            });
            var simulation = manager.systemConfig.simulationConfig;
            Text(card, "当前战场：" + simulation.simulationWorldSize.x.ToString("F0") + " × " + simulation.simulationWorldSize.y.ToString("F0") + " 米。预览按部队范围缩放。");
            string warning = DeploymentWarning(); if (warning != null) Notice(card, warning);
            ActionButton(card, "在 Scene 中查看全军", () => FrameDeployment(null));
        }

        private Bounds DeploymentBounds()
        {
            Bounds bounds = default; bool found = false;
            if (Scenario?.unitTypes != null) foreach (var unit in Scenario.unitTypes)
            {
                if (unit == null || unit.spawnConfig == null) continue;
                var spawn = unit.spawnConfig; Vector3 size = spawn.ResolveSpawnSize(); size.y = 1f;
                var block = new Bounds(spawn.spawnCenter, size);
                if (!found) { bounds = block; found = true; } else bounds.Encapsulate(block);
            }
            return found ? bounds : new Bounds(Vector3.zero, Vector3.one * 100f);
        }

        private string DeploymentWarning()
        {
            UnitTypeConfig[] roster = Scenario.unitTypes;
            Vector2 halfWorld = manager.systemConfig.simulationConfig.simulationWorldSize * 0.5f;
            for (int i = 0; i < roster.Length; i++)
            {
                var a = roster[i]?.spawnConfig; if (a == null) continue;
                Vector3 sizeA = a.ResolveSpawnSize();
                if (Mathf.Abs(a.spawnCenter.x) + sizeA.x * 0.5f > halfWorld.x || Mathf.Abs(a.spawnCenter.z) + sizeA.z * 0.5f > halfWorld.y)
                    return "编成 " + (i + 1) + " 的占地超出当前世界范围。请缩小人数、调整位置，或执行自动布阵扩大容量。";
                for (int j = i + 1; j < roster.Length; j++)
                {
                    var b = roster[j]?.spawnConfig; if (b == null) continue; Vector3 sizeB = b.ResolveSpawnSize();
                    if (Mathf.Abs(a.spawnCenter.x - b.spawnCenter.x) < (sizeA.x + sizeB.x) * 0.5f - 0.01f &&
                        Mathf.Abs(a.spawnCenter.z - b.spawnCenter.z) < (sizeA.z + sizeB.z) * 0.5f - 0.01f)
                        return "编成 " + (i + 1) + " 与 " + (j + 1) + " 的出生范围重叠。两方可自动排开；额外军团需要手动移开。";
                }
            }
            return null;
        }

        private void FrameDeployment(SpawnConfig spawn)
        {
            Bounds bounds = spawn == null ? DeploymentBounds() : new Bounds(spawn.spawnCenter, spawn.ResolveSpawnSize() + Vector3.up);
            SceneView view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
            view.drawGizmos = true; view.Frame(bounds, false); view.Focus();
        }

        private void RunAutoFit(bool applyScale)
        {
            if (ReadOnly) return;
            string problem = SetupProblem() ?? RosterProblem();
            if (problem != null) { SetFeedback(problem, true); return; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Arrange War Sandbox Deployment");
            if (applyScale) WarSandboxScenarioPresets.ApplyPerTeamUnitCount(Scenario,
                WarSandboxScenarioPresets.GetDefinition(scalePreset, customUnitsPerTeam).unitsPerTeam);
            ScenarioAutoFit.AutoFit(manager, engagementGap); Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            SetFeedback("已完成" + (applyScale ? "规模调整和" : "") + "自动布阵并保存配置资产。请检查预览；额外军团仍需手动确认，满意后去保存与试跑。");
        }

        private string RosterProblem()
        {
            if (Scenario?.unitTypes == null || Scenario.unitTypes.Length == 0) return "没有可部署的编成。先去军团配兵新增一个。";
            for (int i = 0; i < Scenario.unitTypes.Length; i++)
            {
                var unit = Scenario.unitTypes[i];
                if (unit == null || unit.spawnConfig == null) return "编成 " + (i + 1) + " 缺少兵种或出生配置，请去军团配兵修复或移除。";
                if (unit.spawnConfig.unitCount <= 0) return "编成 " + (i + 1) + " 的人数必须大于 0。请去军团配兵修改人数。";
                if (unit.teamId < 0 || unit.teamId > ConfigValidator.MaxTeamId) return "编成 " + (i + 1) + " 的军团编号无效，请去军团配兵重新选择军团。";
            }
            return null;
        }

        private string TrialProblem()
        {
            string problem = SetupProblem() ?? RosterProblem() ?? BattlefieldProblem();
            if (problem != null) return problem;
            for (int i = 0; i < Scenario.unitTypes.Length; i++)
            {
                ValidationResult validation = ConfigValidator.Validate(Scenario.unitTypes[i]);
                if (!validation.IsValid)
                    return "编成 " + (i + 1) + " 的兵种配置无效。请在兵种 Inspector 中修复，或换用有效模板：\n" +
                        string.Join("\n", validation.Errors);
            }
            return null;
        }

        private void DrawSavePage()
        {
            Text(pageContent, "保存当前修改，再试打一局。想比较不同布阵时，可以分别另存 A / B 方案。", "lead");
            string problem = SetupProblem();
            if (problem != null)
            {
                Notice(pageContent, problem, true); ActionButton(pageContent, "去连接战场 →", () => Navigate(0)); return;
            }
            var save = Card(pageContent, "1. 保存当前配置");
            Text(save, "把编辑过的项目配置和当前战场场景写入磁盘。保存后也可以继续修改。", "field-help");
            ActionButton(save, "保存配置与当前场景", () =>
            {
                if (SaveConfiguration()) SetFeedback("配置与当前战场已保存。请检查下方试跑状态，确认无误后进入运行。");
            }, "save-config", primary: true, writable: true);

            var plans = Card(pageContent, "2. 保留多个部署版本（可选）");
            Text(plans, "另存创建新快照；载入恢复选中的快照；覆盖更新选中的快照。载入后不会自动重排位置。", "field-help");
            ObjectPicker(plans, "选择已有方案", deploymentPlan, false, value => { deploymentPlan = value; feedback = null; RefreshPage(); });
            if (deploymentPlan != null) Text(plans, "方案“" + deploymentPlan.name + "”含 " + deploymentPlan.UnitTypeCount + " 个编成。");
            var buttons = Element(plans, "actions");
            ActionButton(buttons, "另存新方案…", SaveDeploymentPlanAs, "save-plan", writable: true);
            var load = ActionButton(buttons, "载入所选方案", LoadDeploymentPlan, "load-plan", writable: true);
            var overwrite = ActionButton(buttons, "覆盖所选方案…", () =>
            {
                if (EditorUtility.DisplayDialog("覆盖部署方案", "用当前部署替换“" + deploymentPlan.name + "”？", "覆盖", "取消")) CaptureDeploymentPlan(deploymentPlan);
            }, "overwrite-plan", writable: true);
            load.SetEnabled(!ReadOnly && deploymentPlan != null); overwrite.SetEnabled(!ReadOnly && deploymentPlan != null);
            if (deploymentPlan == null) Text(plans, "还没选方案：可以直接“另存新方案”；选择已有方案后才可载入或覆盖。", "field-help");
            Text(plans, "快照保存编成、人数、阵型、位置与战场尺寸。战斗参数仍引用原资产；不包含进行中的战斗、规则或障碍。", "field-help");

            var play = Card(pageContent, "3. 试跑一局");
            string trialProblem = TrialProblem();
            if (trialProblem != null) Notice(play, trialProblem, true);
            else
            {
                Text(play, "基础配置已就绪。进入运行时先停在部署阶段，不会立刻开战。");
                string warning = DeploymentWarning(); if (warning != null) Notice(play, warning);
            }
            Text(play, "点击 Game 画面 → Enter 开战 → Space 暂停 / 继续。\n退出：点击 Unity 顶部的停止按钮，再回来修改配置。", "field-help");
            var start = ActionButton(play, "保存并进入试跑", () =>
            {
                string currentProblem = TrialProblem();
                if (currentProblem != null) { SetFeedback(currentProblem, true); return; }
                if (SaveConfiguration()) EditorApplication.EnterPlaymode();
            }, "start-play", primary: true, writable: true);
            start.SetEnabled(!ReadOnly && trialProblem == null);
            ActionButton(play, "返回布阵检查位置", () => Navigate(2));
        }

        private bool SaveConfiguration()
        {
            if (ReadOnly || manager == null) return false;
            AssetDatabase.SaveAssets();
            if (EditorSceneManager.SaveScene(manager.gameObject.scene)) return true;
            SetFeedback("场景尚未保存。请完成场景保存后再试跑。", true); return false;
        }

        private void SaveDeploymentPlanAs()
        {
            if (ReadOnly) return;
            string path = EditorUtility.SaveFilePanelInProject("另存部署方案", "DeploymentPlan", "asset", "选择部署方案的保存位置");
            if (string.IsNullOrEmpty(path)) return;
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                if (!(existing is WarSandboxDeploymentPlan plan)) { SetFeedback("该位置已有其他类型的资产，请换个名称保存。", true); return; }
                if (EditorUtility.DisplayDialog("覆盖部署方案", "替换“" + plan.name + "”中的部署数据？", "覆盖", "取消") && CaptureDeploymentPlan(plan))
                { deploymentPlan = plan; RefreshPage(); }
                return;
            }
            var created = CreateInstance<WarSandboxDeploymentPlan>();
            if (!created.TryCapture(manager, engagementGap, out string error))
            { DestroyImmediate(created); SetFeedback("保存失败，请检查当前编成及配置引用。\n" + error, true); return; }
            AssetDatabase.CreateAsset(created, path); AssetDatabase.SaveAssets(); deploymentPlan = created;
            EditorGUIUtility.PingObject(created); SetFeedback("已另存方案“" + created.name + "”。现在可以修改部署，之后随时载入恢复。");
        }

        private bool CaptureDeploymentPlan(WarSandboxDeploymentPlan plan)
        {
            if (ReadOnly || plan == null) return false;
            if (!plan.TryCapture(manager, engagementGap, out string error))
            { SetFeedback("未覆盖方案，请检查当前配置。\n" + error, true); return false; }
            AssetDatabase.SaveAssets(); SetFeedback("已更新方案“" + plan.name + "”。"); return true;
        }

        private void LoadDeploymentPlan()
        {
            if (ReadOnly || deploymentPlan == null) return;
            if (!deploymentPlan.TryApply(manager, out string error))
            { SetFeedback("载入失败，当前部署保持原样。请检查方案中的兵种和出生配置是否仍存在。\n" + error, true); return; }
            Undo.RecordObject(this, "Load War Sandbox Deployment"); engagementGap = deploymentPlan.EngagementGap;
            SceneView.RepaintAll(); SetFeedback("已载入“" + deploymentPlan.name + "”，保留方案原位置，没有重新自动布阵。检查后保存配置；Ctrl+Z 可撤销载入。");
        }
    }
}
