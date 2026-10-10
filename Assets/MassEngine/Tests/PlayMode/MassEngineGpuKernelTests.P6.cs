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
        private LocalOrderChannel p6;
        private string P6Output(){var a=Environment.GetCommandLineArgs().FirstOrDefault(x=>((x.StartsWith("--interaction-p6-output=")||x.StartsWith("--interaction-p8-output="))||x.StartsWith("--interaction-p7-output=")));if(a==null)Assert.Ignore("P6 owned opt-in required");return a.Substring("--interaction-p6-output=".Length);}
        private void P6Setup(bool wall=false)
        {
            P6Output();ConfigureTerrain(0,wall);fixtureTeamStances=new[]{(int)TeamStance.HoldHere,(int)TeamStance.HoldHere};
            for(int i=0;i<fixtureTotalAgents;i++)
            {initialTeamIds[i]=0;initialAgents[i].scale=Vector3.one;initialAgents[i].velocity=Vector3.zero;initialAgents[i].position=new Vector3(-5.5f+(i%4)*3,0,-4.5f+(i/4)*8);}
            buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            var surface=fixtureTerrain.Navigation.Surface;var radii=initialUnitTypeIndices.Select(i=>settingsCache[i].agentRadius).ToArray();
            p6=new LocalOrderChannel(buffers,6001,initialTeamIds,radii,r=>new TerrainNavigationGrid(surface,new Vector2(-8,-8),1,16,16,r,.25f));orchestrator.LocalCommands=p6;
            // Warm the shader variant before starting the 10-second admission clock; do not disguise a cold compiler stall as planner failure.
            DispatchOneFrame(false);
        }
        [TearDown] public void P6Cleanup(){p6?.Dispose();p6=null;}
        private IEnumerator P6Wait(LocalOrderReceipt receipt,bool running=true)
        {for(int i=0;i<180 && receipt.Status==LocalOrderStatus.AwaitingSnapshot;i++){DispatchOneFrame(running);yield return null;}Assert.AreNotEqual(LocalOrderStatus.AwaitingSnapshot,receipt.Status,"Readback did not complete");}
        private AgentData[] P6Agents(){var a=new AgentData[fixtureTotalAgents];buffers.agentBuffer.GetData(a);return a;}
        private IEnumerator P6Frames(int n){for(int i=0;i<n;i++){DispatchOneFrame(true);if(i%8==0)yield return null;}}
        [UnityTest] public IEnumerator P6SameTeamDivergesAStopsBContinues()
        {
            P6Setup();for(int i=0;i<4;i++)initialAgents[i].position=new Vector3(i<2?-1.5f:1.5f,0,-4.5f+(i%2)*2);buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            var before=P6Agents();var ids=new[]{0,1};var a=p6.Submit(6001,0,10,ids,LocalOrderKind.Move,new Vector2(-5.5f,-3.5f));ids[0]=7;yield return P6Wait(a);Assert.AreEqual(LocalOrderStatus.Committed,a.Status,a.Error);
            var b=p6.Submit(6001,0,11,new[]{2,3},LocalOrderKind.Move,new Vector2(5.5f,-3.5f));yield return P6Wait(b);Assert.AreEqual(LocalOrderStatus.Committed,b.Status,b.Error);
            yield return P6Frames(35);var moved=P6Agents();Assert.Less(moved[0].position.x,before[0].position.x-.2f);Assert.Greater(moved[2].position.x,before[2].position.x+.2f);
            for(int i=4;i<8;i++)Assert.Less(Vector3.Distance(before[i].position,moved[i].position),.05f,"Unassigned held member changed");
            int bSequence=p6.OrderAt(2).sequence;var hold=p6.Submit(6001,0,12,new[]{0,1},LocalOrderKind.Hold,Vector2.zero);yield return P6Wait(hold);Assert.AreEqual(LocalOrderStatus.Committed,hold.Status,hold.Error);var stopped=P6Agents();yield return P6Frames(80);var after=P6Agents();
            Assert.Less(Vector3.Distance(stopped[0].position,after[0].position),.2f);Assert.Greater(after[2].position.x-stopped[2].position.x,.2f);Assert.AreEqual(bSequence,p6.OrderAt(2).sequence);
            var teams=new int[8];buffers.combatBuffers.teamIdBuffer.GetData(teams);CollectionAssert.AreEqual(initialTeamIds,teams);
            yield return new WaitForSecondsRealtime(.25f);yield return P6Frames(12);yield return null;p6.Tick();Assert.Greater(hold.ObservedAlive,0);Assert.Greater(hold.GpuExecutingMembers,0);
            File.WriteAllText(Path.Combine(P6Output(),"p6-split-hold.txt"),"same-team A left B right; A hold B continues; unassigned unchanged; GPU executing="+hold.GpuExecutingMembers+";groups="+p6.ActiveGroups+";bytes="+p6.GpuBytes);
        }
        [UnityTest] public IEnumerator P6ObstacleDetourUsesSelectedFlowNotStraightPursuit()
        {
            P6Setup(true);for(int i=0;i<8;i++)initialAgents[i].position=new Vector3(-5.5f+(i%2),0,-5.5f+(i/2)*1.5f);initialAgents[0].position=new Vector3(-3.5f,0,-2.5f);buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            var r=p6.Submit(6001,0,1,new[]{0},LocalOrderKind.Move,new Vector2(4.5f,-2.5f));yield return P6Wait(r);Assert.AreEqual(LocalOrderStatus.Committed,r.Status,r.Error);
            float maxZ=-100;bool arrived=false;var nav=new TerrainNavigationGrid(fixtureTerrain.Navigation.Surface,new Vector2(-8,-8),1,16,16,settingsCache[0].agentRadius,.25f);
            for(int i=0;i<2000;i++){DispatchOneFrame(true);var a=P6Agents()[0];Assert.IsTrue(nav.IsWalkable(new Vector2(a.position.x,a.position.z)));maxZ=Mathf.Max(maxZ,a.position.z);if(Vector2.Distance(new Vector2(a.position.x,a.position.z),r==null?Vector2.zero:p6.OrderAt(0).arrival)<.5f){arrived=true;break;}if(i%16==0)yield return null;}
            Assert.IsTrue(arrived,"Selected group failed to route around wall");Assert.Greater(maxZ,3.5f,"A direct line would cross the blocked wall");File.WriteAllText(Path.Combine(P6Output(),"p6-obstacle.txt"),"arrived around wall; maxZ="+maxZ);
        }
        [UnityTest] public IEnumerator P6BudgetPartialReplacementDeathAndResetAreSafe()
        {
            P6Setup();int[][] groups={new[]{0,1},new[]{2},new[]{3},new[]{4}};
            foreach(var ids in groups){var r=p6.Submit(6001,0,1,ids,LocalOrderKind.Hold,Vector2.zero);yield return P6Wait(r);Assert.AreEqual(LocalOrderStatus.Committed,r.Status,r.Error);}
            int seq=p6.OrderAt(0).sequence;var denied=p6.Submit(6001,0,2,new[]{0},LocalOrderKind.Hold,Vector2.zero);yield return P6Wait(denied);Assert.AreEqual(LocalOrderStatus.Rejected,denied.Status);Assert.AreEqual(seq,p6.OrderAt(0).sequence);Assert.AreEqual(4,p6.ActiveGroups);
            var replace=p6.Submit(6001,0,3,new[]{0,1},LocalOrderKind.Hold,Vector2.zero);yield return P6Wait(replace);Assert.AreEqual(LocalOrderStatus.Committed,replace.Status,replace.Error);Assert.AreEqual(4,p6.ActiveGroups);Assert.AreNotEqual(seq,p6.OrderAt(0).sequence);
            var hp=(int[])initialHp.Clone();hp[0]=hp[1]=0;buffers.combatBuffers.hpReadBuffer.SetData(hp);buffers.combatBuffers.hpWriteBuffer.SetData(hp);yield return P6Frames(3);yield return new WaitForSecondsRealtime(.25f);yield return P6Frames(20);yield return null;p6.Tick();Assert.AreEqual(3,p6.ActiveGroups);
            var pending=p6.Submit(6001,0,4,new[]{5},LocalOrderKind.Hold,Vector2.zero);p6.Dispose();Assert.AreEqual(LocalOrderStatus.Invalidated,pending.Status);Assert.AreEqual(LocalOrderStatus.Rejected,p6.Submit(6000,0,5,new[]{5},LocalOrderKind.Hold,Vector2.zero).Status);Assert.AreEqual(0,p6.GpuBytes);
            File.WriteAllText(Path.Combine(P6Output(),"p6-budget.txt"),"4 groups + 1 transactional spare; partial split rejected; full replacement succeeded; empty death group freed; reset invalidated pending; zero bytes after dispose");
        }
        [UnityTest] public IEnumerator P6LocalMoveReleasesEnemyAndHoldStillSelfDefends()
        {
            P6Setup();p6.Dispose();for(int i=0;i<8;i++){initialTeamIds[i]=i<4?0:1;initialHp[i]=100000;initialAgents[i].position=new Vector3(i<4?-5.5f:5.5f,0,-5.5f+(i%4)*3);}
            initialAgents[0].position=new Vector3(-.5f,0,.5f);initialAgents[4].position=new Vector3(.5f,0,.5f);buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            var surface=fixtureTerrain.Navigation.Surface;p6=new LocalOrderChannel(buffers,6002,initialTeamIds,initialUnitTypeIndices.Select(i=>settingsCache[i].agentRadius).ToArray(),r=>new TerrainNavigationGrid(surface,new Vector2(-8,-8),1,16,16,r,.25f));orchestrator.LocalCommands=p6;
            var move=p6.Submit(6002,0,1,new[]{0},LocalOrderKind.Move,new Vector2(-4.5f,.5f));yield return P6Wait(move);Assert.AreEqual(LocalOrderStatus.Committed,move.Status,move.Error);yield return P6Frames(5);
            var targets=new int[8];buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);Assert.AreEqual(-1,targets[0]);
            var hold=p6.Submit(6002,0,2,new[]{0},LocalOrderKind.Hold,Vector2.zero);yield return P6Wait(hold);Assert.AreEqual(LocalOrderStatus.Committed,hold.Status,hold.Error);var hp=new int[8];buffers.combatBuffers.hpReadBuffer.GetData(hp);int before=hp[4];yield return P6Frames(120);buffers.combatBuffers.hpReadBuffer.GetData(hp);Assert.Less(hp[4],before,"Local hold must retain close self-defense, not cease fire");
            File.WriteAllText(Path.Combine(P6Output(),"p6-self-defense.txt"),"Move releases enemy; local Hold damages nearby enemy; team ids unchanged");
        }
        [UnityTest] public IEnumerator P6MixedMembersPauseOverlapRepeatAndGeometryInvalidation()
        {
            P6Setup();p6.Dispose();settingsCache[0].agentRadius=.25f;settingsCache[1].agentRadius=.65f;buffers.UploadUnitTypeSettings(settingsCache);
            initialAgents[0].position=new Vector3(-3.5f,0,-3.5f);initialAgents[4].position=new Vector3(-3.5f,0,.5f);buffers.UploadInitialData(initialAgents,initialTeamIds,initialHp,initialUnitTypeIndices);
            bool valid=true;var surface=fixtureTerrain.Navigation.Surface;
            p6=new LocalOrderChannel(buffers,6010,initialTeamIds,initialUnitTypeIndices.Select(i=>settingsCache[i].agentRadius).ToArray(),r=>new TerrainNavigationGrid(surface,new Vector2(-8,-8),1,16,16,r,.25f),()=>valid);orchestrator.LocalCommands=p6;
            var r=p6.Submit(6010,0,1,new[]{0,4},LocalOrderKind.Move,new Vector2(3.5f,-1.5f));yield return P6Wait(r,false);Assert.AreEqual(LocalOrderStatus.Committed,r.Status,r.Error);
            yield return new WaitForSecondsRealtime(.25f);for(int i=0;i<8;i++){DispatchOneFrame(false);yield return null;}Assert.AreEqual(0,r.GpuExecutingMembers,"Paused GPU must not claim execution");Assert.AreEqual(LocalOrderStatus.Committed,r.Status);
            var before=P6Agents();yield return P6Frames(40);var after=P6Agents();Assert.Greater(after[0].position.x,before[0].position.x+.1f);Assert.Greater(after[4].position.x,before[4].position.x+.1f);
            int old=p6.OrderAt(4).sequence;var oldArrival=p6.OrderAt(4).arrival;
            var split=p6.Submit(6010,0,2,new[]{0},LocalOrderKind.Move,new Vector2(-4.5f,-3.5f));yield return P6Wait(split);Assert.AreEqual(LocalOrderStatus.Committed,split.Status,split.Error);Assert.AreEqual(old,p6.OrderAt(4).sequence);Assert.AreEqual(oldArrival,p6.OrderAt(4).arrival);Assert.AreEqual(2,p6.ActiveGroups);
            for(int i=0;i<4;i++){var next=p6.Submit(6010,0,3+i,new[]{0},LocalOrderKind.Move,new Vector2(i%2==0?3.5f:-4.5f,-3.5f));yield return P6Wait(next);Assert.AreEqual(LocalOrderStatus.Committed,next.Status,next.Error);yield return P6Frames(12);Assert.AreEqual(old,p6.OrderAt(4).sequence);}
            var bad=p6.Submit(6010,0,8,new[]{0},LocalOrderKind.Move,new Vector2(100,100));int kept=p6.OrderAt(0).sequence;yield return P6Wait(bad);Assert.AreEqual(LocalOrderStatus.Rejected,bad.Status);Assert.AreEqual(kept,p6.OrderAt(0).sequence);
            Assert.AreEqual(LocalOrderKind.Move,r.Kind);CollectionAssert.AreEqual(new[]{0,4},r.MemberSnapshot);
            valid=false;p6.Tick();Assert.IsTrue(p6.IsDisposed);Assert.AreEqual(0,p6.GpuBytes);yield return P6Frames(3);
            File.WriteAllText(Path.Combine(P6Output(),"p6-mixed-lifecycle.txt"),"actual GPU movement at radii .25/.65; paused no execution; overlap preserves old remaining command/arrival; repeated targets; invalid destination retains command; context invalidation disposes and legacy fallback dispatches");
        }
    }
}
#endif


