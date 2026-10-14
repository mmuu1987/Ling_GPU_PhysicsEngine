using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
namespace MassEngine.Game
{
    public interface IModelTrialPreBattleCheck { IEnumerator Run(WarSandboxBattleController controller); }
    /// <summary>Opt-in, bounded large-body movement prelude followed by passive natural-battle observation. Uses real orders, never injects damage.</summary>
    public sealed class LargeBeastProbe : MonoBehaviour,IModelTrialPreBattleCheck
    {
        [Serializable] class Evidence
        {
            public bool passed,routeCompleted,privateGridCoversDiameter,mutualCenterReach;public string method="Real lateral Move/Hold orders, bounded3s, then normal reset; subsequent passive position/rotation/HP sampling. Circular body approximation, not anatomical collision or zero-clipping proof.";
            public float radius,cellSize,beastReach,controlReach,initialMinimumMargin,routeMaximumDisplacement,routeMaximumTurnDegrees,maximumFriendlyBeastOverlapFraction;
            public int routeSamples,naturalSamples,attackSamples,stoppedAttackSamples,beastDamage,controlDamage,errors;
        }
        static LargeBeastProbe instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatics()=>instance=null;
        Evidence report=new Evidence();WarSandboxBattleController controller;MassEngineManager manager;bool natural;float next;string file;int initialBeastHp,initialControlHp;
        void Awake(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--large-beast-probe")<0){enabled=false;return;}if(instance!=null&&instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);}
        static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException("Large body probe: "+why);}
        (AgentData[],int[],int[]) Read()
        {
            int n=manager.UnitTypes.TotalAgentCount;Require(n>0&&n<=128,"Bounded probe only <=128 units.");var agents=new AgentData[n];var teams=new int[n];var hp=new int[n];manager.Buffers.agentBuffer.GetData(agents);manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);return(agents,teams,hp);
        }
        public IEnumerator Run(WarSandboxBattleController c)
        {
            controller=c;manager=c.manager;string key="--model-trial-output=";var arg=Environment.GetCommandLineArgs().First(a=>a.StartsWith(key));file=Path.Combine(arg.Substring(key.Length),"large-body-evidence.json");
            var beast=manager.scenarioConfig.unitTypes.Single(u=>u.teamId==0);var control=manager.scenarioConfig.unitTypes.Single(u=>u.teamId==1);
            report.radius=beast.flockingConfig.agentRadius;report.cellSize=manager.systemConfig.simulationConfig.cellSize;report.beastReach=beast.combatConfig.attackRange;report.controlReach=control.combatConfig.attackRange;
            var(start,teams,hp)=Read();float maximumScaledRadius=0;for(int i=0;i<start.Length;i++)if(teams[i]==0)maximumScaledRadius=Mathf.Max(maximumScaledRadius,report.radius*Mathf.Max(start[i].scale.x,start[i].scale.z));
            report.privateGridCoversDiameter=report.cellSize>=2*maximumScaledRadius;report.mutualCenterReach=Mathf.Min(report.beastReach,report.controlReach)>=maximumScaledRadius+control.flockingConfig.agentRadius;
            report.initialMinimumMargin=10000;Vector3 center=Vector3.zero;int count=0;
            for(int i=0;i<start.Length;i++)if(teams[i]==0){center+=start[i].position;count++;for(int j=i+1;j<start.Length;j++)if(teams[j]==0){float radii=report.radius*(Mathf.Max(start[i].scale.x,start[i].scale.z)+Mathf.Max(start[j].scale.x,start[j].scale.z));var d=start[i].position-start[j].position;d.y=0;report.initialMinimumMargin=Mathf.Min(report.initialMinimumMargin,d.magnitude-radii);}}
            Require(count>=8&&count<=12,"Expected small large-beast formation.");Require(report.initialMinimumMargin>=-.15f,"Initial large bodies overlap.");Require(report.privateGridCoversDiameter&&report.mutualCenterReach,"Private grid or reciprocal contact reach is too small.");
            center/=count;Require(controller.IssueMoveOrder(0,center+Vector3.forward*35,false),"Real move order rejected.");Require(controller.IssueOrder(ArmyOrder.Hold(1)),"Real hold order rejected.");controller.StartOrResumeBattle();
            float started=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup-started<3)
            {
                yield return new WaitForSecondsRealtime(.15f);var(now,nt,nh)=Read();for(int i=0;i<now.Length;i++)if(teams[i]==0){report.routeMaximumDisplacement=Mathf.Max(report.routeMaximumDisplacement,Vector3.Distance(now[i].position,start[i].position));report.routeMaximumTurnDegrees=Mathf.Max(report.routeMaximumTurnDegrees,Mathf.Abs(Mathf.DeltaAngle(start[i].rotation.y,now[i].rotation.y)));}report.routeSamples++;MeasureOverlap(now,nt,nh);
                Require(hp.SequenceEqual(nh),"Movement prelude unexpectedly entered combat.");
            }
            Require(report.routeMaximumDisplacement>1&&report.routeMaximumTurnDegrees>20,"No meaningful movement/turn observed.");controller.PauseBattle();controller.ResetBattle();yield return null;
            var(reset,rt,rh)=Read();Require(rh.SequenceEqual(hp)&&!manager.IsBattleRunning,"Prelude reset failed.");report.routeCompleted=true;initialBeastHp=0;initialControlHp=0;for(int i=0;i<rh.Length;i++){if(rt[i]==0)initialBeastHp+=rh[i];else initialControlHp+=rh[i];}natural=true;Flush();
        }
        void MeasureOverlap(AgentData[] agents,int[] teams,int[] hp)
        {
            for(int i=0;i<agents.Length;i++)if(teams[i]==0&&hp[i]>0)for(int j=i+1;j<agents.Length;j++)if(teams[j]==0&&hp[j]>0){float sum=report.radius*(Mathf.Max(agents[i].scale.x,agents[i].scale.z)+Mathf.Max(agents[j].scale.x,agents[j].scale.z));var d=agents[i].position-agents[j].position;d.y=0;report.maximumFriendlyBeastOverlapFraction=Mathf.Max(report.maximumFriendlyBeastOverlapFraction,Mathf.Max(0,(sum-d.magnitude)/sum));}
        }
        void Update()
        {
            if(!natural||manager==null||!manager.IsBattleRunning||Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+.15f;
            try
            {
                var(a,t,h)=Read();MeasureOverlap(a,t,h);int beastHp=0,controlHp=0;for(int i=0;i<a.Length;i++)if(t[i]==0){beastHp+=Mathf.Max(0,h[i]);if(h[i]>0&&a[i].currentState==(int)AgentState.Attack){report.attackSamples++;if(new Vector2(a[i].velocity.x,a[i].velocity.z).magnitude<.25f)report.stoppedAttackSamples++;}}else controlHp+=Mathf.Max(0,h[i]);
                report.beastDamage=Mathf.Max(report.beastDamage,initialBeastHp-beastHp);report.controlDamage=Mathf.Max(report.controlDamage,initialControlHp-controlHp);report.naturalSamples++;Flush();
            }
            catch(Exception e){report.errors++;natural=false;Debug.LogError(e);Flush();}
        }
        void Flush()
        {
            report.passed=report.errors==0&&report.routeCompleted&&report.privateGridCoversDiameter&&report.mutualCenterReach&&report.beastDamage>0&&report.controlDamage>0&&report.stoppedAttackSamples>0&&report.maximumFriendlyBeastOverlapFraction<=.5f;
            if(!string.IsNullOrEmpty(file))File.WriteAllText(file,JsonUtility.ToJson(report,true));
        }
        void OnApplicationQuit(){Flush();}
    }
}
