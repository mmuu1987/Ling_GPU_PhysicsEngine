using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// First authoring surface for the war sandbox. It edits army intent assets rather
    /// than exposing engine buffers/pipeline details.
    /// </summary>
    public sealed class WarSandboxEditorWindow : EditorWindow
    {
        private MassEngineManager manager;
        private Vector2 scroll;
        [SerializeField, Min(0f)] private float engagementGap = WarSandboxFormationLayout.DefaultEngagementGap;
        [SerializeField] private WarSandboxScalePreset scalePreset = WarSandboxScalePreset.Standard10K;
        [SerializeField, Min(1)] private int customUnitsPerTeam = 10000;
        [SerializeField] private WarSandboxDeploymentPlan deploymentPlan;
        [SerializeField] private UnitTypeConfig rosterTemplate;
        [SerializeField, Min(0)] private int newUnitTeamId;
        private string planFeedback;
        private string rosterFeedback;
        private MessageType planFeedbackType;
        private MessageType rosterFeedbackType;

        [MenuItem("MassEngine/War Sandbox Editor")]
        public static void Open()
        {
            GetWindow<WarSandboxEditorWindow>("War Sandbox");
        }

        private void OnEnable()
        {
            minSize = new Vector2(360f, 400f);
            ResolveManager();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("战争沙盒编辑器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "只编辑游戏意图：兵力、阵营、出生中心和阵型。Auto-Fit 会保持固定阵前距离，并配平世界/网格/流场；运行时不会写回这些资产。",
                MessageType.Info);

            manager = (MassEngineManager)EditorGUILayout.ObjectField("场景 Manager", manager, typeof(MassEngineManager), true);
            if (manager == null && GUILayout.Button("查找当前场景 Manager"))
                ResolveManager();

            if (manager == null)
            {
                EditorGUILayout.HelpBox("当前场景没有 MassEngineManager。", MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                DrawAuthoring();
            EditorGUILayout.EndScrollView();
        }

        private void DrawAuthoring()
        {
            EditorGUI.BeginChangeCheck();
            ScenarioConfig scenarioConfig = (ScenarioConfig)EditorGUILayout.ObjectField(
                "战役方案", manager.scenarioConfig, typeof(ScenarioConfig), false);
            MassEngineSystemConfig systemConfig = (MassEngineSystemConfig)EditorGUILayout.ObjectField(
                "系统配置", manager.systemConfig, typeof(MassEngineSystemConfig), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(manager, "Edit War Sandbox Manager");
                manager.scenarioConfig = scenarioConfig;
                manager.systemConfig = systemConfig;
                EditorUtility.SetDirty(manager);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }

            DrawDeploymentPlans();
            DrawToolbar();

            ScenarioConfig scenario = manager.scenarioConfig;
            if (scenario == null || scenario.unitTypes == null)
            {
                EditorGUILayout.HelpBox("请选择包含兵种的 ScenarioConfig。", MessageType.Warning);
                return;
            }

            DrawRosterComposer(scenario);
            for (int i = 0; i < scenario.unitTypes.Length; i++)
                DrawArmy(scenario, i, scenario.unitTypes[i]);
        }

        private void DrawRosterComposer(ScenarioConfig scenario)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("军团与兵种编排", EditorStyles.boldLabel);
            rosterTemplate = (UnitTypeConfig)EditorGUILayout.ObjectField(
                "复制兵种模板", rosterTemplate, typeof(UnitTypeConfig), false);
            newUnitTeamId = Mathf.Max(0, EditorGUILayout.IntField("新编成 teamId", newUnitTeamId));
            EditorGUILayout.LabelField(
                "当前军团数", WarSandboxScenarioPresets.ResolveTeamCount(scenario).ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加兵种编成"))
                AddRosterEntry(newUnitTeamId, "");
            if (GUILayout.Button("添加新军团"))
                AddRosterEntry(WarSandboxRosterEditor.ResolveNextTeamId(scenario), "Army");
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(rosterFeedback))
                EditorGUILayout.HelpBox(rosterFeedback, rosterFeedbackType);
        }

        private void AddRosterEntry(int teamId, string prefix)
        {
            string templateName = rosterTemplate != null ? rosterTemplate.unitTypeName : string.Empty;
            string displayName = string.IsNullOrEmpty(prefix)
                ? templateName + " Variant"
                : prefix + " " + (teamId + 1) + " " + templateName;
            if (!WarSandboxRosterEditor.TryAddUnitType(
                    manager.scenarioConfig, rosterTemplate, teamId, displayName,
                    out UnitTypeConfig added, out string error))
            {
                SetRosterFeedback(error, MessageType.Error);
                return;
            }

            newUnitTeamId = teamId;
            rosterTemplate = added;
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            SetRosterFeedback("已添加：" + added.unitTypeName, MessageType.Info);
            GUIUtility.ExitGUI();
        }

        private void SetRosterFeedback(string message, MessageType type)
        {
            rosterFeedback = message;
            rosterFeedbackType = type;
        }

        private void DrawDeploymentPlans()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("部署方案", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            deploymentPlan = (WarSandboxDeploymentPlan)EditorGUILayout.ObjectField(
                "方案资产", deploymentPlan, typeof(WarSandboxDeploymentPlan), false);
            if (EditorGUI.EndChangeCheck())
                planFeedback = null;

            if (deploymentPlan != null)
                EditorGUILayout.LabelField("已存兵种数", deploymentPlan.UnitTypeCount.ToString());

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("另存方案", EditorGUIUtility.IconContent("SaveAs").image)))
                SaveDeploymentPlanAs();
            using (new EditorGUI.DisabledScope(deploymentPlan == null))
            {
                if (GUILayout.Button(new GUIContent("覆盖方案", EditorGUIUtility.IconContent("SaveActive").image)) &&
                    EditorUtility.DisplayDialog("覆盖部署方案", "替换「" + deploymentPlan.name + "」中的部署数据？", "覆盖", "取消"))
                    CaptureDeploymentPlan(deploymentPlan);
                if (GUILayout.Button(new GUIContent("载入方案", EditorGUIUtility.IconContent("FolderOpened Icon").image)))
                    LoadDeploymentPlan();
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(planFeedback))
                EditorGUILayout.HelpBox(planFeedback, planFeedbackType);
        }

        private void SaveDeploymentPlanAs()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "另存部署方案", "DeploymentPlan", "asset", "选择部署方案的保存位置");
            if (string.IsNullOrEmpty(path))
                return;

            Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                WarSandboxDeploymentPlan existingPlan = existing as WarSandboxDeploymentPlan;
                if (existingPlan == null)
                {
                    SetPlanFeedback("该路径已有其他类型的资产，未覆盖。", MessageType.Error);
                    return;
                }
                if (EditorUtility.DisplayDialog("覆盖部署方案", "替换「" + existingPlan.name + "」中的部署数据？", "覆盖", "取消") &&
                    CaptureDeploymentPlan(existingPlan))
                    deploymentPlan = existingPlan;
                return;
            }

            WarSandboxDeploymentPlan created = CreateInstance<WarSandboxDeploymentPlan>();
            if (!created.TryCapture(manager, engagementGap, out string error))
            {
                DestroyImmediate(created);
                SetPlanFeedback(error, MessageType.Error);
                return;
            }
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            deploymentPlan = created;
            EditorGUIUtility.PingObject(created);
            SetPlanFeedback("已保存：" + created.name, MessageType.Info);
        }

        private bool CaptureDeploymentPlan(WarSandboxDeploymentPlan plan)
        {
            if (!plan.TryCapture(manager, engagementGap, out string error))
            {
                SetPlanFeedback(error, MessageType.Error);
                return false;
            }
            AssetDatabase.SaveAssets();
            SetPlanFeedback("已保存：" + plan.name, MessageType.Info);
            return true;
        }

        private void LoadDeploymentPlan()
        {
            if (!deploymentPlan.TryApply(manager, out string error))
            {
                SetPlanFeedback(error, MessageType.Error);
                return;
            }
            Undo.RecordObject(this, "Load War Sandbox Deployment");
            engagementGap = deploymentPlan.EngagementGap;
            SceneView.RepaintAll();
            SetPlanFeedback("已载入：" + deploymentPlan.name, MessageType.Info);
        }

        private void SetPlanFeedback(string message, MessageType type)
        {
            planFeedback = message;
            planFeedbackType = type;
        }

        private void DrawToolbar()
        {
            EditorGUILayout.Space();
            scalePreset = (WarSandboxScalePreset)EditorGUILayout.Popup(
                "战役规模", (int)scalePreset, WarSandboxScenarioPresets.DisplayNames);
            if (scalePreset == WarSandboxScalePreset.Custom)
                customUnitsPerTeam = Mathf.Max(1, EditorGUILayout.IntField("自定义每方兵力", customUnitsPerTeam));

            WarSandboxPresetDefinition definition =
                WarSandboxScenarioPresets.GetDefinition(scalePreset, customUnitsPerTeam);
            engagementGap = Mathf.Max(0f, EditorGUILayout.FloatField("初始交战间距（m）", engagementGap));
            EditorGUILayout.HelpBox(definition.performanceNote, definition.messageType);

            ScenarioConfig scenario = manager.scenarioConfig;
            if (scenario != null)
            {
                EditorGUILayout.LabelField("当前各方兵力", DescribeTeamStrengths(scenario));
            }

            SimulationConfig simulation = manager.systemConfig != null ? manager.systemConfig.simulationConfig : null;
            if (simulation != null)
            {
                EditorGUILayout.LabelField(
                    "当前世界尺寸",
                    simulation.simulationWorldSize.x.ToString("F0") + " × " +
                    simulation.simulationWorldSize.y.ToString("F0") + " m");
            }

            if (GUILayout.Button("应用预设并 Auto-Fit", GUILayout.Height(28f)))
                ApplyPreset(definition);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto-Fit 布阵与场景"))
                ScenarioAutoFit.AutoFit(engagementGap);
            if (GUILayout.Button("保存配置"))
            {
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveOpenScenes();
            }
            if (GUILayout.Button("选中 Manager"))
                Selection.activeObject = manager.gameObject;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void ApplyPreset(WarSandboxPresetDefinition definition)
        {
            ScenarioConfig scenario = manager != null ? manager.scenarioConfig : null;
            if (scenario == null)
            {
                Debug.LogError("应用战役预设失败：当前 Manager 没有 ScenarioConfig。", manager);
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply War Sandbox Preset");
            WarSandboxScenarioPresets.ApplyPerTeamUnitCount(scenario, definition.unitsPerTeam);
            ScenarioAutoFit.AutoFit(engagementGap);
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Head count per team, in teamId order. Reads the roster rather than the attacker/defender
        /// pair so an extra army shows up here instead of silently inflating the agent budget.
        /// </summary>
        private static string DescribeTeamStrengths(ScenarioConfig scenario)
        {
            int teamCount = WarSandboxScenarioPresets.ResolveTeamCount(scenario);
            var text = new System.Text.StringBuilder();
            for (int teamId = 0; teamId < teamCount; teamId++)
            {
                if (teamId > 0)
                    text.Append(" vs ");
                text.Append(WarSandboxScenarioPresets.ResolveTeamUnitCount(scenario, teamId));
            }

            return text.ToString();
        }

        private void DrawArmy(ScenarioConfig scenario, int index, UnitTypeConfig unitType)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            string label = unitType != null && !string.IsNullOrEmpty(unitType.unitTypeName)
                ? unitType.unitTypeName
                : "未命名编成";
            EditorGUILayout.LabelField("编成 " + (index + 1) + "  " + label, EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("移除", EditorGUIUtility.IconContent("TreeEditor.Trash").image),
                    GUILayout.Width(58f)))
            {
                if (WarSandboxRosterEditor.RemoveUnitType(scenario, index, out string error))
                    SetRosterFeedback("已从当前战役移除编成；资产仍保留。", MessageType.Info);
                else
                    SetRosterFeedback(error, MessageType.Error);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (unitType == null)
            {
                EditorGUILayout.HelpBox("兵种配置为空。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.ObjectField("兵种配置", unitType, typeof(UnitTypeConfig), false);

            SerializedObject unitObject = new SerializedObject(unitType);
            unitObject.Update();
            EditorGUILayout.PropertyField(
                unitObject.FindProperty("teamId"),
                new GUIContent("阵营（0=攻方，1=守方，2 及以上为额外军团）"));
            unitObject.ApplyModifiedProperties();

            SpawnConfig spawn = unitType.spawnConfig;
            if (spawn == null)
            {
                EditorGUILayout.HelpBox("缺少 SpawnConfig。", MessageType.Error);
                EditorGUILayout.EndVertical();
                return;
            }

            SerializedObject spawnObject = new SerializedObject(spawn);
            spawnObject.Update();
            EditorGUILayout.PropertyField(spawnObject.FindProperty("unitCount"), new GUIContent("兵力"));
            EditorGUILayout.PropertyField(spawnObject.FindProperty("spawnCenter"), new GUIContent("出生中心"));
            EditorGUILayout.PropertyField(spawnObject.FindProperty("formationDensity"), new GUIContent("阵型密度（人/m²）"));
            EditorGUILayout.PropertyField(spawnObject.FindProperty("formationAspect"), new GUIContent("阵面宽深比"));
            EditorGUILayout.PropertyField(spawnObject.FindProperty("spawnSize"), new GUIContent("手动脚印覆盖"));
            spawnObject.ApplyModifiedProperties();

            Vector3 resolvedSize = spawn.ResolveSpawnSize();
            EditorGUILayout.LabelField("推导脚印", resolvedSize.x.ToString("F0") + " × " + resolvedSize.z.ToString("F0") + " m");
            EditorGUILayout.EndVertical();
        }

        private void ResolveManager()
        {
            manager = Object.FindFirstObjectByType<MassEngineManager>();
        }
    }
}
