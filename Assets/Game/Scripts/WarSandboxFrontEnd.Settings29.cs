using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxFrontEnd {
  private void SettingsEscape(){var safety=SettingsSafety29.For(session);if(safety!=null)safety.Escape(session);else CloseSettings();}
  private void DrawSettings29(SettingsSafety29 safety){safety.Begin();var audio=WarSandboxAudio.Ensure();var settings=audio.Settings;
   ui.Panel("settings-shade",new Rect(0,0,ui.Width,ui.Height),session.State==WarSandboxEntryState.Battle?WarSandboxUGUI.Shade:WarSandboxUGUI.Background);
   float w=Mathf.Min(560,ui.Width-40),x=(ui.Width-w)/2,y=Mathf.Max(12,(ui.Height-510)/2);var card=new Rect(x,y,w,510);ui.Panel("settings-card",card);ui.Brackets("settings-card-br",card);
   ui.Code("settings-code",new Rect(x+24,y+18,w-48,20),"SYSTEM // AUDIO",12,WarSandboxUGUI.Accent);
   ui.LabelAligned("settings-title",new Rect(x+14,y+38,w-28,44),safety.ConfirmDefaults?"恢复默认声音设置？":"声音设置",26,WarSandboxUGUI.Ink,true,TextAnchor.MiddleLeft);
   if(safety.ConfirmDefaults){ui.Label("settings-defaults-note",new Rect(x+14,y+110,w-28,110),"音效音量恢复为 65%，关闭静音。\n只影响声音，不改编成、布阵或本地方案。\n确认后仍需保存；取消修改可恢复打开时的值。",16,WarSandboxUGUI.Ink);
    ui.Button("settings-defaults-confirm",new Rect(x+24,y+264,w-48,42),"恢复声音默认值",safety.ApplyDefaults,true);
    ui.Button("settings-defaults-cancel",new Rect(x+24,y+324,w-48,38),"取消，保留当前调整",safety.CancelDefaults);return;}
   ui.Label("settings-note",new Rect(x+14,y+84,w-28,58),"调整立即试听；保存并返回后，下次启动继续使用。\n取消或 Esc 撤销本次调整。打开设置会暂停正在进行的战斗。",13,WarSandboxUGUI.Muted);
   ui.LabelAligned("settings-volume-label",new Rect(x+14,y+146,w-140,28),"音效音量",16,WarSandboxUGUI.Ink,false,TextAnchor.MiddleLeft);
   ui.Code("settings-volume-value",new Rect(x+w-124,y+146,100,28),Mathf.RoundToInt(settings.volume*100)+"%",18,WarSandboxUGUI.Accent,TextAnchor.MiddleRight);
   ui.Slider("settings-volume",new Rect(x+24,y+184,w-48,20),settings.volume,audio.SetVolume);
   ui.Button("settings-mute",new Rect(x+24,y+226,w-48,38),settings.muted?"声音：已静音":"声音：开启",()=>audio.SetMuted(!audio.Settings.muted),false,true,settings.muted);
   ui.Button("settings-preview",new Rect(x+24,y+274,(w-56)/2,36),"试听音效",()=>audio.Play(WarSandboxSoundCue.Start));
   ui.Button("settings-defaults",new Rect(x+32+(w-56)/2,y+274,(w-56)/2,36),"恢复声音默认值",safety.RequestDefaults);
   string error=safety.Error;ui.Label("settings-status",new Rect(x+14,y+320,w-28,28),safety.Pending?"尚未保存 · 本次调整只用于当前会话试听":"当前设置未修改",14,safety.Pending?WarSandboxUGUI.Accent:WarSandboxUGUI.Muted);
   if(!string.IsNullOrEmpty(error))ui.Label("settings-error",new Rect(x+14,y+352,w-28,54),error+"\n不会自动覆盖原文件；可重试保存或取消修改。",13,WarSandboxUGUI.Danger);
   ui.Button("settings-save",new Rect(x+24,y+414,w-48,42),"保存并返回",()=>safety.Save(session),true);
   ui.Button("settings-cancel",new Rect(x+24,y+468,w-48,30),"取消修改并返回",()=>safety.Cancel(session));
  }
 }
}
