#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MassEngine.Projectiles;
using UnityEngine;

namespace MassEngine.Game
{
    public sealed partial class WarSandboxTerrainCycle
    {
        [Serializable] private sealed class RangedEvidence
        {
            public string method = "Real runtime deployment copies using existing ranged templates, natural GPU combat, sampled real pool/source IDs/draw args/HP. No injected agents, requests, damage, results or visual volleys. 30 FPS functional probe, not a 100k benchmark or human acceptance.";
            public bool completed;
            public List<string> checks = new List<string>();
            public List<RangedSceneEvidence> scenes = new List<RangedSceneEvidence>();
        }
        [Serializable] private sealed class RangedSceneEvidence
        {
            public string id, capture;
            public int population, poolCapacity, generated, uniqueShooters, rearPopulation, rearShooters, maxInFlight, maxDraw;
            public int damage, deferrals, reclaims, directRejections, samples;
            public long requested;
            public float maxRelativeHeight, pauseDisplacement, seconds;
            public bool completed;
        }
        private void RangedCheck(bool ok,string message)
        { Require(ok,"Ranged: "+message); report.ranged.checks.Add(message); WriteReport(); }
        private uint RangedDrawCount()
        { var args=new uint[5];manager.Buffers.projectileDrawArgsBuffer.GetData(args);return args[1]; }
        private ProjectileGpuData[] RangedPool()
        { var shots=new ProjectileGpuData[manager.Buffers.MaxProjectiles];manager.Buffers.projectileBuffer.GetData(shots);return shots; }

