using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor {
 public static class TerrainAtmosphereBuilder {
  public const string Root="Assets/Game/Experiments/TerrainAtmosphere", Menu=Root+"/LaunchMenu.unity", Battle=Root+"/ForestHills.unity";
  const string Source="Assets/Game/Experiments/ForestPrototype", Logs="Logs/TerrainAtmosphere-20261004";
  static Color sky=new Color(.65f,.78f,.81f,1);
  static Material MakeMaterial(string name,Color color,bool vertices){
   var m=new Material(Shader.Find("MassEngine/Battlefield Backdrop Haze")){name=name};
   m.SetColor("_BaseColor",color);m.SetFloat("_VertexTint",vertices?1:0);
   m.SetVector("_HazeColor",QualitySettings.activeColorSpace==ColorSpace.Linear?sky.linear:sky);
   m.SetFloat("_HazeStart",190);m.SetFloat("_HazeEnd",540);
   AssetDatabase.CreateAsset(m,Root+"/"+name+".mat");return m;
  }
  public static void Prepare(){
   if(Directory.Exists(Root))throw new Exception("Refuse to overwrite atmosphere scene");
   Directory.CreateDirectory(Root);Directory.CreateDirectory(Logs);AssetDatabase.Refresh();ShaderUtil.allowAsyncCompilation=false;
   if(!AssetDatabase.CopyAsset(Source+"/ForestHills.unity",Battle))throw new Exception("Copy battlefield failed");
   var scene=EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);
   var manager=Object.FindFirstObjectByType<MassEngineManager>();
   if(!manager.TryGetTerrainContext(out var surface,out var nav,out var error)||surface==null||nav==null)throw new Exception(error??"Missing terrain");
   var ground=GameObject.Find("Plan B - Shared Render And Navigation Surface");
   var originalMesh=ground.GetComponent<MeshFilter>().sharedMesh;var originalColors=originalMesh.colors32;
   var groundMaterial=MakeMaterial("AtmosphereGround",Color.white,true);ground.GetComponent<MeshRenderer>().sharedMaterial=groundMaterial;
   // Fine spacing at the original edge gives an exact shared boundary; only the outside is coarsened.
   var coordinates=new List<float>();for(int x=-704;x<-128;x+=16)coordinates.Add(x);for(int x=-128;x<=128;x+=2)coordinates.Add(x);for(int x=144;x<=704;x+=16)coordinates.Add(x);
   int n=coordinates.Count;var vertices=new Vector3[n*n];var colors=new Color32[n*n];var indices=new List<int>();
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){
    float wx=coordinates[x],wz=coordinates[z],outside=Mathf.Max(Mathf.Abs(wx),Mathf.Abs(wz))-128;int i=z*n+x;
    vertices[i]=new Vector3(wx,BattlefieldAtmosphereBounds.BackdropHeight(surface,wx,wz),wz);
    int gx=Mathf.Clamp(Mathf.RoundToInt((wx+128)*.5f),0,128),gz=Mathf.Clamp(Mathf.RoundToInt((wz+128)*.5f),0,128);
    Color edge=originalColors[gz*129+gx];
    var low=Color.Lerp(new Color(.26f,.40f,.20f),new Color(.40f,.48f,.27f),Mathf.PerlinNoise(wx*.014f+80,wz*.014f+50));
    var far=Color.Lerp(new Color(.27f,.34f,.31f),new Color(.40f,.45f,.37f),Mathf.PerlinNoise(wx*.02f+40,wz*.02f+70));
    Color c=Color.Lerp(low,far,Mathf.SmoothStep(0,1,Mathf.InverseLerp(35,140,outside)));
    colors[i]=Color.Lerp(edge,c,Mathf.SmoothStep(0,1,Mathf.Clamp01(outside/35)));
    if(x==n-1||z==n-1)continue;
    float mx=(wx+coordinates[x+1])*.5f,mz=(wz+coordinates[z+1])*.5f;
    if(Mathf.Abs(mx)<128 && Mathf.Abs(mz)<128)continue;
    indices.Add(i);indices.Add(i+n+1);indices.Add(i+1);indices.Add(i);indices.Add(i+n);indices.Add(i+n+1);
   }
   var mesh=new Mesh{name="Visual only seamless apron and distant ridges",indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.colors32=colors;mesh.triangles=indices.ToArray();mesh.RecalculateNormals();MatchSeamNormals(mesh,originalMesh);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Root+"/Backdrop.asset");
   var backdrop=new GameObject("Visual Only Backdrop - No navigation or colliders");backdrop.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=backdrop.AddComponent<MeshRenderer>();renderer.sharedMaterial=groundMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;
   var vegetation=new GameObject("Visual Only Distant Treeline").transform;
   var existing=GameObject.Find("Kenney CC0 - Scenery inside authored exclusion zones").transform.Cast<Transform>().Where(t=>t.name.StartsWith("tree_")).Take(15).ToArray();
   var random=new System.Random(1404);var materials=new Dictionary<Material,Material>();
   for(int i=0;i<120;i++){
    float angle=(float)random.NextDouble()*Mathf.PI*2;
    float radius=148+(float)random.NextDouble()*105;
    float x=Mathf.Cos(angle)*radius,z=Mathf.Sin(angle)*radius;
    if(Mathf.Abs(x)<133&&Mathf.Abs(z)<133){i--;continue;}
    var go=Object.Instantiate(existing[i%existing.Length].gameObject,vegetation);go.name="Distant "+go.name;
    go.transform.localScale*=.85f+(float)random.NextDouble()*.7f;go.transform.rotation=Quaternion.Euler(0,(float)random.NextDouble()*360,0);
    var rs=go.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
    go.transform.position+=new Vector3(x,BattlefieldAtmosphereBounds.BackdropHeight(surface,x,z)-.15f,z)-new Vector3(b.center.x,b.min.y,b.center.z);
    foreach(var collider in go.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
    foreach(var r in rs){r.shadowCastingMode=ShadowCastingMode.Off;var ms=r.sharedMaterials;
     for(int m=0;m<ms.Length;m++){var original=ms[m];if(!materials.TryGetValue(original,out var haze)){haze=MakeMaterial("DistantTree"+materials.Count,original.color,false);materials.Add(original,haze);}ms[m]=haze;}r.sharedMaterials=ms;}
   }
   var camera=Camera.main;camera.backgroundColor=sky;camera.farClipPlane=1800;RenderSettings.fog=false;
   var control=Object.FindFirstObjectByType<MyCameraManager>();control.ControlledCamera=camera;control.MaxZoomDistance=360;
   var envelope=camera.gameObject.AddComponent<BattlefieldAtmosphereBounds>();envelope.cameraController=control;envelope.manager=manager;
   // No change to terrain provider, world bounds, scenario, roster, or deployment rules.
   var catalog=Object.Instantiate(AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Source+"/Catalog.asset"));AssetDatabase.CreateAsset(catalog,Root+"/Catalog.asset");
   foreach(var entry in catalog.entries){entry.scenePath=Battle;entry.displayName="林地丘陵 · 远山薄雾";entry.description="可玩地形不变 · 外围远山与薄雾\n拉远仍保留完整环境，近处部队保持清晰。";entry.briefing+="\n外围远山仅为背景，不可部署；战场镜头有观察范围限制。";}
   Object.FindFirstObjectByType<WarSandboxRuntimeDeployment>().battlefieldCatalog=catalog;EditorUtility.SetDirty(catalog);
   EditorUtility.SetDirty(control);EditorUtility.SetDirty(envelope);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   CaptureViews();
   File.Copy(Logs+"/01-default.png",Root+"/Preview.png");AssetDatabase.ImportAsset(Root+"/Preview.png");
   catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");catalog.entries[0].preview=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Preview.png");EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
   AssetDatabase.CopyAsset(Source+"/LaunchMenu.unity",Menu);var menu=EditorSceneManager.OpenScene(Menu,OpenSceneMode.Single);Object.FindFirstObjectByType<WarSandboxSceneSession>().catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");EditorSceneManager.SaveScene(menu);
   File.WriteAllText(Logs+"/prepare.json","{\"passed\":true,\"playableSize\":256,\"backdropWidth\":1408,\"backgroundTrees\":120,\"maximumCameraDistance\":360}");
  }
  static void MatchSeamNormals(Mesh backdrop,Mesh ground){
   var vertices=backdrop.vertices;var normals=backdrop.normals;var original=ground.normals;
   for(int i=0;i<vertices.Length;i++){var p=vertices[i];if(Mathf.Abs(p.x)<=128.001f&&Mathf.Abs(p.z)<=128.001f){int x=Mathf.Clamp(Mathf.RoundToInt((p.x+128)*.5f),0,128),z=Mathf.Clamp(Mathf.RoundToInt((p.z+128)*.5f),0,128);normals[i]=original[z*129+x];}}
   backdrop.normals=normals;
  }
  public static void RefineSeam(){
   EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);
   var backdrop=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Backdrop.asset");var ground=GameObject.Find("Plan B - Shared Render And Navigation Surface").GetComponent<MeshFilter>().sharedMesh;
   MatchSeamNormals(backdrop,ground);EditorUtility.SetDirty(backdrop);AssetDatabase.SaveAssets();CaptureViews();
   File.Copy(Logs+"/01-default.png",Root+"/Preview.png",true);AssetDatabase.ImportAsset(Root+"/Preview.png");
  }
  public static void CaptureViews(){
   ShaderUtil.allowAsyncCompilation=false;Directory.CreateDirectory(Logs);
   EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);var camera=Camera.main;var envelope=camera.GetComponent<BattlefieldAtmosphereBounds>();
   var pos=camera.transform.position;var rot=camera.transform.rotation;
   Capture(camera,Logs+"/01-default.png");
   foreach(var yaw in new[]{0f,90f,180f,270f}){
    camera.transform.position=envelope.Constrain(new Vector3(0,10,0)+Quaternion.Euler(48,yaw,0)*new Vector3(0,0,-360));camera.transform.LookAt(new Vector3(0,10,0));Capture(camera,Logs+"/02-max-"+yaw+".png");
   }
   camera.transform.position=envelope.Constrain(new Vector3(0,900,0));camera.transform.rotation=Quaternion.Euler(90,0,0);Capture(camera,Logs+"/03-top.png");
   camera.transform.position=pos;camera.transform.rotation=rot;
  }
  static void Capture(Camera camera,string path){var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var old=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
  public static void Build(){const string output="Builds/TerrainAtmosphere-20261004-14";if(Directory.Exists(output))throw new Exception("Refuse overwrite build");Directory.CreateDirectory(output);Directory.CreateDirectory(Logs);
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Menu,Battle},locationPathName=output+"/WarSandbox.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
   File.WriteAllText(Logs+"/build.json",JsonUtility.ToJson(new Receipt{passed=report.summary.result==BuildResult.Succeeded,buildGuid=report.summary.guid.ToString(),errors=(int)report.summary.totalErrors,warnings=(int)report.summary.totalWarnings},true));if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed");
   File.WriteAllText(output+"/Start-Terrain.cmd","@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1080\r\n");
  }
  [Serializable]class Receipt{public bool passed;public string buildGuid;public int errors,warnings;}
 }
}
