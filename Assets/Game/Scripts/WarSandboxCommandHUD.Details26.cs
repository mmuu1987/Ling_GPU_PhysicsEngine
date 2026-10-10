using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxCommandHUD {
  BattleDetails26 Details26=>BattleDetails26.For(controller);
  bool PendingDecision26=>Details26!=null&&Details26.Pending!=BattleDetails26.Decision.None;
  bool RejectInput26=>Details26!=null&&(PendingDecision26||controller==null||IsTerminalPhase(controller.Phase));
  void ClearTransient26(){awaitingMoveTarget=false;commandFeedback=null;feedbackUntil=0;Feedback25?.ClearMarker();Feedback25?.Notice("");nextUiRefresh=0;}
  void SynchronizeDetails26(){var d=Details26;if(d==null)return;bool battle=controller.Phase==WarSandboxBattlePhase.Running||controller.Phase==WarSandboxBattlePhase.Paused;bool blocked=WarSandboxDeploymentHUD.BlocksInput(controller)||(WarSandboxSceneSession.Instance!=null&&WarSandboxSceneSession.Instance.InputBlocked);
   bool keepSetupReset=!blocked&&controller.Phase==WarSandboxBattlePhase.Setup&&d.Pending==BattleDetails26.Decision.Reset;
   if(!battle||blocked){if(awaitingMoveTarget||(PendingDecision26&&!keepSetupReset)||Feedback25!=null&&(Feedback25.Fresh||Feedback25.HasTarget))ClearTransient26();if(!keepSetupReset)d.Pending=BattleDetails26.Decision.None;if(d.WasInBattle){armyStatisticsOpen=false;battleToolsOpen=false;helpOpen=false;diagnosticsOpen=false;}d.WasInBattle=false;}else d.WasInBattle=true;
  }
  bool BlockHotkeys26(){if(!PendingDecision26)return false;if(Input.GetKeyDown(KeyCode.Escape))CancelDecision26();return true;}
  void RequestDecision26(BattleDetails26.Decision decision){var d=Details26;if(d==null){awaitingMoveTarget=false;if(decision==BattleDetails26.Decision.Reset)controller.ResetBattle();else controller.EndBattle();return;}
   if(IsTerminalPhase(controller.Phase))return;ClearTransient26();d.Pending=decision;nextUiRefresh=0;
  }
  void CancelDecision26(){var d=Details26;if(d==null)return;d.Pending=BattleDetails26.Decision.None;SetFeedback("已取消操作\n战斗状态和当前命令未改动");nextUiRefresh=0;}
  void ConfirmDecision26(){var d=Details26;if(d==null||d.Pending==BattleDetails26.Decision.None)return;var decision=d.Pending;d.Pending=BattleDetails26.Decision.None;ClearTransient26();battleToolsOpen=false;armyStatisticsOpen=false;
   if(IsTerminalPhase(controller.Phase))return;if(decision==BattleDetails26.Decision.Reset)controller.ResetBattle();else controller.EndBattle();nextUiRefresh=0;
  }
  string FeedbackText26(string text){if(Details26==null||string.IsNullOrEmpty(text))return text;if(controller.Phase==WarSandboxBattlePhase.Paused)return text.Replace("战斗已恢复","当前已暂停");if(controller.Phase==WarSandboxBattlePhase.Running)return text.Replace("仍保持暂停","当前战斗进行中");return text;}
  void DrawDecision26(WarSandboxUGUI ui){if(!PendingDecision26)return;float w=Mathf.Min(480,ui.Width-32),x=(ui.Width-w)/2,y=Mathf.Max(16,(ui.Height-244)/2);bool reset=Details26.Pending==BattleDetails26.Decision.Reset;
   ui.Panel("decision26-shade",new Rect(0,0,ui.Width,ui.Height),new Color(0.08f,.16f,.17f,.52f),true);
   ui.Panel("decision26-card",new Rect(x,y,w,244),WarSandboxUGUI.Surface,true);
   ui.Label("decision26-title",new Rect(x+22,y+18,w-44,34),reset?"确认重置本局？":"确认结束本局？",23,WarSandboxUGUI.Ink,true);
   ui.Label("decision26-body",new Rect(x+22,y+58,w-44,80),reset?"清除当前命令、路线和战斗进度。\n恢复本局初始配置，回到战前准备。":"立即结束本局，不判定胜负。\n这不是暂停，不能继续当前这一局。",17,WarSandboxUGUI.Ink);
   ui.Label("decision26-state",new Rect(x+22,y+140,w-44,28),controller.Phase==WarSandboxBattlePhase.Running?"当前仍在交战，弹窗不会自动暂停":controller.Phase==WarSandboxBattlePhase.Paused?"当前保持暂停，取消不会恢复战斗":"当前为战前准备，确认不会开始战斗",13,WarSandboxUGUI.Muted);
   ui.Button("decision26-cancel",new Rect(x+22,y+185,(w-56)/2,40),"取消 · 保持现状  Esc",CancelDecision26,true);
   ui.Button("decision26-confirm",new Rect(x+34+(w-56)/2,y+185,(w-56)/2,40),reset?"确认重置":"确认结束",ConfirmDecision26,false);
  }
  void OnApplicationFocus(bool focused){if(!focused)CancelSelectionP7(!playerScopeEnabled);if(!focused&&Details26!=null&&awaitingMoveTarget){CancelMoveTarget();Feedback25?.ClearMarker();SetFeedback("窗口失去焦点，已取消选点\n原命令和路线不变；回来后重新按移动");}}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  public bool DecisionPending26=>PendingDecision26;
  public bool TestHotkeyGate26()=>BlockHotkeys26();
  public void TestFocus26(bool focused)=>OnApplicationFocus(focused);
#endif
 }
}


