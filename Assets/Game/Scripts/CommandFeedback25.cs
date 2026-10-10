using UnityEngine;
namespace MassEngine.Game {
 // Presentation-only receipt. Never issues orders, resumes battle, or rewrites navigation.
 [DisallowMultipleComponent]public sealed class CommandFeedback25:MonoBehaviour {
  public WarSandboxBattleController controller;
  public string Message {get;private set;} public float Until {get;private set;}
  public bool Rejected {get;private set;} public int Accepted {get;private set;} public int Failures {get;private set;}
  public int Selected=-1; public Vector3 Target {get;private set;} public bool HasTarget {get;private set;}
  public string TargetLabel {get;private set;} float targetUntil;WarSandboxUGUI markers;
  public static CommandFeedback25 For(WarSandboxBattleController c){if(c==null)return null;var f=c.GetComponent<CommandFeedback25>();return f!=null&&f.isActiveAndEnabled?f:null;}
  public bool Fresh=>Time.unscaledTime<Until&&!string.IsNullOrEmpty(Message);
  public void Notice(string message,bool rejected=false){Message=message;Rejected=rejected;Until=Time.unscaledTime+4.5f;}
  public void Result(bool success,string message,Vector3? target,string label){if(success)Accepted++;else Failures++;Notice(message,!success);HasTarget=success&&target.HasValue;if(HasTarget){Target=target.Value;TargetLabel=label;targetUntil=Time.unscaledTime+3.2f;}}
  public void ClearMarker(){HasTarget=false;}
  void LateUpdate(){if(controller==null)controller=GetComponent<WarSandboxBattleController>();bool visible=HasTarget&&Time.unscaledTime<targetUntil&&controller!=null&&(controller.Phase==WarSandboxBattlePhase.Running||controller.Phase==WarSandboxBattlePhase.Paused)&&!WarSandboxDeploymentHUD.BlocksInput(controller)&&(WarSandboxSceneSession.Instance==null||!WarSandboxSceneSession.Instance.InputBlocked);
   if(!visible){markers?.SetVisible(false);if(Time.unscaledTime>=targetUntil)HasTarget=false;return;}
   var hud=controller.GetComponent<WarSandboxCommandHUD>();var cam=hud!=null&&hud.commandCamera!=null?hud.commandCamera:Camera.main;if(cam==null)return;
   var center=cam.WorldToScreenPoint(Target+Vector3.up*.2f);if(center.z<=0||center.x<0||center.y<0||center.x>Screen.width||center.y>Screen.height){markers?.SetVisible(false);return;}
   if(markers==null)markers=new WarSandboxUGUI(transform,"Command25 target feedback",45);markers.Begin();float scale=markers.Scale,x=center.x/scale,y=(Screen.height-center.y)/scale;var color=WarSandboxUGUI.Accent;float age=3.2f-(targetUntil-Time.unscaledTime);float radius=5+2*Mathf.Sin(Mathf.Min(age,1)*Mathf.PI);
   for(int i=0;i<24;i++){float angle=i*Mathf.PI*2/24;var p=Target+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;if(!controller.TryResolveGroundPoint(p,out p,out _))continue;var s=cam.WorldToScreenPoint(p+Vector3.up*.15f);if(s.z<=0)continue;markers.Panel("target-dot-"+i,new Rect(s.x/scale-2,(Screen.height-s.y)/scale-2,4,4),color,false);}
   markers.Panel("target-cross-h",new Rect(x-9,y-1,18,2),color,false);markers.Panel("target-cross-v",new Rect(x-1,y-9,2,18),color,false);
   float tx=Mathf.Clamp(x-102,8,Mathf.Max(8,markers.Width-212)),ty=Mathf.Clamp(y-44,8,Mathf.Max(8,markers.Height-40));markers.Panel("target-caption-bg",new Rect(tx,ty,204,28),WarSandboxUGUI.Surface,false);markers.LabelAligned("target-caption",new Rect(tx+4,ty,196,28),TargetLabel,13,WarSandboxUGUI.Ink,true,TextAnchor.MiddleCenter);markers.End();
  }
  void OnDisable(){markers?.SetVisible(false);HasTarget=false;}
  void OnDestroy(){markers?.Dispose();}
 }
}
