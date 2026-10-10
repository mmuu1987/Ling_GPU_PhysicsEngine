using System;using System.IO;using UnityEngine;
namespace MassEngine.Game {
 [DisallowMultipleComponent]public sealed class SettingsSafety29:MonoBehaviour {
  WarSandboxAudioSettings opened;bool editing;string unresolvedLoadError;
  public bool ConfirmDefaults {get;private set;}
  public string Error {get;private set;}
  public static SettingsSafety29 For(WarSandboxSceneSession session){if(session==null)return null;var p=session.GetComponent<SettingsSafety29>();return p!=null&&p.isActiveAndEnabled?p:null;}
  void Start(){var a=WarSandboxAudio.Ensure();unresolvedLoadError=a.SettingsError;if(!string.IsNullOrEmpty(unresolvedLoadError))GetComponent<WarSandboxSceneSession>().OpenSettings();}
  static bool Same(WarSandboxAudioSettings a,WarSandboxAudioSettings b)=>a.version==b.version&&Mathf.Abs(a.volume-b.volume)<.000001f&&a.muted==b.muted;
  public bool Pending=>editing&&!Same(opened,WarSandboxAudio.Ensure().Settings);
  public void Begin(){if(editing)return;opened=WarSandboxAudio.Ensure().Settings;Error=unresolvedLoadError;editing=true;ConfirmDefaults=false;}
  public void RequestDefaults(){Begin();ConfirmDefaults=true;}
  public void CancelDefaults(){ConfirmDefaults=false;}
  public void ApplyDefaults(){WarSandboxAudio.Ensure().RestoreDefaults();ConfirmDefaults=false;}
  public void Cancel(WarSandboxSceneSession session){Begin();var a=WarSandboxAudio.Ensure();a.SetVolume(opened.volume);a.SetMuted(opened.muted);editing=false;ConfirmDefaults=false;session.CloseSettings();}
  public void Escape(WarSandboxSceneSession session){if(ConfirmDefaults)CancelDefaults();else Cancel(session);}
  public bool Save(WarSandboxSceneSession session){Begin();var a=WarSandboxAudio.Ensure();if(!a.SaveSettings()){Error=a.SettingsError;return false;}var store=new WarSandboxSettingsStore(a.SettingsPath);if(!File.Exists(a.SettingsPath)||!store.TryLoad(out var loaded,out _)||!Same(loaded,a.Settings)){Error="设置已写入，但读回核验未通过。请重试；本次试听仍保留。";return false;}Error=null;unresolvedLoadError=null;editing=false;session.CloseSettings();return true;}
 }
}
