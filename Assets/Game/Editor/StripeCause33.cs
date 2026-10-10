// Editor-only observation. No runtime settings, navigation or shaders are modified.
using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Globalization;using System.Text;using UnityEngine;using UnityEngine.UI;using UnityEditor;using UnityEditor.SceneManagement;using static UnityEngine.Object;
namespace MassEngine.Game.Editor {
 [InitializeOnLoad]public static class StripeCause33Driver {
  const string Key="StripeCause33Active";
  static StripeCause33Driver(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Begin(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Refuse existing Play Mode");if(Directory.Exists(StripeCause33.Output))throw new Exception("Refuse existing evidence");Directory.CreateDirectory(StripeCause33.Output);SessionState.SetBool(Key,true);EditorBuildSettings.scenes=new[]{"LaunchMenu","Green","Autumn","Winter"}.Select(n=>new EditorBuildSettingsScene("Assets/Game/Experiments/DeploymentChecks/"+n+".unity",true)).ToArray();EditorSceneManager.OpenScene("Assets/Game/Experiments/DeploymentChecks/LaunchMenu.unity",OpenSceneMode.Single);EditorApplication.EnterPlaymode();}
  static void Changed(PlayModeStateChange s){if(s!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;SessionState.SetBool(Key,false);new StripeCause33().Start();}
 }
 public sealed class StripeCause33 {
  public static string Output=>Path.GetFullPath("Logs/StripeCause-20261005/observations");
  [Serializable]class Receipt{public bool passed,editorPlayMode=true,osInputTest=false;public string error,stage;public int units,resolution,fields;public float cellSize,realSeconds;public Vector2 origin,world;}
  Receipt r=new Receipt();Stack<IEnumerator> stack;WarSandboxBattleController c;float start;
  static void Check(bool b,string e){if(!b)throw new Exception(e);}
  static Button B(string n)=>FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name==n&&b.gameObject.activeInHierarchy);
  static IEnumerator Wait(float seconds){float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)yield return null;}
  public void Start(){stack=new Stack<IEnumerator>();stack.Push(Run());EditorApplication.update+=Pump;}
  void Pump(){try{Check(EditorApplication.isPlaying,"Play mode stopped");while(stack.Count>0){if(!stack.Peek().MoveNext()){stack.Pop();continue;}if(stack.Peek().Current is IEnumerator nested){stack.Push(nested);continue;}return;}r.passed=true;Finish(0);}catch(Exception e){r.error=e.ToString();Finish(1);}}
  void Finish(int code){EditorApplication.update-=Pump;File.WriteAllText(Path.Combine(Output,"receipt.json"),JsonUtility.ToJson(r,true));Debug.Log("STRIPE33_RESULT "+JsonUtility.ToJson(r));EditorApplication.Exit(code);}
  static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
  void Snapshot(int second){var b=c.manager.Buffers;var agents=new AgentData[b.AgentCount];var teams=new int[b.AgentCount];b.agentBuffer.GetData(agents);b.combatBuffers.teamIdBuffer.GetData(teams);var s=new StringBuilder("id,team,x,y,z,vx,vz,state\n");for(int i=0;i<agents.Length;i++){var a=agents[i];s.Append(i).Append(',').Append(teams[i]).Append(',').Append(F(a.position.x)).Append(',').Append(F(a.position.y)).Append(',').Append(F(a.position.z)).Append(',').Append(F(a.velocity.x)).Append(',').Append(F(a.velocity.z)).Append(',').Append(a.currentState).Append('\n');}File.WriteAllText(Path.Combine(Output,"positions-"+second+".csv"),s.ToString());
   if(second==5){ExportControls();var dirs=new Vector2[b.FlowCellCount];b.flowFieldDirectionsBuffer.GetData(dirs,0,0,dirs.Length);s=new StringBuilder("cell,dx,dz\n");for(int i=0;i<dirs.Length;i++)s.Append(i).Append(',').Append(F(dirs[i].x)).Append(',').Append(F(dirs[i].y)).Append('\n');File.WriteAllText(Path.Combine(Output,"flow-team0-5.csv"),s.ToString());}
   Debug.Log("STRIPE33_SNAPSHOT "+second+" fields="+c.manager.TerrainCompletedFields+" actualReal="+(Time.realtimeSinceStartup-start));
  }

  void ExportControls(){
   var nav=c.manager.TerrainNavigation;var b=c.manager.Buffers;var density=new uint[b.FlowCellCount];b.runtimeFlowTargetDensityBuffer.GetData(density,0,0,density.Length);
   var targets=new List<Vector2>();for(int i=0;i<density.Length;i++)if(density[i]>0&&nav.IsWalkable(nav.CellCenter(i)))targets.Add(nav.CellCenter(i));Check(targets.Count>0,"No actual goals");
   var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
   var costs=(double[])typeof(TerrainNavigationGrid).GetField("edgeCosts",flags).GetValue(nav);var original=(double[])costs.Clone();var mask=(byte[])typeof(TerrainNavigationGrid).GetField("neighbours",flags).GetValue(nav);
   var sb=new StringBuilder("cell,mask,east,north,ne,nw,density\n");for(int i=0;i<density.Length;i++){sb.Append(i).Append(',').Append(mask[i]);for(int j=0;j<4;j++)sb.Append(',').Append(original[i*4+j].ToString("R",CultureInfo.InvariantCulture));sb.Append(',').Append(density[i]).Append('\n');}File.WriteAllText(Path.Combine(Output,"graph.csv"),sb.ToString());
   var front=new List<Vector2>();for(int z=108;z<=147;z++){var v=nav.CellCenter(z*256+183);if(nav.IsWalkable(v))front.Add(v);}
   try {foreach(bool flat in new[]{false,true}){for(int i=0;i<costs.Length;i++)costs[i]=flat?(i%4<2?2:Math.Sqrt(8)):original[i];foreach(bool regular in new[]{false,true}){var dirs=nav.CreateFlowField(regular?front:targets,.6f);sb=new StringBuilder("cell,dx,dz\n");for(int i=0;i<dirs.Length;i++)sb.Append(i).Append(',').Append(F(dirs[i].x)).Append(',').Append(F(dirs[i].y)).Append('\n');File.WriteAllText(Path.Combine(Output,"control-"+(flat?"flat":"terrain")+"-"+(regular?"front":"targets")+".csv"),sb.ToString());}}}
   finally {Array.Copy(original,costs,costs.Length);}
   Debug.Log("CAUSE33_CONTROLS goals="+targets.Count+" regular="+front.Count+" restored="+costs.SequenceEqual(original));
  }
  IEnumerator Run(){r.stage="load";yield return Wait(3);var session=WarSandboxSceneSession.Instance;B("home-play").onClick.Invoke();yield return Wait(.3f);B("card-0").onClick.Invoke();yield return Wait(.3f);B("card-0-enter").onClick.Invoke();float deadline=Time.realtimeSinceStartup+120;while(session.IsLoading&&Time.realtimeSinceStartup<deadline)yield return null;yield return Wait(1);Check(session.State==WarSandboxEntryState.Battle&&!session.HelpOpen,"Load/guide isolation failed");c=session.Controller;var d=c.GetComponent<WarSandboxRuntimeDeployment>();Check(d.IsEditing&&d.Draft.Snapshot().Sum(e=>e.count)==2048,"Not default2048");B("deployment-apply").onClick.Invoke();yield return Wait(.4f);Check(c.Phase==WarSandboxBattlePhase.Setup&&c.manager.TerrainGpuAllocated,"No terrain runtime");var f=c.manager.systemConfig.runtimeFlowConfig;r.units=c.manager.Buffers.AgentCount;r.resolution=f.flowFieldResolution;r.cellSize=f.flowFieldCellSize;r.origin=f.flowFieldOrigin;r.world=d.WorldSize;File.WriteAllText(Path.Combine(Output,"runtime-flow-config.json"),JsonUtility.ToJson(f,true));
   start=Time.realtimeSinceStartup;Snapshot(0);B("start").onClick.Invoke();Check(c.Phase==WarSandboxBattlePhase.Running,"Start failed");start=Time.realtimeSinceStartup;int first=Time.frameCount;
   foreach(int second in new[]{5}){r.stage="observe-"+second;while(Time.realtimeSinceStartup-start<second){if(Time.realtimeSinceStartup-start>10)Check((Time.frameCount-first)/(Time.realtimeSinceStartup-start)>=10,"Under10fps safety stop");yield return null;}Snapshot(second);}
   r.fields=c.manager.TerrainCompletedFields;r.realSeconds=Time.realtimeSinceStartup-start;c.PauseBattle();r.stage="complete";
  }
 }
}
