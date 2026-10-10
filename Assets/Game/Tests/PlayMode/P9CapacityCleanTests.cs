#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
namespace MassEngine.Game.Tests {
 public sealed class P9CapacityCleanTests {
  const BindingFlags P=BindingFlags.NonPublic|BindingFlags.Instance;
  P9CapacityTests owner;MassEngineManager m;string output;
  [UnitySetUp]public IEnumerator Setup(){owner=new P9CapacityTests();yield return owner.Setup();m=(MassEngineManager)typeof(P9CapacityTests).GetField("m",P).GetValue(owner);output=(string)typeof(P9CapacityTests).GetField("output",P).GetValue(owner);foreach(var v in Resources.FindObjectsOfTypeAll<SceneView>())v.Close();yield return null;}
  [UnityTearDown]public IEnumerator Cleanup(){if(owner!=null)yield return owner.Cleanup();}
  [Serializable]class CameraInfo{public int id;public string name,type,scene;public bool primary;}
  [Serializable]class Audit{public string scope="Only GameView. Reuses frozen original four-state measurement; immutable per-frame camera audit; no shared mutable camera window objects. Positive GPU counters are diagnostic only unless validated against frame timings.";public string frameTimingFeature,frameTimingPlayerSetting;public List<CameraInfo> cameras=new List<CameraInfo>();public List<string> counterNames=new List<string>(),counterUnits=new List<string>();public int totalCallbacks,unexpectedCallbacks;}
  struct Row{public int frame,cameraId;public long[] values;}
  IEnumerator Measure(int count){var a=new Audit();var method=typeof(FrameTimingManager).GetMethod("IsFeatureEnabled",BindingFlags.Public|BindingFlags.Static);a.frameTimingFeature=method==null?"API unavailable":Convert.ToString(method.Invoke(null,null));var setting=typeof(PlayerSettings).GetProperty("enableFrameTimingStats",BindingFlags.Public|BindingFlags.Static);a.frameTimingPlayerSetting=setting==null?"API unavailable":Convert.ToString(setting.GetValue(null));var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);var recorders=new List<ProfilerRecorder>();foreach(var h in handles){var d=ProfilerRecorderHandle.GetDescription(h);if(d.Name.IndexOf("GPU",StringComparison.OrdinalIgnoreCase)<0||d.Name.IndexOf("Time",StringComparison.OrdinalIgnoreCase)<0)continue;if(a.counterNames.Contains(d.Name))continue;a.counterNames.Add(d.Name);a.counterUnits.Add(d.UnitType.ToString());recorders.Add(ProfilerRecorder.StartNew(d.Category,d.Name,1));}
   var rows=new List<Row>(16384);var cameras=new HashSet<int>();var last=new Dictionary<int,int>();Action<Camera> observe=cam=>{int id=cam.GetInstanceID();if(last.TryGetValue(id,out int f)&&f==Time.frameCount)return;last[id]=Time.frameCount;if(cameras.Add(id))a.cameras.Add(new CameraInfo{id=id,name=cam.name,type=cam.cameraType.ToString(),scene=cam.gameObject.scene.path,primary=cam==m.cullingCamera});a.totalCallbacks++;if(cam!=m.cullingCamera)a.unexpectedCallbacks++;var values=new long[recorders.Count];for(int i=0;i<values.Length;i++)values[i]=recorders[i].Valid?recorders[i].LastValue:-1;rows.Add(new Row{frame=Time.frameCount,cameraId=id,values=values});};Camera.CameraCallback post=cam=>observe(cam);Action<ScriptableRenderContext,Camera> srp=(ctx,cam)=>observe(cam);Camera.onPostRender+=post;RenderPipelineManager.endCameraRendering+=srp;
   try{yield return (IEnumerator)typeof(P9CapacityTests).GetMethod("Measure",P).Invoke(owner,new object[]{count});}
   finally{Camera.onPostRender-=post;RenderPipelineManager.endCameraRendering-=srp;File.WriteAllText(Path.Combine(output,"clean-audit.json"),JsonUtility.ToJson(a,true));File.WriteAllLines(Path.Combine(output,"camera-gpu-audit.csv"),new[]{"frame,camera_id,"+string.Join(",",a.counterNames.Select(x=>"\""+x.Replace("\"","\"\"")+"\""))}.Concat(rows.Select(x=>x.frame+","+x.cameraId+","+string.Join(",",x.values))));foreach(var r in recorders)r.Dispose();}
   Assert.AreEqual(0,a.unexpectedCallbacks,"Unexpected camera rendered; invalidate clean matrix");Assert.AreEqual(0,Resources.FindObjectsOfTypeAll<SceneView>().Length);Assert.AreEqual(1,a.cameras.Count);Assert.True(a.cameras[0].primary);Assert.AreEqual(0,Time.captureDeltaTime);Debug.Log("P9_STAGE clean-audit "+JsonUtility.ToJson(a));
  }

