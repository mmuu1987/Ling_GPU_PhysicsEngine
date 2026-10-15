using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine.UI;
#endif
namespace MassEngine.Game {
 public sealed class TerrainScalePlayerSmoke:MonoBehaviour {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
  [Serializable]class Receipt {public bool passed;public string buildGuid,error,stage;public int units,moved,samples;public float minHeight,maxHeight,maxGroundError;public bool blockedDeploymentRejected,connected;public bool osInputTest=false;public bool sceneryOnly,viewLimitsChecked;public int farViews;public bool rejected200k;public string rejection200k,gpu;public int frames;public float diagnosticSeconds,diagnosticFps;}
  Receipt receipt=new Receipt();string output;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var args=Environment.GetCommandLineArgs();if(!args.Contains("--terrain-scale-smoke"))return;var path=args.FirstOrDefault(x=>x.StartsWith("--terrain-output="));if(path==null){Application.Quit(2);return;}var go=new GameObject("Terrain atmosphere opt-in check");DontDestroyOnLoad(go);go.AddComponent<TerrainScalePlayerSmoke>().output=path.Substring("--terrain-output=".Length);}
  IEnumerator Start(){Directory.CreateDirectory(output);receipt.buildGuid=Application.buildGUID;var check=Check();while(true){object next;try{if(!check.MoveNext())break;next=check.Current;}catch(Exception ex){receipt.error=ex.ToString();Finish(1);yield break;}yield return next;}receipt.passed=true;receipt.stage="complete";Finish(0);}
  void Update(){if(receipt.stage=="movement"){receipt.frames++;receipt.diagnosticSeconds+=Time.unscaledDeltaTime;}}
  void Finish(int code){receipt.diagnosticFps=receipt.frames/Mathf.Max(.001f,receipt.diagnosticSeconds);receipt.gpu=SystemInfo.graphicsDeviceName;File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(receipt,true));Debug.Log("TERRAIN_SCALE_RESULT "+JsonUtility.ToJson(receipt));Application.Quit(code);}
  static void Require(bool b,string msg){if(!b)throw new Exception(msg);}
  static Button Button(string name)=>FindObjectsByType<Button>(FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy&&x.name==name);
  void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
  IEnumerator Check(){
   receipt.stage="menu";yield return new WaitForSecondsRealtime(3);var session=WarSandboxSceneSession.Instance;Require(session!=null,"No session");
   Button("home-play").onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);int index=Array.FindIndex(session.catalog.entries,e=>e.id=="forest-hills-scale15");Require(index>=0,"No terrain entry");Button("card-"+index).onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);Shot("01-catalog");yield return new WaitForSecondsRealtime(.5f);Button("card-"+index+"-enter").onClick.Invoke();
   receipt.stage="load";float until=Time.realtimeSinceStartup+120;while(session.IsLoading&&Time.realtimeSinceStartup<until)yield return null;Require(session.State==WarSandboxEntryState.Battle,"Load failed: "+session.Error);yield return new WaitForSecondsRealtime(2);
   var c=session.Controller;var m=c.manager;var d=c.GetComponent<WarSandboxRuntimeDeployment>();Require(d.IsEditing,"Not in deployment");
   int requested=2048;var countArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--terrain-scale-units="));if(countArg!=null)requested=int.Parse(countArg.Split('=')[1]);Require(requested>=2&&requested<=50000&&requested%2==0,"Unsafe smoke count");
   for(int i=0;i<2;i++){var entry=d.Draft[i];entry.count=requested/2;entry.manualSize=Vector3.zero;entry.density=.4f;entry.aspect=2.2f;d.Draft.Set(i,entry);}
