#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class ReadabilityEvidence
        {
            public string method = "Real uGUI button events with raycast/geometry checks; minimap callback supplies explicit Shift state. 512/384 authored agents, isolated settings, 30 FPS cap. Not OS keyboard/mouse or performance acceptance.";
            public List<string> checks = new List<string>();
            public List<string> captures = new List<string>();
            public bool wide, compact, completed;
        }
        private void UiCheck(bool ok, string message)
        {
            Require(ok, "Readability: " + message);
            if (!report.readability.checks.Contains(message)) report.readability.checks.Add(message);
        }
        private Text ReadUiText(string id)
        {
            var matches = FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && t.transform.parent.name == id).ToArray();
            Require(matches.Length == 1, "Expected one active UI text: " + id); return matches[0];
        }
        private void UiTextContains(string id, string value) => UiCheck(ReadUiText(id).text.Contains(value), id + " contains " + value);
        private void UiTextFits(params string[] ids)
        {
            Canvas.ForceUpdateCanvases();
            foreach (string id in ids)
            {
                var text = ReadUiText(id);
                UiCheck(text.preferredHeight <= text.rectTransform.rect.height + 2, id + " text fits at " + Screen.width + "x" + Screen.height);
            }
        }
        private Button UiButton(string id) => FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b => b.isActiveAndEnabled && b.name == id);
        private IEnumerator UiClick(string id)
        {
            yield return new WaitForSecondsRealtime(.2f); Canvas.ForceUpdateCanvases();
            var button = UiButton(id); Require(button.IsInteractable(), "Disabled UI button: " + id);
            var rect = (RectTransform)button.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Require(point.x > 0 && point.x < Screen.width && point.y > 0 && point.y < Screen.height, "Offscreen click: " + id);
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            UiCheck(hits.Count > 0 && hits[0].gameObject == button.gameObject, id + " is pointer-accessible");
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
            report.actions.Add("uGUI raycast + pointerClick: " + id);
            yield return new WaitForSecondsRealtime(.25f);
        }
        private IEnumerator UiMinimapMove(Vector3 target, bool append)
        {
            var hud = controller.GetComponent<WarSandboxCommandHUD>();
            var ui = typeof(WarSandboxCommandHUD).GetField("runtimeUi", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
            var nodes = (IDictionary)typeof(WarSandboxUGUI).GetField("nodes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui);
            var node = nodes["mini-input"];
            var pointer = (Action<Vector2, int, bool>)node.GetType().GetField("pointer").GetValue(node);
            var normalized = WarSandboxMinimapProjection.WorldToMap(target, manager.systemConfig.simulationConfig.simulationWorldSize, new Rect(0, 0, 1, 1));
            pointer(normalized, 1, append);
            report.actions.Add("uGUI minimap callback: target=" + target + " Shift=" + append);
            yield return new WaitForSecondsRealtime(.25f);
        }
        private IEnumerator UiCapture(string file)
        {
            yield return CapturePresetUI(file); report.readability.captures.Add(file); WriteReport();
        }
        private IEnumerator UiResize(int width, int height)
        {
            ReleaseCamera(); Screen.SetResolution(width, height, FullScreenMode.Windowed);
            float deadline = Time.realtimeSinceStartup + 10;
            while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(Screen.width == width && Screen.height == height, "Requested UI viewport did not apply.");
            ConfigureCamera(manager.TerrainSurface != null);
            yield return new WaitForSecondsRealtime(.35f);
        }
        private IEnumerator RunReadabilitySmoke()
        {
            report.readability = new ReadabilityEvidence();
            var audio = WarSandboxAudio.Ensure();
            Require(audio.SettingsPath == settingsFile && string.IsNullOrEmpty(audio.SettingsError), "Wrong readability settings path.");
            audio.SetMuted(true);
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == "launch-open", "Wrong readability startup scene.");
            BindBattle(false); VerifyFullStrength(); Require(manager.UnitTypes.TotalAgentCount == 512, "Wrong flat population.");
            Require(Screen.width == 1280 && Screen.height == 720, "Start UI probe at 1280x720.");
            yield return new WaitForSecondsRealtime(.35f);
            Stage("ui-wide-setup");
            UiTextContains("battle-toggle", "开始"); UiTextContains("selected-army", "攻方");
            UiTextContains("order-status", "等待命令");
            UiTextFits("battle-title", "battle-toggle", "selected-army", "order-status", "order-detail", "preset-briefing");
            yield return UiCapture("ui-setup-720p.png");
            yield return UiClick("army-1"); yield return UiClick("move");
            UiTextContains("feedback-title", "守方");
            yield return UiClick("army-0"); UiTextContains("feedback-title", "攻方");
            yield return UiClick("move-cancel");
            UiCheck(controller.Phase == WarSandboxBattlePhase.Setup && !controller.GetArmy(0).hasOrder && !controller.GetArmy(1).hasOrder,
                "cancel selection does not start battle or issue an order");
            yield return UiClick("army-1"); yield return UiClick("hold");
            UiTextContains("order-status", "原地防守");
            yield return UiClick("army-0"); yield return UiClick("move");
            yield return UiMinimapMove(new Vector3(-180, 0, -40), false);
            UiTextContains("order-detail", "X -180");
            yield return UiClick("battle-toggle"); UiCheck(!manager.IsBattleRunning, "top pause button pauses simulation");
            yield return UiMinimapMove(new Vector3(-190, 0, 20), true);
            UiTextContains("order-detail", "后续航点 1 个");
            UiCheck(!manager.IsBattleRunning && controller.GetMoveRoutePointCount(0) == 2, "paused append retains pause and current leg");
            UiTextFits("feedback-title", "feedback-text", "order-detail", "battle-toggle");
            var target = controller.GetArmy(0).currentOrder.target;
            yield return UiClick("battle-toggle");
            UiCheck(manager.IsBattleRunning && controller.GetMoveRoutePointCount(0) == 2 && controller.GetArmy(0).currentOrder.target == target,
                "top resume keeps route and active target");
            yield return UiClick("battle-toggle");
            // Scrolling must not hide the pinned selection or the global pause/resume button.
            var scroll = FindObjectsByType<ScrollRect>(FindObjectsSortMode.None).Single(r => r.name == "orders-scroll");
            var selected = ReadUiText("selected-army"); Vector3 pinned = selected.transform.position;
            scroll.verticalNormalizedPosition = 0; yield return new WaitForSecondsRealtime(.3f);
            UiCheck(Vector3.Distance(pinned, selected.transform.position) < .01f, "selected command card remains pinned while scrolling");
            yield return UiClick("battle-toggle"); yield return UiClick("battle-toggle");
            scroll.verticalNormalizedPosition = 1; yield return new WaitForSecondsRealtime(.25f);
            yield return UiCapture("ui-paused-route-720p.png"); report.readability.wide = true;

            Stage("ui-compact-target-picking");
            yield return UiResize(640, 480);
            yield return UiClick("move");
            UiTextContains("feedback-text", "不解除暂停");
            UiTextFits("battle-title", "battle-toggle", "selected-army", "order-detail", "feedback-title", "feedback-text", "move-cancel");
            yield return UiCapture("ui-target-compact.png");
            yield return UiClick("move-cancel");
            UiCheck(controller.GetMoveRoutePointCount(0) == 2 && !manager.IsBattleRunning, "compact cancel preserves queued route and pause");
            yield return UiClick("orders-toggle");
            UiTextContains("compact-status-text", "攻方"); UiTextFits("compact-status-text");
            yield return UiClick("battle-toggle"); UiCheck(manager.IsBattleRunning, "collapsed panel still permits resume");
            yield return UiClick("battle-toggle");
            yield return UiClick("battle-help"); UiTextContains("details-text", "Shift"); UiTextFits("details-text");
            yield return UiClick("battle-debug"); UiTextContains("details-text", "技术信息");
            yield return UiClick("battle-debug");
            report.readability.compact = true;
            // Return to a normal viewport and verify the three-army/invalid-terrain presentation.
            yield return UiResize(1280, 720);
            controller.ResetBattle(); yield return new WaitForSecondsRealtime(.25f); VerifyFullStrength();
            yield return Leave(false); VerifySources(); VerifyPlans();
            yield return Enter("launch-mountain", true); Require(manager.UnitTypes.TotalAgentCount == 384, "Wrong mountain population.");
            yield return new WaitForSecondsRealtime(.35f);
            yield return UiClick("army-2"); UiTextContains("selected-army", controller.GetArmy(2).displayName);
            yield return UiClick("army-0"); yield return UiClick("move");
            yield return UiMinimapMove(new Vector3(-80, 0, 73), false);
            UiTextContains("feedback-text", "原命令保留");
            UiCheck(controller.Phase == WarSandboxBattlePhase.Setup && !controller.GetArmy(0).hasOrder, "invalid cliff click preserves Setup and no order");
            UiTextFits("feedback-title", "feedback-text", "selected-army", "order-detail");
            yield return UiCapture("ui-mountain-invalid.png");
            yield return UiClick("move-cancel");
            yield return Leave(false); VerifySources(); VerifyPlans();
            Require(!System.IO.File.Exists(settingsFile), "Readability probe wrote settings.");
            report.readability.completed = true; Stage("complete");
        }
    }
}
#endif
