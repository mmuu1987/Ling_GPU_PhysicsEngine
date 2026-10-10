using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.UI;
#endif
namespace MassEngine.Game {
 public sealed class Scene19PlayerSmoke:MonoBehaviour {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  [Serializable]class Receipt{public bool baseMapBindingsChecked;public int checkedMaterialLods;public bool passed,osInputTest=false,presetsChecked,trialCancelChecked,trialUndoChecked,startConfirmationChecked,normal50kRejected,initialOverlapChecked,performanceStopped;public string buildGuid,error,stage,gpu;public int units,initialOverlapPairs,initialTerrainSamples,moved,samples,frames;public float minimumInitialSeparation,maxGroundError,diagnosticSeconds,diagnosticFps;}
  Receipt r=new Receipt();string output;Vector3 benchmarkPosition=new Vector3(250,280,-384),benchmarkTarget=new Vector3(0,8,0);
  void LateUpdate(){if(r.stage=="movement"&&Camera.main!=null){Camera.main.transform.position=benchmarkPosition;Camera.main.transform.LookAt(benchmarkTarget);}}
  [Serializable]class Binding{public string unit,material,shader,texture;public int lod,width,height;}
  [Serializable]class Bindings{public Binding[] rows;}
  void CheckBindings(MassEngineManager manager){var rows=new List<Binding>();foreach(var u in manager.scenarioConfig.unitTypes){var runtime=ResolvedUnitTypeRuntime.Resolve(u,1.5f);for(int lod=0;lod<3;lod++){var mat=runtime.GetMaterial(lod);Require(mat!=null&&mat.shader!=null&&mat.shader.isSupported,"Unsupported actor material LOD"+lod);var t=mat.GetTexture("_BaseMap");Require(t!=null&&t.width>4&&t.height>4,"Missing actor albedo LOD"+lod+" "+u.unitTypeName);rows.Add(new Binding{unit=u.unitTypeName,material=mat.name,shader=mat.shader.name,texture=t.name,lod=lod,width=t.width,height=t.height});r.checkedMaterialLods++;}}r.baseMapBindingsChecked=true;File.WriteAllText(Path.Combine(output,"actor-bindings.json"),JsonUtility.ToJson(new Bindings{rows=rows.ToArray()},true));}

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();if(!args.Contains("--scene19-smoke"))return;var p=args.FirstOrDefault(x=>x.StartsWith("--terrain-output="));if(p==null){Application.Quit(2);return;}var go=new GameObject("Tight formation opt-in smoke");DontDestroyOnLoad(go);go.AddComponent<Scene19PlayerSmoke>().output=p.Substring("--terrain-output=".Length);}
  static void Require(bool b,string e){if(!b)throw new Exception(e);}
  static Button B(string n)=>FindObjectsByType<Button>(FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy&&x.name==n);
  void Shot(string n)=>ScreenCapture.CaptureScreenshot(Path.Combine(output,n+".png"));
  IEnumerator Start(){Directory.CreateDirectory(output);r.buildGuid=Application.buildGUID;var e=Check();while(true){object next;try{if(!e.MoveNext())break;next=e.Current;}catch(Exception ex){r.error=ex.ToString();Finish(1);yield break;}yield return next;}r.passed=true;r.stage="complete";Finish(0);}
  void Update(){if(r.stage=="movement"){r.frames++;r.diagnosticSeconds+=Time.unscaledDeltaTime;}}
  void Finish(int code){r.diagnosticFps=r.frames/Mathf.Max(.001f,r.diagnosticSeconds);r.gpu=SystemInfo.graphicsDeviceName;File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(r,true));Debug.Log("SCENE19_RESULT "+JsonUtility.ToJson(r));Application.Quit(code);}
  void CheckInitial(AgentData[] a,TerrainSurface surface){
   var cells=new Dictionary<Vector2Int,List<int>>();const float diameter=1.1f;r.minimumInitialSeparation=float.PositiveInfinity;
   for(int i=0;i<a.Length;i++){var p=a[i].position;Require(!float.IsNaN(p.x)&&!float.IsNaN(p.z),"Nonfinite spawn");Require(surface.TrySample(new Vector2(p.x,p.z),out var q)&&q.Walkable,"Spawn outside terrain");r.initialTerrainSamples++;var key=new Vector2Int(Mathf.FloorToInt(p.x/diameter),Mathf.FloorToInt(p.z/diameter));
    for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++){if(!cells.TryGetValue(key+new Vector2Int(x,z),out var list))continue;foreach(int j in list){var v=a[j].position;float sq=(p.x-v.x)*(p.x-v.x)+(p.z-v.z)*(p.z-v.z);r.minimumInitialSeparation=Mathf.Min(r.minimumInitialSeparation,Mathf.Sqrt(sq));if(sq<diameter*diameter-.00001f)r.initialOverlapPairs++;}}
    if(!cells.TryGetValue(key,out var bucket)){bucket=new List<int>();cells.Add(key,bucket);}bucket.Add(i);
   }Require(r.initialOverlapPairs==0,"Actual GPU initial positions overlap");r.initialOverlapChecked=true;
  }
  IEnumerator Check(){
   var args=Environment.GetCommandLineArgs();int requested=int.Parse(args.First(x=>x.StartsWith("--tight-units=")).Split('=')[1]);Require(new[]{2048,50000,100000}.Contains(requested),"Unapproved stage");
   r.stage="menu";yield return new WaitForSecondsRealtime(3);var session=WarSandboxSceneSession.Instance;B("home-play").onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);int idx=Array.FindIndex(session.catalog.entries,e=>e.id=="forest-scenery19");Require(idx>=0,"Missing tight scene");B("card-"+idx).onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);B("card-"+idx+"-enter").onClick.Invoke();float until=Time.realtimeSinceStartup+120;while(session.IsLoading&&Time.realtimeSinceStartup<until)yield return null;Require(session.State==WarSandboxEntryState.Battle,"Load failed");yield return new WaitForSecondsRealtime(2);
   var c=session.Controller;var m=c.manager;var d=c.GetComponent<WarSandboxRuntimeDeployment>();Require(d.IsEditing&&d.GetComponent<TightFormationTrial>()!=null,"No opt-in trial");Require(d.Draft[0].count+d.Draft[1].count==2048&&Mathf.Abs(d.Draft[0].density-.4f)<.001f,"Default changed to high count/density");Require(m.TryGetTerrainContext(out var surface,out _,out var error),error);
   B("army-position-0").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);var center=d.Draft[0].center;var template=d.Draft[0].template;
   B("formation-tight").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(Mathf.Abs(d.Draft[0].density-.7f)<.001f&&d.Draft[0].center==center&&d.Draft[0].template==template&&d.Draft[0].count==1024,"Preset mutated identity/count/location");
   B("roster-undo").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(Mathf.Abs(d.Draft[0].density-.4f)<.001f,"Preset undo failed");B("roster-redo").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(Mathf.Abs(d.Draft[0].density-.7f)<.001f,"Preset redo failed");B("formation-standard").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(Mathf.Abs(d.Draft[0].density-.4f)<.001f,"Standard restore failed");r.presetsChecked=true;Shot("01-presets");yield return new WaitForSecondsRealtime(.3f);
   B("tight-trial-open").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Shot("02-trial-warning");yield return new WaitForSecondsRealtime(.25f);B("tight-dialog-cancel").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(d.Draft[0].count==1024,"Cancel replaced draft");r.trialCancelChecked=true;
   B("tight-trial-open").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);B("tight-dialog-confirm").onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);Require(d.Draft[0].count==50000&&d.Draft[1].count==50000&&d.TryValidate(out error),"Trial footprint invalid: "+error);Require(c.Phase==WarSandboxBattlePhase.Setup&&d.IsEditing,"Trial auto-started");
   B("roster-undo").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(d.Draft[0].count==1024,"Trial undo failed");B("roster-redo").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(d.Draft[0].count==50000&&d.TryValidate(out error),"Trial redo failed");r.trialUndoChecked=true;
   var tight=d.Draft[0];var loose=tight;loose.density=.4f;d.Draft.Set(0,loose);r.normal50kRejected=!d.TryValidate(out error);Require(r.normal50kRejected,"Loose 50k incorrectly accepted");d.Draft.Set(0,tight);yield return new WaitForSecondsRealtime(.3f);Shot("03-100k-deployment");yield return new WaitForSecondsRealtime(.3f);
   // Exercise both cancellation and confirmation paths without allocating high load until this stage requests it.
   B("deployment-start").onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);Require(c.Phase==WarSandboxBattlePhase.Setup&&d.IsEditing,"Start bypassed confirmation");Shot("04-start-warning");yield return new WaitForSecondsRealtime(.3f);B("tight-dialog-cancel").onClick.Invoke();yield return new WaitForSecondsRealtime(.25f);Require(d.IsEditing&&c.Phase==WarSandboxBattlePhase.Setup,"Cancelled start mutated state");r.startConfirmationChecked=true;
   if(requested!=100000){for(int i=0;i<2;i++){var e=d.Draft[i];e.count=requested/2;e.density=requested==2048?.4f:.7f;d.Draft.Set(i,e);}yield return new WaitForSecondsRealtime(.3f);}
   Require(d.TryValidate(out error),error);B("deployment-start").onClick.Invoke();if(requested>50000){yield return new WaitForSecondsRealtime(.25f);B("tight-dialog-confirm").onClick.Invoke();}
   Require(c.Phase==WarSandboxBattlePhase.Running,"Failed start: "+d.Error);CheckBindings(m);var controls=FindFirstObjectByType<MyCameraManager>();if(controls!=null)controls.enabled=false;r.units=m.scenarioConfig.unitTypes.Sum(u=>u.spawnConfig.unitCount);Require(r.units==requested,"Wrong count");
   // Same frame as Apply/Start: no Update or FixedUpdate can have advanced the new agents yet.
   var initial=new AgentData[r.units];var agents=new AgentData[r.units];m.Buffers.agentBuffer.GetData(initial,0,0,r.units);r.stage="initial";CheckInitial(initial,surface);yield return new WaitForSecondsRealtime(2);
   r.stage="movement";for(int step=0;step<30;step++){
    yield return new WaitForSecondsRealtime(.5f);m.Buffers.agentBuffer.GetData(agents,0,0,r.units);
    for(int i=0;i<512;i++){var p=agents[i*agents.Length/512].position;Require(surface.TrySample(new Vector2(p.x,p.z),out var q)&&q.Walkable,"Movement outside terrain");r.maxGroundError=Mathf.Max(r.maxGroundError,Mathf.Abs(p.y-q.Position.y));r.samples++;}
    if(step==4)Shot("05-battle-wide");if(step==18){benchmarkPosition=new Vector3(100,155,-215);benchmarkTarget=new Vector3(0,6,0);}if(step==22)Shot("06-battle-detail");
    if(r.diagnosticSeconds>5&&r.frames/r.diagnosticSeconds<10){r.performanceStopped=true;Shot("07-safety-stop");break;}
   }
   r.stage="finish";for(int i=0;i<agents.Length;i++)if(Vector3.Distance(initial[i].position,agents[i].position)>2)r.moved++;Require(r.moved>r.units/2,"Most units stationary");Require(r.maxGroundError<.12f&&string.IsNullOrEmpty(m.TerrainError),"Terrain grounding failed");
   var scenic=GameObject.Find("18 Lightweight Store Scenery - outside navigation");Require(scenic!=null&&scenic.GetComponentsInChildren<Collider>(true).Length==0,"Missing scenery or unexpected colliders");
   if(c.Phase==WarSandboxBattlePhase.Running)B("start").onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);Require(c.Phase==WarSandboxBattlePhase.Paused,"Pause failed");Shot("08-paused");yield return new WaitForSecondsRealtime(.5f);
   Camera.main.transform.position=new Vector3(-190,48,-316);Camera.main.transform.LookAt(new Vector3(-115,15,-238));yield return new WaitForSecondsRealtime(.6f);Shot("09-grove-detail");yield return new WaitForSecondsRealtime(.7f);
   Camera.main.transform.position=new Vector3(280,70,-125);Camera.main.transform.LookAt(new Vector3(237,14,-10));yield return new WaitForSecondsRealtime(.6f);Shot("10-rocks-and-edge");yield return new WaitForSecondsRealtime(.7f);
   Camera.main.transform.position=new Vector3(-100,50,-90);Camera.main.transform.LookAt(new Vector3(-45,5,0));yield return new WaitForSecondsRealtime(.6f);Shot("11-actor-color-detail");yield return new WaitForSecondsRealtime(.7f);
  }
#endif
 }
}
