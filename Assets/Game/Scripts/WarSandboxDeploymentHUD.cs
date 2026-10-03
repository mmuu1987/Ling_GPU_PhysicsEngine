using System.Globalization;
using UnityEngine;

namespace MassEngine.Game
{
    [DisallowMultipleComponent]
    public sealed partial class WarSandboxDeploymentHUD : MonoBehaviour
    {
        public WarSandboxRuntimeDeployment deployment;
        public bool Confirming { get; private set; }
        private bool resumeAfterConfirmation;
        private bool placing, narrowMap, templateMenu, armyMenu;
        private bool clearFocusRequested;
        private int selected;
        private Vector2 rosterScroll, fieldScroll;
        private string count, x, z, density, aspect, depth, width, inputError;
        private bool manual;
        private GUIStyle label, heading, button, field, note;
        private Font font;
        private WarSandboxDeploymentDraft displayedDraft;
        private int displayedRevision;
        private MyCameraManager cameraInput;
        private bool cameraWasEnabled, cameraCaptured;
        private static readonly Color Background = new Color(0.09f, 0.105f, 0.11f);
        private static readonly Color Surface = new Color(0.15f, 0.17f, 0.18f);

        public static bool BlocksInput(WarSandboxBattleController controller)
        {
            var hud = controller != null ? controller.GetComponent<WarSandboxDeploymentHUD>() : null;
            return WarSandboxRuntimeDeployment.BlocksCommands(controller) || (hud != null && hud.Confirming);
        }

        public void RequestEdit()
        {
            if (deployment == null || deployment.controller == null) return;
            var controller = deployment.controller;
            if (controller.Phase == WarSandboxBattlePhase.Running || controller.Phase == WarSandboxBattlePhase.Paused)
            {
                resumeAfterConfirmation = controller.Phase == WarSandboxBattlePhase.Running;
                controller.PauseBattle(); Confirming = true; CaptureCamera(); return;
            }
            Open(false);
        }

        private void Open(bool confirmed)
        {
            if (deployment.TryBeginEdit(confirmed, out inputError))
            {
                CaptureCamera(); selected = 0; placing = false; templateMenu = armyMenu = false;
                plansOpen = false; pendingPlanAction = null; statsOpen = pushConfirm = false;
                ReadFields();
            }
        }

        private void CaptureCamera()
        {
            if (cameraCaptured) { if (cameraInput != null) cameraInput.enabled = false; return; }
            var camera = deployment.controller.manager.cullingCamera;
            cameraInput = camera != null ? camera.GetComponent<MyCameraManager>() : null;
            if (cameraInput != null) { cameraWasEnabled = cameraInput.enabled; cameraInput.enabled = false; }
            cameraCaptured = true;
        }

        private void ReleaseCamera()
        {
            if (!cameraCaptured) return;
            if (cameraInput == null) { cameraCaptured = false; return; }
            // Deferred release: while a session transition blocks input the camera component may
            // still be disabled; keep the captured flag so a later capture does not overwrite the
            // original baseline (which would leave camera input permanently disabled).
            if (WarSandboxSceneSession.Instance != null && WarSandboxSceneSession.Instance.InputBlocked) return;
            cameraInput.enabled = cameraWasEnabled;
            cameraCaptured = false;
        }

        private void Update()
        {
            if (WarSandboxSceneSession.Instance != null && WarSandboxSceneSession.Instance.InputBlocked) { runtimeUi?.SetVisible(false); return; }
            if (deployment == null) return;
            if (!Confirming && !deployment.IsEditing) { runtimeUi?.SetVisible(false); ReleaseCamera(); return; }
            CaptureCamera();
            if (deployment.IsEditing && (displayedDraft != deployment.Draft || displayedRevision != deployment.Draft.Revision))
            { ClampSelection(); ReadFields(); }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (statsOpen && deployment.IsEditing && !Confirming && !plansOpen)
                {
                    if (pushConfirm) pushConfirm = false; else CloseStats();
                    clearFocusRequested = true; return;
                }
                if (plansOpen)
                {
                    if (pendingPlanAction != null) pendingPlanAction = null;
                    else plansOpen = false;
                    clearFocusRequested = true; return;
                }
                if (Confirming) CancelConfirmation();
                else { placing = templateMenu = armyMenu = false; clearFocusRequested = true; }
            }
            RefreshUGUI();
        }

