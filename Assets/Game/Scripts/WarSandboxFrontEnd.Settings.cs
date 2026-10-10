using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxFrontEnd
    {
        private void CloseSettings()
        {
            if (WarSandboxAudio.Ensure().SaveSettings()) session.CloseSettings();
        }

        private void DrawSettings()
        {
            var safety = SettingsSafety29.For(session);
            if (safety != null) { DrawSettings29(safety); return; }
            var audio = WarSandboxAudio.Ensure(); var settings = audio.Settings;
            ui.Panel("settings-shade", new Rect(0, 0, ui.Width, ui.Height), session.State == WarSandboxEntryState.Battle ? WarSandboxUGUI.Shade : WarSandboxUGUI.Background);
            float w = Mathf.Min(500, ui.Width - 40), x = (ui.Width - w) / 2, y = Mathf.Max(12, (ui.Height - 480) / 2);
            var card = new Rect(x, y, w, 480);
            ui.Panel("settings-card", card); ui.Brackets("settings-card-br", card);
            ui.Code("settings-code", new Rect(x + 24, y + 18, w - 48, 20), "SYSTEM // AUDIO", 12, WarSandboxUGUI.Accent);
            ui.LabelAligned("settings-title", new Rect(x + 14, y + 38, w - 28, 44), "声音设置", 26, WarSandboxUGUI.Ink, true, TextAnchor.MiddleLeft);
            ui.Label("settings-note", new Rect(x + 14, y + 84, w - 28, 50), "调整立即生效，保存后下次启动继续使用。\n打开设置时，正在进行的战斗会暂停。", 13, WarSandboxUGUI.Muted);
            ui.LabelAligned("settings-volume-label", new Rect(x + 14, y + 146, w - 140, 28), "音效音量", 16, WarSandboxUGUI.Ink, false, TextAnchor.MiddleLeft);
            ui.Code("settings-volume-value", new Rect(x + w - 124, y + 146, 100, 28), Mathf.RoundToInt(settings.volume * 100) + "%", 18, WarSandboxUGUI.Accent, TextAnchor.MiddleRight);
            ui.Slider("settings-volume", new Rect(x + 24, y + 184, w - 48, 20), settings.volume, audio.SetVolume);
            ui.Button("settings-mute", new Rect(x + 24, y + 226, w - 48, 38), settings.muted ? "声音：已静音" : "声音：开启", () => audio.SetMuted(!audio.Settings.muted), false, true, settings.muted);
            ui.Button("settings-preview", new Rect(x + 24, y + 274, (w - 56) / 2, 36), "试听音效", () => audio.Play(WarSandboxSoundCue.Start));
            ui.Button("settings-defaults", new Rect(x + 32 + (w - 56) / 2, y + 274, (w - 56) / 2, 36), "恢复默认", audio.RestoreDefaults);
            if (!string.IsNullOrEmpty(audio.SettingsError))
            {
                ui.Label("settings-error", new Rect(x + 14, y + 318, w - 28, 50), audio.SettingsError, 13, WarSandboxUGUI.Danger);
                ui.Button("settings-without-save", new Rect(x + 24, y + 428, w - 48, 32), "返回（不保存）", session.CloseSettings);
            }
            ui.Button("settings-save", new Rect(x + 24, y + 374, w - 48, 42), "保存并返回", CloseSettings, true);
        }
    }
}
