using UnityEngine;
namespace MassEngine.Game {
 [DisallowMultipleComponent]public sealed class BattleResult27:MonoBehaviour {
  public static bool Active(WarSandboxBattleController controller){if(controller==null)return false;var marker=controller.GetComponent<BattleResult27>();return marker!=null&&marker.isActiveAndEnabled;}
 }
}
