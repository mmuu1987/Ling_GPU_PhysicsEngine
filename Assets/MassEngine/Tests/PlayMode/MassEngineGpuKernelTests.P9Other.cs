#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MassEngine.Tests
{
 public sealed partial class MassEngineGpuKernelTests
 {
 // Test-only v12 simulation/launch-state comparison; projectile flight is not enabled.
  P9Snapshot P9RunOther(bool prototype,int scenarioId,int kind,int frames)
  {
   const int reject=0;
   string[] variants={"move-melee","hold-melee","attack-ranged","move-ranged","hold-ranged"};
   int stance=kind==0||kind==3?3:(kind==2?1:4);
   if(kind>=2)foreach(var unit in scenario.unitTypes){var c=unit.combatConfig;c.projectileRange=20;c.projectileSpeed=12;c.projectileGravity=scenarioId%2==0?0:-9.81f;c.projectileHitRadius=.1f;c.projectileMaxLifetime=5;}
   bool wall=scenarioId==3||scenarioId==9;float slope=scenarioId==2||scenarioId==8?.5f:0;
   ConfigureTerrain(slope,wall);bool far=wall||scenarioId==1||scenarioId==7||scenarioId==8||scenarioId==10;
   UploadTerrainAgents(i=>new Vector2((initialTeamIds[i]==0?-1:1)*(far?3.5f:.5f),-3.5f+(i%4)*1.5f));
   if(scenarioId==6){initialHp[0]=0;initialHp[4]=0;}
   if(scenarioId==4||scenarioId==7||scenarioId==10){for(int i=0;i<initialAgents.Length;i++){var a=initialAgents[i];a.velocity=new Vector3(initialTeamIds[i]==0?.3f:-.3f,0,.1f);a.currentState=1;initialAgents[i]=a;}}
   buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
   if(scenarioId==4||scenarioId==10){lodNearRadius=.01f;lodMidRadius=.02f;simFarInterval=4;}
   if(reject==4)foreach(var unit in scenario.unitTypes)unit.combatConfig.projectileRange=20;
   var o=new LocalAgentOrder[initialAgents.Length];for(int i=0;i<o.Length;i++)o[i]=new LocalAgentOrder{slotPlusOne=reject==1?0:initialTeamIds[i]+1,epoch=reject==2?2:1,sequence=1,stance=stance,arrival=new Vector2(stance==3?-initialAgents[i].position.x:initialAgents[i].position.x,initialAgents[i].position.z),stopRadius=.25f};
   var f=new Vector2[512];var w=new uint[512];var nav=fixtureTerrain.Navigation;
   for(int team=0;team<2;team++){var goals=initialAgents.Where((a,i)=>initialTeamIds[i]!=team).Select(a=>new Vector2(a.position.x,a.position.z)).ToArray();Array.Copy(nav.CreateFlowField(goals,0),0,f,team*256,256);Array.Copy(nav.CopyWalkable(),0,w,team*256,256);}
   if(scenarioId==9){w[8*16+8]=0;w[256+9*16+9]=0;}
   var assignment=P9Read<int>(buffers.combatBuffers.engagementSlotAssignmentBuffer);
   for(int i=0;i<initialAgents.Length;i++){
    int b=initialAgents.Length+i*12;
    assignment[b+6]=BitConverter.SingleToInt32Bits(-1f);
    if(scenarioId==7){assignment[b+5]=BitConverter.SingleToInt32Bits(1f);assignment[b+4]=BitConverter.SingleToInt32Bits(.6f);}
   }
   buffers.combatBuffers.engagementSlotAssignmentBuffer.SetData(assignment);
   ComputeShader owned=null;
   try{
    if(prototype){var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Tests/PlayMode/P9Other-"+variants[kind]+".compute");Assert.NotNull(asset);owned=UnityEngine.Object.Instantiate(asset);shaderSet=MassGpuShaderSet.Find(shaderSet.SpatialHashShader,shaderSet.RuntimeFlowShader,owned,shaderSet.LodClassificationShader,shaderSet.ProjectileShader);}
    using(var binder=new P9LocalBinder(shaderSet,o,f,w,prototype,reject>0?buffers.combatBuffers.hpWriteBuffer:null)){
     orchestrator=new ComputePipelineOrchestrator(shaderSet,buffers,binder);dispatchedFrames=scenarioId==11?23:(scenarioId==4||scenarioId==10?0:3);
     for(int frame=0;frame<frames;frame++)DispatchOneFrame(scenarioId!=5,scenarioId==11?.05f:FrameDt);
     return new P9Snapshot{agents=P9Read<AgentData>(buffers.agentBuffer),hp=P9Read<int>(buffers.combatBuffers.hpReadBuffer),damage=P9Read<int>(buffers.combatBuffers.pendingDamageReadBuffer),targets=P9Read<int>(buffers.combatBuffers.targetAgentIndexBuffer),launch=P9Read<int>(buffers.combatBuffers.launchRequestBuffer),assignment=P9Read<int>(buffers.combatBuffers.engagementSlotAssignmentBuffer),cooldown=P9Read<float>(buffers.combatBuffers.attackCooldownBuffer)};
    }
   }finally{if(owned!=null)UnityEngine.Object.DestroyImmediate(owned);}
  }
  [TestCase(0,0,1)]
  [TestCase(0,1,1)]
  [TestCase(0,2,1)]
  [TestCase(0,3,1)]
  [TestCase(0,4,1)]
  [TestCase(0,5,1)]
  [TestCase(0,6,1)]
  [TestCase(0,7,1)]
  [TestCase(0,8,1)]
  [TestCase(0,9,1)]
  [TestCase(0,10,1)]
  [TestCase(0,11,1)]
  [TestCase(0,0,32)]
  [TestCase(0,1,32)]
  [TestCase(0,2,32)]
  [TestCase(0,3,32)]
  [TestCase(0,4,32)]
  [TestCase(0,5,32)]
  [TestCase(0,6,32)]
  [TestCase(0,7,32)]
  [TestCase(0,8,32)]
  [TestCase(0,9,32)]
  [TestCase(0,10,32)]
  [TestCase(0,11,32)]
  [TestCase(1,0,1)]
  [TestCase(1,1,1)]
  [TestCase(1,2,1)]
  [TestCase(1,3,1)]
  [TestCase(1,4,1)]
  [TestCase(1,5,1)]
  [TestCase(1,6,1)]
  [TestCase(1,7,1)]
  [TestCase(1,8,1)]
  [TestCase(1,9,1)]
  [TestCase(1,10,1)]
  [TestCase(1,11,1)]
  [TestCase(1,0,32)]
  [TestCase(1,1,32)]
  [TestCase(1,2,32)]
  [TestCase(1,3,32)]
  [TestCase(1,4,32)]
  [TestCase(1,5,32)]
  [TestCase(1,6,32)]
  [TestCase(1,7,32)]
  [TestCase(1,8,32)]
  [TestCase(1,9,32)]
  [TestCase(1,10,32)]
  [TestCase(1,11,32)]
  [TestCase(2,0,1)]
  [TestCase(2,1,1)]
  [TestCase(2,2,1)]
  [TestCase(2,3,1)]
  [TestCase(2,4,1)]
  [TestCase(2,5,1)]
  [TestCase(2,6,1)]
  [TestCase(2,7,1)]
  [TestCase(2,8,1)]
  [TestCase(2,9,1)]
  [TestCase(2,10,1)]
  [TestCase(2,11,1)]
  [TestCase(2,0,32)]
  [TestCase(2,1,32)]
  [TestCase(2,2,32)]
  [TestCase(2,3,32)]
  [TestCase(2,4,32)]
  [TestCase(2,5,32)]
  [TestCase(2,6,32)]
  [TestCase(2,7,32)]
  [TestCase(2,8,32)]
  [TestCase(2,9,32)]
  [TestCase(2,10,32)]
  [TestCase(2,11,32)]
  [TestCase(3,0,1)]
  [TestCase(3,1,1)]
  [TestCase(3,2,1)]
  [TestCase(3,3,1)]
  [TestCase(3,4,1)]
  [TestCase(3,5,1)]
  [TestCase(3,6,1)]
  [TestCase(3,7,1)]
  [TestCase(3,8,1)]
  [TestCase(3,9,1)]
  [TestCase(3,10,1)]
  [TestCase(3,11,1)]
  [TestCase(3,0,32)]
  [TestCase(3,1,32)]
  [TestCase(3,2,32)]
  [TestCase(3,3,32)]
  [TestCase(3,4,32)]
  [TestCase(3,5,32)]
  [TestCase(3,6,32)]
  [TestCase(3,7,32)]
  [TestCase(3,8,32)]
  [TestCase(3,9,32)]
  [TestCase(3,10,32)]
  [TestCase(3,11,32)]
  [TestCase(4,0,1)]
  [TestCase(4,1,1)]
  [TestCase(4,2,1)]
  [TestCase(4,3,1)]
  [TestCase(4,4,1)]
  [TestCase(4,5,1)]
  [TestCase(4,6,1)]
  [TestCase(4,7,1)]
  [TestCase(4,8,1)]
  [TestCase(4,9,1)]
  [TestCase(4,10,1)]
  [TestCase(4,11,1)]
  [TestCase(4,0,32)]
  [TestCase(4,1,32)]
  [TestCase(4,2,32)]
  [TestCase(4,3,32)]
  [TestCase(4,4,32)]
  [TestCase(4,5,32)]
  [TestCase(4,6,32)]
  [TestCase(4,7,32)]
  [TestCase(4,8,32)]
  [TestCase(4,9,32)]
  [TestCase(4,10,32)]
  [TestCase(4,11,32)]
  public void P9OtherCommandsMatchReference(int kind,int scenarioId,int frames)
  {
   var expected=P9RunOther(false,scenarioId,kind,frames);TearDown();SetUp();var actual=P9RunOther(true,scenarioId,kind,frames);
   if(kind>=2){
    int clear=0,blocked=0;for(int i=0;i<expected.agents.Length;i++){float status=BitConverter.Int32BitsToSingle(expected.assignment[expected.agents.Length+i*12+7]);if(status>0)clear++;else if(status<0)blocked++;}
    Debug.Log($"P9_STAGE other-coverage kind={kind} scenario={scenarioId} frames={frames} targets={expected.targets.Count(t=>t>=0)} clear={clear} blocked={blocked} launchPositive={expected.launch.Count(v=>v>0)}");
    if(kind==2&&scenarioId==1&&frames==32){Assert.True(expected.targets.Any(t=>t>=0),"Reference must acquire a target");Assert.Greater(clear+blocked,0,"Reference must exercise ranged clearance");}
   }
   CollectionAssert.AreEqual(expected.hp,actual.hp,"hp");CollectionAssert.AreEqual(expected.damage,actual.damage,"pending damage");CollectionAssert.AreEqual(expected.targets,actual.targets,"targets");CollectionAssert.AreEqual(expected.launch,actual.launch,"launches");
   for(int i=0;i<expected.agents.Length;i++){
    var a=expected.agents[i];var b=actual.agents[i];Assert.LessOrEqual(Vector3.Distance(a.position,b.position),.0001f,"position "+i);Assert.LessOrEqual(Vector3.Distance(a.velocity,b.velocity),.0001f,"velocity "+i);Assert.LessOrEqual(Vector3.Distance(a.rotation,b.rotation),.0001f,"rotation "+i);Assert.AreEqual(a.scale,b.scale);Assert.AreEqual(a.currentState,b.currentState);Assert.AreEqual(a.presentationState,b.presentationState);Assert.AreEqual(a.currentAnimationTime,b.currentAnimationTime,.0001f);Assert.AreEqual(a.locomotionSpeed,b.locomotionSpeed,.0001f);Assert.AreEqual(expected.cooldown[i],actual.cooldown[i],.0001f);Assert.AreEqual(expected.assignment[i],actual.assignment[i]);
    int start=expected.agents.Length+i*12;for(int j=0;j<12;j++){if(j==11)Assert.AreEqual(expected.assignment[start+j],actual.assignment[start+j]);else Assert.AreEqual(BitConverter.Int32BitsToSingle(expected.assignment[start+j]),BitConverter.Int32BitsToSingle(actual.assignment[start+j]),.0001f,"congestion "+i+"/"+j);}
   }
  }
 }
}
#endif
