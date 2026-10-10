using UnityEngine;
namespace MassEngine.Game {
 // Read-only height samples baked from the existing visual mesh; no navigation edits.
 public sealed class CameraGround24:ScriptableObject {
  public float[] coordinates, heights;
  int Cell(float p){int lo=0,hi=coordinates.Length-1;while(hi-lo>1){int m=(lo+hi)/2;if(coordinates[m]<=p)lo=m;else hi=m;}return Mathf.Clamp(lo,0,coordinates.Length-2);}
  public float Height(float x,float z){if(coordinates==null||coordinates.Length<2)return 0;int n=coordinates.Length,ix=Cell(x),iz=Cell(z),a=iz*n+ix;float tx=Mathf.InverseLerp(coordinates[ix],coordinates[ix+1],x),tz=Mathf.InverseLerp(coordinates[iz],coordinates[iz+1],z);return tx>=tz?(1-tx)*heights[a]+(tx-tz)*heights[a+1]+tz*heights[a+n+1]:(1-tz)*heights[a]+(tz-tx)*heights[a+n]+tx*heights[a+n+1];}
 }
}
