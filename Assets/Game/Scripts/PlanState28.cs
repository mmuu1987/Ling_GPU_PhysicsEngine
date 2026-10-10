using System;using System.Collections.Generic;using UnityEngine;
namespace MassEngine.Game {
 [DisallowMultipleComponent]public sealed class PlanState28:MonoBehaviour {
  public WarSandboxRuntimeDeployment deployment;
  string baseline;bool initialized;public bool Applied {get;private set;}
  sealed class Saved{public string directory,fingerprint;}
  readonly Dictionary<string,Saved> saved=new Dictionary<string,Saved>(StringComparer.OrdinalIgnoreCase);
  public static PlanState28 For(WarSandboxBattleController c){if(c==null)return null;var p=c.GetComponent<PlanState28>();return p!=null&&p.isActiveAndEnabled?p:null;}
  void Awake(){if(deployment==null)deployment=GetComponent<WarSandboxRuntimeDeployment>();}
  public void Initialize(string value){if(initialized||string.IsNullOrEmpty(value))return;baseline=value;initialized=true;}
  public void MarkApplied(){Applied=true;}
  public static string Canonical(WarSandboxPlanFile plan){if(plan==null)return null;plan.planId="state28";plan.displayName="state28";return JsonUtility.ToJson(plan);}
  public string Current=>deployment!=null?deployment.PlanSignature28(true):null;
  public bool DraftChanged=>deployment!=null&&deployment.IsEditing&&Current!=deployment.PlanSignature28(false);
  public bool CurrentStored{get{string value=Current;if(value==null)return false;foreach(var v in saved.Values)if(v.directory==deployment.PlanStore.DirectoryPath&&v.fingerprint==value)return true;return false;}}
  public bool NeedsLeaveWarning=>initialized&&((Current!=baseline&&!CurrentStored)||PendingInputs);
  bool PendingInputs=>deployment!=null&&deployment.IsEditing&&deployment.GetComponent<WarSandboxDeploymentHUD>()!=null&&deployment.GetComponent<WarSandboxDeploymentHUD>().HasUncommittedInput28;
  public bool RecordStored(string slot,out string error){if(deployment.PlanStore.TryLoad(slot,out var plan,out error)){saved[slot]=new Saved{directory=deployment.PlanStore.DirectoryPath,fingerprint=Canonical(plan)};return true;}saved.Remove(slot);return false;}
  public void RefreshKnownFiles(){var keys=new List<string>(saved.Keys);foreach(string slot in keys){if(saved[slot].directory!=deployment.PlanStore.DirectoryPath)continue;if(!RecordStored(slot,out _))saved.Remove(slot);}}
  public string DraftStatus(bool pending){if(pending)return "输入未更新 · 完成或更新后再保存；应用不等于保存";return (DraftChanged?"草稿已修改 · 尚未应用到本局":Applied?"与本局已应用配置一致":"当前为默认布阵")+" · "+(CurrentStored?"已保存为本地方案":"方案需另存");}
  public static string SetupText(WarSandboxBattleController c,string fallback){var p=For(c);return p==null||!p.Applied?fallback:"已应用到本局 · "+(p.CurrentStored?"方案已保存":"方案未保存")+" · 可开战";}
  public static string LeaveText(WarSandboxBattleController c,string fallback){var p=For(c);if(p==null)return fallback;if(p.PendingInputs)return "还有未更新的输入，离开后不会保留。\n应用到本局不等于保存方案。\n取消后可返回方案库检查并另存。";if(p.NeedsLeaveWarning)return "当前自定义配置尚未保存为本地方案。\n应用到本局不等于保存到磁盘。\n离开后将丢失；取消后可在方案库另存。";return fallback+"\n已保存的本地方案文件不受影响。";}
  public static void RequestCatalog(WarSandboxSceneSession session){if(session==null)return;if(For(session.Controller)==null){session.TryReturnToMenu(false,out _);return;}var front=UnityEngine.Object.FindFirstObjectByType<WarSandboxFrontEnd>();if(front!=null)front.RequestReturn28();}
 }
}
