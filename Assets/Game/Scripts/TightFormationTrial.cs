using UnityEngine;
namespace MassEngine.Game {
 // Opt-in marker and preset policy for the independent tight-formation battlefield.
 public sealed class TightFormationTrial:MonoBehaviour {
  public const float TightDensity=.7f,StandardDensity=.4f,Aspect=2.2f;
  public static float MinimumGuaranteedSpacing(SpawnConfig spawn,int count,Vector3 size){
   int columns=Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count*Mathf.Max(.01f,size.z/Mathf.Max(.01f,size.x)))),1,count);
   int rows=Mathf.CeilToInt(count/(float)columns);float sx=rows>1?size.x/(rows-1):float.PositiveInfinity,sz=columns>1?size.z/(columns-1):float.PositiveInfinity;
   float jitter=spawn!=null?Mathf.Clamp(spawn.formationJitterFraction,0,.08f):.08f;
   return Mathf.Min(sx,sz)*(1-2*jitter);
  }
  public static bool TryPreset(WarSandboxDeploymentEntry prior,bool tight,out WarSandboxDeploymentEntry next,out string error){
   next=prior;error=null;next.manualSize=Vector3.zero;next.density=tight?TightDensity:StandardDensity;next.aspect=Aspect;
   if(next.template==null||next.template.flockingConfig==null){error="缺少角色占地配置。";return false;}
   float diameter=next.template.flockingConfig.agentRadius*2;
   if(MinimumGuaranteedSpacing(next.template.spawnConfig,next.count,next.Size)+.0001f<diameter){error="该角色体型或生成间距不适合此预设，请保留较低密度。";return false;}
   return true;
  }
 }
}
