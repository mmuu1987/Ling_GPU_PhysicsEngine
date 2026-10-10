using UnityEngine;
namespace MassEngine.Game {
 [DefaultExecutionOrder(1000)]
 public sealed class TerrainScaleBounds:MonoBehaviour {
  public MyCameraManager cameraController; public MassEngineManager manager;
  public float maximumDistance=720,maximumHorizontalRadius=480,maximumHeight=600;
  public Vector3 center=new Vector3(0,10,0);
  public Vector3 Constrain(Vector3 p){var r=Vector3.ClampMagnitude(p-center,maximumDistance);var xz=Vector2.ClampMagnitude(new Vector2(r.x,r.z),maximumHorizontalRadius);p=new Vector3(center.x+xz.x,Mathf.Clamp(center.y+r.y,8,maximumHeight),center.z+xz.y);if(manager!=null&&manager.TryGetTerrainContext(out var s,out _,out _)&&s!=null)p.y=Mathf.Max(p.y,BackdropHeight(s,p.x,p.z)+5);return p;}
  void LateUpdate(){if(cameraController!=null&&cameraController.ControlledCamera!=null)cameraController.ConstrainWorldPosition(Constrain(cameraController.ControlledCamera.transform.position));}
  public static float BackdropHeight(TerrainSurface s,float x,float z){s.TrySample(new Vector2(Mathf.Clamp(x,-256,256),Mathf.Clamp(z,-256,256)),out var q);float outside=Mathf.Max(Mathf.Abs(x),Mathf.Abs(z))-256;if(outside<=0)return q.Position.y;float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(24,260,outside));float ridge=55+55*Mathf.PerlinNoise(x*.0045f+37,z*.0045f+71)+24*Mathf.Sin(Mathf.Atan2(z,x)*5+.7f);return Mathf.Lerp(q.Position.y,ridge,rise);}
 }
}
