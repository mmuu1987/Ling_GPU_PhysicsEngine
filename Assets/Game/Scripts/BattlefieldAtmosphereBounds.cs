using UnityEngine;
namespace MassEngine.Game {
 // Opt-in on the new battlefield only. Does not change simulation or portrait cameras.
 [DefaultExecutionOrder(1000)]
 public sealed class BattlefieldAtmosphereBounds : MonoBehaviour {
  public MyCameraManager cameraController;
  public MassEngineManager manager;
  public float maximumDistance=360, maximumHorizontalRadius=240, maximumHeight=300;
  public Vector3 center=new Vector3(0,10,0);
  public Vector3 Constrain(Vector3 position){
   Vector3 relative=position-center;
   relative=Vector3.ClampMagnitude(relative,maximumDistance);
   Vector2 xz=Vector2.ClampMagnitude(new Vector2(relative.x,relative.z),maximumHorizontalRadius);
   position=new Vector3(center.x+xz.x,Mathf.Clamp(center.y+relative.y,8,maximumHeight),center.z+xz.y);
   if(manager!=null && manager.TryGetTerrainContext(out var surface,out _,out _) && surface!=null)
    position.y=Mathf.Max(position.y,BackdropHeight(surface,position.x,position.z)+5);
   return position;
  }
  void LateUpdate(){if(cameraController!=null && cameraController.ControlledCamera!=null)cameraController.ConstrainWorldPosition(Constrain(cameraController.ControlledCamera.transform.position));}
  public static float BackdropHeight(TerrainSurface surface,float x,float z){
   float bx=Mathf.Clamp(x,-128,128),bz=Mathf.Clamp(z,-128,128);
   surface.TrySample(new Vector2(bx,bz),out var sample);
   float outside=Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))-128;
   if(outside<=0)return sample.Position.y;
   float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,130,outside));
   float a=Mathf.Atan2(z,x);
   float ridges=55+55*Mathf.PerlinNoise(x*.009f+37,z*.009f+71)+24*Mathf.Sin(a*5+.7f);
   return Mathf.Lerp(sample.Position.y,ridges,rise);
  }
 }
}