var mapImage=FindObjectsByType<RawImage>(FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy&&x.name=="deployment-terrain-map");Require(mapImage.texture is Texture2D,"No terrain deployment image");var mapTexture=(Texture2D)mapImage.texture;Require(m.TryGetTerrainContext(out var surface,out var nav,out var error)&&surface!=null&&nav!=null,"No terrain: "+error);Require(d.TryValidate(out error),"Initial draft invalid: "+error);
   var envelope=Camera.main.GetComponent<TerrainScaleBounds>();Require(envelope!=null,"Missing camera envelope");
   Require(surface.Size==new Vector2(512,512),"Playable terrain must be 512");
   var backdrop=GameObject.Find("Visual Only Backdrop - No navigation or colliders");var treeline=GameObject.Find("Visual Only Distant Treeline");
   Require(backdrop!=null&&treeline!=null,"Missing scenery");Require(backdrop.GetComponentsInChildren<Collider>().Length==0&&treeline.GetComponentsInChildren<Collider>().Length==0,"Scenery must not add physical terrain");
   Require(backdrop.GetComponent<MeshFilter>().sharedMesh.bounds.size.x>1000,"Backdrop too small");
   var fogMaterial=backdrop.GetComponent<Renderer>().sharedMaterial;Require(fogMaterial.GetFloat("_HazeStart")>362.1f,"Haze intrudes into playable area");Require(fogMaterial.GetFloat("_HazeEnd")<1408,"Backdrop outer edge not hidden");receipt.sceneryOnly=true;
   receipt.connected=nav.AreConnected(new Vector2(-128,0),new Vector2(128,0));Require(receipt.connected,"Disconnected deployment");var prior=d.Draft[0];var bad=prior;bad.center=new Vector3(0,0,238);d.Draft.Set(0,bad);receipt.blockedDeploymentRejected=!d.TryValidate(out error);Require(receipt.blockedDeploymentRejected,"Rock deployment accepted");d.Draft.Set(0,prior);
   var second=d.Draft[1];var oversized=prior;oversized.count=100000;d.Draft.Set(0,oversized);oversized=second;oversized.count=100000;d.Draft.Set(1,oversized);receipt.rejected200k=!d.TryValidate(out error);receipt.rejection200k=error;Require(receipt.rejected200k,"200k should not fit normal density");d.Draft.Set(0,prior);d.Draft.Set(1,second);Require(d.TryValidate(out error),error);yield return new WaitForSecondsRealtime(.5f);Shot("02-deployment");yield return new WaitForSecondsRealtime(.5f);
   Button("deployment-start").onClick.Invoke();yield return new WaitForSecondsRealtime(2);Require(c.Phase==WarSandboxBattlePhase.Running,"Cannot start battle: "+d.Error);Require(m.TerrainGpuAllocated,"Terrain GPU not allocated");receipt.units=m.scenarioConfig.unitTypes.Sum(u=>u.spawnConfig.unitCount);Require(receipt.units==requested,"Unexpected unit count");
   var initial=new AgentData[receipt.units];var agents=new AgentData[receipt.units];m.Buffers.agentBuffer.GetData(initial,0,0,receipt.units);receipt.minHeight=float.PositiveInfinity;receipt.maxHeight=float.NegativeInfinity;
   receipt.stage="movement";for(int step=0;step<40;step++){
    yield return new WaitForSecondsRealtime(.5f);m.Buffers.agentBuffer.GetData(agents,0,0,receipt.units);
    for(int sample=0;sample<Mathf.Min(512,agents.Length);sample++){var a=agents[sample*agents.Length/Mathf.Min(512,agents.Length)];var p=a.position;Require(!float.IsNaN(p.y)&&surface.TrySample(new Vector2(p.x,p.z),out _),"Agent outside terrain");surface.TrySample(new Vector2(p.x,p.z),out var q);Require(q.Walkable,"Agent entered exclusion");receipt.maxGroundError=Mathf.Max(receipt.maxGroundError,Mathf.Abs(p.y-q.Position.y));receipt.minHeight=Mathf.Min(receipt.minHeight,p.y);receipt.maxHeight=Mathf.Max(receipt.maxHeight,p.y);receipt.samples++;}
    if(step==4){Shot("03-battle-wide");}if(step==20){var detailCamera=Camera.main;detailCamera.transform.position=new Vector3(100,155,-215);detailCamera.transform.LookAt(new Vector3(0,6,0));}if(step==24)Shot("04-battle-detail");
   }
   for(int i=0;i<agents.Length;i++)if(Vector3.Distance(initial[i].position,agents[i].position)>2)receipt.moved++;
   Require(receipt.moved>receipt.units/2,"Most agents did not move");Require(receipt.maxGroundError<.12f,"Units not grounded: "+receipt.maxGroundError);Require(receipt.maxHeight-receipt.minHeight>1,"No meaningful terrain height variation");Require(string.IsNullOrEmpty(m.TerrainError),m.TerrainError);
   receipt.stage="views";if(c.Phase==WarSandboxBattlePhase.Running){Button("start").onClick.Invoke();yield return new WaitForSecondsRealtime(.5f);Require(c.Phase==WarSandboxBattlePhase.Paused,"Pause failed");}
   var camera=Camera.main;var originalPosition=camera.transform.position;var originalRotation=camera.transform.rotation;
   foreach(var extreme in new[]{new Vector3(1000,900,1000),new Vector3(-1000,50,-1000),new Vector3(0,1000,0),new Vector3(1000,-100,0)}){
    camera.transform.position=extreme;yield return null;yield return null;var p=camera.transform.position;
    Require(Vector3.Distance(p,envelope.center)<=envelope.maximumDistance+.1f,"Camera escaped distance envelope");Require(p.y<=envelope.maximumHeight+.1f,"Camera escaped height envelope");Require(new Vector2(p.x-envelope.center.x,p.z-envelope.center.z).magnitude<=envelope.maximumHorizontalRadius+.1f,"Camera escaped horizontal envelope");
   }
   receipt.viewLimitsChecked=true;
   foreach(var yaw in new[]{0f,90f,180f,270f}){
    envelope.cameraController.ConstrainWorldPosition(envelope.Constrain(envelope.center+Quaternion.Euler(48,yaw,0)*new Vector3(0,0,-720)));camera.transform.LookAt(envelope.center);yield return new WaitForSecondsRealtime(.4f);Shot("05-max-"+yaw);receipt.farViews++;yield return new WaitForSecondsRealtime(.4f);
   }
   envelope.cameraController.ConstrainWorldPosition(envelope.Constrain(new Vector3(0,900,0)));camera.transform.rotation=Quaternion.Euler(90,0,0);yield return new WaitForSecondsRealtime(.4f);Shot("06-top");yield return new WaitForSecondsRealtime(.5f);
   envelope.cameraController.ConstrainWorldPosition(originalPosition);camera.transform.rotation=originalRotation;

  }
#endif
 }
}
