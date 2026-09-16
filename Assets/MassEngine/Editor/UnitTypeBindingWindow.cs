using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Editor
{
    /// <summary>
    /// 兵种绑定向导：把 VAT profile 接到兵种渲染配置上，并当场校验整条绑定链。
    ///
    /// 与 <see cref="VatBakerWindow"/> 同构 —— 这里只是表单外壳，
    /// 落位、校验、建整套兵种的逻辑全部走 <see cref="UnitTypeBinder"/>，
    /// 避免窗口与批处理各写一份接线规则。
    ///
    /// 放在 MassEngine.Editor 而不是 Game.Editor：UnitTypeConfig / ScenarioConfig /
    /// ConfigValidator / RenderConfig 都在 MassEngine 程序集里，这里零 asmdef 改动就能全用到。
    /// </summary>
    public sealed class UnitTypeBindingWindow : EditorWindow
    {
        private const string DefaultDirectory = "Assets/Game/Settings";

        [SerializeField] private ScenarioConfig scenario;
        [SerializeField] private int selectedIndex = -1;
        [SerializeField] private ScriptableObject profile;
        [SerializeField] private bool showCreate = true;
        [SerializeField] private UnitTypeConfig template;
        [SerializeField] private string newUnitName = "新兵种";
        [SerializeField] private int newTeamId;
        [SerializeField] private string outputDirectory = DefaultDirectory;
        [SerializeField] private bool exclusiveRender = true;

        private string feedback;
        private bool feedbackIsError;
        private Vector2 scroll;
        private readonly List<string> bindingReport = new List<string>();

        private bool ReadOnly => EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("MassEngine/兵种绑定向导")]
        public static void Open() => GetWindow<UnitTypeBindingWindow>("兵种绑定");

        private void OnEnable()
        {
            if (scenario == null)
                scenario = FindScenario();
            if (template == null && scenario != null && scenario.unitTypes != null && scenario.unitTypes.Length > 0)
                template = scenario.unitTypes[0];
            if (newTeamId <= 0)
                newTeamId = ResolveNextTeamId();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("兵种绑定向导（VAT profile -> 兵种渲染配置）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "把 VAT profile 接到兵种的 RenderConfig，并校验整条绑定链。\n" +
                "LOD 落位规则与运行时 ResolvedUnitTypeRuntime 完全一致：near 恒为 cleanMesh，" +
                "mid 优先 midLod（缺失退 lowLod），far 优先 lowLod（缺失退 midLod）。\n" +
                "校验只读，绑定只改 RenderConfig 的 profile 引用与三个网格槽位。", MessageType.Info);

            EditorGUILayout.Space();
            scenario = (ScenarioConfig)EditorGUILayout.ObjectField(
                "战役配置", scenario, typeof(ScenarioConfig), false);
            if (scenario == null)
            {
                EditorGUILayout.HelpBox(
                    "兵种必须登记进 ScenarioConfig 才会被运行时加载 —— 运行时按清单取兵种，不扫文件夹。",
                    MessageType.Warning);
            }

            DrawRoster();
            DrawBinding();
            DrawCreate();

            if (!string.IsNullOrEmpty(feedback))
                EditorGUILayout.HelpBox(feedback, feedbackIsError ? MessageType.Error : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------
        // 现有兵种
        // ------------------------------------------------------------------

        private void DrawRoster()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("现有兵种", EditorStyles.boldLabel);

            UnitTypeConfig[] units = scenario != null ? scenario.unitTypes : null;
            if (units == null || units.Length == 0)
            {
                EditorGUILayout.HelpBox("这份战役配置里还没有兵种。用下面的「新建兵种」建一个。", MessageType.None);
                return;
            }

            var labels = new string[units.Length];
            for (int i = 0; i < units.Length; i++)
                labels[i] = units[i] != null
                    ? string.Format("{0}（军团 {1}）", units[i].unitTypeName, units[i].teamId)
                    : "（空引用）";

            using (new EditorGUI.DisabledScope(ReadOnly))
            {
                int picked = EditorGUILayout.Popup("选择兵种", Mathf.Clamp(selectedIndex, 0, units.Length - 1), labels);
                if (picked != selectedIndex)
                {
                    selectedIndex = picked;
                    bindingReport.Clear();
                    feedback = null;
                    // 选中即把该兵种现有 profile 带出来，便于"看一眼再改"。
                    UnitTypeConfig unit = units[selectedIndex];
                    profile = unit != null && unit.renderConfig != null ? unit.renderConfig.vatProfile : null;
                }
            }

            if (selectedIndex < 0 || selectedIndex >= units.Length)
                selectedIndex = 0;

            UnitTypeConfig current = units[selectedIndex];
            if (current == null)
            {
                EditorGUILayout.HelpBox("这个清单项是空引用，请在战役配置里清掉它。", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("当前绑定", UnitTypeBinder.Describe(current.renderConfig));

            // 现役结构里同一军团的近战+远程共用一份 RenderConfig（即共用一个模型）。
            // 绑 profile 就是改这份共享资产，会同时影响另一个兵种 —— 必须说清楚影响面，
            // 否则用户以为只改了选中的那个。
            int shared = CountSharingRenderConfig(units, current.renderConfig);
            if (shared > 1)
                EditorGUILayout.HelpBox(
                    "注意：这份 RenderConfig 被 " + shared + " 个兵种共用，绑定会同时改变它们全部。",
                    MessageType.Warning);
        }

        private static int CountSharingRenderConfig(UnitTypeConfig[] units, RenderConfig render)
        {
            if (render == null)
                return 0;
            int count = 0;
            foreach (UnitTypeConfig unit in units)
                if (unit != null && unit.renderConfig == render)
                    count++;
            return count;
        }

        // ------------------------------------------------------------------
        // 绑定
        // ------------------------------------------------------------------

        private void DrawBinding()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("绑定 VAT profile", EditorStyles.boldLabel);
            profile = (ScriptableObject)EditorGUILayout.ObjectField(
                "VAT profile", profile, typeof(ScriptableObject), false);
            EditorGUILayout.HelpBox(
                "字段是弱类型 ScriptableObject：任何实现同一批字段名的 profile 类型都能用。",
                MessageType.None);

            if (profile != null)
            {
                if (UnitTypeBinder.TryReadProfile(profile, out VatProfileData data, out string readError))
                {
                    UnitTypeBinder.ResolveLodMeshes(data, out Mesh near, out Mesh mid, out Mesh far);
                    EditorGUILayout.HelpBox(string.Format(
                        "{0} 顶点 / {1} 帧 @ {2}fps / 纹理 {3}×{4}（每帧 {5} 行）\n" +
                        "将接成：near {6} / mid {7} / far {8}",
                        data.cleanMesh != null ? data.cleanMesh.vertexCount : 0,
                        data.totalFrameCount, data.frameRate, data.textureWidth, data.textureHeight,
                        data.rowsPerFrame, Name(near), Name(mid), Name(far)), MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox("VAT profile 不可读：" + readError, MessageType.Error);
                }
            }

            using (new EditorGUI.DisabledScope(ReadOnly || profile == null))
            {
                if (GUILayout.Button("绑定到选中兵种", GUILayout.Height(30)))
                    BindSelected();
            }

            using (new EditorGUI.DisabledScope(ReadOnly))
            {
                if (GUILayout.Button("只校验选中兵种（不改资产）"))
                    ValidateSelected();
            }

            if (bindingReport.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("校验结果", EditorStyles.boldLabel);
                foreach (string line in bindingReport)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            }
        }

        private UnitTypeConfig SelectedUnit()
        {
            if (scenario == null || scenario.unitTypes == null ||
                selectedIndex < 0 || selectedIndex >= scenario.unitTypes.Length)
                return null;
            return scenario.unitTypes[selectedIndex];
        }

        private void BindSelected()
        {
            UnitTypeConfig unit = SelectedUnit();
            if (unit == null)
            {
                SetFeedback("请先选择一个兵种。", true);
                return;
            }

            RenderConfig render = unit.renderConfig;
            if (render == null)
            {
                SetFeedback("该兵种没有 RenderConfig，无法绑定。请先在 Inspector 里补上，或改用下面的「新建兵种」。", true);
                return;
            }

            // 绑定要改资产，登记 Undo 让用户能撤回。
            Undo.RecordObject(render, "Bind VAT Profile");
            try
            {
                ValidationResult result = UnitTypeBinder.Bind(unit, profile);
                EditorUtility.SetDirty(render);
                AssetDatabase.SaveAssets();
                Report(result);
                SetFeedback(result.IsValid
                    ? "绑定完成：" + UnitTypeBinder.Describe(render)
                    : "绑定已写入，但校验未通过，请按下面的错误修掉。", !result.IsValid);
            }
            catch (Exception exception)
            {
                SetFeedback(exception.Message, true);
            }
        }

        private void ValidateSelected()
        {
            UnitTypeConfig unit = SelectedUnit();
            if (unit == null)
            {
                SetFeedback("请先选择一个兵种。", true);
                return;
            }
            ValidationResult result = UnitTypeBinder.ValidateBinding(unit);
            Report(result);
            SetFeedback(result.IsValid ? "校验通过。" : "校验未通过。", !result.IsValid);
        }

        private void Report(ValidationResult result)
        {
            bindingReport.Clear();
            foreach (string error in result.Errors)
                bindingReport.Add("错误：" + error);
            foreach (string warning in result.Warnings)
                bindingReport.Add("提示：" + warning);
            if (bindingReport.Count == 0)
                bindingReport.Add("没有发现问题。");
        }

        // ------------------------------------------------------------------
        // 新建兵种
        // ------------------------------------------------------------------

        private void DrawCreate()
        {
            EditorGUILayout.Space();
            showCreate = EditorGUILayout.Foldout(showCreate, "新建兵种（从模板生成整套子配置并登记进战役清单）");
            if (!showCreate)
                return;

            EditorGUI.indentLevel++;
            template = (UnitTypeConfig)EditorGUILayout.ObjectField(
                "数值模板", template, typeof(UnitTypeConfig), false);
            EditorGUILayout.HelpBox(
                "新兵种从模板复制：出生与战斗各自独占（人数、伤害是这个兵种自己的），" +
                "移动 / 群体 / 动画按模板共享。", MessageType.None);

            newUnitName = EditorGUILayout.TextField("兵种名", newUnitName);
            newTeamId = EditorGUILayout.IntSlider("军团编号", newTeamId, 0, ConfigValidator.MaxTeamId);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("用下一个空军团编号", GUILayout.Width(160)))
                    newTeamId = ResolveNextTeamId();
            }
            if (IsTeamIdTaken(newTeamId))
                EditorGUILayout.HelpBox(
                    "军团编号 " + newTeamId + " 已被清单里的兵种占用。同编号会与既有军团混在一起" +
                    "（共用流场与据点判定），确认是有意混编再继续。", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                outputDirectory = EditorGUILayout.TextField("输出目录", outputDirectory);
                if (GUILayout.Button("选择", GUILayout.Width(60)))
                {
                    string picked = EditorUtility.OpenFolderPanel("选择输出目录", outputDirectory, string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                        outputDirectory = ToAssetPath(picked);
                }
            }

            exclusiveRender = EditorGUILayout.Toggle("独占 RenderConfig", exclusiveRender);
            EditorGUILayout.HelpBox(exclusiveRender
                ? "绑新模型时必须独占：否则同一军团共享渲染配置的另一个兵种会跟着换模型。"
                : "沿用模板的共享渲染配置：只在新兵种用同一套模型时才这么选。", MessageType.None);

            using (new EditorGUI.DisabledScope(ReadOnly))
            {
                if (GUILayout.Button("新建并绑定", GUILayout.Height(30)))
                    CreateUnit();
            }
            EditorGUI.indentLevel--;
        }

        private void CreateUnit()
        {
            var request = new UnitTypeCreationRequest
            {
                scenario = scenario,
                template = template,
                unitTypeName = newUnitName,
                teamId = newTeamId,
                directory = outputDirectory,
                profile = profile,
                exclusiveRender = exclusiveRender
            };

            try
            {
                UnitTypeConfig created = UnitTypeBinder.CreateUnitType(request, out string error);
                if (created == null)
                {
                    SetFeedback(error, true);
                    return;
                }

                AssetDatabase.Refresh();
                selectedIndex = IndexOf(created);

                if (request.profile == null)
                {
                    // "只建兵种不绑模型"是支持的路径，此时不跑绑定校验（那会报"没有绑定 VAT profile"）。
                    //
                    // 但不能就此断言"尚未绑定"：独占 RenderConfig 是模板那份的副本（Instantiate），
                    // 模板若已绑好模型，副本会连同 profile 与三个网格槽位一起继承过来。
                    // 所以这里按实际状态说话，而不是按"没传 profile"这个入参推断。
                    bindingReport.Clear();
                    ScriptableObject inherited = created.renderConfig != null ? created.renderConfig.vatProfile : null;
                    SetFeedback("已新建兵种：" + created.unitTypeName + "（军团 " + created.teamId + "）\n" +
                        (inherited != null
                            ? "已从模板继承 VAT profile：" + inherited.name + "。若要换模型，选中它并在上面重新绑定。\n"
                            : "尚未绑定 VAT profile —— 选中它并在上面绑定模型后即可上战场。\n") +
                        "资产：" + AssetDatabase.GetAssetPath(created), false);
                }
                else
                {
                    // 与「绑定到选中兵种」保持同一口径：校验没过就不能用"成功"的语气报。
                    // 建出来的兵种缺材质/缺网格时，这里必须显示成错误，
                    // 否则用户看到的是一句平静的"已新建兵种"，而下面的错误列表容易被忽略。
                    ValidationResult validation = UnitTypeBinder.ValidateBinding(created);
                    Report(validation);
                    SetFeedback("已新建兵种：" + created.unitTypeName + "（军团 " + created.teamId + "）\n" +
                        "资产：" + AssetDatabase.GetAssetPath(created) +
                        (validation.IsValid ? "" : "\n但校验未通过，请按下面的错误修掉。"), !validation.IsValid);
                }
                EditorGUIUtility.PingObject(created);
            }
            catch (Exception exception)
            {
                SetFeedback(exception.Message, true);
            }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private void SetFeedback(string message, bool isError)
        {
            feedback = message;
            feedbackIsError = isError;
        }

        private int IndexOf(UnitTypeConfig unit)
        {
            if (scenario == null || scenario.unitTypes == null)
                return 0;
            for (int i = 0; i < scenario.unitTypes.Length; i++)
                if (scenario.unitTypes[i] == unit)
                    return i;
            return 0;
        }

        private int ResolveNextTeamId()
        {
            if (scenario == null || scenario.unitTypes == null || scenario.unitTypes.Length == 0)
                return 0;
            int highest = -1;
            foreach (UnitTypeConfig unit in scenario.unitTypes)
                if (unit != null)
                    highest = Mathf.Max(highest, unit.teamId);
            return Mathf.Min(highest + 1, ConfigValidator.MaxTeamId);
        }

        /// <summary>
        /// 该军团编号是否已被占用。注意这不是错误而是需要确认的选择：
        /// 多军团混编是支持的（同编号 = 同一军团），但"顺手点下一个"点到重复编号多半是误操作。
        /// </summary>
        private bool IsTeamIdTaken(int teamId)
        {
            if (scenario == null || scenario.unitTypes == null)
                return false;
            foreach (UnitTypeConfig unit in scenario.unitTypes)
                if (unit != null && unit.teamId == teamId)
                    return true;
            return false;
        }

        /// <summary>找一个现役 ScenarioConfig 作为默认值，省得每次手拖。</summary>
        private static ScenarioConfig FindScenario()
        {
            string[] guids = AssetDatabase.FindAssets("t:ScenarioConfig");
            if (guids.Length == 0)
                return null;

            // 优先挑运行时真正在用的那份：被 MassEngineManager 引用的场景配置。
            foreach (string guid in guids)
            {
                ScenarioConfig candidate = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.unitTypes != null && candidate.unitTypes.Length > 0)
                    return candidate;
            }
            return AssetDatabase.LoadAssetAtPath<ScenarioConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static string Name(Mesh mesh) => mesh != null ? mesh.name : "（空）";

        private static string ToAssetPath(string absolute)
        {
            string normalized = absolute.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            return normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalized.Substring(dataPath.Length)
                : absolute;
        }
    }
}
