using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    /// <summary>Task-based authoring pages. Navigation and help never write scenario assets.</summary>
    public sealed partial class WarSandboxEditorWindow : EditorWindow
    {
        private const string StylePath = "Assets/Game/Editor/WarSandboxEditor.uss";
        private const string FontPreference = "MassEngine.WarSandboxEditor.FontSize";
        private static readonly string[] PageNames = { "开始使用", "军团配兵", "战场布阵", "保存与试跑", "帮助中心" };
        private static readonly string[] PageHints = { "连接场景，了解流程", "选兵种，改人数", "摆位置，检查占地", "保存方案，开始验证", "教程、术语与常见问题" };
        [SerializeField] private MassEngineManager manager;
        [SerializeField] private int page;
        [SerializeField] private int selectedArmy;
        [SerializeField] private bool addingArmy;
        [SerializeField] private bool showFieldHelp = true;
        [SerializeField] private float engagementGap = WarSandboxFormationLayout.DefaultEngagementGap;
        [SerializeField] private WarSandboxScalePreset scalePreset = WarSandboxScalePreset.Standard10K;
        [SerializeField] private int customUnitsPerTeam = 10000;
        [SerializeField] private WarSandboxDeploymentPlan deploymentPlan;
        [SerializeField] private UnitTypeConfig rosterTemplate;
        [SerializeField] private int newUnitTeamId;
        private string feedback;
        private bool feedbackIsError;
        private ScrollView pageScroll;
        private VisualElement pageContent;
        private VisualElement navigation;
        private VisualElement footer;
        private Label summary;
        private readonly float[] scrollOffsets = new float[5];
        private ScenarioConfig Scenario => manager != null ? manager.scenarioConfig : null;
        private bool ReadOnly => EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("MassEngine/War Sandbox Editor")]
        public static void Open() => GetWindow<WarSandboxEditorWindow>("战争沙盒");

        private void OnEnable()
        {
            minSize = new Vector2(540f, 440f);
            if (manager == null) ResolveManager();
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private void OnFocus() => RefreshPage();

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.AddToClassList("war-editor");
            root.EnableInClassList("light-theme", !EditorGUIUtility.isProSkin);
            root.EnableInClassList("compact", position.width < 860f);
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            ApplyTextSize(Mathf.Clamp(EditorPrefs.GetInt(FontPreference, 16), 14, 18));
            root.EnableInClassList("hide-field-help", !showFieldHelp);
            root.RegisterCallback<GeometryChangedEvent>(evt => root.EnableInClassList("compact", evt.newRect.width < 860f));

            var header = Element(root, "header");
            var identity = Element(header, "identity");
            Text(identity, "战争沙盒编辑器", "app-title");
            summary = Text(identity, "", "summary");
            var preferences = Element(header, "preferences");
            var font = new PopupField<string>(new List<string> { "标准字 14", "大字 16", "特大字 18" },
                (Mathf.Clamp(EditorPrefs.GetInt(FontPreference, 16), 14, 18) - 14) / 2) { name = "font-size" };
            font.tooltip = "只调整这个窗口的文字和输入框；窄窗口会换行，不会缩小字体。";
            font.RegisterValueChangedCallback(evt =>
            {
                int size = 14 + font.index * 2;
                ApplyTextSize(size);
                EditorPrefs.SetInt(FontPreference, size);
            });
            preferences.Add(font);
            var hints = new Toggle("逐项讲解") { value = showFieldHelp, tooltip = "第一次使用建议开启。收起后仍保留关键操作提醒。" };
            hints.RegisterValueChangedCallback(evt =>
            {
                showFieldHelp = evt.newValue;
                root.EnableInClassList("hide-field-help", !showFieldHelp);
            });
            preferences.Add(hints);
            var workspace = Element(root, "workspace");
            navigation = Element(workspace, "navigation");
            pageScroll = new ScrollView(ScrollViewMode.Vertical) { name = "page-scroll" };
            pageScroll.AddToClassList("page-scroll");
            workspace.Add(pageScroll);
            pageContent = Element(pageScroll, "page-content");
            footer = Element(root, "footer");
            RefreshPage();
        }

        private void Navigate(int target)
        {
            scrollOffsets[page] = pageScroll.scrollOffset.y;
            page = Mathf.Clamp(target, 0, PageNames.Length - 1);
            RefreshPage();
            pageScroll.schedule.Execute(() => pageScroll.scrollOffset = new Vector2(0, scrollOffsets[page]));
        }

        private void ApplyTextSize(int size)
        {
            rootVisualElement.style.fontSize = size;
            rootVisualElement.EnableInClassList("font-standard", size == 14);
            rootVisualElement.EnableInClassList("font-extra", size == 18);
        }

        private void RefreshPage()
        {
            if (pageContent == null) return;
            page = Mathf.Clamp(page, 0, PageNames.Length - 1);
            navigation.Clear();
            for (int i = 0; i < PageNames.Length; i++)
            {
                int target = i;
                var button = ActionButton(navigation, (i < 4 ? (i + 1) + "  " : "?  ") + PageNames[i],
                    () => Navigate(target), "page-" + i);
                button.AddToClassList("nav-button");
                button.EnableInClassList("selected", page == i);
                button.tooltip = PageHints[i];
            }
            pageContent.Clear();
            Text(pageContent, PageNames[page], "page-title");
            if (ReadOnly) Notice(pageContent, "正在试跑：配置暂时只读。用 Unity 顶部的停止按钮退出运行后再修改。分页、预览和帮助仍可使用。");
            if (!string.IsNullOrEmpty(feedback)) Notice(pageContent, feedback, feedbackIsError);
            switch (page)
            {
                case 0: DrawStartPage(); break;
                case 1: DrawRosterPage(); break;
                case 2: DrawDeploymentPage(); break;
                case 3: DrawSavePage(); break;
                default: DrawHelpPage(); break;
            }
            footer.Clear();
            if (page > 0 && page < 4) ActionButton(footer, "← " + PageNames[page - 1], () => Navigate(page - 1));
            var state = Text(footer, ReadOnly ? "试跑中 · 配置只读" : "修改可用 Ctrl+Z 撤销", "footer-state");
            state.style.flexGrow = 1;
            if (page < 3) ActionButton(footer, "下一步：" + PageNames[page + 1] + " →", () => Navigate(page + 1), primary: true);
            if (page == 4) ActionButton(footer, "回到开始使用", () => Navigate(0));
            UpdateSummary();
        }

        private void DrawStartPage()
        {
            Text(pageContent, "先跑通一局，再改一个参数看区别。每页只处理一件事，随时可以返回。", "lead");
            var connection = Card(pageContent, "当前编辑的战场");
            Text(connection, manager == null ? "还没有连接战场" : "场景：" + manager.gameObject.scene.name);
            string problem = SetupProblem();
            if (problem != null) Notice(connection, problem, true);
            else Text(connection, "配置已连接。可以直接修改现有编成，也可以先去试跑。", "field-help");
            var actions = Element(connection, "actions");
            ActionButton(actions, "重新查找场景", () => { ResolveManager(); RefreshPage(); });
            ActionButton(actions, "打开默认战场", OpenDefaultScene, "open-scene", writable: true);
            var references = new Foldout { text = "手动连接 / 更换配置（通常不用改）", value = problem != null };
            connection.Add(references);
            ObjectPicker(references, "场景中的管理器", manager, true, value =>
            {
                manager = value; selectedArmy = 0; feedback = null; RefreshPage();
            }, "从 Hierarchy 拖入带 MassEngineManager 的对象；通常可自动找到。");
            if (manager != null)
            {
                ObjectPicker(references, "战役配置", manager.scenarioConfig, false, value =>
                {
                    Edit(manager, "Change War Sandbox Scenario", () => manager.scenarioConfig = value); RefreshPage();
                }, "保存当前战役的兵种清单。更换它就会编辑另一份战役。");
                ObjectPicker(references, "系统配置", manager.systemConfig, false, value =>
                {
                    Edit(manager, "Change War Sandbox System", () => manager.systemConfig = value); RefreshPage();
                }, "提供战场边界和导航配置。默认场景已经配好。");
            }
            var tutorial = Card(pageContent, "第一次使用，照这三步做");
            Text(tutorial, "1. 军团配兵：选一个已有编成，只修改人数。例如先把它改成 1,000 人。\n\n" +
                "2. 战场布阵：点击“按当前兵力自动布阵”，然后看占地预览。\n\n" +
                "3. 保存与试跑：保存后进入运行；点一下 Game 画面，再按 Enter 开战。");
            var shortcuts = Element(tutorial, "actions");
            ActionButton(shortcuts, "开始调整兵力 →", () => { addingArmy = false; Navigate(1); }, primary: true);
            ActionButton(shortcuts, "先用现有配置试跑", () => Navigate(3));
            Text(pageContent, "想保留原来的布阵？先去“保存与试跑”另存一个方案，再开始修改。", "field-help");
        }

        private void DrawHelpPage()
        {
            Text(pageContent, "搜索“人数”“远程”“保存”“不显示”等词，说明里有对应的操作入口。", "lead");
            var search = new TextField("搜索帮助") { name = "help-search" };
            search.AddToClassList("stacked-field"); pageContent.Add(search);
            var results = Element(pageContent, "help-results");
            var topics = new[]
            {
                new[] { "第一次怎么开战？", "打开默认战场 → 军团配兵选编成并改人数 → 战场布阵执行自动布阵 → 保存与试跑。进入 Play 后点击 Game 画面，按 Enter 开战。Space 暂停；退出 Play 后再调整。", "3" },
                new[] { "军团、兵种、编成分别是什么？", "军团是一支可以独立下命令的队伍。兵种是近战、远程等模板；编成是当前战役中这一批士兵。例如军团 0 可以有 1,000 名近战和 500 名远程两个编成。同一编号是友军，不同编号互为敌人，没有联盟。", "1" },
                new[] { "怎么增加远程兵或第三支军团？", "去军团配兵 → 新增编成，从兵种列表选远程模板。想混编就选已有军团再点添加；想增加敌对第三方就点添加独立军团。模板没有的能力不能只靠改名字获得。新编成的兵力和站位可以单独改。", "1" },
                new[] { "人数、密度、宽深比怎么填？", "人数填这一批士兵的总数，第一次可用 1,000。密度 0.5 表示每平方米 0.5 人；越大越紧密。宽深比 2 表示阵线宽是纵深的两倍；0.5 是纵队。先用 0.5 和 2，再只改一项观察占地。", "1" },
                new[] { "自动布阵会改掉我的手工位置吗？", "会移动军团 0 和 1，使它们沿 X 轴对阵，并按编成清单顺序前后排开。军团 2 及以上保持原位置，仍需手动避开其他部队。所有军团都会计入战场尺寸。已手工摆好的两方可以跳过自动布阵。", "2" },
                new[] { "X、Y、Z 与占地怎么看？", "这是从上往下看的 X/Z 平面：X 是左右（默认交战方向），Z 是前后（阵线展开方向），Y 是高度。预览中的色块是实际出生占地；相互压住表示可能重叠。部署页可一键让 Scene 镜头对准部队。", "2" },
                new[] { "保存配置、另存方案、覆盖、载入有什么区别？", "保存配置把当前修改写回项目。另存方案保留一个可恢复的部署版本；覆盖方案更新选中的版本；载入方案恢复其中的人数、阵型、位置和战场尺寸，不会重新自动布阵。想比较 A/B：另存 A → 修改 → 另存 B → 选择 A 并载入。", "3" },
                new[] { "方案包含战斗存档或兵种伤害吗？", "方案保存部署和兵种引用，不保存进行中的战斗、命令、录像、规则或障碍，也不复制战斗/移动/渲染参数。新增编成共享模板的这些参数；需要独立改伤害时，应先在 Inspector 复制战斗配置再引用它。", "3" },
                new[] { "按钮为什么不能点？士兵为什么不显示？", "先看页面上的提示：Play 期间配置只读；缺少场景或系统配置时先回开始使用；没有模板时先选择模板。兵力要大于 0，编成要有出生配置。只看到地面时可在布阵页定位全军，再确认兵种的渲染资源已配置。", "0" },
                new[] { "改错了怎么恢复？移除会删文件吗？", "Ctrl+Z 撤销，Ctrl+Y 重做。移除编成只从当前清单解绑，资产文件仍保留；撤销新增也只撤回清单变动。若已保存过部署方案，可载入恢复部署。切换页面、文字大小或阅读帮助不会改动战役配置。", "3" },
                new[] { "卡顿时选什么规模？", "第一次建议轻量档：军团 0 和 1 各 1 万人。5 万对 5 万是默认大军档，更高档用于压力测试。第三方人数不随两方预设改变；远程占比和军团数量也会影响速度。", "2" }
            };
            var cards = new List<VisualElement>();
            foreach (string[] topic in topics)
            {
                var card = Card(results, topic[0]); Text(card, topic[1]);
                int target = int.Parse(topic[2]);
                ActionButton(card, "前往" + PageNames[target] + " →", () => Navigate(target)); cards.Add(card);
            }
            var empty = Text(results, "没有匹配的说明。试试“编成”“自动布阵”或清空搜索。");
            empty.style.display = DisplayStyle.None;
            search.RegisterValueChangedCallback(evt =>
            {
                int visible = 0; string query = evt.newValue.Trim();
                for (int i = 0; i < topics.Length; i++)
                {
                    bool match = (topics[i][0] + topics[i][1]).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    cards[i].style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
                    if (match) visible++;
                }
                empty.style.display = visible == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            });
        }

        private static VisualElement Element(VisualElement parent, string className)
        {
            var element = new VisualElement(); element.AddToClassList(className); parent.Add(element); return element;
        }
        private static Label Text(VisualElement parent, string text, string className = "body-text")
        {
            var label = new Label(text); label.AddToClassList(className); parent.Add(label); return label;
        }
        private static VisualElement Card(VisualElement parent, string title)
        {
            var card = Element(parent, "card"); Text(card, title, "card-title"); return card;
        }
        private static void Notice(VisualElement parent, string message, bool error = false)
        {
            var label = Text(parent, message, "notice"); label.EnableInClassList("error", error);
        }
        private Button ActionButton(VisualElement parent, string title, Action action, string name = null,
            bool primary = false, bool writable = false)
        {
            var button = new Button(() => { if (!writable || !ReadOnly) action(); }) { text = title, name = name };
            button.AddToClassList("action-button"); button.EnableInClassList("primary", primary);
            button.SetEnabled(!writable || !ReadOnly); parent.Add(button); return button;
        }
        private void ObjectPicker<T>(VisualElement parent, string title, T value, bool sceneObjects,
            Action<T> changed, string help = null) where T : Object
        {
            var field = new ObjectField(title) { objectType = typeof(T), allowSceneObjects = sceneObjects, value = value };
            field.AddToClassList("stacked-field"); field.SetEnabled(!ReadOnly); parent.Add(field);
            field.RegisterValueChangedCallback(evt => { if (!ReadOnly) changed(evt.newValue as T); });
            if (help != null) Text(parent, help, "field-help");
        }
        private void Edit(Object target, string action, Action change)
        {
            if (ReadOnly || target == null) return;
            Undo.RecordObject(target, action); change(); EditorUtility.SetDirty(target);
            if (target is Component component) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            feedback = null; UpdateSummary(); SceneView.RepaintAll();
        }
        private void SetFeedback(string message, bool error = false)
        {
            feedback = message; feedbackIsError = error; RefreshPage(); pageScroll.scrollOffset = Vector2.zero;
        }
        private string SetupProblem()
        {
            if (manager == null) return "请先打开默认战场，或在手动连接中选择场景管理器。";
            if (!manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                return "请选择已打开场景里的管理器，不能选择 Project 中的预制体。";
            if (Scenario == null) return "缺少战役配置。请展开手动连接，选择战役配置资产。";
            if (manager.systemConfig == null || manager.systemConfig.simulationConfig == null || manager.systemConfig.runtimeFlowConfig == null)
                return "系统配置不完整，需要场景模拟和导航配置。可打开默认战场使用已配好的配置。";
            return null;
        }
        private void UpdateSummary()
        {
            if (summary == null) return;
            long count = 0; var teams = new HashSet<int>();
            UnitTypeConfig[] roster = Scenario != null ? Scenario.unitTypes : null;
            if (roster != null) foreach (UnitTypeConfig unit in roster)
            {
                if (unit == null) continue;
                teams.Add(unit.teamId);
                if (unit.spawnConfig != null) count += Mathf.Max(0, unit.spawnConfig.unitCount);
            }
            summary.text = manager == null ? "从一个能开战的场景开始" : teams.Count + " 支军团 · " + (roster?.Length ?? 0) + " 个编成 · " + count.ToString("N0") + " 人";
        }
        private void ResolveManager() => manager = Object.FindFirstObjectByType<MassEngineManager>();
        private void OnUndoRedo() { feedback = "已撤销 / 重做。请检查当前配置；满意后保存。"; feedbackIsError = false; RefreshPage(); }
        private void OnPlayModeChanged(PlayModeStateChange state) => RefreshPage();
        private void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            ResolveManager(); selectedArmy = 0; feedback = null; RefreshPage();
        }
        private void OpenDefaultScene()
        {
            if (ReadOnly || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene("Assets/Game/Scenes/WarSandbox.unity"); ResolveManager(); RefreshPage();
        }
    }
}
