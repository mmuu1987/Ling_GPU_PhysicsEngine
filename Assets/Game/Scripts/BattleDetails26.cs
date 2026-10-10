using UnityEngine;
namespace MassEngine.Game {
 // Scene opt-in only. Presentation/input state; no battle simulation changes.
 [DisallowMultipleComponent]public sealed class BattleDetails26:MonoBehaviour {
  public enum Decision {None,Reset,End}
  public Decision Pending {get;set;}
  public bool WasInBattle {get;set;}
  public static BattleDetails26 For(WarSandboxBattleController c){if(c==null)return null;var x=c.GetComponent<BattleDetails26>();return x!=null&&x.isActiveAndEnabled?x:null;}
 }
}
