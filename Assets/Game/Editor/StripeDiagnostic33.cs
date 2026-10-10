// Editor-only observation. No runtime settings, navigation or shaders are modified.
using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Globalization;using System.Text;using UnityEngine;using UnityEngine.UI;using UnityEditor;using UnityEditor.SceneManagement;using static UnityEngine.Object;
namespace MassEngine.Game.Editor {
 [InitializeOnLoad]public static class StripeDiagnostic33Driver {
  const string Key="StripeDiagnostic33Active";
  static StripeDiagnostic33Driver(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Begin(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Refuse existing Play Mode");if(Directory.Exists(StripeDiagnostic33.Output))throw new Exception("Refuse existing evidence");Directory.CreateDirectory(StripeDiagnostic33.Output);SessionState.SetBool(Key,true);EditorBuildSettings.scenes=new[]{"LaunchMenu","Green","Autumn","Winter"}.Select(n=>new EditorBuildSettingsScene("Assets/Game/Experiments/DeploymentChecks/"+n+".unity",true)).ToArray();EditorSceneManager.OpenScene("Assets/Game/Experiments/DeploymentChecks/LaunchMenu.unity",OpenSceneMode.Single);EditorApplication.EnterPlaymode();}
  static void Changed(PlayModeStateChange s){if(s!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Key,false))return;SessionState.SetBool(Key,false);new StripeDiagnostic33().Start();}
 }
 public sealed class StripeDiagnostic33 {
  public static string Output=>Path.GetFullPath("Logs/StripeDiagnostic-20261005/observations");
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
   if(second==5){var dirs=new Vector2[b.FlowCellCount];b.flowFieldDirectionsBuffer.GetData(dirs,0,0,dirs.Length);s=new StringBuilder("cell,dx,dz\n");for(int i=0;i<dirs.Length;i++)s.Append(i).Append(',').Append(F(dirs[i].x)).Append(',').Append(F(dirs[i].y)).Append('\n');File.WriteAllText(Path.Combine(Output,"flow-team0-5.csv"),s.ToString());}
   Debug.Log("STRIPE33_SNAPSHOT "+second+" fields="+c.manager.TerrainCompletedFields+" actualReal="+(Time.realtimeSinceStartup-start));
  }
  IEnumerator Run(){r.stage="load";yield return Wait(3);var session=WarSandboxSceneSession.Instance;B("home-play").onClick.Invoke();yield return Wait(.3f);B("card-0").onClick.Invoke();yield return Wait(.3f);B("card-0-enter").onClick.Invoke();float deadline=Time.realtimeSinceStartup+120;while(session.IsLoading&&Time.realtimeSinceStartup<deadline)yield return null;yield return Wait(1);Check(session.State==WarSandboxEntryState.Battle&&!session.HelpOpen,"Load/guide isolation failed");c=session.Controller;var d=c.GetComponent<WarSandboxRuntimeDeployment>();Check(d.IsEditing&&d.Draft.Snapshot().Sum(e=>e.count)==2048,"Not default2048");B("deployment-apply").onClick.Invoke();yield return Wait(.4f);Check(c.Phase==WarSandboxBattlePhase.Setup&&c.manager.TerrainGpuAllocated,"No terrain runtime");var f=c.manager.systemConfig.runtimeFlowConfig;r.units=c.manager.Buffers.AgentCount;r.resolution=f.flowFieldResolution;r.cellSize=f.flowFieldCellSize;r.origin=f.flowFieldOrigin;r.world=d.WorldSize;File.WriteAllText(Path.Combine(Output,"runtime-flow-config.json"),JsonUtility.ToJson(f,true));
   start=Time.realtimeSinceStartup;Snapshot(0);B("start").onClick.Invoke();Check(c.Phase==WarSandboxBattlePhase.Running,"Start failed");start=Time.realtimeSinceStartup;int first=Time.frameCount;
   foreach(int second in new[]{5,15,30}){r.stage="observe-"+second;while(Time.realtimeSinceStartup-start<second){if(Time.realtimeSinceStartup-start>10)Check((Time.frameCount-first)/(Time.realtimeSinceStartup-start)>=10,"Under10fps safety stop");yield return null;}Snapshot(second);}
   r.fields=c.manager.TerrainCompletedFields;r.realSeconds=Time.realtimeSinceStartup-start;c.PauseBattle();r.stage="complete";
  }
 }
}
