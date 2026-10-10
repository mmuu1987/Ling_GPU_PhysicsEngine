#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace MassEngine.Tests
{
 public sealed partial class MassEngineGpuKernelTests
 {
  private void P8CombatSetup(bool wall=false,bool refresh=true)
  {
   P6Setup(wall);p6.Dispose();for(int i=0;i<8;i++){initialTeamIds[i]=i<4?0:1;initialHp[i]=100000;initialAgents[i].position=new Vector3(i<4?-5.5f:5.5f,0,-5.5f+(i%4)*3);}
   initialAgents[0].position=new Vector3(-3.5f,0,-2.5f);initialAgents[4].position=new Vector3(4.5f,0,-2.5f);
   buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
   var surface=fixtureTerrain.Navigation.Surface;p6=new LocalOrderChannel(buffers,8001,initialTeamIds,initialUnitTypeIndices.Select(i=>settingsCache[i].agentRadius).ToArray(),r=>new TerrainNavigationGrid(surface,new Vector2(-8,-8),1,16,16,r,.25f),null,()=>refresh);orchestrator.LocalCommands=p6;DispatchOneFrame(false);
  }
  [UnityTest]public IEnumerator P8AttackRetreatAttackAndUnselectedRemainIndependent()
  {
   P8CombatSetup();var before=P6Agents();var attack=p6.Submit(8001,0,1,new[]{0},LocalOrderKind.Attack,Vector2.zero);yield return P6Wait(attack);Assert.AreEqual(LocalOrderStatus.Committed,attack.Status,attack.Error);Assert.AreEqual((int)TeamStance.Advance,p6.OrderAt(0).stance);
   yield return P6Frames(70);var after=P6Agents();Assert.Greater(after[0].position.x,before[0].position.x+.2f);for(int i=1;i<4;i++)Assert.AreEqual(0,p6.OrderAt(i).slotPlusOne);
   var retreat=p6.Submit(8001,0,2,new[]{0},LocalOrderKind.Retreat,new Vector2(-5.5f,-2.5f));yield return P6Wait(retreat);Assert.AreEqual(LocalOrderStatus.Committed,retreat.Status,retreat.Error);var from=P6Agents()[0].position;yield return P6Frames(65);Assert.Less(P6Agents()[0].position.x,from.x-.2f);
   var targets=new int[8];buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);Assert.AreEqual(-1,targets[0]);
   var again=p6.Submit(8001,0,3,new[]{0},LocalOrderKind.Attack,Vector2.zero);yield return P6Wait(again);Assert.AreEqual(LocalOrderStatus.Committed,again.Status,again.Error);yield return P6Frames(190);
   var hp=new int[8];buffers.combatBuffers.hpReadBuffer.GetData(hp);Assert.Less(hp[4],initialHp[4],"Reattack must engage and damage, not only move");
   var teams=new int[8];buffers.combatBuffers.teamIdBuffer.GetData(teams);CollectionAssert.AreEqual(initialTeamIds,teams);File.WriteAllText(Path.Combine(P6Output(),"p8-attack-retreat.txt"),"Selected attack advances and damages; retreat MoveOnly releases target; reattack damages; unselected overrides zero; teams unchanged");
  }
  [UnityTest]public IEnumerator P8AttackUsesObstacleFlowAndRefreshKeepsSequence()
  {
   P8CombatSetup(true);for(int i=5;i<8;i++)initialHp[i]=0;buffers.combatBuffers.hpReadBuffer.SetData(initialHp);buffers.combatBuffers.hpWriteBuffer.SetData(initialHp);
   var attack=p6.Submit(8001,0,1,new[]{0},LocalOrderKind.Attack,Vector2.zero);yield return P6Wait(attack);Assert.AreEqual(LocalOrderStatus.Committed,attack.Status,attack.Error);
   float maxZ=-100;var nav=new TerrainNavigationGrid(fixtureTerrain.Navigation.Surface,new Vector2(-8,-8),1,16,16,settingsCache[0].agentRadius,.25f);bool reached=false;
   for(int i=0;i<2200;i++){DispatchOneFrame(true);var pos=P6Agents()[0].position;Assert.True(nav.IsWalkable(new Vector2(pos.x,pos.z)));maxZ=Mathf.Max(maxZ,pos.z);if(pos.x>2.5f){reached=true;break;}if(i%16==0)yield return null;}
   Assert.True(reached,"Attack must route around wall");Assert.Greater(maxZ,3.5f);
   yield return new WaitForSecondsRealtime(.3f);int seq=attack.Sequence;double end=Time.realtimeSinceStartupAsDouble+4;while(attack.NavigationRefreshes==0&&Time.realtimeSinceStartupAsDouble<end){DispatchOneFrame(true);yield return null;}Assert.Greater(attack.NavigationRefreshes,0,attack.NavigationError);Assert.AreEqual(seq,p6.OrderAt(0).sequence);
   File.WriteAllText(Path.Combine(P6Output(),"p8-attack-obstacle.txt"),"Attack maxZ="+maxZ+"; independent obstacle flow; refreshes="+attack.NavigationRefreshes+"; sequence retained="+seq);
  }
  [UnityTest]public IEnumerator P8AttackRefreshCannotUndoNewHoldAndNoEnemyRejects()
  {
   P8CombatSetup();var attack=p6.Submit(8001,0,1,new[]{0},LocalOrderKind.Attack,Vector2.zero);yield return P6Wait(attack);Assert.AreEqual(LocalOrderStatus.Committed,attack.Status,attack.Error);
   yield return new WaitForSecondsRealtime(.3f);DispatchOneFrame(true);
   LocalOrderReceipt hold=null;double end=Time.realtimeSinceStartupAsDouble+4;do{hold=p6.Submit(8001,0,2,new[]{0},LocalOrderKind.Hold,Vector2.zero);if(hold.Status!=LocalOrderStatus.Rejected)break;yield return null;DispatchOneFrame(true);}while(Time.realtimeSinceStartupAsDouble<end);
   yield return P6Wait(hold);Assert.AreEqual(LocalOrderStatus.Committed,hold.Status,hold.Error);yield return new WaitForSecondsRealtime(.4f);yield return P6Frames(20);Assert.AreEqual(hold.Sequence,p6.OrderAt(0).sequence);Assert.AreEqual((int)TeamStance.HoldHere,p6.OrderAt(0).stance);var heldPosition=P6Agents()[0].position;yield return P6Frames(40);Assert.Less(Vector3.Distance(heldPosition,P6Agents()[0].position),.5f,"Hold stops active marching/chasing");var heldTargets=new int[8];buffers.combatBuffers.targetAgentIndexBuffer.GetData(heldTargets);Assert.AreEqual(-1,heldTargets[0],"Hold drops distant pursuit target");
   var hp=new int[8];buffers.combatBuffers.hpReadBuffer.GetData(hp);for(int i=4;i<8;i++)hp[i]=0;buffers.combatBuffers.hpReadBuffer.SetData(hp);buffers.combatBuffers.hpWriteBuffer.SetData(hp);
   var bad=p6.Submit(8001,0,3,new[]{0},LocalOrderKind.Attack,Vector2.zero);yield return P6Wait(bad);Assert.AreEqual(LocalOrderStatus.Rejected,bad.Status);Assert.AreEqual(hold.Sequence,p6.OrderAt(0).sequence);File.WriteAllText(Path.Combine(P6Output(),"p8-refresh-safety.txt"),"New Hold survives retired Attack refresh; no live enemies rejects Attack and keeps old Hold");
  }
  [UnityTest]public IEnumerator P8FourKindsCoexistAndRetreatArrivesBeforeOverlap()
  {
   P8CombatSetup(false,false);var a=p6.Submit(8001,0,1,new[]{0},LocalOrderKind.Attack,Vector2.zero);yield return P6Wait(a);Assert.AreEqual(LocalOrderStatus.Committed,a.Status,a.Error);
   var b=p6.Submit(8001,0,2,new[]{1},LocalOrderKind.Move,new Vector2(-6.5f,-2.5f));yield return P6Wait(b);Assert.AreEqual(LocalOrderStatus.Committed,b.Status,b.Error);
   var h=p6.Submit(8001,0,3,new[]{2},LocalOrderKind.Hold,Vector2.zero);yield return P6Wait(h);Assert.AreEqual(LocalOrderStatus.Committed,h.Status,h.Error);
   var r=p6.Submit(8001,0,4,new[]{3},LocalOrderKind.Retreat,new Vector2(-5.5f,-4.5f));yield return P6Wait(r);Assert.AreEqual(LocalOrderStatus.Committed,r.Status,r.Error);Assert.AreEqual(4,p6.ActiveGroups);
   var before=P6Agents();yield return P6Frames(30);var after=P6Agents();Assert.Greater(after[0].position.x,before[0].position.x+.1f);Assert.Less(after[1].position.x,before[1].position.x-.1f);
   yield return P6Frames(620);var position=P6Agents()[3].position;var order=p6.OrderAt(3);float distance=Vector2.Distance(new Vector2(position.x,position.z),r.Target);Assert.LessOrEqual(distance,order.arrivalRegionRadius+order.stopRadius+.6f,"Retreat reaches validated arrival region");
   var overlap=p6.Submit(8001,0,5,new[]{0,1},LocalOrderKind.Retreat,new Vector2(-4.5f,.5f));yield return P6Wait(overlap);Assert.AreEqual(LocalOrderStatus.Committed,overlap.Status,overlap.Error);Assert.AreEqual(overlap.Sequence,p6.OrderAt(0).sequence);Assert.AreEqual(overlap.Sequence,p6.OrderAt(1).sequence);Assert.AreEqual(h.Sequence,p6.OrderAt(2).sequence);Assert.AreEqual(r.Sequence,p6.OrderAt(3).sequence);Assert.AreEqual(3,p6.ActiveGroups);
   File.WriteAllText(Path.Combine(P6Output(),"p8-four-kinds-arrival.txt"),"Four kinds coexist; same-team opposite x motion; retreat distance="+distance+" regionRadius="+order.arrivalRegionRadius+"; overlap transfers only selected IDs, unrelated Hold/Retreat preserved. Refresh intentionally disabled in this controlled coexistence fixture; separate tests cover live refresh.");
  }

 }
}
#endif
