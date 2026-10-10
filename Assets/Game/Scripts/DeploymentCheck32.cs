using System;
using UnityEngine;
namespace MassEngine.Game {
 // Presentation only. Validation, allocation and start continue through existing code.
 [DisallowMultipleComponent] public sealed class DeploymentCheck32:MonoBehaviour {
  public static bool Active(WarSandboxRuntimeDeployment d){if(d==null||d.controller==null)return false;var m=d.controller.GetComponent<DeploymentCheck32>();return m!=null&&m.isActiveAndEnabled;}
  // Recognize only the existing validator's explicit index prefix; never guess an army from free text.
  public static int Index(string error,WarSandboxDeploymentDraft draft){if(draft==null||string.IsNullOrEmpty(error)||!error.StartsWith("编成 ",StringComparison.Ordinal))return -1;int end=error.IndexOf('：');if(end<4||!int.TryParse(error.Substring(3,end-3),out int n)||n<1||n>draft.Count)return -1;return n-1;}
  public static string Scope(WarSandboxDeploymentDraft draft,int i)=>WarSandboxBattleController.DefaultArmyName(draft[i].teamId)+" · 编成 "+(i+1).ToString("00");
  public static string Problem(string error,WarSandboxDeploymentDraft draft){int i=Index(error,draft);return i<0?error:Scope(draft,i)+error.Substring(error.IndexOf('：'));}
  public static bool Spatial(string error)=>error!=null&&(error.Contains("脚印")||error.Contains("位置")||error.Contains("阵型")||error.Contains("密度")||error.Contains("宽深")||error.Contains("占地"));
  public static string Hint(string error){if(error==null)return "";if(error.Contains("重叠"))return "移动此编成，避开另一编成；不会自动挪动。";if(error.Contains("越出"))return "调整中心或缩小阵型，确保完整脚印在战场内。";if(error.Contains("禁行"))return "调整位置或阵型，避开禁行区域；修正后再检查。";if(error.Contains("人数"))return "在军团详情调整人数；修正后还会校验位置和阵型。";if(Spatial(error))return "检查位置、密度或阵型尺寸；不会自动修改配置。";return "先处理此项再检查；校验不通过时不会应用或开战。";}
 }
}
