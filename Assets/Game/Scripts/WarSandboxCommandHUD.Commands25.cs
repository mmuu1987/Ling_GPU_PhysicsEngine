using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxCommandHUD {
  CommandFeedback25 Feedback25=>CommandFeedback25.For(controller);
  void ObserveSelection25(){var f=Feedback25;if(f==null)return;int team=controller.selectedTeam;if(f.Selected==team)return;bool changed=f.Selected>=0;f.Selected=team;f.ClearMarker();if(changed){bool cancel=awaitingMoveTarget;awaitingMoveTarget=false;SetFeedback("已选 · "+FormatTeamName(team)+(cancel?"\n已取消选点，原命令和路线不变":"\n仅切换选择，不下达命令"));}nextUiRefresh=0;}
  void RecordResult25(bool success,string label,bool paused,bool append=false){var f=Feedback25;if(f==null)return;Vector3? target=null;
   if(success){var a=controller.SelectedArmy;if(append){int n=controller.GetMoveRoutePointCount(controller.selectedTeam);if(n>0&&controller.TryGetMoveRoutePoint(controller.selectedTeam,n-1,out var p))target=p;}else if(a!=null&&a.hasOrder&&a.currentOrder.hasTarget)target=a.currentOrder.target;}
   string text=success?FormatTeamName(controller.selectedTeam)+" · "+(append?"已追加航点":"已下达"+label):"未下达 · "+(controller.CommandError??"当前状态不允许此命令");
   if(success)text+="\n"+(paused?(controller.Phase==WarSandboxBattlePhase.Paused?"仍保持暂停":"战斗已恢复"):(append?"保留当前目标，按顺序接续":"仅影响当前所选阵营"));else text+="\n原命令和路线保留";
   SetFeedback(text);f.Result(success,text,target,FormatTeamName(controller.selectedTeam)+" · "+(append?"新增航点":label+"目标"));nextUiRefresh=0;
  }
  string Selection25(string fallback){if(selectionPreviewEnabled)return SelectionStatusP7();if(Feedback25==null)return fallback;var a=controller.SelectedArmy;if(a==null)return "尚未选择阵营";string order=a.hasOrder?FormatOrder(a.currentOrder.type):"等待命令";if(a.hasOrder&&a.currentOrder.hasTarget)order+=" → X "+a.currentOrder.target.x.ToString("F0")+" / Z "+a.currentOrder.target.z.ToString("F0");return "已选 · "+FormatTeamName(controller.selectedTeam)+" · 全阵营 "+controller.GetAliveUnitCount(controller.selectedTeam).ToString("N0")+" 人  |  "+order;}
  string FeedbackText25(string fallback){var f=Feedback25;if(f==null)return fallback;if(f.Fresh&&(f.Rejected||!awaitingMoveTarget))return f.Message;if(awaitingMoveTarget&&moveIntent!=null&&moveIntent.Scope==MemberSelectionScope.Local)return MoveTargetHint();if(awaitingMoveTarget)return FormatTeamName(controller.selectedTeam)+" · 请选择地面目标\n"+(controller.Phase==WarSandboxBattlePhase.Paused?(controller.GetMoveRoutePointCount(controller.selectedTeam)>0?"点击替换会恢复；Shift追加保持暂停":"确认目标将恢复战斗") : "点击替换 · Shift追加 · Esc取消，原命令不变");if(controller.Phase==WarSandboxBattlePhase.Paused)return "战斗已暂停 · 查看/选中不会恢复\n下达新命令会恢复；Shift追加已有路线仍暂停";return "";}
  // Deterministic smoke drives the same paths as the live buttons and ground click.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  public bool TestMove25(Vector3 target,bool append=false)=>IssueMoveTo(target,append);
  public bool AwaitingMove25=>awaitingMoveTarget;
#endif
 }
}


