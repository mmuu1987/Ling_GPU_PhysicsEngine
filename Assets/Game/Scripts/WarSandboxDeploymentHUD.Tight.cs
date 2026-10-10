using UnityEngine;
namespace MassEngine.Game {
 public sealed partial class WarSandboxDeploymentHUD {
  private int tightDialog; // 1 replace draft, 2 start high-load battle.
  private bool TightEnabled => deployment!=null&&deployment.GetComponent<TightFormationTrial>()!=null;
  private int TightPopulation(){int n=0;if(deployment?.Draft!=null)for(int i=0;i<deployment.Draft.Count;i++)n+=deployment.Draft[i].count;return n;}
  private string TightDraftNotice()=>TightEnabled&&TightPopulation()>50000?"高负载试验 · 可能明显卡顿；建议先保存方案，开战前还需确认":null;
  private void DrawTightHeader(WarSandboxUGUI ui){if(TightEnabled)ui.Button("tight-trial-open",new Rect(ui.Width-398,62,184,38),"十万人压力试验",()=>{if(CommitFields()){tightDialog=1;clearFocusRequested=true;}});}
  private void SetTightPreset(bool tight){
   if(!CommitFields()||deployment.Draft.Count==0)return;
   if(!TightFormationTrial.TryPreset(deployment.Draft[selected],tight,out var e,out var error)){inputError=error;return;}
   deployment.Draft.Set(selected,e);RefreshAfterUiChange();nextUiRefresh=0;
  }
  private void DrawTightPresets(WarSandboxUGUI ui,ref float y,float width){
   if(!TightEnabled)return;
   ui.Button("formation-standard",new Rect(0,y,(width-6)/2,34),"标准 0.4",()=>SetTightPreset(false));
   ui.Button("formation-tight",new Rect((width+6)/2,y,(width-6)/2,34),"紧密 0.7",()=>SetTightPreset(true));y+=38;
   ui.Label("formation-preset-note",new Rect(0,y,width,42),"切回自动占地；不改人数、位置和角色大小",12,WarSandboxUGUI.Muted);y+=46;
  }
  private void LoadTightTrial(){
   tightDialog=0;if(!TightEnabled||!CommitFields())return;
   var source=deployment.SourceScenario;if(source==null||source.unitTypes.Length<2){inputError="缺少试验模板。";return;}
   var entries=new WarSandboxDeploymentEntry[2];
   for(int i=0;i<2;i++){var e=WarSandboxDeploymentEntry.From(source.unitTypes[i]);e.teamId=i;e.count=50000;e.center=new Vector3(i==0?-128:128,0,0);if(!TightFormationTrial.TryPreset(e,true,out entries[i],out var error)){inputError=error;return;}}
   var candidate=new WarSandboxDeploymentDraft(entries,deployment.Draft.Rules,deployment.Draft.Stats);
   if(!deployment.TryReplaceDraft(candidate,out var problem)){inputError=problem;return;}
   selected=0;spatialSelection=true;placing=legionOpen=pickerOpen=false;RefreshAfterUiChange();nextUiRefresh=0;
  }
  // Returns true when the new scene has handled or rejected this request.
  private bool RequestTightStart(){
   if(!TightEnabled)return false;
   if(!CommitFields())return true;
   if(TightPopulation()<=50000)return false;
   if(!deployment.TryValidate(out var error)){inputError=error;return true;}
   tightDialog=2;clearFocusRequested=true;return true;
  }
  private void DrawTightDialog(WarSandboxUGUI ui){
   if(!TightEnabled||tightDialog==0)return;
   ui.Panel("tight-dialog-shade",new Rect(0,0,ui.Width,ui.Height),new Color(.08f,.14f,.18f,.82f));
   float w=Mathf.Min(550,ui.Width-40),x=(ui.Width-w)/2,y=(ui.Height-280)/2;
   ui.Panel("tight-dialog-card",new Rect(x,y,w,280));
   ui.Label("tight-dialog-title",new Rect(x+24,y+16,w-48,42),tightDialog==1?"载入双方各五万人的试验布阵？":"确认开始高负载试验？",23,WarSandboxUGUI.Ink,true);
   ui.Label("tight-dialog-warning",new Rect(x+24,y+70,w-48,120),tightDialog==1?"密度0.7，角色体型不变。\n将替换当前草稿，可撤销；建议先保存原方案。\n载入不会自动开战。十万人可能严重卡顿。":"这不是流畅性能保证，可能明显卡顿。\n保持现有碰撞、禁行与占地检查，不缩小角色。\n确认后才会应用当前布阵并开始战斗。",17,WarSandboxUGUI.Ink);
   ui.Button("tight-dialog-cancel",new Rect(x+24,y+222,(w-60)/2,38),"取消",()=>{tightDialog=0;clearFocusRequested=true;});
   ui.Button("tight-dialog-confirm",new Rect(x+36+(w-60)/2,y+222,(w-60)/2,38),tightDialog==1?"确认载入草稿":"确认开战",()=>{if(tightDialog==1)LoadTightTrial();else{tightDialog=0;Apply(true);}},true);
  }
 }
}
