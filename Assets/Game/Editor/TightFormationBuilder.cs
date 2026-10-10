using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Editor {
 public static class TightFormationBuilder {
  public const string Root="Assets/Game/Content/Battlefields/FormationSetup",Source="Assets/Game/Content/Battlefields/ForestTerrain",Battle=Root+"/ForestHills.unity",Menu=Root+"/LaunchMenu.unity",Logs="Logs/TightFormation-20261004";
  static void Require(bool b,string e){if(!b)throw new Exception(e);}
  static T Copy<T>(T obj,string name)where T:Object{var a=Object.Instantiate(obj);a.name=name;AssetDatabase.CreateAsset(a,Root+"/"+name+".asset");return a;}
  public static void Prepare(){
   Require(!Directory.Exists(Root),"Refuse overwrite 17 assets");Directory.CreateDirectory(Root);Directory.CreateDirectory(Logs);AssetDatabase.Refresh();
   Require(AssetDatabase.CopyAsset(Source+"/ForestHills.unity",Battle),"Scene copy");var scene=EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);var m=Object.FindFirstObjectByType<MassEngineManager>();var d=Object.FindFirstObjectByType<WarSandboxRuntimeDeployment>();
   var scenario=Copy(m.scenarioConfig,"Scenario");var policy=Copy(d.rosterPolicy,"RosterPolicy");var catalog=Copy(d.battlefieldCatalog,"Catalog");var map=new Dictionary<UnitTypeConfig,UnitTypeConfig>();
   foreach(var original in policy.templates){var unit=Copy(original,"Template"+map.Count);unit.spawnConfig=Copy(original.spawnConfig,"Spawn"+map.Count);unit.spawnConfig.formationJitterFraction=.02f;map[original]=unit;EditorUtility.SetDirty(unit);EditorUtility.SetDirty(unit.spawnConfig);}
   scenario.unitTypes=scenario.unitTypes.Select(t=>map[t]).ToArray();policy.templates=policy.templates.Select(t=>map[t]).ToArray();foreach(var t in catalog.templates)if(t.config!=null&&map.TryGetValue(t.config,out var replacement))t.config=replacement;
   policy.maximumUnits=100000;policy.explanation="512紧密阵型试验：默认2048人。可选标准0.4/紧密0.7；合计十万人仅为压力试验，可能严重卡顿，不是流畅性能保证。角色体型和碰撞半径不变。";
   foreach(var e in catalog.entries){e.id="forest-hills-tight17";e.scenePath=Battle;e.displayName="林地丘陵 · 紧密阵型试验";e.description="标准0.4 / 紧密0.7 · 默认2048人\n可载入双方各五万人压力试验，可能严重卡顿。";e.briefing="512×512可玩区不变；仅本战场新增紧密阵型预设和十万人试验草稿。\n选择预设不改人数/位置/角色体型；十万人需确认载入，再确认开战。\n此为高负载试验，不是二十万人目标或流畅性能验收。";}catalog.defaultEntryId="forest-hills-tight17";
   m.scenarioConfig=scenario;d.rosterPolicy=policy;d.battlefieldCatalog=catalog;d.gameObject.AddComponent<TightFormationTrial>();foreach(var a in new Object[]{m,d,scenario,policy,catalog})EditorUtility.SetDirty(a);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
   Require(AssetDatabase.CopyAsset(Source+"/LaunchMenu.unity",Menu),"Menu copy");var menu=EditorSceneManager.OpenScene(Menu,OpenSceneMode.Single);Object.FindFirstObjectByType<WarSandboxSceneSession>().catalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Root+"/Catalog.asset");EditorSceneManager.SaveScene(menu);Audit();
  }
  [Serializable]class AuditReport{public bool passed,normalDensityRejected,tightFootprintsFit,oldJitterDefaultRetained;public int count=100000,overlapPairs,oldJitterOverlapPairs,terrainSamples;public float density=.7f,radius=.55f,depth,front,minimumSeparation,minimumGuaranteedSpacing,jitter=.02f;}
  static int PairCount(AgentData[] agents,float diameter,out float minimum){var cells=new Dictionary<Vector2Int,List<int>>();minimum=float.PositiveInfinity;int pairs=0;float threshold=diameter*diameter;
   for(int i=0;i<agents.Length;i++){var p=agents[i].position;var key=new Vector2Int(Mathf.FloorToInt(p.x/diameter),Mathf.FloorToInt(p.z/diameter));for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++){if(!cells.TryGetValue(key+new Vector2Int(x,z),out var list))continue;foreach(int j in list){var q=agents[j].position;float sq=(p.x-q.x)*(p.x-q.x)+(p.z-q.z)*(p.z-q.z);minimum=Mathf.Min(minimum,Mathf.Sqrt(sq));if(sq<threshold-1e-5f)pairs++;}}if(!cells.TryGetValue(key,out var bucket)){bucket=new List<int>();cells.Add(key,bucket);}bucket.Add(i);}return pairs;
  }
  public static void Audit(){
   EditorSceneManager.OpenScene(Battle,OpenSceneMode.Single);var m=Object.FindFirstObjectByType<MassEngineManager>();Require(m.TryGetTerrainContext(out var surface,out var nav,out var error),error);Require(surface.Size==new Vector2(512,512),"Unexpected map");
   var r=new AuditReport();var positions=new AgentData[100000];var oldPositions=new AgentData[100000];var standard=SpawnConfig.ResolveSpawnSize(50000,.4f,2.2f,Vector3.zero);r.normalDensityRejected=!nav.IsFootprintWalkable(new Vector2(-128,0),new Vector2(standard.x,standard.z));Require(r.normalDensityRejected,"Normal 50k should overflow");
   var old=AssetDatabase.LoadAssetAtPath<ScenarioConfig>(Source+"/Scenario.asset");r.oldJitterDefaultRetained=old.unitTypes.All(u=>Mathf.Abs(u.spawnConfig.formationJitterFraction-.08f)<.000001f);Require(r.oldJitterDefaultRetained,"Old scenes changed jitter default");
   for(int i=0;i<2;i++){var unit=m.scenarioConfig.unitTypes[i];var e=WarSandboxDeploymentEntry.From(unit);e.count=50000;Require(TightFormationTrial.TryPreset(e,true,out e,out error),error);var size=e.Size;r.depth=size.x;r.front=size.z;r.radius=unit.flockingConfig.agentRadius;Require(nav.IsFootprintWalkable(new Vector2(e.center.x,e.center.z),new Vector2(size.x,size.z)),"Tight footprint blocked");
    var spawn=Object.Instantiate(unit.spawnConfig);spawn.unitCount=50000;spawn.spawnSize=Vector3.zero;spawn.formationDensity=.7f;spawn.formationAspect=2.2f;r.minimumGuaranteedSpacing=TightFormationTrial.MinimumGuaranteedSpacing(spawn,50000,spawn.ResolveSpawnSize());new DefaultSpawnModule(spawn).GenerateAgents(positions,i*50000,50000,i);spawn.formationJitterFraction=.08f;new DefaultSpawnModule(spawn).GenerateAgents(oldPositions,i*50000,50000,i);Object.DestroyImmediate(spawn);
   }
   r.tightFootprintsFit=true;r.overlapPairs=PairCount(positions,1.1f,out r.minimumSeparation);r.oldJitterOverlapPairs=PairCount(oldPositions,1.1f,out _);Require(r.overlapPairs==0,"Tight initial positions overlap");
   foreach(var a in positions){Require(surface.TrySample(new Vector2(a.position.x,a.position.z),out var q)&&q.Walkable,"Initial position outside playable terrain");r.terrainSamples++;}
   Require(r.oldJitterOverlapPairs>0,"Expected old jitter risk for dense formation");r.passed=true;File.WriteAllText(Logs+"/capacity-and-overlap.json",JsonUtility.ToJson(r,true));
  }
  [Serializable]class BuildReceipt{public bool passed;public string buildGuid;public int errors,warnings;}
  public static void Build(){const string output="Builds/TightFormation-20261004-17";Require(!Directory.Exists(output),"Refuse overwrite 17 build");Directory.CreateDirectory(output);var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Menu,Battle},locationPathName=output+"/WarSandbox.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});File.WriteAllText(Logs+"/build.json",JsonUtility.ToJson(new BuildReceipt{passed=r.summary.result==BuildResult.Succeeded,buildGuid=r.summary.guid.ToString(),errors=(int)r.summary.totalErrors,warnings=(int)r.summary.totalWarnings},true));Require(r.summary.result==BuildResult.Succeeded,"Build failed");File.WriteAllText(output+"/Start-Terrain.cmd","@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" \"WarSandbox.exe\" -screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1080\r\n");}
 }
}
