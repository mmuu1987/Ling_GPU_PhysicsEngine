using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MassEngine.Game.Editor
{
    public sealed partial class WarSandboxEditorWindow
    {
        [SerializeField] private int newArmyCount = 1000;

        private void DrawRosterPage()
        {
            Text(pageContent, "一次只改一个编成。相同军团编号的兵种共同作战，不同军团互为敌人。", "lead");
            if (Scenario == null)
            {
                Notice(pageContent, "还没有连接战役。先在开始使用页打开默认战场。", true);
                ActionButton(pageContent, "去连接战场 →", () => Navigate(0)); return;
            }
            var tabs = Element(pageContent, "actions");
            ActionButton(tabs, "修改现有编成", () => { addingArmy = false; RefreshPage(); }, "edit-roster", primary: !addingArmy);
            ActionButton(tabs, "+ 新增编成", () => { addingArmy = true; RefreshPage(); }, "new-roster", primary: addingArmy);
            if (addingArmy) { DrawRosterComposer(); return; }
            if (!SelectArmy(pageContent)) return;
            UnitTypeConfig unit = Scenario.unitTypes[selectedArmy];
            var card = Card(pageContent, "兵力与所属军团");
            if (unit == null)
            {
                Notice(card, "这个清单位置的兵种引用已丢失。可以移除它，再从模板重新添加。", true);
                ActionButton(card, "移除空编成", RemoveSelectedArmy, writable: true); return;
            }
            SpawnConfig spawn = unit.spawnConfig;
            if (spawn != null)
            {
                IntegerControl(card, "兵力（人）", spawn.unitCount, value =>
                {
                    Edit(spawn, "Change War Sandbox Strength", () => spawn.unitCount = value); RefreshPage();
                }, "army-count");
                Text(card, "只改当前编成的人数。第一次可试 1,000 人；改完去战场布阵检查占地。", "field-help");
            }
            var teams = new List<string>();
            for (int i = 0; i <= ConfigValidator.MaxTeamId; i++) teams.Add(TeamName(i));
            var team = new PopupField<string>("所属军团", teams, Mathf.Clamp(unit.teamId, 0, ConfigValidator.MaxTeamId)) { name = "army-team" };
            team.AddToClassList("stacked-field"); team.SetEnabled(!ReadOnly); card.Add(team);
            team.RegisterValueChangedCallback(evt =>
            {
                Edit(unit, "Change War Sandbox Army", () => unit.teamId = team.index); RefreshPage();
            });
            Text(card, "想让近战和远程成为友军，就把两个编成都设为同一军团；设成不同军团会互相攻击。", "field-help");
            if (unit.teamId < 0 || unit.teamId > ConfigValidator.MaxTeamId)
                Notice(card, "当前军团编号无效，请重新选择 0～7 范围内的军团。", true);
            if (spawn == null)
            {
                Notice(card, "缺少出生配置，无法生成士兵。请展开下方资产设置指定出生配置，或从有效模板重新添加。", true);
            }
            else
            {
                var shape = Card(pageContent, "阵型：先用推荐值，再按需要调整");
                var footprint = new Label { name = "footprint-summary" }; footprint.AddToClassList("notice");
                Action refreshFootprint = () =>
                {
                    Vector3 size = spawn.ResolveSpawnSize();
                    footprint.text = (spawn.HasManualFootprint ? "手动占地" : "自动推导占地") + "：阵线宽 " + size.z.ToString("F1") +
                        " m × 纵深 " + size.x.ToString("F1") + " m";
                };
                FloatControl(shape, "密度（人 / 平方米）", spawn.formationDensity, 0.05f, SpawnConfig.PackingLimitPerSquareMeter, value =>
                {
                    Edit(spawn, "Change Formation Density", () => spawn.formationDensity = value); refreshFootprint();
                }, "army-density");
                Text(shape, "推荐 0.5。数值越大，士兵站得越紧；1.5 已接近常见兵种的拥挤上限。", "field-help");
                FloatControl(shape, "阵线宽 ÷ 纵深", spawn.formationAspect, 0.1f, 10f, value =>
                {
                    Edit(spawn, "Change Formation Aspect", () => spawn.formationAspect = value); refreshFootprint();
                }, "army-aspect");
                Text(shape, "推荐 2：横向宽阵线。1 是方阵，0.5 是前后较长的纵队。", "field-help");
                ActionButton(shape, "使用推荐阵型：密度 0.5 / 宽深比 2", () =>
                {
                    Edit(spawn, "Use Recommended Formation", () =>
                    {
                        spawn.formationDensity = 0.5f; spawn.formationAspect = 2f; spawn.spawnSize = Vector3.zero;
                    });
                    SetFeedback("已使用推荐阵型并关闭手动占地。人数和位置保持原值；下一步去战场布阵检查。");
                }, "recommended-formation", writable: true);
                if (spawn.HasManualFootprint) Notice(shape, "当前启用了手动占地，密度和宽深比暂不决定占地。点击推荐阵型可恢复自动推导。");
                refreshFootprint(); shape.Add(footprint);
                ActionButton(shape, "去调整出生位置 / 查看预览 →", () => Navigate(2));
            }
            var advanced = new Foldout { text = "命名、资产设置与移除（可选）", value = false }; pageContent.Add(advanced);
            var nameField = new TextField("编成名称") { value = unit.unitTypeName, isDelayed = true, name = "army-name" };
            nameField.AddToClassList("stacked-field"); nameField.SetEnabled(!ReadOnly); advanced.Add(nameField);
            nameField.RegisterValueChangedCallback(evt =>
            {
                Edit(unit, "Rename War Sandbox Formation", () => unit.unitTypeName = evt.newValue); RefreshPage();
            });
            Text(advanced, "例如“前排近战”或“后排远程”。名称只帮助识别，不会改变兵种能力。", "field-help");
            ObjectPicker(advanced, "出生配置", unit.spawnConfig, false, value =>
            {
                Edit(unit, "Change Spawn Config", () => unit.spawnConfig = value); RefreshPage();
            }, "只有需要修复引用时才改。多个编成共享同一出生配置时，修改人数和位置会一起生效。");
            ActionButton(advanced, "在 Inspector 中查看完整兵种配置", () => Selection.activeObject = unit);
            Text(advanced, "移除只从本战役清单解绑，保留磁盘资产；可 Ctrl+Z 恢复。", "field-help");
            ActionButton(advanced, "移除当前编成", RemoveSelectedArmy, "remove-army", writable: true);
        }

        private bool SelectArmy(VisualElement parent)
        {
            UnitTypeConfig[] roster = Scenario?.unitTypes;
            if (roster == null || roster.Length == 0)
            {
                Notice(parent, "战役里还没有编成。去“新增编成”选择一个兵种模板，先添加第一支军团。");
                ActionButton(parent, "添加第一个编成 →", () => { addingArmy = true; Navigate(1); }); return false;
            }
            selectedArmy = Mathf.Clamp(selectedArmy, 0, roster.Length - 1);
            var names = new List<string>();
            for (int i = 0; i < roster.Length; i++) names.Add((i + 1) + ". " + DescribeArmy(roster[i]));
            var selector = new PopupField<string>("选择要修改的编成", names, selectedArmy) { name = "army-selector" };
            selector.AddToClassList("stacked-field"); parent.Add(selector);
            selector.RegisterValueChangedCallback(evt => { selectedArmy = selector.index; RefreshPage(); });
            return true;
        }

        private void DrawRosterComposer()
        {
            var card = Card(pageContent, "1. 选择兵种模板");
            Text(card, "模板决定这批士兵的基础能力。先选择，再决定加入哪支军团。", "field-help");
            var templates = new List<UnitTypeConfig>();
            var labels = new List<string> { "请选择兵种模板…" };
            foreach (string guid in AssetDatabase.FindAssets("t:UnitTypeConfig", new[] { "Assets/Game" }))
            {
                var template = AssetDatabase.LoadAssetAtPath<UnitTypeConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.spawnConfig == null) continue;
                templates.Add(template); labels.Add((template.combatConfig != null && template.combatConfig.projectileRange > 0f ? "远程" : "近战") + " · " + template.unitTypeName + "  [" + template.name + "]");
            }
            int templateIndex = rosterTemplate != null ? templates.IndexOf(rosterTemplate) + 1 : 0;
            var source = new PopupField<string>("可用兵种", labels, Mathf.Max(0, templateIndex)) { name = "template-selector" };
            source.AddToClassList("stacked-field"); source.SetEnabled(!ReadOnly); card.Add(source);
            source.RegisterValueChangedCallback(evt =>
            {
                rosterTemplate = source.index > 0 ? templates[source.index - 1] : null; feedback = null; RefreshPage();
            });
            var custom = new Foldout { text = "从其他文件夹选择模板", value = rosterTemplate != null && templateIndex == 0 }; card.Add(custom);
            ObjectPicker(custom, "自选模板", rosterTemplate, false, value => { rosterTemplate = value; RefreshPage(); });
            if (rosterTemplate != null)
            {
                Text(card, "当前复制来源：" + rosterTemplate.unitTypeName + "。连续新增会一直使用这个模板。");
                var combat = rosterTemplate.combatConfig;
                if (combat != null)
                    Notice(card, (combat.projectileRange > 0f ? "远程兵 · 发射弹丸，射程 " + combat.projectileRange.ToString("0.#") + " 米" : "近战兵 · 靠近敌军后攻击") +
                        "\n生命 " + combat.maxHp + " · 单次伤害 " + combat.attackDamage + " · 攻击间隔 " + combat.attackInterval.ToString("0.##") + " 秒");
            }

            var destination = Card(pageContent, "2. 设置人数并选择加入方式");
            IntegerControl(destination, "新编成兵力（人）", newArmyCount, value => newArmyCount = value, "new-army-count");
            Text(destination, "默认 1,000 人，方便先检查站位。添加后可独立修改人数和阵型。", "field-help");
            var ids = new List<int>(); var teamLabels = new List<string>();
            if (Scenario.unitTypes != null) foreach (var unit in Scenario.unitTypes)
                if (unit != null && unit.teamId >= 0 && unit.teamId <= ConfigValidator.MaxTeamId && !ids.Contains(unit.teamId)) ids.Add(unit.teamId);
            ids.Sort(); foreach (int id in ids) teamLabels.Add(TeamName(id));
            if (ids.Count > 0)
            {
                int index = Mathf.Max(0, ids.IndexOf(newUnitTeamId)); newUnitTeamId = ids[index];
                var team = new PopupField<string>("加入现有军团", teamLabels, index);
                team.AddToClassList("stacked-field"); team.SetEnabled(!ReadOnly); destination.Add(team);
                team.RegisterValueChangedCallback(evt => newUnitTeamId = ids[team.index]);
            }
            string problem = TemplateProblem();
            if (problem != null) Notice(destination, problem);
            var same = ActionButton(destination, "添加到所选军团（成为友军）", () => AddRosterEntry(newUnitTeamId), "add-same-army", primary: true, writable: true);
            same.SetEnabled(!ReadOnly && problem == null && ids.Count > 0);
            int next = WarSandboxRosterEditor.ResolveNextTeamId(Scenario);
            var independent = ActionButton(destination, "添加独立军团" + (next <= ConfigValidator.MaxTeamId ? " " + next + "（与其他军团敌对）" : "（编号已用到上限）"),
                () => AddRosterEntry(next), "add-independent-army", writable: true);
            independent.SetEnabled(!ReadOnly && problem == null && next <= ConfigValidator.MaxTeamId);
            if (ids.Count == 0) Text(destination, "当前没有军团，请使用“添加独立军团”。", "field-help");
            if (next > ConfigValidator.MaxTeamId) Notice(destination, "新军团编号已到上限（0～7）。仍可给现有军团添加兵种。");
            Text(destination, "新增会复制兵种和出生配置；战斗、移动和渲染参数仍与模板共享。新军团先沿用模板位置，下一步必须检查是否重叠。", "field-help");
        }

        private string TemplateProblem()
        {
            if (rosterTemplate == null) return "先在上方选择一个兵种模板，添加按钮才会启用。";
            if (rosterTemplate.spawnConfig == null) return "此模板缺少出生配置，请换一个完整模板。";
            if (!AssetDatabase.Contains(rosterTemplate) || !AssetDatabase.Contains(rosterTemplate.spawnConfig)) return "模板和出生配置需要先保存为项目资产。";
            if (Scenario == null || !AssetDatabase.Contains(Scenario)) return "战役配置需要先保存为项目资产。";
            return null;
        }

        private void AddRosterEntry(int teamId)
        {
            if (ReadOnly) return;
            string displayName = "军团" + teamId + " " + rosterTemplate.unitTypeName;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            if (!WarSandboxRosterEditor.TryAddUnitType(Scenario, rosterTemplate, teamId, displayName, out UnitTypeConfig added, out string error))
            { SetFeedback("新增失败：请检查模板与战役是否已保存。\n" + error, true); return; }
            Edit(added.spawnConfig, "Set New Formation Strength", () => added.spawnConfig.unitCount = Mathf.Max(1, newArmyCount));
            Undo.CollapseUndoOperations(group);
            selectedArmy = Scenario.unitTypes.Length - 1; newUnitTeamId = teamId; addingArmy = false;
            SetFeedback("已添加“" + added.unitTypeName + "”，并选中新编成。检查人数后去战场布阵；模板来源保持不变。");
        }

        private void RemoveSelectedArmy()
        {
            if (ReadOnly) return;
            if (!WarSandboxRosterEditor.RemoveUnitType(Scenario, selectedArmy, out string error)) { SetFeedback(error, true); return; }
            selectedArmy = Mathf.Max(0, selectedArmy - 1); SetFeedback("已从战役中移除编成，资产文件仍保留。按 Ctrl+Z 可恢复。");
        }

        private IntegerField IntegerControl(VisualElement parent, string title, int current, Action<int> changed, string name)
        {
            var field = new IntegerField(title) { value = current, isDelayed = true, name = name };
            field.AddToClassList("stacked-field"); field.SetEnabled(!ReadOnly); parent.Add(field);
            field.RegisterValueChangedCallback(evt =>
            {
                if (ReadOnly) return;
                int value = Mathf.Max(1, evt.newValue); field.SetValueWithoutNotify(value); changed(value);
            });
            return field;
        }

        private FloatField FloatControl(VisualElement parent, string title, float current, float min, float max, Action<float> changed, string name)
        {
            var field = new FloatField(title) { value = current, isDelayed = true, name = name };
            field.AddToClassList("stacked-field"); field.SetEnabled(!ReadOnly); parent.Add(field);
            field.RegisterValueChangedCallback(evt =>
            {
                if (ReadOnly) return;
                if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)) { field.SetValueWithoutNotify(evt.previousValue); return; }
                float value = Mathf.Clamp(evt.newValue, min, max); field.SetValueWithoutNotify(value); changed(value);
            });
            return field;
        }

        private static string TeamName(int id) => "军团 " + id + (id == 0 ? " · 默认攻方" : id == 1 ? " · 默认守方" : " · 独立军团");
        private static string DescribeArmy(UnitTypeConfig unit) => unit == null ? "引用缺失" : "军团 " + unit.teamId + " / " + unit.unitTypeName +
            (unit.spawnConfig != null ? " / " + unit.spawnConfig.unitCount.ToString("N0") + " 人" : " / 缺少出生配置");
    }
}