  [Serializable]class Slot{public int lod,vertices;public long indices;public string mesh,meshAsset,material,materialAsset,shader,shadow;}
  [Serializable]class Role{public string config,configAsset,renderAsset,profileAsset;public int count,team;public List<Slot> slots=new List<Slot>();public List<string> profileMetadata=new List<string>();}
  [Serializable]class ScenarioAudit{public string label,asset;public List<Role> roles=new List<Role>();}
  [Serializable]class AssetAudit{public string scope="Read-only current runtime configs and current on-disk historical launch-standard configs. Not re-extraction of old player binary. Collected after measured windows, no asset writes.";public List<ScenarioAudit> scenarios=new List<ScenarioAudit>();}
  void AssetComparison(){var result=new AssetAudit();var historical=AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Content/Battlefields/LaunchPresets/launch-standard/Scenario.asset");foreach(var pair in new[]{(label:"current-Green",config:m.scenarioConfig),(label:"historical-launch-standard-current-source",config:historical)}){Assert.NotNull(pair.config);var sa=new ScenarioAudit{label=pair.label,asset=AssetDatabase.GetAssetPath(pair.config)};foreach(var u in pair.config.unitTypes){var r=u.renderConfig;var profile=r.vatProfile;var role=new Role{config=u.name,configAsset=AssetDatabase.GetAssetPath(u),renderAsset=AssetDatabase.GetAssetPath(r),profileAsset=AssetDatabase.GetAssetPath(profile),count=u.spawnConfig.unitCount,team=u.teamId};var runtime=ResolvedUnitTypeRuntime.Resolve(u,1.5f);for(int l=0;l<3;l++){var mesh=runtime.GetMesh(l);var mat=runtime.GetMaterial(l);role.slots.Add(new Slot{lod=l,vertices=mesh==null?0:mesh.vertexCount,indices=mesh==null?0:(long)mesh.GetIndexCount(0),mesh=mesh==null?"":mesh.name,meshAsset=AssetDatabase.GetAssetPath(mesh),material=mat==null?"":mat.name,materialAsset=AssetDatabase.GetAssetPath(mat),shader=mat==null?"":mat.shader.name,shadow=runtime.GetShadowCasting(l).ToString()});}if(profile!=null)foreach(var f in profile.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance)){var value=f.GetValue(profile);if(value is int||value is float||value is bool)role.profileMetadata.Add(f.Name+"="+Convert.ToString(value,CultureInfo.InvariantCulture));else if(value is Texture tex&&tex!=null)role.profileMetadata.Add(f.Name+"="+tex.width+"x"+tex.height+";"+tex.graphicsFormat+";"+AssetDatabase.GetAssetPath(tex));}sa.roles.Add(role);}result.scenarios.Add(sa);}File.WriteAllText(Path.Combine(output,"asset-comparison.json"),JsonUtility.ToJson(result,true));}

  [UnityTest,Timeout(120000)]public IEnumerator CapacityZAssetsOnly(){AssetComparison();yield return null;}
  [UnityTest,Timeout(900000)]public IEnumerator CapacityClean2048(){yield return Measure(2048);}
  [UnityTest,Timeout(900000)]public IEnumerator CapacityClean50000(){yield return Measure(50000);}
 }
}
#endif