        private IEnumerator RangedScene(string id,bool terrain)
        {
            Stage("ranged-deploy-"+id);
            Require(deployment.TryBeginEdit(false,out string error),error);
            var original=deployment.Draft.Snapshot();
            var ranged=deployment.Templates.Where(u=>u.combatConfig!=null && u.combatConfig.projectileRange>0).ToArray();
            Require(ranged.Length>0,"Preset has no legal ranged template.");
            Vector3 center=terrain ? original[0].center : Vector3.zero;
            int count=terrain ? 24 : 48;
            var formations=new WarSandboxDeploymentEntry[2];
            for (int team=0;team<2;team++)
            {
                var template=ranged.FirstOrDefault(u=>u.teamId==team) ?? ranged[0];
                formations[team]=WarSandboxDeploymentEntry.From(template);
                formations[team].teamId=team;formations[team].count=count;
                formations[team].center=center+new Vector3(team==0 ? -6 : 6,0,0);
                formations[team].density=.5f;formations[team].aspect=4f;formations[team].manualSize=Vector3.zero;
            }
            var candidate=new WarSandboxDeploymentDraft(formations,deployment.Draft.Rules);
            Require(deployment.TryReplaceDraft(candidate,out error),error);
            var previous=manager.Buffers;
            Require(deployment.TryApply(out error),error);
            yield return null;
            RangedCheck(!previous.IsAllocated && manager.scenarioConfig!=sourceScenario,id+": validated runtime deployment replaces and releases old GPU buffers");
            VerifyFullStrength(); VerifySources();
            int n=manager.UnitTypes.TotalAgentCount;
            var evidence=new RangedSceneEvidence { id=id,population=n,poolCapacity=manager.Buffers.MaxProjectiles };
            report.ranged.scenes.Add(evidence);
            RangedCheck(n==count*2,id+": small ranged-only population; authored preset unchanged");
            var engineProjectiles=(ProjectileGpuManager)typeof(MassEngineManager).GetField("projectileManager",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
            Require(engineProjectiles!=null,"Projectile manager absent.");
            var start=new AgentData[n];var teams=new int[n];var hp=new int[n];
            manager.Buffers.agentBuffer.GetData(start);manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
            manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);int initialHp=hp.Sum();
            var rear=new bool[n];var seen=new bool[n];
            for (int i=0;i<n;i++) { rear[i]=teams[i]==0 ? start[i].position.x < center.x-6 : start[i].position.x > center.x+6; if(rear[i])evidence.rearPopulation++; }
            if (terrain && manager.TerrainSurface.TrySample(new Vector2(center.x,center.z),out var ground))center.y=ground.Position.y;
            renderCamera.transform.position=center+new Vector3(0,20,-27);
            renderCamera.transform.LookAt(center+Vector3.up*2);
            if(manager.lodCenter!=null && manager.lodCenter!=renderCamera.transform)manager.lodCenter.position=renderCamera.transform.position;
            controller.StartOrResumeBattle();
            Stage("ranged-observe-"+id);float started=Time.realtimeSinceStartup;
            bool captured=false;
            while(Time.realtimeSinceStartup-started<5 && manager.IsBattleRunning)
            {
                yield return new WaitForSecondsRealtime(.1f);
                var pool=RangedPool();int active=0;
                foreach(var shot in pool)
                {
                    if(shot.targetAgentIndex<0)continue;active++;
                    int source=shot.sourceAgentIndexPlusOne-1;if(source>=0&&source<n)seen[source]=true;
                    float y=0;
                    if(terrain && manager.TerrainSurface.TrySample(new Vector2(shot.position.x,shot.position.z),out var sample))y=sample.Position.y;
                    evidence.maxRelativeHeight=Mathf.Max(evidence.maxRelativeHeight,shot.position.y-y);
                }
                evidence.samples++;evidence.maxInFlight=Mathf.Max(evidence.maxInFlight,active);
                evidence.maxDraw=Mathf.Max(evidence.maxDraw,(int)RangedDrawCount());
                if(!captured && Time.realtimeSinceStartup-started>2 && active>0)
                { evidence.capture=id+"-ranged.png";yield return CaptureWorld(evidence.capture);captured=true; }
            }
            evidence.seconds=Time.realtimeSinceStartup-started;
            evidence.generated=manager.TotalLaunchedProjectiles;evidence.requested=engineProjectiles.TotalRequested;
            evidence.deferrals=engineProjectiles.CapacityDeferralCount;evidence.reclaims=engineProjectiles.ReclaimedSlots;
            evidence.directRejections=engineProjectiles.OverflowCount;
            for(int i=0;i<n;i++)if(seen[i]){evidence.uniqueShooters++;if(rear[i])evidence.rearShooters++;}
            manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);evidence.damage=initialHp-hp.Sum();
            RangedCheck(evidence.generated>n && evidence.maxInFlight>0 && evidence.maxDraw>0,id+": repeated real emission, in-flight slots and indirect drawing");
            RangedCheck(evidence.rearShooters>=Mathf.Max(2,evidence.rearPopulation/4),id+": meaningful in-range rear participation from actual projectile source IDs");
            RangedCheck(evidence.damage>0 && evidence.maxRelativeHeight>2,id+": real high arcs and combat damage, not cosmetic volleys");
            RangedCheck(evidence.directRejections==0 && evidence.deferrals==0,id+": bounded config budget sustains this small volley without capacity loss or stall");
            Require(manager.IsBattleRunning,"Small battle ended before pause validation.");
            controller.PauseBattle();
            var frozen=RangedPool();var positions=new AgentData[n];manager.Buffers.agentBuffer.GetData(positions);
            yield return new WaitForSecondsRealtime(.5f);
            var after=RangedPool();var bodies=new AgentData[n];manager.Buffers.agentBuffer.GetData(bodies);
            for(int i=0;i<n;i++)evidence.pauseDisplacement=Mathf.Max(evidence.pauseDisplacement,Vector3.Distance(positions[i].position,bodies[i].position));
            for(int i=0;i<frozen.Length;i++)if(frozen[i].targetAgentIndex>=0)
                Require(frozen[i].launchTime==after[i].launchTime && frozen[i].position==after[i].position,"Pause moved/overwrote an existing live projectile.");
            RangedCheck(evidence.pauseDisplacement<.00001f,id+": pause freezes bodies and existing shots (pending pre-pause requests may finish uploading)");
            controller.StartOrResumeBattle();yield return new WaitForSecondsRealtime(.3f);
            RangedCheck(manager.IsBattleRunning,id+": resume keeps battle functional");
            controller.ResetBattle();yield return new WaitForSecondsRealtime(.25f);VerifyFullStrength();
            RangedCheck(RangedDrawCount()==0,id+": reset clears drawing and restores units");
            controller.StartOrResumeBattle();yield return new WaitForSecondsRealtime(1.5f);
            RangedCheck(manager.TotalLaunchedProjectiles>0,id+": fresh fight can emit after reset");
            yield return Leave(true);VerifySources();VerifyPlans();
            RangedCheck(manager==null,id+": scene exit releases runtime copies and GPU resources");
            evidence.completed=true;WriteReport();
        }
        private IEnumerator RunRangedSmoke()
        {
            report.ranged=new RangedEvidence();var audio=WarSandboxAudio.Ensure();
            Require(audio.SettingsPath==settingsFile && string.IsNullOrEmpty(audio.SettingsError),"Wrong isolated ranged settings.");audio.SetMuted(true);
            yield return WaitForLoad(WarSandboxEntryState.Battle);BindBattle(false);
            yield return RangedScene("flat-96",false);
            yield return Enter("launch-mountain",true);yield return RangedScene("mountain-48",true);
            RangedCheck(!System.IO.File.Exists(settingsFile),"probe never writes player preferences");
            report.ranged.completed=true;Stage("complete");
        }
    }
}
#endif
