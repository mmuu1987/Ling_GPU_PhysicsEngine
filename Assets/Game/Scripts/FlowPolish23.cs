using UnityEngine;
namespace MassEngine.Game {
 // Opt-in to the new scenes only. Existing scenes retain their presentation.
 [DisallowMultipleComponent] public sealed class FlowPolish23:MonoBehaviour {
  public static bool Active(Component owner) { if(owner==null)return false;var d=owner as WarSandboxRuntimeDeployment;var c=d!=null?d.controller:owner.GetComponent<WarSandboxBattleController>();return c!=null&&c.GetComponent<FlowPolish23>()!=null; }
  public static string DraftSummary(WarSandboxRuntimeDeployment d){long n=0;for(int i=0;i<d.Draft.Count;i++)n+=d.Draft[i].count;return d.Draft.Count+"个编成 / "+n.ToString("N0")+"人 · 可以开战；方案需另存";}
  public static string MapHint(WarSandboxRuntimeDeployment d,int selected,bool placing,bool selectedOnMap){if(d.Draft==null||d.Draft.Count==0)return "先添加编成，再调整位置";var e=d.Draft[Mathf.Clamp(selected,0,d.Draft.Count-1)];string name=e.Name;if(name.Length>12)name=name.Substring(0,12)+"…";string label=WarSandboxBattleController.DefaultArmyName(e.teamId)+" · "+name+" · "+e.count.ToString("N0")+"人";return placing?"点击地图放置："+label:selectedOnMap?"已选："+label+" · 点击放置可调整位置":"先点军团旁的“布阵”选中编成，再点“点击放置”";}
 }
}
