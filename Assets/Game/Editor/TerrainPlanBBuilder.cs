using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor
{
 public static class TerrainPlanBBuilder
 {
  public const string Root="Assets/Game/Experiments/ForestPrototype";
  public const string Menu=Root+"/LaunchMenu.unity", Battle=Root+"/ForestHills.unity";
  static Dictionary<int,Material> mats=new Dictionary<int,Material>();
  static T Copy<T>(T source,string name) where T:Object {var a=Object.Instantiate(source);a.name=name;AssetDatabase.CreateAsset(a,Root+"/"+name+".asset");return a;}
  static float Hill(float x,float z) {return 23*Mathf.Exp(-(x*x/2700+(z-18)*(z-18)/1800))+14*Mathf.Exp(-((x+62)*(x+62)/1300+(z+42)*(z+42)/800))+Mathf.Pow(Mathf.Max(0,Mathf.Abs(z)-57),2)*.006f;}
  static bool Block(float x,float z) {return Mathf.Abs(z)>82 || Mathf.Abs(x)>119 || ((x+15)*(x+15)/144+(z-29)*(z-29)/100<1) || ((x-34)*(x-34)/100+(z+30)*(z+30)/121<1);}
  static Material Convert(Material src) {
   if(mats.TryGetValue(src.GetInstanceID(),out var m)) return m;
   m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Kenney "+src.name,enableInstancing=true};
   m.color=src.HasProperty("_Color")?src.GetColor("_Color"):Color.white;
   if(src.mainTexture!=null)m.SetTexture("_BaseMap",src.mainTexture);
   m.SetFloat("_Smoothness",.06f);AssetDatabase.CreateAsset(m,Root+"/NatureMaterial"+mats.Count+".mat");mats[src.GetInstanceID()]=m;return m;
  }
  static void Prop(string name,Vector3 p,float height,float yaw,Transform parent) {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/KenneyNaturePlanB/"+name+".fbx");if(prefab==null)throw new Exception("Missing model "+name);
   var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetParent(parent);go.transform.rotation=Quaternion.Euler(0,yaw,0);
   var rs=go.GetComponentsInChildren<Renderer>();var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
   go.transform.localScale*=height/Mathf.Max(.01f,bounds.size.y);bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
   go.transform.position+=p-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
   foreach(var r in rs)r.sharedMaterials=r.sharedMaterials.Select(Convert).ToArray();
  }
  public static void Prepare() {
   if(Directory.Exists(Root))throw new Exception("Refuse overwrite "+Root);Directory.CreateDirectory(Root);AssetDatabase.Refresh();ShaderUtil.allowAsyncCompilation=false;
   var source=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset");
   var catalog=Copy(source,"Catalog");
   const int n=129;float[] heights=new float[n*n];bool[] blocked=new bool[(n-1)*(n-1)];
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){float wx=-128+2*x,wz=-128+2*z;heights[z*n+x]=Hill(wx,wz);if(x<n-1&&z<n-1)blocked[z*(n-1)+x]=Block(wx+1,wz+1);}
   var a=ScriptableObject.CreateInstance<TerrainSurfaceAsset>();a.Initialize("forest-hills-b12",1,n,n,new Vector2(-128,-128),new Vector2(256,256),38,heights,blocked);AssetDatabase.CreateAsset(a,Root+"/Surface.asset");
   if(!a.TryCreateSurface(out var surface,out var error))throw new Exception(error);
   var mesh=TerrainPrototype.CreateMesh(surface);var colors=new Color32[mesh.vertexCount];var vs=mesh.vertices;
   for(int i=0;i<vs.Length;i++){
    var p=vs[i];float path=Mathf.Abs(p.z-8*Mathf.Sin(p.x/27));float side=Mathf.Abs(p.z-(45+4*Mathf.Sin(p.x/20)));
    var c=Color.Lerp(new Color(.34f,.49f,.20f),new Color(.53f,.63f,.29f),Mathf.PerlinNoise(p.x*.04f+70,p.z*.04f+50));
    float road=1-Mathf.SmoothStep(4,8,Mathf.Min(path,side+2));c=Color.Lerp(c,new Color(.67f,.57f,.37f),road);
    if(Block(p.x,p.z)&&Mathf.Abs(p.z)<65&&Mathf.Abs(p.x)<65)c=new Color(.45f,.47f,.32f);
    colors[i]=c;
   }
   mesh.colors32=colors;AssetDatabase.CreateAsset(mesh,Root+"/GroundMesh.asset");
   var mat=new Material(Shader.Find("MassEngine/Terrain Prototype"));AssetDatabase.CreateAsset(mat,Root+"/Ground.mat");
   AssetDatabase.CopyAsset(source.entries.First(e=>e.id=="robot-expressive").scenePath,Battle);
   var scene=EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);
   source=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Content/Characters/OfficialRoster/Version08/Catalog.asset");catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");
   a=AssetDatabase.LoadAssetAtPath<TerrainSurfaceAsset>(Root+"/Surface.asset");if(!a.TryCreateSurface(out surface,out error))throw new Exception(error);
   mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/GroundMesh.asset");mat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Ground.mat");
   var manager=Object.FindFirstObjectByType<MassEngineManager>();var deployment=Object.FindFirstObjectByType<WarSandboxRuntimeDeployment>();
   var system=Copy(manager.systemConfig,"System");system.simulationConfig=Copy(system.simulationConfig,"Simulation");system.simulationConfig.simulationWorldSize=new Vector2(256,256);
   system.runtimeFlowConfig=Copy(system.runtimeFlowConfig,"Flow");system.runtimeFlowConfig.flowFieldOrigin=new Vector2(-128,-128);system.runtimeFlowConfig.flowFieldResolution=128;system.runtimeFlowConfig.flowFieldCellSize=2;
   system.runtimeFlowConfig.defenderFlowFieldEnabled=true;system.runtimeFlowConfig.runtimeDynamicDefenderFlowEnabled=true;
   manager.systemConfig=system;manager.terrainSurfaceAsset=a;manager.battleStarted=false;
   var scenario=Copy(manager.scenarioConfig,"Scenario");scenario.unitTypes=new UnitTypeConfig[2];
   var policy=Copy(deployment.rosterPolicy,"RosterPolicy");policy.maximumUnits=512;policy.useTemplateSpawnDefaults=false;policy.explanation="丘陵林地试装：512人以内；坡地真实贴地，岩群与林缘禁行。先验证地图，不代表大规模性能验收。";
   for(int i=0;i<2;i++){
    var original=source.FindTemplate(i==0?"roster-male":"roster-knight").config;
    var unit=Copy(original,"Formation"+i);unit.teamId=i;unit.spawnConfig=Copy(original.spawnConfig,"Spawn"+i);unit.spawnConfig.unitCount=128;unit.spawnConfig.spawnCenter=new Vector3(i==0?-83:83,0,0);unit.spawnConfig.spawnSize=new Vector3(20,0,16);scenario.unitTypes[i]=unit;EditorUtility.SetDirty(unit);EditorUtility.SetDirty(unit.spawnConfig);
    foreach(var e in catalog.templates)if(e.config==original)e.config=unit;
    policy.templates=policy.templates.Select(t=>t==original?unit:t).ToArray();if(!policy.templates.Contains(unit))policy.templates=policy.templates.Concat(new[]{unit}).ToArray();
   }
   manager.scenarioConfig=scenario;deployment.rosterPolicy=policy;deployment.battlefieldCatalog=catalog;
   var plane=GameObject.Find("Plane");if(plane!=null)plane.SetActive(false);
   var ground=new GameObject("Plan B - Shared Render And Navigation Surface");ground.AddComponent<MeshFilter>().sharedMesh=mesh;ground.AddComponent<MeshRenderer>().sharedMaterial=mat;ground.AddComponent<MeshCollider>().sharedMesh=mesh;
   var props=new GameObject("Kenney CC0 - Scenery inside authored exclusion zones").transform;var rng=new System.Random(1204);
   for(int i=0;i<170;i++){
    float x=(float)rng.NextDouble()*244-122,z=(i%2==0?1:-1)*(88+(float)rng.NextDouble()*34);
    surface.TrySample(new Vector2(x,z),out var q);string[] trees={"tree_oak","tree_default","tree_pineRoundA","tree_pineRoundB","tree_small"};
    Prop(trees[i%trees.Length],q.Position,8+(float)rng.NextDouble()*9,(float)rng.NextDouble()*360,props);
   }
   for(int i=0;i<32;i++){
    float cx=i<16?-15:34,cz=i<16?29:-30;float angle=(float)rng.NextDouble()*Mathf.PI*2,radius=(float)rng.NextDouble()*5;
    float x=cx+Mathf.Cos(angle)*radius,z=cz+Mathf.Sin(angle)*radius;surface.TrySample(new Vector2(x,z),out var q);
    Prop(i%3==0?"rock_tallA":i%2==0?"rock_largeA":"rock_largeB",q.Position,3+(float)rng.NextDouble()*5,(float)rng.NextDouble()*360,props);
   }
   for(int i=0;i<130;i++){
    float x=(float)rng.NextDouble()*230-115,z=(i%2==0?1:-1)*(60+(float)rng.NextDouble()*20);surface.TrySample(new Vector2(x,z),out var q);
    Prop(i%4==0?"flower_yellowA":"grass_leafs",q.Position,.5f+(float)rng.NextDouble()*.65f,(float)rng.NextDouble()*360,props);
   }
   var camera=manager.cullingCamera!=null?manager.cullingCamera:Camera.main; if(camera==null)camera=Object.FindFirstObjectByType<Camera>(); if(camera==null)throw new Exception("No scene camera"); manager.cullingCamera=camera;camera.transform.position=new Vector3(125,163,-192);camera.transform.LookAt(new Vector3(0,5,0));camera.fieldOfView=48;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.65f,.78f,.81f);
   var control=Object.FindFirstObjectByType<MyCameraManager>();if(control!=null){control.Target=ground.transform;control.FlyMoveSpeed=35;}
   RenderSettings.ambientLight=new Color(.65f,.68f,.62f);RenderSettings.fog=false;
   manager.systemConfig=system;manager.scenarioConfig=scenario;manager.terrainSurfaceAsset=AssetDatabase.LoadAssetAtPath<TerrainSurfaceAsset>(Root+"/Surface.asset"); deployment.rosterPolicy=policy;deployment.battlefieldCatalog=catalog;
   EditorUtility.SetDirty(manager);EditorUtility.SetDirty(deployment);
   if(!manager.TryGetTerrainContext(out _,out var nav,out error))throw new Exception(error);
   if(nav==null)throw new Exception("Missing nav, asset="+manager.terrainSurfaceAsset);
   if(!nav.AreConnected(new Vector2(-83,0),new Vector2(83,0)))throw new Exception("Spawns disconnected");
   foreach(var u in scenario.unitTypes)if(!nav.IsFootprintWalkable(new Vector2(u.spawnConfig.spawnCenter.x,0),new Vector2(20,16)))throw new Exception("Bad spawn");
   var entry=new WarSandboxBattlefieldEntry{id="forest-hills-b12",displayName="林地丘陵 · 地形试装",scenePath=Battle,terrainId=a.Id,terrainVersion=a.Version,terrainSurface=a,rules=source.entries.First(e=>e.id=="robot-expressive").rules,description="真实起伏地形 · 林缘与岩群禁行 · 双路推进\n256人默认对阵，最多512人。",briefing="方案B首张试装地图：两侧部署，经中央土路或北侧坡路推进。树林边缘与中央岩群不可部署。\n保留已有布阵、选兵与战斗操作；高地加成/森林隐蔽尚未加入。",featuredTemplateIds=new[]{"roster-male","roster-knight"}};
   catalog.entries=new[]{entry};catalog.defaultEntryId=entry.id;
   EditorUtility.SetDirty(catalog);EditorUtility.SetDirty(scenario);EditorUtility.SetDirty(system);EditorUtility.SetDirty(system.simulationConfig);EditorUtility.SetDirty(system.runtimeFlowConfig);EditorUtility.SetDirty(policy);
   EditorSceneManager.SaveScene(scene);
   // Raw scene render is real imported scenery, not a concept image.
   var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var old=RenderTexture.active;
   camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();Directory.CreateDirectory("Logs/TerrainPlanB-20261004");File.WriteAllBytes("Logs/TerrainPlanB-20261004/01-scene.png",tex.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);File.Copy("Logs/TerrainPlanB-20261004/01-scene.png",Root+"/Preview.png");AssetDatabase.ImportAsset(Root+"/Preview.png");entry.preview=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Preview.png");EditorUtility.SetDirty(catalog);
   AssetDatabase.SaveAssets();AssetDatabase.CopyAsset("Assets/Game/Content/Characters/OfficialRoster/Version08/LaunchMenu.unity",Menu);var menu=EditorSceneManager.OpenScene(Menu,OpenSceneMode.Single);Object.FindFirstObjectByType<WarSandboxSceneSession>().catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");EditorSceneManager.SaveScene(menu);AssetDatabase.SaveAssets();
   File.WriteAllText("Logs/TerrainPlanB-20261004/prepare.json","{\"passed\":true,\"width\":256,\"props\":332,\"defaultUnits\":256,\"maxUnits\":512}");Debug.Log("TERRAIN_PLAN_B_PREPARED");
  }
  public static void Polish(){
   ShaderUtil.allowAsyncCompilation=false;
   var scene=EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);
   var asset=AssetDatabase.LoadAssetAtPath<TerrainSurfaceAsset>(Root+"/Surface.asset");asset.TryCreateSurface(out var oldSurface,out _);
   const int n=129;var heights=new float[n*n];var blocked=new bool[128*128];
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){heights[z*n+x]=Hill(-128+x*2,-128+z*2);if(x<128&&z<128)blocked[z*128+x]=Block(-127+x*2,-127+z*2);}
   asset.Initialize("forest-hills-b12",1,n,n,new Vector2(-128,-128),new Vector2(256,256),38,heights,blocked);asset.TryCreateSurface(out var surface,out _);EditorUtility.SetDirty(asset);
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/GroundMesh.asset");var vs=mesh.vertices;var colors=new Color32[vs.Length];
   for(int i=0;i<vs.Length;i++){
    var p=vs[i];surface.TrySample(new Vector2(p.x,p.z),out var q);vs[i]=q.Position;
    float roadDistance=Mathf.Min(Mathf.Abs(p.z-8*Mathf.Sin(p.x/27)),Mathf.Abs(p.z-(45+4*Mathf.Sin(p.x/20)))+2);
    var c=Color.Lerp(new Color(.25f,.39f,.12f),new Color(.43f,.55f,.22f),Mathf.PerlinNoise(p.x*.04f+70,p.z*.04f+50));
    float road=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,8,roadDistance));c=Color.Lerp(c,new Color(.53f,.40f,.24f),road);
    if(Block(p.x,p.z)&&Mathf.Abs(p.z)<65&&Mathf.Abs(p.x)<65)c=new Color(.33f,.37f,.24f);colors[i]=c;
   }
   mesh.vertices=vs;mesh.colors32=colors;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   var ground=GameObject.Find("Plan B - Shared Render And Navigation Surface");ground.GetComponent<MeshCollider>().sharedMesh=null;ground.GetComponent<MeshCollider>().sharedMesh=mesh;
   var mat=ground.GetComponent<MeshRenderer>().sharedMaterial;mat.shader=Shader.Find("MassEngine/Terrain Plan B");EditorUtility.SetDirty(mat);
   var props=GameObject.Find("Kenney CC0 - Scenery inside authored exclusion zones").transform;
   var seen=new HashSet<Material>();foreach(Transform t in props){
    var p=t.position;oldSurface.TrySample(new Vector2(p.x,p.z),out var a);surface.TrySample(new Vector2(p.x,p.z),out var b);t.position+=Vector3.up*(b.Position.y-a.Position.y);
    foreach(var r in t.GetComponentsInChildren<Renderer>())foreach(var m in r.sharedMaterials)if(seen.Add(m)){
     var c=m.color;bool green=c.g>c.r*1.15f;
     m.color=t.name.StartsWith("rock")?(green?new Color(.26f,.36f,.17f):new Color(.34f,.37f,.34f)):(green?new Color(.20f,.40f,.13f):new Color(.36f,.22f,.11f));EditorUtility.SetDirty(m);
    }
   }
   EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   var camera=Object.FindFirstObjectByType<Camera>();camera.tag="MainCamera";EditorSceneManager.SaveScene(scene);var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var old=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes("Logs/TerrainPlanB-20261004/02-scene-polished.png",tex.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=old;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
   File.Copy("Logs/TerrainPlanB-20261004/02-scene-polished.png",Root+"/Preview.png",true);AssetDatabase.ImportAsset(Root+"/Preview.png");
   Debug.Log("TERRAIN_B_POLISH_COMPLETE");
  }

  public static void Build(){
   const string output="Builds/TerrainPlanB-20261004-13";if(Directory.Exists(output))throw new Exception("Refuse overwrite build");Directory.CreateDirectory(output);
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Menu,Battle},locationPathName=output+"/WarSandbox.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
   File.WriteAllText("Logs/TerrainPlanB-20261004/build-13.json",JsonUtility.ToJson(new Receipt{passed=report.summary.result==BuildResult.Succeeded,buildGuid=report.summary.guid.ToString(),errors=(int)report.summary.totalErrors,warnings=(int)report.summary.totalWarnings},true));
   if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Terrain build failed");
   File.WriteAllText(output+"/Start-Terrain.cmd","@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1080\r\n");
   File.Copy("Assets/ThirdParty/KenneyNaturePlanB/License.txt",output+"/Kenney-CC0-License.txt");File.Copy("Assets/ThirdParty/KenneyNaturePlanB/SOURCE.md",output+"/Kenney-Source.md");
  }
  [Serializable]class Receipt{public bool passed;public string buildGuid;public int errors,warnings;}
 }
}