        private void CancelConfirmation()
        {
            Confirming = false;
            if (resumeAfterConfirmation) deployment.controller.StartOrResumeBattle();
            resumeAfterConfirmation = false; ReleaseCamera();
        }

        private void OnDisable() { Confirming = false; runtimeUi?.SetVisible(false); ReleaseCamera(); }
        private void OnDestroy() { runtimeUi?.Dispose(); if (font != null) Destroy(font); }

        // Legacy reference only; the player uses the retained uGUI presenter.
        private void DrawLegacyEditor()
        {
            if (deployment == null || (WarSandboxSceneSession.Instance != null && WarSandboxSceneSession.Instance.InputBlocked)) return;
            if (!Confirming && !deployment.IsEditing) return;
            EnsureStyles(); GUI.depth = -100;
            if (clearFocusRequested) { GUI.FocusControl(null); clearFocusRequested = false; }
            Fill(new Rect(0, 0, Screen.width, Screen.height - 60), Background);
            if (Confirming) { DrawConfirmation(); return; }
            var draft = deployment.Draft;
            if (draft == null) return;
            if (plansOpen) { DrawPlanLibrary(); return; }
            bool narrow = Screen.width < 850;
            float libraryX = narrow ? Screen.width - 280 : Screen.width - 132;
            GUI.Label(new Rect(16, 10, Mathf.Max(90, libraryX - 24), 30), "战前布阵", heading);
            if (GUI.Button(new Rect(libraryX, 10, 108, 30), "本地方案", button))
            { OpenPlanLibrary(); GUIUtility.ExitGUI(); }
            if (narrow)
            {
                bool wasMap = narrowMap;
                narrowMap = GUI.Toggle(new Rect(Screen.width - 164, 10, 74, 30), narrowMap, "部署图", button);
                if (GUI.Toggle(new Rect(Screen.width - 86, 10, 70, 30), !narrowMap, "编成", button)) narrowMap = false;
                if (wasMap != narrowMap) GUIUtility.ExitGUI();
            }
            float areaHeight = Mathf.Max(180, Screen.height - 196);
            float leftWidth = narrow ? Screen.width - 32 : 368;
            if (!narrow || !narrowMap) DrawRosterAndFields(new Rect(16, 50, leftWidth, areaHeight));
            if (!narrow || narrowMap)
            {
                float left = narrow ? 16 : 404;
                DrawMap(new Rect(left, 50, Screen.width - left - 16, areaHeight));
            }
            DrawFooter(new Rect(16, Screen.height - 138, Screen.width - 32, 74));
            if (!string.IsNullOrEmpty(GUI.tooltip))
                GUI.Label(new Rect(16, 40, Screen.width - 32, 22), GUI.tooltip, note);
        }

