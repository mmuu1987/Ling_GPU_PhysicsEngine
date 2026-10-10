using System;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Settings → 镜头 page (2026-10-10). Same save / cancel / defaults flow as the audio page via SettingsSafety29.</summary>
    public sealed partial class WarSandboxFrontEnd
    {
        private void SettingsTabs(SettingsSafety29 safety, float x, float y, float w)
        {
            ui.Button("settings-tab-audio", new Rect(x + w - 220, y + 46, 94, 32), "声音", () => safety.Page = 0, false, true, safety.Page == 0);
            ui.Button("settings-tab-camera", new Rect(x + w - 118, y + 46, 94, 32), "镜头", () => safety.Page = 1, false, true, safety.Page == 1);
        }

        private static void EditCamera(Func<WarSandboxCameraSettings, WarSandboxCameraSettings> change) =>
            WarSandboxCameraPrefs.Set(change(WarSandboxCameraPrefs.Current));

        private void CameraSlider(string id, string label, float x, float top, float w, string value, float slider, Action<float> change)
        {
            ui.LabelAligned(id + "-label", new Rect(x + 14, top, w - 140, 26), label, 16, WarSandboxUGUI.Ink, false, TextAnchor.MiddleLeft);
            ui.Code(id + "-value", new Rect(x + w - 124, top, 100, 26), value, 18, WarSandboxUGUI.Accent, TextAnchor.MiddleRight);
            ui.Slider(id, new Rect(x + 24, top + 30, w - 48, 18), slider, change);
        }

        private void DrawCameraSettings(SettingsSafety29 safety)
        {
            var s = WarSandboxCameraPrefs.Current;
            ui.Panel("settings-shade", new Rect(0, 0, ui.Width, ui.Height), session.State == WarSandboxEntryState.Battle ? WarSandboxUGUI.Shade : WarSandboxUGUI.Background);
            float h = 600, w = Mathf.Min(560, ui.Width - 40), x = (ui.Width - w) / 2, y = Mathf.Max(12, (ui.Height - h) / 2);
            var card = new Rect(x, y, w, h); ui.Panel("settings-card", card); ui.Brackets("settings-card-br", card);
            ui.Code("settings-code", new Rect(x + 24, y + 18, w - 48, 20), "SYSTEM // CAMERA", 12, WarSandboxUGUI.Accent);
            ui.LabelAligned("settings-title", new Rect(x + 14, y + 38, w - 28, 44), safety.ConfirmDefaults ? "恢复默认镜头设置？" : "镜头设置", 26, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            if (safety.ConfirmDefaults)
            {
                ui.Label("settings-defaults-note", new Rect(x + 14, y + 110, w - 28, 110), "平移、缩放、转向恢复 ×1.00，聚焦俯角 50°，取景适中，跟随自动缩放开启。\n只影响镜头，不改声音、编成、布阵或本地方案。\n确认后仍需保存；取消修改可恢复打开时的值。", 16, WarSandboxUGUI.Ink);
                ui.Button("settings-defaults-confirm", new Rect(x + 24, y + 264, w - 48, 42), "恢复镜头默认值", safety.ApplyDefaults, true);
                ui.Button("settings-defaults-cancel", new Rect(x + 24, y + 324, w - 48, 38), "取消，保留当前调整", safety.CancelDefaults);
                return;
            }
            SettingsTabs(safety, x, y, w);
            float row = y + 96, step = 56;
            CameraSlider("camera-pan", "平移速度（拖拽 / 方向键 / 右键+WASD）", x, row, w, "×" + s.panSpeed.ToString("0.00"),
                WarSandboxCameraSettings.MultiplierToSlider(s.panSpeed), v => EditCamera(c => { c.panSpeed = WarSandboxCameraSettings.SliderToMultiplier(v); return c; }));
            CameraSlider("camera-zoom", "缩放速度（滚轮）", x, row + step, w, "×" + s.zoomSpeed.ToString("0.00"),
                WarSandboxCameraSettings.MultiplierToSlider(s.zoomSpeed), v => EditCamera(c => { c.zoomSpeed = WarSandboxCameraSettings.SliderToMultiplier(v); return c; }));
            CameraSlider("camera-look", "转向灵敏度（右键 / Alt+左键）", x, row + step * 2, w, "×" + s.lookSpeed.ToString("0.00"),
                WarSandboxCameraSettings.MultiplierToSlider(s.lookSpeed), v => EditCamera(c => { c.lookSpeed = WarSandboxCameraSettings.SliderToMultiplier(v); return c; }));
            CameraSlider("camera-pitch", "聚焦俯角", x, row + step * 3, w, Mathf.RoundToInt(s.focusPitch) + "°",
                Mathf.InverseLerp(30f, 75f, s.focusPitch), v => EditCamera(c => { c.focusPitch = Mathf.Round(Mathf.Lerp(30f, 75f, Mathf.Clamp01(v))); return c; }));
            CameraSlider("camera-framing", "聚焦取景", x, row + step * 4, w, s.FramingLabel,
                s.framing, v => EditCamera(c => { c.framing = Mathf.Round(Mathf.Clamp01(v) * 20f) / 20f; return c; }));
            float buttons = row + step * 5 + 4, half = (w - 56) / 2;
            ui.Button("camera-follow-zoom", new Rect(x + 24, buttons, half, 36), s.followAutoZoom ? "跟随自动缩放：开" : "跟随自动缩放：关",
                () => EditCamera(c => { c.followAutoZoom = !c.followAutoZoom; return c; }), false, true, s.followAutoZoom);
            ui.Button("settings-defaults", new Rect(x + 32 + half, buttons, half, 36), "恢复镜头默认值", safety.RequestDefaults);
            ui.Label("settings-status", new Rect(x + 14, y + 430, w - 28, 28), safety.Pending ? "尚未保存 · 关闭设置后即可试用，取消修改会恢复" : "当前设置未修改", 14, safety.Pending ? WarSandboxUGUI.Accent : WarSandboxUGUI.Muted);
            string error = safety.Error;
            if (!string.IsNullOrEmpty(error)) ui.Label("settings-error", new Rect(x + 14, y + 456, w - 28, 44), error, 13, WarSandboxUGUI.Danger);
            ui.Button("settings-save", new Rect(x + 24, y + 504, w - 48, 42), "保存并返回", () => safety.Save(session), true);
            ui.Button("settings-cancel", new Rect(x + 24, y + 556, w - 48, 30), "取消修改并返回", () => safety.Cancel(session));
        }
    }
}
