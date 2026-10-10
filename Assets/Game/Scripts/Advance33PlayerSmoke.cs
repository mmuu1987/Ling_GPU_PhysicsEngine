using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;using System.IO;using System.Linq;using System.Collections;using System.Collections.Generic;using System.Globalization;using System.Text;using UnityEngine.UI;
#endif
namespace MassEngine.Game {
 public sealed class Advance33PlayerSmoke:MonoBehaviour {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
 [Serializable]class Receipt{public bool passed,osInputTest=false,obstacle;public string stage,error,buildGuid;public int units,positionChecks,crossed,alive0;public float cv30,maxSolveMs;}
 Receipt r=new Receipt();string output;WarSandboxBattleController c;int[] teams;AgentData[] agents;
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var a=Environment.GetCommandLineArgs();if(!a.Contains("--advance33-smoke"))return;var g=new GameObject("Advance33 isolated smoke");DontDestroyOnLoad(g);var p=g.AddComponent<Advance33PlayerSmoke>();p.output=a.First(x=>x.StartsWith("--terrain-output=")).Substring(17);p.r.obstacle=a.Contains("--winter");}
 static void Check(bool b,string e){if(!b)throw new Exception(e);}
 static Button B(string n)=>FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name==n&&b.gameObject.activeInHierarchy);
 IEnumerator Start(){Directory.CreateDirectory(output);r.buildGuid=Application.buildGUID;var stack=new Stack<IEnumerator>();stack.Push(Run());while(stack.Count>0){bool moved;object next;try{moved=stack.Peek().MoveNext();next=moved?stack.Peek().Current:null;}catch(Exception ex){r.error=ex.ToString();Finish(1);yield break;}if(!moved){stack.Pop();continue;}if(next is IEnumerator e)stack.Push(e);else yield return next;}r.passed=true;Finish(0);}
 void Finish(int code){File.WriteAllText(Path.Combine(output,"receipt.json"),JsonUtility.ToJson(r,true));Debug.Log("ADVANCE33_RESULT "+JsonUtility.ToJson(r));Application.Quit(code);}
 void Read(){c.manager.Buffers.agentBuffer.GetData(agents);for(int i=0;i<agents.Length;i++)if(agents[i].currentState!=(int)AgentState.Dead){if(!c.manager.TerrainNavigation.IsWalkable(new Vector2(agents[i].position.x,agents[i].position.z))){var nav=c.manager.TerrainNavigation;var p=new Vector2(agents[i].position.x,agents[i].position.z);nav.TryGetCell(p,out int cpu);int gx=Mathf.FloorToInt((p.x-nav.Origin.x)/nav.CellSize),gz=Mathf.FloorToInt((p.y-nav.Origin.y)/nav.CellSize);File.WriteAllText(Path.Combine(output,"blocked-detail.txt"),"id="+i+" pos="+F(p.x)+","+F(p.y)+" cpu="+cpu+" gpuFloat="+(gz*nav.ResolutionX+gx)+" shaderTerrain="+c.manager.shaderConfig.combatSimulationShader.IsKeywordEnabled("MASS_TERRAIN_ENABLED"));throw new Exception("Unit entered blocked navigation cell; see blocked-detail.txt");}r.positionChecks++;}}
 static string F(float x)=>x.ToString("R",CultureInfo.InvariantCulture);
 void Snapshot(int second){Read();var s=new StringBuilder("id,team,x,y,z,vx,vz,state\n");for(int i=0;i<agents.Length;i++){var a=agents[i];s.Append(i).Append(',').Append(teams[i]).Append(',').Append(F(a.position.x)).Append(',').Append(F(a.position.y)).Append(',').Append(F(a.position.z)).Append(',').Append(F(a.velocity.x)).Append(',').Append(F(a.velocity.z)).Append(',').Append(a.currentState).Append('\n');}File.WriteAllText(Path.Combine(output,"positions-"+second+".csv"),s.ToString());}
 IEnumerator Run(){r.stage="load";yield return new WaitForSecondsRealtime(3);var session=WarSandboxSceneSession.Instance;B("home-play").onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);int season=r.obstacle?2:0;B("card-"+season).onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);B("card-"+season+"-enter").onClick.Invoke();float limit=Time.realtimeSinceStartup+120;while(session.IsLoading&&Time.realtimeSinceStartup<limit)yield return null;yield return new WaitForSecondsRealtime(.6f);Check(session.State==WarSandboxEntryState.Battle&&!session.HelpOpen,"Load failed");c=session.Controller;var d=c.GetComponent<WarSandboxRuntimeDeployment>();Check(d.IsEditing&&d.Draft.Snapshot().Sum(e=>e.count)==2048&&c.manager.systemConfig.runtimeFlowConfig.terrainLaneApproach33,"Not default opt-in33");
  if(Environment.GetCommandLineArgs().Contains("--advance33-baseline")){var system=Instantiate(c.manager.systemConfig);system.runtimeFlowConfig=Instantiate(system.runtimeFlowConfig);system.runtimeFlowConfig.terrainLaneApproach33=false;c.manager.systemConfig=system;}
  if(r.obstacle){var rules=d.Draft.Rules;rules.staticObstaclesEnabled=true;rules.staticObstacles=new[]{new StaticObstacleRect(new Vector2(-65,0),new Vector2(8,60))};d.Draft.Replace(d.Draft.Snapshot(),rules,d.Draft.Stats);Check(d.TryValidate(out var problem),problem);yield return new WaitForSecondsRealtime(.3f);}
  B("deployment-start").onClick.Invoke();yield return new WaitForSecondsRealtime(.3f);Check(c.Phase==WarSandboxBattlePhase.Running&&!d.IsEditing&&c.manager.TerrainGpuAllocated,"Could not start");r.units=c.manager.Buffers.AgentCount;agents=new AgentData[r.units];teams=new int[r.units];c.manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);Check(r.units==2048,"Count changed");Snapshot(0);
  if(r.obstacle)Check(!c.IssueMoveOrder(0,new Vector3(-65,0,0),false),"Accepted goal inside obstacle");
  r.stage="advance";float start=Time.realtimeSinceStartup;int first=Time.frameCount,nextShot=10;float nextRead=0;float duration=r.obstacle?40:30;
  while(Time.realtimeSinceStartup-start<duration){float elapsed=Time.realtimeSinceStartup-start;if(elapsed>10)Check((Time.frameCount-first)/elapsed>=10,"Under10fps stop");if(r.obstacle&&elapsed>=nextRead){Read();nextRead=elapsed+.25f;}if(elapsed>=nextShot){Snapshot(nextShot);nextShot+=10;}yield return null;}
  Snapshot((int)duration);Read();r.alive0=0;int[] bins=new int[30];for(int i=0;i<agents.Length;i++)if(teams[i]==0&&agents[i].currentState!=(int)AgentState.Dead){r.alive0++;var p=agents[i].position;if(p.x>-59)r.crossed++;int b=Mathf.FloorToInt((p.z+30)/2);if(b>=0&&b<30)bins[b]++;}
  float mean=(float)bins.Average();r.cv30=Mathf.Sqrt(bins.Sum(n=>(n-mean)*(n-mean))/30)/Mathf.Max(.001f,mean);r.maxSolveMs=c.manager.TerrainMaxSolveMilliseconds;
  if(r.obstacle){Check(r.crossed>20,"Did not route around finite wall");}
  else {Check(r.alive0>=1000&&r.cv30<.45f&&bins.Count(n=>n==0)<=1,"Parallel gaps remain in open advance");}
  ScreenCapture.CaptureScreenshot(Path.Combine(output,"running.png"));yield return new WaitForSecondsRealtime(.3f);
  r.stage="commands";Check(c.IssueMoveOrder(0,new Vector3(-100,0,55),false),"Move command rejected");yield return new WaitForSecondsRealtime(2);Read();Check(c.IssueOrder(ArmyOrder.Attack(0)),"Attack command rejected");yield return new WaitForSecondsRealtime(2);Read();Check(c.Phase==WarSandboxBattlePhase.Running,"Commands lost running state");c.PauseBattle();r.stage="complete";
 }
#endif
 }
}
