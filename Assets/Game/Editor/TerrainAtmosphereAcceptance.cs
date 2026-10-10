using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor {
 public static class TerrainAtmosphereAcceptance {
  [Serializable]class Receipt {public bool passed;public int seamVertices,cameraCases;public bool sourceReferencesUnchanged,noSceneryColliders,hazeOutsidePlayable;}
  static void Check(bool b,string note){if(!b)throw new Exception(note);}
  public static void Run(){
   var r=new Receipt();EditorSceneManager.OpenScene(TerrainAtmosphereBuilder.Battle,OpenSceneMode.Single);
   var manager=Object.FindFirstObjectByType<MassEngineManager>();var source="Assets/Game/Experiments/ForestPrototype/";
   Check(manager.terrainSurfaceAsset==AssetDatabase.LoadAssetAtPath<TerrainSurfaceAsset>(source+"Surface.asset"),"Terrain changed");
   Check(manager.systemConfig==AssetDatabase.LoadAssetAtPath<MassEngineSystemConfig>(source+"System.asset"),"System changed");
   Check(manager.scenarioConfig==AssetDatabase.LoadAssetAtPath<ScenarioConfig>(source+"Scenario.asset"),"Scenario changed");r.sourceReferencesUnchanged=true;
   var ground=GameObject.Find("Plan B - Shared Render And Navigation Surface");var back=GameObject.Find("Visual Only Backdrop - No navigation or colliders");var trees=GameObject.Find("Visual Only Distant Treeline");
   Check(back.GetComponentsInChildren<Collider>().Length==0&&trees.GetComponentsInChildren<Collider>().Length==0,"Background colliders");r.noSceneryColliders=true;
   var mesh=back.GetComponent<MeshFilter>().sharedMesh;var original=ground.GetComponent<MeshFilter>().sharedMesh;
   var vertices=mesh.vertices;var normals=mesh.normals;var colors=mesh.colors32;var oldV=original.vertices;var oldN=original.normals;var oldC=original.colors32;
   for(int i=0;i<vertices.Length;i++){
    var v=vertices[i];if(Mathf.Abs(v.x)>128.001f||Mathf.Abs(v.z)>128.001f)continue;
    if(Mathf.Abs(Mathf.Abs(v.x)-128)>.001f&&Mathf.Abs(Mathf.Abs(v.z)-128)>.001f)continue;
    int x=Mathf.RoundToInt((v.x+128)/2),z=Mathf.RoundToInt((v.z+128)/2),j=z*129+x;
    Check(Vector3.Distance(v,oldV[j])<.0001f,"Geometry seam");Check(Vector3.Distance(normals[i],oldN[j])<.0001f,"Normal seam");Check(colors[i].Equals(oldC[j]),"Color seam");r.seamVertices++;
   }
   Check(r.seamVertices==512,"Expected exact 512 vertex boundary");
   var indices=mesh.triangles;for(int i=0;i<indices.Length;i+=3){var c=(vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3;Check(Mathf.Abs(c.x)>=128||Mathf.Abs(c.z)>=128,"Backdrop overlaps playing surface");}
   var material=back.GetComponent<Renderer>().sharedMaterial;
   Check(material==ground.GetComponent<Renderer>().sharedMaterial,"Different seam materials");Check(material.GetFloat("_HazeStart")>Mathf.Sqrt(2)*128,"Fog touches playable corners");Check(material.GetFloat("_HazeEnd")<704,"Outer geometry not fully faded");Check(!RenderSettings.fog,"Global fog washes out units");
   var sky=Camera.main.backgroundColor;var expected=QualitySettings.activeColorSpace==ColorSpace.Linear?sky.linear:sky;var color=material.GetVector("_HazeColor");Check(Vector3.Distance(new Vector3(color.x,color.y,color.z),new Vector3(expected.r,expected.g,expected.b))<.0001f,"Fog/clear color mismatch");r.hazeOutsidePlayable=true;
   var bounds=Camera.main.GetComponent<BattlefieldAtmosphereBounds>();for(int i=0;i<24;i++)foreach(float h in new[]{-100f,900f}){
    float angle=i*Mathf.PI/12;var p=bounds.Constrain(new Vector3(Mathf.Cos(angle)*1000,h,Mathf.Sin(angle)*1000));
    Check((p-bounds.center).magnitude<=bounds.maximumDistance+.01f,"Radius escape");Check(new Vector2(p.x-bounds.center.x,p.z-bounds.center.z).magnitude<=bounds.maximumHorizontalRadius+.01f,"Horizontal escape");Check(p.y<=bounds.maximumHeight+.01f&&p.y>=8,"Height escape");r.cameraCases++;
   }
   r.passed=true;File.WriteAllText("Logs/TerrainAtmosphere-20261004/acceptance.json",JsonUtility.ToJson(r,true));Debug.Log("TERRAIN_ATMOSPHERE_ACCEPTANCE_PASS "+JsonUtility.ToJson(r));
  }
 }
}
