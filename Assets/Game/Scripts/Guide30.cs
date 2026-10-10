using System;using System.IO;using UnityEngine;
namespace MassEngine.Game {
 [DisallowMultipleComponent]public sealed class Guide30:MonoBehaviour {
  [Serializable]class Record{public int version=1;public bool dismissed;}
  public bool Seen {get;private set;}public bool Intro {get;private set;}public int Page {get;private set;}public string Error {get;private set;}public string FilePath {get;private set;}
  bool offered;WarSandboxSceneSession session;
  public static Guide30 For(WarSandboxSceneSession s){if(s==null)return null;var g=s.GetComponent<Guide30>();return g!=null&&g.isActiveAndEnabled?g:null;}
  void Awake(){session=GetComponent<WarSandboxSceneSession>();FilePath=Path.Combine(Application.persistentDataPath,"WarSandboxGuide30.json");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
   foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--war-sandbox-guide-file=")){string p=arg.Substring("--war-sandbox-guide-file=".Length);if(!Path.IsPathFullyQualified(p))throw new ArgumentException("Guide override must be absolute");FilePath=p;}
#endif
   try{if(File.Exists(FilePath)){var r=JsonUtility.FromJson<Record>(File.ReadAllText(FilePath));if(r==null||r.version!=1)throw new IOException();Seen=r.dismissed;}}catch(Exception ex)when(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException){Error="引导记录无法读取；只影响首次提示，不影响方案或设置。";}
  }
  public void Tick(){if(Seen||offered||session.InputBlocked||session.Controller==null)return;var d=session.Controller.GetComponent<WarSandboxRuntimeDeployment>();if(d!=null&&d.IsEditing){offered=true;Open(true);}}
  public void Open(bool intro=false){if(session.IsLoading||session.SettingsOpen||session.ConfirmationOpen||session.HelpInputBlocked30)return;Intro=intro;Page=0;session.OpenHelp30();}
  public void Select(int page){Page=Mathf.Clamp(page,0,2);}
  public void Close(){if(Intro){Seen=true;Intro=false;string temp=FilePath+".tmp-"+Guid.NewGuid().ToString("N");try{Directory.CreateDirectory(Path.GetDirectoryName(FilePath));File.WriteAllText(temp,JsonUtility.ToJson(new Record{dismissed=true}));if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);Error=null;}catch(Exception ex)when(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException){Error="本次不再自动提示，但记录保存失败，重启后可能再次提示。点击关闭即可继续。";return;}finally{try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}}session.CloseHelp30();}
 }
}
