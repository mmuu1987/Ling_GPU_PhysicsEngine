#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace MassEngine.Tests
{
 public sealed partial class MassEngineGpuKernelTests
 {
  // TEST-ONLY: fixed 8-agent membership mask. No production manager/channel routing is installed.
  // A second orchestrator is used ONLY as a buffer binder, never for a second DispatchFrame.
  sealed class P9PartitionBinder:IDispatchListener,IDisposable
  {
   readonly MassGpuShaderSet shader,selectedShaders;
   readonly ComputePipelineOrchestrator selectedBindings;
   readonly TerrainNavigationRuntime terrain;
   readonly ComputeBuffer orders,flow,mask;
   readonly bool prototype;
   readonly uint excluded;
   readonly int groups;
   readonly MethodInfo bind;
   public int clearCount,globalCount,selectedCount;
   public P9PartitionBinder(MassGpuShaderSet s,MassGpuBufferManager buffers,TerrainNavigationRuntime t,LocalAgentOrder[] o,Vector2[] f,uint[] w,bool p,uint membership)
   {
    shader=s;terrain=t;prototype=p;excluded=membership;groups=(o.Length+63)/64;
    if(p){
     object boxed=s;
     typeof(MassGpuShaderSet).GetField("SimulateCombatAndAccumulateDamage").SetValue(boxed,s.CombatSimulationShader.FindKernel("P9SimulateSelected"));
     selectedShaders=(MassGpuShaderSet)boxed;
     selectedBindings=new ComputePipelineOrchestrator(selectedShaders,buffers);
     bind=typeof(ComputePipelineOrchestrator).GetMethod("BindCombatBuffers",BindingFlags.NonPublic|BindingFlags.Instance);
     Assert.NotNull(bind);
    }
    orders=new ComputeBuffer(o.Length,32);flow=new ComputeBuffer(f.Length,8);mask=new ComputeBuffer(w.Length,4);
    orders.SetData(o);flow.SetData(f);mask.SetData(w);
   }
   void BindLocal(int k){var s=shader.CombatSimulationShader;s.SetInt("_LocalOrderEpoch",1);s.SetInt("_LocalOrderCellCount",256);s.SetBuffer(k,"_LocalOrders",orders);s.SetBuffer(k,"_LocalOrderFlow",flow);s.SetBuffer(k,"_LocalOrderMask",mask);}
   public void OnDispatch(string label)
   {
    if(label=="ClearPendingDamage")clearCount++;
    if(label!="SimulateCombatAndAccumulateDamage")return;
    globalCount++;var s=shader.CombatSimulationShader;
    if(!prototype){s.EnableKeyword(LocalOrderChannel.Keyword);BindLocal(shader.SimulateCombatAndAccumulateDamage);return;}
    s.DisableKeyword(LocalOrderChannel.Keyword);
    s.SetInt("_P9ExcludedMask",unchecked((int)excluded));
    if(excluded==0)return;
    // Rebind after each swap, sharing all uploaded frame constants on this same shader asset.
    bind.Invoke(selectedBindings,null);terrain.Bind(selectedShaders);BindLocal(selectedShaders.SimulateCombatAndAccumulateDamage);
    s.Dispatch(selectedShaders.SimulateCombatAndAccumulateDamage,groups,1,1);selectedCount++;
    // The regular pipeline now dispatches the disjoint global remainder, then projects/LODs/swaps ONCE.
   }
   public void Dispose(){orders.Release();flow.Release();mask.Release();shader.CombatSimulationShader.DisableKeyword(LocalOrderChannel.Keyword);}
  }
  P9Snapshot P9RunPartition(bool prototype,int scenarioId,int frames)
  {
   const int reject=0; uint selectedMask=scenarioId==12?0u:(scenarioId%3==0?255u:(scenarioId%3==1?85u:1u));
   Assert.AreEqual(8,initialAgents.Length,"Test-only mask deliberately supports this eight-agent fixture, not production population routing.");
   bool wall=scenarioId==3||scenarioId==9;float slope=scenarioId==2||scenarioId==8?.5f:0;
   ConfigureTerrain(slope,wall);bool far=wall||scenarioId==1||scenarioId==7||scenarioId==8||scenarioId==10;
   UploadTerrainAgents(i=>new Vector2((initialTeamIds[i]==0?-1:1)*(far?3.5f:.5f),-3.5f+(i%4)*1.5f));
   if(scenarioId==6){initialHp[0]=0;initialHp[4]=0;}
   if(scenarioId==4||scenarioId==7||scenarioId==10){for(int i=0;i<initialAgents.Length;i++){var a=initialAgents[i];a.velocity=new Vector3(initialTeamIds[i]==0?.3f:-.3f,0,.1f);a.currentState=1;initialAgents[i]=a;}}
   buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
   if(scenarioId==4||scenarioId==10){lodNearRadius=.01f;lodMidRadius=.02f;simFarInterval=4;}
   if(reject==4)foreach(var unit in scenario.unitTypes)unit.combatConfig.projectileRange=20;
   var o=new LocalAgentOrder[initialAgents.Length];for(int i=0;i<o.Length;i++)o[i]=new LocalAgentOrder{slotPlusOne=reject==1?0:initialTeamIds[i]+1,epoch=reject==2?2:1,sequence=1,stance=reject==3?4:1,arrival=new Vector2(initialAgents[i].position.x,initialAgents[i].position.z),stopRadius=.25f};
   for(int i=0;i<o.Length;i++)if((selectedMask & (1u<<i))==0)o[i].slotPlusOne=0;
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
    if(prototype){var asset=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/MassEngine/Tests/PlayMode/P9PartitionedAttackMelee.compute");Assert.NotNull(asset);owned=UnityEngine.Object.Instantiate(asset);shaderSet=MassGpuShaderSet.Find(shaderSet.SpatialHashShader,shaderSet.RuntimeFlowShader,owned,shaderSet.LodClassificationShader,shaderSet.ProjectileShader);}
    using(var binder=new P9PartitionBinder(shaderSet,buffers,fixtureTerrain,o,f,w,prototype,selectedMask)){
     orchestrator=new ComputePipelineOrchestrator(shaderSet,buffers,binder);dispatchedFrames=scenarioId==11?23:(scenarioId==4||scenarioId==10?0:3);
     for(int frame=0;frame<frames;frame++)DispatchOneFrame(scenarioId!=5,scenarioId==11?.05f:FrameDt);
     Assert.AreEqual(frames,binder.clearCount,"Exactly one damage clear per frame");
     Assert.AreEqual(frames,binder.globalCount,"Exactly one global simulation per frame");
     Assert.AreEqual(prototype&&selectedMask!=0?frames:0,binder.selectedCount,"Selected simulation count");
     return new P9Snapshot{agents=P9Read<AgentData>(buffers.agentBuffer),hp=P9Read<int>(buffers.combatBuffers.hpReadBuffer),damage=P9Read<int>(buffers.combatBuffers.pendingDamageReadBuffer),targets=P9Read<int>(buffers.combatBuffers.targetAgentIndexBuffer),launch=P9Read<int>(buffers.combatBuffers.launchRequestBuffer),assignment=P9Read<int>(buffers.combatBuffers.engagementSlotAssignmentBuffer),cooldown=P9Read<float>(buffers.combatBuffers.attackCooldownBuffer)};
    }
   }finally{if(owned!=null)UnityEngine.Object.DestroyImmediate(owned);}
  }
  [TestCase(0,1)]
  [TestCase(1,1)]
  [TestCase(2,1)]
  [TestCase(3,1)]
  [TestCase(4,1)]
  [TestCase(5,1)]
  [TestCase(6,1)]
  [TestCase(7,1)]
  [TestCase(8,1)]
  [TestCase(9,1)]
  [TestCase(10,1)]
  [TestCase(11,1)]
  [TestCase(12,1)]
  [TestCase(0,32)]
  [TestCase(1,32)]
  [TestCase(2,32)]
  [TestCase(3,32)]
  [TestCase(4,32)]
  [TestCase(5,32)]
  [TestCase(6,32)]
  [TestCase(7,32)]
  [TestCase(8,32)]
  [TestCase(9,32)]
  [TestCase(10,32)]
  [TestCase(11,32)]
  [TestCase(12,32)]
  public void P9PartitionedAttackMeleeMatchesReference(int scenarioId,int frames)
  {
   var expected=P9RunPartition(false,scenarioId,frames);TearDown();SetUp();var actual=P9RunPartition(true,scenarioId,frames);
   CollectionAssert.AreEqual(expected.hp,actual.hp,"hp");CollectionAssert.AreEqual(expected.damage,actual.damage,"pending damage");CollectionAssert.AreEqual(expected.targets,actual.targets,"targets");CollectionAssert.AreEqual(expected.launch,actual.launch,"launches");
   for(int i=0;i<expected.agents.Length;i++){
    var a=expected.agents[i];var b=actual.agents[i];Assert.LessOrEqual(Vector3.Distance(a.position,b.position),.0001f,"position "+i);Assert.LessOrEqual(Vector3.Distance(a.velocity,b.velocity),.0001f,"velocity "+i);Assert.LessOrEqual(Vector3.Distance(a.rotation,b.rotation),.0001f,"rotation "+i);Assert.AreEqual(a.scale,b.scale);Assert.AreEqual(a.currentState,b.currentState);Assert.AreEqual(a.presentationState,b.presentationState);Assert.AreEqual(a.currentAnimationTime,b.currentAnimationTime,.0001f);Assert.AreEqual(a.locomotionSpeed,b.locomotionSpeed,.0001f);Assert.AreEqual(expected.cooldown[i],actual.cooldown[i],.0001f);Assert.AreEqual(expected.assignment[i],actual.assignment[i]);
    int start=expected.agents.Length+i*12;for(int j=0;j<12;j++){if(j==11)Assert.AreEqual(expected.assignment[start+j],actual.assignment[start+j]);else Assert.AreEqual(BitConverter.Int32BitsToSingle(expected.assignment[start+j]),BitConverter.Int32BitsToSingle(actual.assignment[start+j]),.0001f,"congestion "+i+"/"+j);}
   }
  }
 }
}
#endif
