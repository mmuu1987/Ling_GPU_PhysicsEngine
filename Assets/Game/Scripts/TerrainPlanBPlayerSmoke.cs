using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine.UI;
#endif
namespace MassEngine.Game {
 public sealed class TerrainPlanBPlayerSmoke:MonoBehaviour {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  [Serializable]class Receipt {public bool passed;public string buildGuid,error,stage;public int units,moved,samples;public float minHeight,maxHeight,maxGroundError;public bool blockedDeploymentRejected,connected;public bool osInputTest=false;}
  Receipt receipt=new Receipt();string output;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();if(!args.Contains("--terrain-plan-b-smoke"))return;var path=args.FirstOrDefault(x=>x.StartsWith("--terrain-output="));if(path==null){Application.Quit(2);return;}var go=new GameObject("Terrain B opt-in check");DontDestroyOnLoad(go);go.AddComponent<TerrainPlanBPlayerSmoke>().output=path.Substring("--terrain-output=".Length);}
  IEnumerator Start(){Directory.CreateDirectory(output);receipt.buildGuid=Application.buildGUID;var check=Check();while(true){object next;try{if(!check.MoveNext())break;next=check.Current;}catch(Exception ex){receipt.error=ex.ToString();Finish(1);yield break;}yield return next;}receipt.passed=true;receipt.stage="complete";Finish(0);}
  void Finish(int code){File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(receipt,true));Debug.Log("TERRAIN_B_RESULT "+JsonUtility.ToJson(receipt));Application.Quit(code);}
  static void Require(bool b,string msg){if(!b)throw new Exception(msg);}
  static Button Button(string name)=>FindObjectsByType<Button>(FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy&&x.name==name);
  void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
  IEnumerator Check(){
   receipt.stage="menu";yield return new WaitForSecondsRealtime(3);var session=WarSandboxSceneSession.Instance;Require(session!=null,"No session");
   Button("home-play").onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);int index=Array.FindIndex(session.catalog.entries,e=>e.id=="forest-hills-b12");Require(index>=0,"No terrain entry");Button("card-"+index).onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);Shot("01-catalog");yield return new WaitForSecondsRealtime(.5f);Button("card-"+index+"-enter").onClick.Invoke();
   receipt.stage="load";float until=Time.realtimeSinceStartup+120;while(session.IsLoading&&Time.realtimeSinceStartup<until)yield return null;Require(session.State==WarSandboxEntryState.Battle,"Load failed: "+session.Error);yield return new WaitForSecondsRealtime(2);
   var c=session.Controller;var m=c.manager;var d=c.GetComponent<WarSandboxRuntimeDeployment>();Require(d.IsEditing,"Not in deployment");var mapImage=FindObjectsByType<RawImage>(FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy&&x.name=="deployment-terrain-map");Require(mapImage.texture is Texture2D,"No terrain deployment image");var mapTexture=(Texture2D)mapImage.texture;var maskColor=mapTexture.GetPixel(56,78);Require(Mathf.Abs(maskColor.r-maskColor.g)<.09f&&maskColor.g<.4f,"Rock exclusion missing or vertically flipped");Require(m.TryGetTerrainContext(out var surface,out var nav,out var error)&&surface!=null&&nav!=null,"No terrain: "+error);Require(d.TryValidate(out error),"Initial draft invalid: "+error);
   receipt.connected=nav.AreConnected(new Vector2(-83,0),new Vector2(83,0));Require(receipt.connected,"Disconnected deployment");var prior=d.Draft[0];var bad=prior;bad.center=new Vector3(-15,0,29);d.Draft.Set(0,bad);receipt.blockedDeploymentRejected=!d.TryValidate(out error);Require(receipt.blockedDeploymentRejected,"Rock deployment accepted");d.Draft.Set(0,prior);yield return new WaitForSecondsRealtime(.5f);Shot("02-deployment");yield return new WaitForSecondsRealtime(.5f);
   Button("deployment-start").onClick.Invoke();yield return new WaitForSecondsRealtime(2);Require(c.Phase==WarSandboxBattlePhase.Running,"Cannot start battle: "+d.Error);Require(m.TerrainGpuAllocated,"Terrain GPU not allocated");receipt.units=m.scenarioConfig.unitTypes.Sum(u=>u.spawnConfig.unitCount);Require(receipt.units==256,"Unexpected unit count");
   var initial=new AgentData[receipt.units];var agents=new AgentData[receipt.units];m.Buffers.agentBuffer.GetData(initial,0,0,receipt.units);receipt.minHeight=float.PositiveInfinity;receipt.maxHeight=float.NegativeInfinity;
   receipt.stage="movement";for(int step=0;step<40;step++){
    yield return new WaitForSecondsRealtime(.5f);m.Buffers.agentBuffer.GetData(agents,0,0,receipt.units);
    foreach(var a in agents){var p=a.position;Require(!float.IsNaN(p.y)&&surface.TrySample(new Vector2(p.x,p.z),out _),"Agent outside terrain");surface.TrySample(new Vector2(p.x,p.z),out var q);Require(q.Walkable,"Agent entered exclusion");receipt.maxGroundError=Mathf.Max(receipt.maxGroundError,Mathf.Abs(p.y-q.Position.y));receipt.minHeight=Mathf.Min(receipt.minHeight,p.y);receipt.maxHeight=Mathf.Max(receipt.maxHeight,p.y);receipt.samples++;}
    if(step==4){Shot("03-battle-wide");}if(step==20){var camera=Camera.main;camera.transform.position=new Vector3(65,90,-118);camera.transform.LookAt(new Vector3(0,6,0));}if(step==24)Shot("04-battle-detail");
   }
   for(int i=0;i<agents.Length;i++)if(Vector3.Distance(initial[i].position,agents[i].position)>2)receipt.moved++;
   Require(receipt.moved>receipt.units/2,"Most agents did not move");Require(receipt.maxGroundError<.12f,"Units not grounded: "+receipt.maxGroundError);Require(receipt.maxHeight-receipt.minHeight>1,"No meaningful terrain height variation");Require(string.IsNullOrEmpty(m.TerrainError),m.TerrainError);
   if(c.Phase==WarSandboxBattlePhase.Running){Button("start").onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);Require(c.Phase==WarSandboxBattlePhase.Paused,"Pause failed");}
  }
#endif
 }
}
