using UnityEngine;

namespace MassEngine.Game
{
    [RequireComponent(typeof(WarSandboxSceneSession))]
    public sealed partial class WarSandboxFrontEnd : MonoBehaviour
    {
        private WarSandboxSceneSession session;
        private WarSandboxUGUI ui;
        private bool confirmingQuit;
        private float nextRefresh;
        private void Awake() => session = GetComponent<WarSandboxSceneSession>();
        private void Update()
        {
            if (session == null || WarSandboxSceneSession.Instance != session) return;
            if (libraryOpen && (session.State != WarSandboxEntryState.Menu || session.SettingsOpen || session.ConfirmationOpen)) CloseLibrary();
            if (Input.GetKeyDown(KeyCode.Escape) && libraryOpen) LibraryEscape();
            if (Input.GetKeyDown(KeyCode.Escape) && session.SettingsOpen) CloseSettings();
            if (Input.GetKeyDown(KeyCode.Escape) && session.ConfirmationOpen) session.CancelConfirmation();
            if (ui == null) ui = new WarSandboxUGUI(transform, "Front End Canvas", 200);
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            ui.Begin();
            if (session.SettingsOpen) DrawSettings();
            else if (session.ConfirmationOpen) DrawConfirmation();
            else switch (session.State)
            {
                case WarSandboxEntryState.Battle: DrawNavigation(); break;
                case WarSandboxEntryState.Loading: DrawLoading(); break;
                case WarSandboxEntryState.Failed: DrawFailure(); break;
                default: if (libraryOpen) DrawLibrary(); else DrawCatalog(); break;
            }
            ui.End();
        }
        public static Rect NavigationRect(float width, float height)
        {
            float scale = Mathf.Clamp(Mathf.Min(width / 1280f, height / 720f), 0.85f, 1.5f);
            float w = Mathf.Min(488 * scale, width - 24);
            return new Rect((width - w) * 0.5f, height - 54 * scale, w, 42 * scale);
        }
        public static bool IsOverNavigation(Vector2 point)
        {
            var session = WarSandboxSceneSession.Instance;
            return session != null && (session.InputBlocked || NavigationRect(Screen.width, Screen.height).Contains(point));
        }
        private void DrawNavigation()
        {
            float w = Mathf.Min(488, ui.Width - 24), x = (ui.Width - w) / 2, y = ui.Height - 54;
            ui.Panel("nav", new Rect(x, y, w, 42));
            ui.Label("nav-name", new Rect(x + 4, y + 3, w - 264, 36), session.CurrentDisplayName, 14, WarSandboxUGUI.Muted);
            ui.Button("nav-settings", new Rect(x + w - 258, y + 4, 80, 34), "设置", session.OpenSettings);
            ui.Button("nav-menu", new Rect(x + w - 170, y + 4, 108, 34), "战场目录", RequestReturn);
            ui.Button("nav-quit", new Rect(x + w - 56, y + 4, 52, 34), "退出", RequestQuit);
            if (!string.IsNullOrEmpty(session.Error))
            {
                ui.Panel("nav-error-bg", new Rect(x, y - 104, w, 96));
                ui.Label("nav-error", new Rect(x + 6, y - 100, w - 12, 88), session.Error, 15, WarSandboxUGUI.Danger);
            }
        }
        private void DrawCatalog()
        {
            ui.Panel("catalog-bg", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            float width = Mathf.Min(1100, ui.Width - 48), left = (ui.Width - width) / 2;
            ui.Label("brand", new Rect(left, 22, width - 100, 22), "MASS WAR SANDBOX  /  战争沙盒", 13, WarSandboxUGUI.Accent, true);
            ui.Label("catalog-title", new Rect(left, 52, width - 200, 44), "选择你的下一场战役", 30, null, true);
            ui.Label("catalog-note", new Rect(left, 98, width, 32), "选择战场 · 调整军团 · 保存方案 · 自由指挥", 15, WarSandboxUGUI.Muted);
            ui.Button("quit", new Rect(left + width - 80, 52, 80, 38), "退出", RequestQuit);
            ui.Button("menu-settings", new Rect(left + width - 170, 52, 80, 38), "设置", session.OpenSettings);
            ui.Button("menu-unit-library", new Rect(left + width - 280, 52, 100, 38), "兵种库", OpenLibrary);
            string validation = null;
            bool valid = session.catalog != null && session.catalog.TryValidate(WarSandboxSceneSession.CanLoadScene, out validation);
            string message = session.Error ?? validation;
            if (!valid || message != null)
            {
                ui.Label("catalog-error", new Rect(left, 160, width, 110), message ?? "没有可用的战场目录。", 18, WarSandboxUGUI.Danger);
                ui.Button("catalog-recheck", new Rect(left, 282, 140, 40), "重新检查", session.ClearError); return;
            }
            var entries = session.catalog.entries;
            int columns = width >= 780 ? 2 : 1;
            float cw = (width - 20 * (columns - 1)) / columns, ih = cw * 0.46f, ch = ih + 172;
            ui.Scroll("catalog-scroll", new Rect(left, 154, width, ui.Height - 178), ((entries.Length + columns - 1) / columns) * (ch + 20));
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; float x = (i % columns) * (cw + 20), y = (i / columns) * (ch + 20); string k = "card-" + i;
                ui.Panel(k, new Rect(x, y, cw, ch));
                ui.Picture(k + "-image", new Rect(x, y, cw, ih), entry.preview);
                ui.Label(k + "-name", new Rect(x + 14, y + ih + 10, cw - 28, 34), entry.displayName, 23, null, true);
                var rules = entry.rules.rules;
                string mode = rules.gameMode == WarSandboxGameMode.ControlPoint ? "据点战" : "歼灭战";
                string detail = rules.staticObstaclesEnabled ? rules.staticObstacles.Length + " 处障碍" : entry.terrainSurface != null ? "山地地形" : "开阔地形";
                ui.Label(k + "-detail", new Rect(x + 14, y + ih + 48, cw - 28, 54),
                    string.IsNullOrEmpty(entry.description) ? mode + "  /  " + detail : entry.description, 15, WarSandboxUGUI.Muted);
                ui.Button(k + "-enter", new Rect(x + 24, y + ch - 60, cw - 48, 42), "进入战场  →", () => session.TryEnterBattlefield(entry.id, false, out _), true);
            }
            ui.EndScroll();
        }
        private void DrawLoading()
        {
            ui.Panel("loading-bg", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            float w = Mathf.Min(480, ui.Width - 48), x = (ui.Width - w) / 2, y = ui.Height * 0.4f;
            ui.Label("loading-title", new Rect(x, y, w, 46), "正在部署战场", 28, null, true);
            ui.Panel("loading-track", new Rect(x + 10, y + 68, w - 20, 5), WarSandboxUGUI.Line);
            ui.Panel("loading-progress", new Rect(x + 10, y + 68, (w - 20) * session.LoadingProgress, 5), WarSandboxUGUI.Accent);
            ui.Label("loading-value", new Rect(x, y + 90, w, 32), (session.LoadingProgress * 100).ToString("0") + "%  ·  准备规则与军团", 16, WarSandboxUGUI.Muted);
        }
        private void DrawFailure()
        {
            ui.Panel("failure-bg", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            float w = Mathf.Min(640, ui.Width - 48), x = (ui.Width - w) / 2;
            ui.Label("failure-title", new Rect(x, 64, w, 44), "战场未能就绪", 28, null, true);
            ui.Scroll("failure-scroll", new Rect(x, 128, w, ui.Height - 264), 480);
            ui.Label("failure-text", new Rect(6, 8, w - 12, 460), session.Error, 16, WarSandboxUGUI.Danger); ui.EndScroll();
            ui.Button("failure-return", new Rect(x, ui.Height - 114, w, 42), "返回战场目录", () => session.TryReturnToMenu(true, out _), true);
            ui.Button("failure-quit", new Rect(x, ui.Height - 62, w, 38), "退出游戏", RequestQuit);
        }
        private void RequestReturn()
        {
            confirmingQuit = false;
            if (session.RequiresEndConfirmation) session.BeginConfirmation(); else session.TryReturnToMenu(false, out _);
        }
        private void RequestQuit() { confirmingQuit = true; session.BeginConfirmation(); }
        private void DrawConfirmation()
        {
            ui.Panel("modal-shade", new Rect(0, 0, ui.Width, ui.Height), new Color(0.82f, 0.87f, 0.9f, 0.96f));
            float w = Mathf.Min(480, ui.Width - 40), x = (ui.Width - w) / 2, y = Mathf.Max(20, (ui.Height - 270) / 2);
            ui.Panel("modal-card", new Rect(x, y, w, 270));
            ui.Label("modal-title", new Rect(x + 18, y + 20, w - 36, 44), confirmingQuit ? "退出游戏？" : "结束本局？", 26, null, true);
            ui.Label("modal-body", new Rect(x + 18, y + 76, w - 36, 90), (WarSandboxRuntimeDeployment.BlocksCommands(session.Controller)
                ? "未应用的布阵修改不会保留。" : "当前战斗进度不会保留。") + (session.Error == null ? "" : "\n" + session.Error), 16, WarSandboxUGUI.Muted);
            ui.Button("modal-confirm", new Rect(x + 28, y + 178, w - 56, 38), confirmingQuit ? "退出游戏" : "结束并返回目录", () =>
            { if (confirmingQuit) Application.Quit(); else session.TryReturnToMenu(true, out _); }, true);
            ui.Button("modal-cancel", new Rect(x + 28, y + 224, w - 56, 32), "取消", session.CancelConfirmation);
        }
        private void OnEnable() { nextRefresh = 0; }
        private void OnDisable() { ui?.SetVisible(false); }
        private void OnDestroy() { ui?.Dispose(); }
    }
}