        private void DrawConfirmation()
        {
            float w = Mathf.Min(430, Screen.width - 32);
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2, Mathf.Max(40, Screen.height / 2 - 125), w, 230));
            GUILayout.Label("结束本局并返回布阵？", heading);
            GUILayout.Label("当前战斗进度不会保留。", label);
            GUILayout.Space(16);
            if (GUILayout.Button("结束并布阵", button, GUILayout.Height(40)))
            {
                Confirming = false; resumeAfterConfirmation = false; Open(true);
            }
            if (GUILayout.Button("取消", button, GUILayout.Height(36))) CancelConfirmation();
            GUILayout.EndArea();
        }

        private void DrawRosterAndFields(Rect area)
        {
            var draft = deployment.Draft;
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            if (Tool("+", "新增编成", true) && CommitFields())
            {
                var template = draft.Count > 0 ? draft[Mathf.Clamp(selected, 0, draft.Count - 1)].template : deployment.Templates[0];
                int team = draft.Count > 0 ? draft[Mathf.Clamp(selected, 0, draft.Count - 1)].teamId : 0;
                if (draft.Add(template, team, deployment.UseTemplateSpawnDefaults)) { selected = draft.Count - 1; RefreshAfterUiChange(); }
            }
            if (Tool("−", "删除编成", draft.Count > 0)) { draft.Remove(selected); selected = Mathf.Max(0, selected - 1); RefreshAfterUiChange(); }
            if (Tool("↶", "撤销", draft.CanUndo)) { draft.Undo(); ClampSelection(); RefreshAfterUiChange(); }
            if (Tool("↷", "重做", draft.CanRedo)) { draft.Redo(); ClampSelection(); RefreshAfterUiChange(); }
            GUILayout.EndHorizontal();
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(Mathf.Min(180, area.height * 0.33f)));
            for (int i = 0; i < draft.Count; i++)
            {
                var e = draft[i]; var previous = GUI.backgroundColor;
                GUI.backgroundColor = i == selected ? WarSandboxTeamPalette.Resolve(e.teamId) : Color.white;
                if (GUILayout.Button(WarSandboxBattleController.DefaultArmyName(e.teamId) + " / " + e.Name + "  " + e.count, button, GUILayout.MinHeight(34)) && i != selected && CommitFields())
                { selected = i; templateMenu = armyMenu = false; RefreshAfterUiChange(); }
                GUI.backgroundColor = previous;
            }
            GUILayout.EndScrollView();
            fieldScroll = GUILayout.BeginScrollView(fieldScroll);
            if (draft.Count == 0) GUILayout.Label("暂无编成", label);
            else DrawFields();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawFields()
        {
            var draft = deployment.Draft; var e = draft[selected];
            if (GUILayout.Button("兵种：" + e.Name + " ▾", button, GUILayout.MinHeight(32))) { templateMenu = !templateMenu; GUIUtility.ExitGUI(); }
            if (templateMenu)
                foreach (var template in deployment.Templates)
                    if (GUILayout.Button(template.unitTypeName, button, GUILayout.MinHeight(30)) && CommitFields())
                    { deployment.SelectTemplate(selected, template); templateMenu = false; RefreshAfterUiChange(); }
            if (GUILayout.Button("军团：" + WarSandboxBattleController.DefaultArmyName(draft[selected].teamId) + " ▾", button, GUILayout.Height(32))) { armyMenu = !armyMenu; GUIUtility.ExitGUI(); }
            if (armyMenu)
                for (int team = 0; team <= ConfigValidator.MaxTeamId; team++)
                    if (GUILayout.Button(WarSandboxBattleController.DefaultArmyName(team), button, GUILayout.Height(28)) && CommitFields())
                    { e = draft[selected]; e.teamId = team; draft.Set(selected, e); armyMenu = false; RefreshAfterUiChange(); }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 军团", button, GUILayout.Height(30)) && CommitFields())
            {
                int team = draft.NextArmyId();
                if (team >= 0 && draft.Add(draft[selected].template, team)) { selected = draft.Count - 1; RefreshAfterUiChange(); }
                else inputError = "军团或编成数量已达上限。";
            }
            if (GUILayout.Button("删除军团", button, GUILayout.Height(30)))
            { draft.RemoveArmy(draft[selected].teamId); ClampSelection(); RefreshAfterUiChange(); }
            GUILayout.EndHorizontal();
            if (draft.Count == 0) return;
            count = Number("人数", count);
            x = Number("中心 X", x); z = Number("中心 Z", z);
            manual = GUILayout.Toggle(manual, "手动阵型尺寸", GUILayout.Height(26));
            if (manual) { depth = Number("纵深 X", depth); width = Number("正面宽 Z", width); }
            else { density = Number("密度", density); aspect = Number("宽深比", aspect); }
            if (GUILayout.Button("更新编成", button, GUILayout.Height(32))) CommitFields();
        }

        private string Number(string name, string value)
        {
            GUILayout.BeginHorizontal(); GUILayout.Label(name, label, GUILayout.Width(100), GUILayout.Height(28));
            GUI.SetNextControlName("deployment-" + name);
            string result = GUILayout.TextField(value ?? "", 24, field, GUILayout.Height(28));
            GUILayout.EndHorizontal(); return result;
        }

        private bool CommitFields()
        {
            var draft = deployment.Draft;
            if (draft == null || draft.Count == 0) { inputError = null; return true; }
            var e = draft[selected];
            if (!int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ||
                !Parse(x, out float px) || !Parse(z, out float pz) || !Parse(density, out float d) || !Parse(aspect, out float a) ||
                (manual && (!Parse(depth, out _) || !Parse(width, out _))))
            { inputError = "请输入有效的有限数值。"; return false; }
            e.count = n; e.center = new Vector3(px, e.center.y, pz); e.density = d; e.aspect = a;
            if (manual) { Parse(depth, out float sx); Parse(width, out float sz); e.manualSize = new Vector3(sx, e.manualSize.y, sz); }
            else e.manualSize = Vector3.zero;
            draft.Set(selected, e); inputError = null; return true;
        }

        private void ReadFields()
        {
            inputError = null;
            displayedDraft = deployment.Draft;
            displayedRevision = displayedDraft != null ? displayedDraft.Revision : 0;
            if (deployment.Draft == null || deployment.Draft.Count == 0) return;
            var e = deployment.Draft[selected];
            count = e.count.ToString(CultureInfo.InvariantCulture); x = Format(e.center.x); z = Format(e.center.z);
            density = Format(e.density); aspect = Format(e.aspect); manual = e.manualSize.x > 0 && e.manualSize.z > 0;
            depth = Format(e.Size.x); width = Format(e.Size.z);
        }

        private void ClampSelection() => selected = Mathf.Clamp(selected, 0, Mathf.Max(0, deployment.Draft.Count - 1));
        private void RefreshAfterUiChange() { ReadFields(); clearFocusRequested = true; }
        private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static bool Parse(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);

        private void DrawFooter(Rect area)
        {
            bool valid = deployment.TryValidate(out string error);
            string message = inputError ?? error;
            GUI.Label(new Rect(area.x, area.y, area.width, 34), message ?? (HasPendingInputs() ? "编成输入待更新" : "布阵有效"), note);
            float w = Mathf.Min(150, (area.width - 16) / 3);
            if (GUI.Button(new Rect(area.x, area.y + 38, w, 34), "取消修改", button))
            { deployment.CancelEditing(); ReleaseCamera(); GUI.FocusControl(null); return; }
            // Apply rechecks pending text as well as the complete draft before allocation.
            bool enabled = GUI.enabled; GUI.enabled = valid;
            if (GUI.Button(new Rect(area.x + w + 8, area.y + 38, w, 34), "应用布阵", button)) Apply(false);
            if (GUI.Button(new Rect(area.x + (w + 8) * 2, area.y + 38, w, 34), "应用并开战", button)) Apply(true);
            GUI.enabled = enabled;
        }

        private void Apply(bool start)
        {
            if (!CommitFields() || !deployment.TryApply(out inputError)) return;
            ReleaseCamera(); clearFocusRequested = true;
            if (start) deployment.controller.StartDefaultBattle();
        }

        private bool HasPendingInputs()
        {
            if (deployment.Draft.Count == 0) return false;
            var e = deployment.Draft[selected];
            return count != e.count.ToString(CultureInfo.InvariantCulture) || x != Format(e.center.x) || z != Format(e.center.z) ||
                manual != (e.manualSize.x > 0 && e.manualSize.z > 0) ||
                (manual ? depth != Format(e.Size.x) || width != Format(e.Size.z) : density != Format(e.density) || aspect != Format(e.aspect));
        }

        private void DrawMap(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width - 86, 30), "部署图", heading);
            placing = GUI.Toggle(new Rect(area.xMax - 80, area.y, 80, 30), placing, new GUIContent("⌖", "点击部署图放置选中编成"), button);
            Vector2 world = deployment.WorldSize;
            if (world.x <= 0 || world.y <= 0) return;
            float scale = Mathf.Min(area.width / world.x, (area.height - 44) / world.y);
            Rect map = new Rect(area.center.x - world.x * scale * 0.5f, area.y + 40, world.x * scale, world.y * scale);
            Fill(map, Surface);
            for (int i = 1; i < 8; i++)
            {
                Fill(new Rect(map.x + map.width * i / 8, map.y, 1, map.height), new Color(0.23f, 0.26f, 0.26f));
                Fill(new Rect(map.x, map.y + map.height * i / 8, map.width, 1), new Color(0.23f, 0.26f, 0.26f));
            }
            var rules = deployment.Draft.Rules;
            if (rules.staticObstaclesEnabled && rules.staticObstacles != null)
                foreach (var obstacle in rules.staticObstacles) Fill(MapBounds(obstacle.Bounds, world, map), new Color(0.7f, 0.36f, 0.34f));
            if (rules.gameMode == WarSandboxGameMode.ControlPoint)
            {
                Vector2 point = WarSandboxMinimapProjection.WorldToMap(rules.controlPointCenter, world, map);
                GUI.Label(new Rect(point.x - 12, point.y - 12, 24, 24), "◎", heading);
            }
            var draft = deployment.Draft;
            for (int i = 0; i < draft.Count; i++)
            {
                var e = draft[i]; var bounds = e.Bounds;
                if (float.IsNaN(bounds.width) || float.IsInfinity(bounds.width) || float.IsNaN(bounds.height) || float.IsInfinity(bounds.height)) continue;
                Rect rect = MapBounds(bounds, world, map);
                rect = Rect.MinMaxRect(Mathf.Max(map.xMin, rect.xMin), Mathf.Max(map.yMin, rect.yMin), Mathf.Min(map.xMax, rect.xMax), Mathf.Min(map.yMax, rect.yMax));
                if (rect.width <= 0 || rect.height <= 0) continue;
                Color color = WarSandboxTeamPalette.Resolve(e.teamId); color.a = 0.65f; Fill(rect, color);
                Outline(rect, i == selected ? Color.white : Color.gray, i == selected ? 3 : 1);
                if (rect.width > 36 && rect.height > 20) GUI.Label(new Rect(rect.x + 3, rect.y, rect.width - 6, 22), (i + 1).ToString(), label);
            }
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && map.Contains(current.mousePosition))
            {
                GUI.FocusControl(null);
                if (placing && draft.Count > 0 && CommitFields())
                {
                    var e = draft[selected]; var point = WarSandboxMinimapProjection.MapToWorld(current.mousePosition, world, map);
                    e.center = new Vector3(point.x, e.center.y, point.z); draft.Set(selected, e); ReadFields(); placing = false;
                }
                else if (CommitFields())
                    for (int i = draft.Count - 1; i >= 0; i--)
                        if (MapBounds(draft[i].Bounds, world, map).Contains(current.mousePosition)) { selected = i; ReadFields(); break; }
                current.Use();
            }
        }

        private static Rect MapBounds(Rect bounds, Vector2 world, Rect map)
        {
            var a = WarSandboxMinimapProjection.WorldToMap(new Vector3(bounds.xMin, 0, bounds.yMin), world, map);
            var b = WarSandboxMinimapProjection.WorldToMap(new Vector3(bounds.xMax, 0, bounds.yMax), world, map);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private bool Tool(string symbol, string tooltip, bool enabled)
        {
            bool previous = GUI.enabled; GUI.enabled = enabled;
            bool clicked = GUILayout.Button(new GUIContent(symbol, tooltip), button, GUILayout.Width(46), GUILayout.Height(30));
            GUI.enabled = previous; return clicked;
        }

        private void EnsureStyles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 15, wordWrap = true };
            label.normal.textColor = new Color(0.92f, 0.94f, 0.94f);
            heading = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            note = new GUIStyle(label) { fontSize = 13 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 14, wordWrap = true };
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 16 };
        }

        private static void Fill(Rect rect, Color color) { var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous; }
        private static void Outline(Rect rect, Color color, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color); Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color); Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
