using UnityEngine;
namespace MassEngine.Game {
 [DisallowMultipleComponent] public sealed class RosterInfo31:MonoBehaviour {
  public static bool Active(WarSandboxRuntimeDeployment d){if(d==null||d.controller==null)return false;var m=d.controller.GetComponent<RosterInfo31>();return m!=null&&m.isActiveAndEnabled;}
  public static string Source(WarSandboxStatSource s)=>s==WarSandboxStatSource.Local?"本局":s==WarSandboxStatSource.Global?"全局":"官方";
  public static string Origin(WarSandboxStatResolver.Value value,WarSandboxStatDefinition def){if(!value.applies)return "不适用";return value.source==WarSandboxStatSource.Official?"官方默认":Source(value.source)+" · 默认 "+def.Format(value.official)+def.Unit;}
  public static string Effective(WarSandboxRuntimeDeployment d,UnitTypeConfig t,WarSandboxUnitStat key){var def=WarSandboxUnitStats.Get(key);var v=d.DraftStats.Resolve(t,def);return v.applies?def.Format(v.effective)+def.Unit:"不适用";}
  public static string Sources(WarSandboxRuntimeDeployment d,UnitTypeConfig t){var keys=new[]{WarSandboxUnitStat.MaxHp,WarSandboxUnitStat.AttackDamage,WarSandboxUnitStat.AttackInterval,WarSandboxUnitStat.AttackRange};var names=new[]{"生命","伤害","间隔","距离"};string[] labels=new string[4];for(int i=0;i<4;i++){var v=d.DraftStats.Resolve(t,WarSandboxUnitStats.Get(keys[i]));labels[i]=names[i]+" "+(v.applies?Source(v.source):"—");}return labels[0]+" · "+labels[1]+"\n"+labels[2]+" · "+labels[3];}
  public static string Composition(WarSandboxDeploymentDraft draft,int team){int close=0,far=0;for(int i=0;i<draft.Count;i++)if(draft[i].teamId==team){if(WarSandboxUnitStats.IsRanged(draft[i].template))far+=draft[i].count;else close+=draft[i].count;}return "近战 "+close.ToString("N0")+" · 远程 "+far.ToString("N0");}
 }
}
