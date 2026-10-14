using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using MassEngine.Projectiles;

namespace MassEngine.Game
{
    /// <summary>Opt-in passive small-trial observer. Never injects requests, damage, orders or projectile geometry.</summary>
    public sealed class RangerFiringProbe : MonoBehaviour
    {
        [Serializable] class Evidence
        {
            public string method="Passive reads of real projectile pool/source IDs/draw args/HP and animation phase. No injected requests/damage/volley. Approximate timing, not bone-socket/frame-perfect firing.";
            public bool passed;public int maximumLaunched,maximumInFlight,maximumDraw,negativeGravitySamples,enemyDamage,uniqueRangerShooters,epochs,errors;
            public float maximumHeight,maximumVerticalVelocityChange;public List<Shot> launchSamples=new List<Shot>();
        }
        [Serializable] class Shot {public int shooter,state,presentationState;public float launchTime,animationTime,gravity,height;}
        static RangerFiringProbe instance;
        Evidence report=new Evidence();string output;float next;MassEngineManager manager;object buffers;
        int initialEnemyHp,lastLaunched;bool wasRunning;readonly HashSet<int> shooters=new HashSet<int>();readonly HashSet<string> seen=new HashSet<string>();
        readonly Dictionary<int,ProjectileGpuData> previous=new Dictionary<int,ProjectileGpuData>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatic()=>instance=null;
        void Awake()
        {
            string[] args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--ranger-probe")<0){enabled=false;return;}
            if(instance!=null && instance!=this){Destroy(gameObject);return;}instance=this;DontDestroyOnLoad(gameObject);
            string key="--model-trial-output=";output=args.FirstOrDefault(a=>a.StartsWith(key))?.Substring(key.Length);
            if(string.IsNullOrEmpty(output)){enabled=false;return;}
        }
        void Update()
        {
            if(Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+.15f;
            try
            {
                if(manager==null)manager=FindFirstObjectByType<MassEngineManager>();
                if(manager==null || manager.Buffers==null || !manager.Buffers.IsAllocated){wasRunning=false;Flush();return;}
                bool running=manager.IsBattleRunning;
                if(!running){wasRunning=false;Flush();return;}
                int n=manager.UnitTypes.TotalAgentCount;if(n<=0 || n>256)throw new InvalidOperationException("Probe is bounded to <=256 units.");
                var teams=new int[n];var hp=new int[n];var agents=new AgentData[n];manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);manager.Buffers.combatBuffers.hpReadBuffer.GetData(hp);manager.Buffers.agentBuffer.GetData(agents);
                int enemyHp=0;for(int i=0;i<n;i++)if(teams[i]==1)enemyHp+=Mathf.Max(0,hp[i]);
                if(!wasRunning || buffers!=manager.Buffers || manager.TotalLaunchedProjectiles<lastLaunched)
                {report.epochs++;buffers=manager.Buffers;initialEnemyHp=enemyHp;previous.Clear();}
                wasRunning=true;lastLaunched=manager.TotalLaunchedProjectiles;report.maximumLaunched=Mathf.Max(report.maximumLaunched,lastLaunched);report.enemyDamage=Mathf.Max(report.enemyDamage,initialEnemyHp-enemyHp);
                var pool=new ProjectileGpuData[manager.Buffers.MaxProjectiles];manager.Buffers.projectileBuffer.GetData(pool);var args=new uint[5];manager.Buffers.projectileDrawArgsBuffer.GetData(args);report.maximumDraw=Mathf.Max(report.maximumDraw,(int)args[1]);int active=0;
                for(int index=0;index<pool.Length;index++)
                {
                    var p=pool[index];if(p.targetAgentIndex<0)continue;active++;
                    if(p.sourceTeamId==0 && p.sourceAgentIndexPlusOne>0 && p.sourceAgentIndexPlusOne<=n)
                    {
                        int owner=p.sourceAgentIndexPlusOne-1;shooters.Add(owner);if(p.gravity<0)report.negativeGravitySamples++;
                        report.maximumHeight=Mathf.Max(report.maximumHeight,p.position.y);
                        if(previous.TryGetValue(index,out var old) && old.launchTime==p.launchTime)report.maximumVerticalVelocityChange=Mathf.Max(report.maximumVerticalVelocityChange,Mathf.Abs(old.velocity.y-p.velocity.y));previous[index]=p;
                        string id=report.epochs+":"+index+":"+p.launchTime.ToString("R");
                        if(seen.Add(id) && report.launchSamples.Count<96)report.launchSamples.Add(new Shot{shooter=owner,state=agents[owner].currentState,presentationState=agents[owner].presentationState,launchTime=p.launchTime,animationTime=agents[owner].currentAnimationTime,gravity=p.gravity,height=p.position.y});
                    }
                }
                report.maximumInFlight=Mathf.Max(report.maximumInFlight,active);report.uniqueRangerShooters=shooters.Count;Flush();
            }
            catch(Exception e){report.errors++;enabled=false;Debug.LogWarning("Ranger passive probe stopped: "+e.Message);Flush();}
        }
        void Flush()
        {
            if(string.IsNullOrEmpty(output) || !Directory.Exists(output))return;
            report.passed=report.errors==0 && report.maximumLaunched>0 && report.maximumInFlight>0 && report.maximumDraw>0 && report.negativeGravitySamples>0 && report.enemyDamage>0 && report.uniqueRangerShooters>0 && report.maximumVerticalVelocityChange>.1f;
            File.WriteAllText(Path.Combine(output,"ranged-evidence.json"),JsonUtility.ToJson(report,true));
        }
        void OnApplicationQuit(){Flush();}
        void OnDestroy(){if(instance==this)instance=null;}
    }
}
