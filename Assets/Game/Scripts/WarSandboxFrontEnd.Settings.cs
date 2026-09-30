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
            var audio = WarSandboxAudio.Ensure(); var settings = audio.Settings;
            ui.Panel("settings-shade", new Rect(0, 0, ui.Width, ui.Height), WarSandboxUGUI.Background);
            float w = Mathf.Min(480, ui.Width - 40), x = (ui.Width - w) / 2, y = Mathf.Max(12, (ui.Height - 464) / 2);
            ui.Panel("settings-card", new Rect(x, y, w, 464));
            ui.Label("settings-title", new Rect(x + 20, y + 14, w - 40, 48), "声音设置", 26, null, true);
            ui.Label("settings-note", new Rect(x + 20, y + 66, w - 40, 54), "调整立即生效，保存后下次启动继续使用。\n打开设置时，正在进行的战斗会暂停。", 14, WarSandboxUGUI.Muted);
            ui.Label("settings-volume-label", new Rect(x + 20, y + 130, w - 40, 28), "音效音量  " + Mathf.RoundToInt(settings.volume * 100) + "%", 18);
            ui.Slider("settings-volume", new Rect(x + 40, y + 176, w - 80, 24), settings.volume, audio.SetVolume);
            ui.Button("settings-mute", new Rect(x + 30, y + 220, w - 60, 38), settings.muted ? "声音：已静音" : "声音：开启", () => audio.SetMuted(!audio.Settings.muted), false, true, settings.muted);
            ui.Button("settings-preview", new Rect(x + 30, y + 270, (w - 70) / 2, 36), "试听音效", () => audio.Play(WarSandboxSoundCue.Start));
            ui.Button("settings-defaults", new Rect(x + 40 + (w - 70) / 2, y + 270, (w - 70) / 2, 36), "恢复默认", audio.RestoreDefaults);
            if (!string.IsNullOrEmpty(audio.SettingsError))
            {
                ui.Label("settings-error", new Rect(x + 20, y + 314, w - 40, 50), audio.SettingsError, 14, WarSandboxUGUI.Danger);
                ui.Button("settings-without-save", new Rect(x + 30, y + 416, w - 60, 30), "返回（不保存）", session.CloseSettings);
            }
            ui.Button("settings-save", new Rect(x + 30, y + 366, w - 60, 40), "保存并返回", CloseSettings, true);
        }
    }
}
