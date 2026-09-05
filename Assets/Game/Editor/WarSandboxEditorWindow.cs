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

        [MenuItem("MassEngine/War Sandbox Editor")]
        public static void Open()
        {
            GetWindow<WarSandboxEditorWindow>("War Sandbox");
        }

        private void OnEnable()
        {
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

            DrawToolbar();

            ScenarioConfig scenario = manager.scenarioConfig;
            if (scenario == null || scenario.unitTypes == null)
            {
                EditorGUILayout.HelpBox("请选择包含兵种的 ScenarioConfig。", MessageType.Warning);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < scenario.unitTypes.Length; i++)
                DrawArmy(i, scenario.unitTypes[i]);
            EditorGUILayout.EndScrollView();
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

        private static void DrawArmy(int index, UnitTypeConfig unitType)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("军团 " + (index + 1), EditorStyles.boldLabel);

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
