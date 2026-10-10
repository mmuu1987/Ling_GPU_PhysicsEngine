using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxDeploymentHUD {
  private bool Check32=>DeploymentCheck32.Active(deployment);
  private string StartLabel32(){if(HasPendingInputs())return "更新并开战  →";var p=PlanState28.For(deployment.controller);return p!=null&&p.Applied&&!p.DraftChanged?"开战  →":"应用并开战  →";}
  private void DrawCheck32(WarSandboxUGUI ui,Rect rect){
   bool valid=deployment.TryValidate(out string validation);bool pending=HasPendingInputs();
   string problem=inputError??validation;int index=DeploymentCheck32.Index(problem,deployment.Draft);
   // A parse error describes current input, not another formation's validated draft.
   bool raw=inputError!=null&&index<0&&(inputError.Contains("当前输入未写入草稿")||inputError=="请输入有效的有限数值。");
   string message;
   if(problem!=null){message="暂不能开战 · "+(raw?DeploymentCheck32.Scope(deployment.Draft,selected)+"："+problem:DeploymentCheck32.Problem(problem,deployment.Draft));message+="\n"+(raw?"输入尚未写入草稿；请修正后再更新。":DeploymentCheck32.Hint(problem));}
   else if(pending)message="输入未更新 · 点击开战会先更新输入、校验并应用\n只想检查位置可先更新；应用到本局不等于保存方案。";
   else {var p=PlanState28.For(deployment.controller);message=(p!=null?p.DraftStatus(false):"布阵校验通过")+"\n"+(TightDraftNotice()??(legionOpen?"完成返回仅更新草稿；返回布阵后可应用并开战。":"校验通过 · 开战会自动应用当前草稿，无需先点应用。"));}
   bool locate=raw||index>=0;
   Rect labelRect=rect;if(locate)labelRect.width-=126;
   ui.Label("deployment-validation",labelRect,message,13,problem!=null||!valid?WarSandboxUGUI.Danger:WarSandboxUGUI.Muted);
   if(locate)ui.Button("deployment-fix32",new Rect(rect.xMax-118,rect.y+5,114,32),raw?"修正输入":"定位问题",()=>Locate32(raw,index,problem));
  }
  private void Locate32(bool raw,int index,string problem){
   if(deployment.Draft==null||deployment.Draft.Count==0)return;
   if(raw){
    // Do not read/commit or discard invalid text just to expose the existing editor.
    bool badCount=!int.TryParse(count,out int n)||n<=0;
    legionOpen=badCount;spatialSelection=!badCount;
   }else{
    if(index<0||index>=deployment.Draft.Count)return;
    if(index!=selected){if(!CommitFields())return;selected=index;RefreshAfterUiChange();}
    legionOpen=!DeploymentCheck32.Spatial(problem);spatialSelection=!legionOpen;
   }
   pickerOpen=armyMenu=templateMenu=placing=removeArmyConfirm=false;clearFocusRequested=true;nextUiRefresh=0;
  }
 }
}
