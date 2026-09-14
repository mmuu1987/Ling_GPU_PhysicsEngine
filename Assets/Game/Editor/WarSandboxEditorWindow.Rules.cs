using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public sealed partial class WarSandboxEditorWindow
    {
        [SerializeField] private WarSandboxBattlefieldConfig selectedBattlefieldRules;
        [SerializeField] private bool showBattlefieldRules = true;
        private WarSandboxBattleController RuleController => manager != null
            ? manager.GetComponent<WarSandboxBattleController>() : null;

        private void DrawBattlefieldRules()
        {
            var section = new Foldout { text = "战场规则与障碍", value = showBattlefieldRules, name = "battlefield-rules" };
            section.RegisterValueChangedCallback(evt => showBattlefieldRules = section.value);
            pageContent.Add(section);
            WarSandboxBattleController controller = RuleController;
            if (controller == null || manager.GetComponent<WarSandboxCommandHUD>() == null)
            {
                ActionButton(section, controller == null ? "创建场景规则与指挥组件" : "补齐场景指挥组件", () =>
                {
                    if (!WarSandboxBattlefieldEditor.TryEnsureController(manager, out _, out string error))
                        SetFeedback(error, true);
                    else SetFeedback("场景规则与指挥组件已创建。");
                }, "create-rule-controller", writable: true);
                if (controller == null) return;
            }

            Text(section, "当前绑定：" + (controller.battlefieldConfig != null ? controller.battlefieldConfig.name : "无（场景草稿）"));
            string problem = BattlefieldProblem();
            if (problem != null) Notice(section, problem, true);
            var picker = new ObjectField("规则资产")
            {
                objectType = typeof(WarSandboxBattlefieldConfig), allowSceneObjects = false,
                value = selectedBattlefieldRules, name = "rules-asset",
                tooltip = "选择后点击载入。修改场景规则会解除绑定；另存或覆盖后重新绑定。"
            };
            picker.AddToClassList("stacked-field"); picker.SetEnabled(!ReadOnly); section.Add(picker);
            picker.RegisterValueChangedCallback(evt =>
            {
                if (ReadOnly) return;
                selectedBattlefieldRules = evt.newValue as WarSandboxBattlefieldConfig; RefreshPage();
            });
            var actions = Element(section, "actions");
            ActionButton(actions, "另存规则…", SaveBattlefieldRulesAs, "save-rules", writable: true);
            var load = ActionButton(actions, "载入规则", LoadBattlefieldRules, "load-rules", writable: true);
            var overwrite = ActionButton(actions, "覆盖规则…", () =>
            {
                if (EditorUtility.DisplayDialog("覆盖战场规则", "替换“" + selectedBattlefieldRules.name + "”中的规则与障碍？", "覆盖", "取消"))
                    CaptureBattlefieldRules(selectedBattlefieldRules);
            }, "overwrite-rules", writable: true);
            load.SetEnabled(!ReadOnly && selectedBattlefieldRules != null);
            overwrite.SetEnabled(!ReadOnly && selectedBattlefieldRules != null);

            var mode = new PopupField<string>("胜负模式", new System.Collections.Generic.List<string> { "歼灭战", "据点战" },
                controller.gameMode == WarSandboxGameMode.ControlPoint ? 1 : 0) { name = "rules-mode" };
            mode.AddToClassList("stacked-field"); mode.SetEnabled(!ReadOnly); section.Add(mode);
            mode.RegisterValueChangedCallback(evt => EditBattlefield(controller, () => controller.gameMode = (WarSandboxGameMode)mode.index));
            var center = new Vector3Field("据点中心") { value = controller.controlPointCenter, name = "rules-center" };
            center.AddToClassList("stacked-field"); center.SetEnabled(!ReadOnly); section.Add(center);
            center.Query<FloatField>().ForEach(field => field.isDelayed = true);
            center.RegisterValueChangedCallback(evt => EditBattlefield(controller, () => controller.controlPointCenter = evt.newValue));
            FloatControl(section, "据点半径（米）", controller.controlPointRadius, 2f, 100000f,
                value => EditBattlefield(controller, () => controller.controlPointRadius = value), "rules-radius");
            FloatControl(section, "占领时长（秒）", controller.controlPointCaptureSeconds, 5f, 100000f,
                value => EditBattlefield(controller, () => controller.controlPointCaptureSeconds = value), "rules-capture-seconds");
            var enabled = new Toggle("启用静态障碍") { value = controller.staticObstaclesEnabled, name = "rules-obstacles-enabled" };
            enabled.SetEnabled(!ReadOnly); section.Add(enabled);
            enabled.RegisterValueChangedCallback(evt => EditBattlefield(controller, () => controller.staticObstaclesEnabled = evt.newValue));
            FloatControl(section, "障碍避让间距（米）", controller.staticObstacleClearance, 0f, 100000f,
                value => EditBattlefield(controller, () => controller.staticObstacleClearance = value), "rules-clearance");

            StaticObstacleRect[] obstacles = controller.CaptureBattlefieldRules().staticObstacles;
            Text(section, "障碍矩形 " + obstacles.Length + " / " + StaticObstacleMath.MaxObstacleCount +
                (controller.useCustomStaticObstacleLayout ? " · 自定义布局" : " · 内置布局"));
            for (int i = 0; i < obstacles.Length; i++)
            {
                int index = i;
                var item = new Foldout { text = "障碍 " + (i + 1), value = false }; section.Add(item);
                var location = new Vector2Field("中心 X / Z") { value = obstacles[i].center, name = "obstacle-center-" + i };
                var size = new Vector2Field("尺寸 X / Z") { value = obstacles[i].size, name = "obstacle-size-" + i };
                item.Add(location); item.Add(size);
                location.AddToClassList("stacked-field"); size.AddToClassList("stacked-field");
                item.Query<FloatField>().ForEach(field => field.isDelayed = true);
                location.SetEnabled(!ReadOnly); size.SetEnabled(!ReadOnly);
                location.RegisterValueChangedCallback(evt => EditObstacle(controller, index, evt.newValue, null));
                size.RegisterValueChangedCallback(evt => EditObstacle(controller, index, null, evt.newValue));
                var remove = ActionButton(item, "−", () => EditBattlefield(controller, () =>
                {
                    var layout = new System.Collections.Generic.List<StaticObstacleRect>(controller.CaptureBattlefieldRules().staticObstacles);
                    layout.RemoveAt(index); controller.staticObstacles = layout.ToArray(); controller.useCustomStaticObstacleLayout = true;
                }), "remove-obstacle-" + i, writable: true);
                remove.tooltip = "移除障碍 " + (i + 1); remove.AddToClassList("rule-icon");
            }
            var add = ActionButton(section, "+", () => EditBattlefield(controller, () =>
            {
                var layout = new System.Collections.Generic.List<StaticObstacleRect>(controller.CaptureBattlefieldRules().staticObstacles);
                layout.Add(new StaticObstacleRect(new Vector2(0f, 80f), new Vector2(14f, 30f)));
                controller.staticObstacles = layout.ToArray(); controller.useCustomStaticObstacleLayout = true;
            }), "add-obstacle", writable: true);
            add.tooltip = "添加障碍矩形"; add.AddToClassList("rule-icon");
            add.SetEnabled(!ReadOnly && obstacles.Length < StaticObstacleMath.MaxObstacleCount);
        }

        private void EditBattlefield(WarSandboxBattleController controller, Action change)
        {
            if (ReadOnly || controller == null) return;
            Edit(controller, "Edit War Sandbox Battlefield Rules", () =>
            {
                change(); controller.battlefieldConfig = null;
                WarSandboxBattlefieldEditor.MarkSceneObject(controller);
            });
            RefreshPage();
        }

        private void EditObstacle(WarSandboxBattleController controller, int index, Vector2? center, Vector2? size)
        {
            EditBattlefield(controller, () =>
            {
                var layout = controller.CaptureBattlefieldRules().staticObstacles;
                if (center.HasValue) layout[index].center = center.Value;
                if (size.HasValue) layout[index].size = size.Value;
                controller.staticObstacles = layout; controller.useCustomStaticObstacleLayout = true;
            });
        }

        private string BattlefieldProblem()
        {
            var controller = RuleController;
            if (controller == null) return null;
            string error;
            bool valid = controller.battlefieldConfig != null
                ? controller.battlefieldConfig.TryCreateSnapshot(out _, out error)
                : controller.CaptureBattlefieldRules().TryValidate(out error);
            return valid ? null : "战场规则无效：" + error;
        }

        private void SaveBattlefieldRulesAs()
        {
            if (ReadOnly) return;
            if (!WarSandboxBattlefieldEditor.TryCaptureSnapshot(RuleController, out WarSandboxBattlefieldRules snapshot, out string error))
            { SetFeedback("规则未保存：" + error, true); return; }
            string path = EditorUtility.SaveFilePanelInProject("另存战场规则", "BattlefieldRules", "asset", "选择规则资产的保存位置");
            if (string.IsNullOrEmpty(path)) return;
            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                if (!(existing is WarSandboxBattlefieldConfig config))
                { SetFeedback("该位置已有其他类型的资产。", true); return; }
                if (EditorUtility.DisplayDialog("覆盖战场规则", "替换“" + config.name + "”？", "覆盖", "取消")) CaptureBattlefieldRules(config);
                return;
            }
            var created = CreateInstance<WarSandboxBattlefieldConfig>();
            created.rules = snapshot;
            AssetDatabase.CreateAsset(created, path);
            CaptureBattlefieldRules(created);
            EditorGUIUtility.PingObject(created);
        }

        private void CaptureBattlefieldRules(WarSandboxBattlefieldConfig config)
        {
            if (ReadOnly) return;
            if (!WarSandboxBattlefieldEditor.TryCapture(RuleController, config, out string error))
            { SetFeedback("规则未保存：" + error, true); return; }
            AssetDatabase.SaveAssetIfDirty(config); selectedBattlefieldRules = config;
            SetFeedback("已保存规则“" + config.name + "”。部署方案未改动。");
        }

        private void LoadBattlefieldRules()
        {
            if (ReadOnly) return;
            if (!WarSandboxBattlefieldEditor.TryApply(RuleController, selectedBattlefieldRules, out string error))
            { SetFeedback("规则未载入：" + error, true); return; }
            SceneView.RepaintAll(); SetFeedback("已载入规则“" + selectedBattlefieldRules.name + "”。部署方案未改动。");
        }
    }
}
