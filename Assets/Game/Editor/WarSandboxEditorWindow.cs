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
        [SerializeField] private WarSandboxScenarioPreset battlefieldPreset;

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
            DrawBattlefieldPresetToolbar();

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
                int attackers = WarSandboxScenarioPresets.ResolveTeamUnitCount(scenario, 0);
                int defenders = WarSandboxScenarioPresets.ResolveTeamUnitCount(scenario, 1);
                EditorGUILayout.LabelField("当前双方兵力", attackers + " vs " + defenders);
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

        private void DrawBattlefieldPresetToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("可复用战场方案", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "保存/载入双方部署、世界网格、流场、战斗模式、据点与静态障碍。载入会修改当前配置资产，但支持 Undo。",
                MessageType.Info);
            battlefieldPreset = (WarSandboxScenarioPreset)EditorGUILayout.ObjectField(
                "战场方案资产", battlefieldPreset, typeof(WarSandboxScenarioPreset), false);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("新建并捕获当前战场", GUILayout.Height(26f)))
                    CreateAndCaptureBattlefieldPreset();

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(battlefieldPreset == null))
                {
                    if (GUILayout.Button("覆盖保存当前战场"))
                        CaptureCurrentBattlefield();
                    if (GUILayout.Button("载入到当前场景"))
                        ApplySelectedBattlefield();
                    if (GUILayout.Button("定位资产"))
                        Selection.activeObject = battlefieldPreset;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("请退出 Play Mode 后再保存或载入战场方案。", MessageType.Warning);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void CreateAndCaptureBattlefieldPreset()
        {
            string sceneName = manager != null && manager.gameObject.scene.IsValid()
                ? manager.gameObject.scene.name
                : "WarSandbox";
            string path = EditorUtility.SaveFilePanelInProject(
                "新建战场方案",
                sceneName + "_ScenarioPreset",
                "asset",
                "请选择战场方案资产的保存位置。",
                "Assets/Game/Settings");
            if (string.IsNullOrEmpty(path))
                return;

            var preset = CreateInstance<WarSandboxScenarioPreset>();
            AssetDatabase.CreateAsset(preset, path);
            battlefieldPreset = preset;
            if (!CaptureCurrentBattlefield())
            {
                Debug.LogError("战场方案资产已创建，但捕获失败；请检查场景 Manager 的 ScenarioConfig。", preset);
                return;
            }

            Selection.activeObject = preset;
            EditorGUIUtility.PingObject(preset);
        }

        private bool CaptureCurrentBattlefield()
        {
            WarSandboxBattleController controller = ResolveBattleController(true);
            if (!WarSandboxScenarioPresetAuthoring.Capture(battlefieldPreset, manager, controller))
            {
                Debug.LogError("捕获战场方案失败：需要有效的 Manager、BattleController 和 ScenarioConfig。", manager);
                return false;
            }

            Debug.Log("已保存战场方案：" + battlefieldPreset.name, battlefieldPreset);
            Repaint();
            return true;
        }

        private void ApplySelectedBattlefield()
        {
            if (!EditorUtility.DisplayDialog(
                    "载入战场方案",
                    "这会用方案快照覆盖当前双方部署及相关系统配置。该操作支持 Undo。",
                    "载入",
                    "取消"))
                return;

            WarSandboxBattleController controller = ResolveBattleController(true);
            if (!WarSandboxScenarioPresetAuthoring.Apply(battlefieldPreset, manager, controller))
            {
                Debug.LogError("载入战场方案失败：方案缺少 ScenarioConfig 或当前场景对象无效。", battlefieldPreset);
                return;
            }

            Debug.Log("已载入战场方案：" + battlefieldPreset.name, battlefieldPreset);
            SceneView.RepaintAll();
            Repaint();
        }

        private WarSandboxBattleController ResolveBattleController(bool createIfMissing)
        {
            if (manager == null)
                return null;

            WarSandboxBattleController controller = manager.GetComponent<WarSandboxBattleController>();
            if (controller == null && createIfMissing)
                controller = Undo.AddComponent<WarSandboxBattleController>(manager.gameObject);
            if (controller != null && controller.manager != manager)
            {
                Undo.RecordObject(controller, "Assign War Sandbox Manager");
                controller.manager = manager;
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }
            return controller;
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
            EditorGUILayout.PropertyField(unitObject.FindProperty("teamId"), new GUIContent("阵营（0攻/1守）"));
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
