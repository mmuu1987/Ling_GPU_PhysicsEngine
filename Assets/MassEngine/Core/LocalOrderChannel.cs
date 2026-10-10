using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
namespace MassEngine
{
    /// <summary>Opt-in P6 prototype. Main-thread, ordered graphics queue only; one admission request at a time.</summary>
    public sealed class LocalOrderChannel : IDisposable
    {
        public const string Keyword="MASS_LOCAL_ORDERS";
        public const int GroupBudget=4,PhysicalSlots=GroupBudget+1,MaxCells=65536;
        private readonly MassGpuBufferManager buffers;private readonly Func<float,TerrainNavigationGrid> makeNavigation;
        private readonly Func<bool> canRefreshAttack;private float nextAttackRefresh;private int lastAttackSequence;
        private readonly Func<bool> contextCurrent;private readonly int[] teams;private readonly float[] radii;
        private readonly Dictionary<float,TerrainNavigationGrid> cache=new Dictionary<float,TerrainNavigationGrid>();
        private LocalAgentOrder[] orders;private ComputeBuffer orderBuffer,spareOrderBuffer,flowBuffer,maskBuffer,ackBuffer;
        private readonly Dictionary<int,LocalOrderReceipt> receipts=new Dictionary<int,LocalOrderReceipt>();
        private Task<PlanResult> retiredPlanTask;private Pending pending;private bool disposed,ackPending;private AsyncGPUReadbackRequest ackRequest;
        private int serial,version,ackVersion;private float nextAckTime;private readonly int cells;
        public int Epoch {get;} public int ActiveGroups {get;private set;} public int ActiveMembers {get;private set;}
        public long GpuBytes => disposed?0:(long)orders.Length*68+(long)PhysicalSlots*cells*12;
        public int NavigationContexts=>cache.Count;
        public long NavigationCpuUpperEstimate=>(long)cache.Count*cells*96;
        public string LastObservationError {get;private set;} public float LastObservationTime {get;private set;}
        public int SnapshotRequests {get;private set;} public int AckRequests {get;private set;}
        private sealed class Pending
        {
            public bool refresh;public int[] members;public LocalOrderKind kind;public Vector2 target;public LocalOrderReceipt receipt;public Stopwatch watch;
            public AsyncGPUReadbackRequest positionsRequest,hpRequest;public Vector2[] positions;public int[] hp;public Task<PlanResult> task;
        }
        private sealed class PlanResult {public LocalOrderPlan plan;public string error;public double milliseconds;}
        public LocalOrderChannel(MassGpuBufferManager buffers,int epoch,int[] teams,float[] radii,
            Func<float,TerrainNavigationGrid> navigationFactory,Func<bool> contextCurrent=null,Func<bool> canRefreshAttack=null)
        {
            if(buffers==null||!buffers.IsAllocated||buffers.FlowCellCount>MaxCells||teams.Length!=buffers.AgentCount||radii.Length!=teams.Length)
                throw new ArgumentException("Local prototype requires allocated buffers, matching immutable membership and <=65536 flow cells.");
            this.buffers=buffers;Epoch=epoch;this.teams=(int[])teams.Clone();this.radii=(float[])radii.Clone();makeNavigation=navigationFactory;
            this.canRefreshAttack=canRefreshAttack;this.contextCurrent=contextCurrent;cells=buffers.FlowCellCount;orders=new LocalAgentOrder[teams.Length];
            try{orderBuffer=new ComputeBuffer(orders.Length,32);spareOrderBuffer=new ComputeBuffer(orders.Length,32);flowBuffer=new ComputeBuffer(cells*PhysicalSlots,8);maskBuffer=new ComputeBuffer(cells*PhysicalSlots,4);ackBuffer=new ComputeBuffer(orders.Length,4);orderBuffer.SetData(orders);spareOrderBuffer.SetData(orders);ackBuffer.SetData(new uint[orders.Length]);}
            catch{Dispose();throw;}
        }
        public LocalOrderReceipt Submit(int epoch,int team,int memberVersion,int[] members,LocalOrderKind kind,Vector2 target)
        {
            var r=new LocalOrderReceipt{Epoch=Epoch,Team=team,MemberVersion=memberVersion,Kind=kind,Target=target,Status=LocalOrderStatus.Rejected};
            if(disposed||epoch!=Epoch){r.Error="Old battle/prototype generation.";return r;}
            if(pending!=null&&pending.refresh){retiredPlanTask=pending.task;pending=null;}
            if(pending!=null||(retiredPlanTask!=null&&!retiredPlanTask.IsCompleted)){r.Error="One command is awaiting its live snapshot; retry, old state unchanged.";return r;}
            if(members==null||members.Length==0||members.Length>4096||!LocalOrderPlan.Finite(target)||serial>=8000000){r.Error="Invalid/empty/oversized members, nonfinite target or sequence exhausted.";return r;}
            if(kind!=LocalOrderKind.Move&&kind!=LocalOrderKind.Hold&&kind!=LocalOrderKind.Attack&&kind!=LocalOrderKind.Retreat){r.Error="Unknown local command; no fallback to whole army.";return r;}
            r.Sequence=++serial;r.Status=LocalOrderStatus.AwaitingSnapshot;
            var p=new Pending{members=(int[])members.Clone(),target=target,kind=kind,receipt=r,watch=Stopwatch.StartNew()};
            r.MemberSnapshot=Array.AsReadOnly(p.members);
            try{p.positionsRequest=AsyncGPUReadback.Request(buffers.agentPositionReadBuffer);p.hpRequest=AsyncGPUReadback.Request(buffers.combatBuffers.hpReadBuffer);pending=p;SnapshotRequests++;}
            catch(Exception ex){r.Status=LocalOrderStatus.Rejected;r.Error=ex.Message;}
            return r;
        }
        public void Tick()
        {
            if(disposed)return;
            if(contextCurrent!=null&&!contextCurrent()){Dispose();return;}
            if(ackPending && ackRequest.done)
            {
                ackPending=false;
                try
                {
                    if(ackRequest.hasError)throw new InvalidOperationException("GPU observation readback failed; commands retained, no new execution claim.");
                    if(ackVersion!=version)return;
                    var ack=ackRequest.GetData<uint>();
                    foreach(var r in receipts.Values){r.Status=LocalOrderStatus.Committed;r.ObservedAlive=r.GpuExecutingMembers=r.GpuBlockedMembers=0;}
                    for(int i=0;i<orders.Length;i++)
                    {
                        var o=orders[i];if(o.slotPlusOne==0)continue;uint a=ack[i];
                        if((a&0x00ffffffu)!=(uint)o.sequence)continue;
                        if(receipts.TryGetValue(o.sequence,out var r)){r.ObservedAlive++;if(a==(uint)o.sequence){r.GpuExecutingMembers++;r.Status=LocalOrderStatus.GpuExecuting;}if((a&0x40000000u)!=0)r.GpuBlockedMembers++;}
                    }
                    LocalAgentOrder[] next=null;
                    for(int i=0;i<orders.Length;i++)if(orders[i].slotPlusOne!=0 && ack[i]==0){if(next==null)next=(LocalAgentOrder[])orders.Clone();next[i]=default;}
                    if(next!=null){UploadReplacement(next);orders=next;Recount();}
                    LastObservationTime=Time.realtimeSinceStartup;LastObservationError=null;
                }
                catch(Exception ex){LastObservationError=ex.Message;foreach(var r in receipts.Values){r.Status=LocalOrderStatus.Committed;r.GpuExecutingMembers=0;}}
            }
            if(pending==null)TryRefreshAttack();
            if(pending==null)return;
            var p=pending;
            try
            {
                // Copy each completed request in its valid frame; never retain NativeArray across frames.
                if(p.positions==null && p.positionsRequest.done){if(p.positionsRequest.hasError)throw new InvalidOperationException("Position snapshot failed.");p.positions=p.positionsRequest.GetData<Vector2>().ToArray();}
                if(p.hp==null && p.hpRequest.done){if(p.hpRequest.hasError)throw new InvalidOperationException("Liveness snapshot failed.");p.hp=p.hpRequest.GetData<int>().ToArray();}
                if(p.watch.Elapsed.TotalSeconds>10)throw new InvalidOperationException("Live snapshot timed out.");
                if(p.positions==null||p.hp==null)return;
                if(p.task==null)
                {
                    p.receipt.ReadbackMilliseconds=p.watch.Elapsed.TotalMilliseconds;
                    var cached=new Dictionary<float,TerrainNavigationGrid>(cache);
                    // One worker per channel; all inputs/factory data are immutable CPU snapshots. No GPU/Unity Object access.
                    p.task=Task.Run(()=>{
                        var timer=Stopwatch.StartNew();var result=new PlanResult();
                        try{LocalOrderPlan.TryCreate(p.receipt.Team,p.members,teams,radii,p.positions,p.hp,p.kind,p.target,
                            r=>cached.TryGetValue(r,out var nav)?nav:makeNavigation(r),out result.plan,out result.error);}
                        catch(Exception ex){result.error=ex.Message;}
                        result.milliseconds=timer.Elapsed.TotalMilliseconds;return result;
                    });return;
                }
                if(!p.task.IsCompleted)return;
                var planned=p.task.Result;var plan=planned.plan;if(plan==null)throw new InvalidOperationException(planned.error);
                p.receipt.PlanMilliseconds=planned.milliseconds;
                if(plan.Navigation.CellCount!=cells)throw new InvalidOperationException("Navigation shape changed.");
                if(!cache.ContainsKey(plan.Radius)){if(cache.Count>=PhysicalSlots)cache.Clear();cache[plan.Radius]=plan.Navigation;}
                var watch=Stopwatch.StartNew();
                var replacing=new HashSet<int>(plan.Members);var used=new HashSet<int>();var surviving=new HashSet<int>();
                for(int i=0;i<orders.Length;i++)if(orders[i].slotPlusOne!=0){used.Add(orders[i].slotPlusOne);if(!replacing.Contains(i)&&p.hp[i]>0)surviving.Add(orders[i].slotPlusOne);}
                if(surviving.Count>=GroupBudget)throw new InvalidOperationException("Four independent groups exhausted; old groups retained, no merging.");
                int slot=1;while(used.Contains(slot))slot++;if(slot>PhysicalSlots)throw new InvalidOperationException("No transactional staging slot.");
                var next=(LocalAgentOrder[])orders.Clone();for(int i=0;i<next.Length;i++)if(p.hp[i]<=0)next[i]=default;
                for(int n=0;n<plan.Members.Length;n++)next[plan.Members[n]]=new LocalAgentOrder{slotPlusOne=slot,sequence=p.receipt.Sequence,epoch=Epoch,
                    stance=(int)(p.kind==LocalOrderKind.Hold?TeamStance.HoldHere:p.kind==LocalOrderKind.Attack?TeamStance.Advance:TeamStance.MoveOnly),arrival=plan.Arrivals[n],arrivalRegionRadius=plan.ArrivalRegionRadius,stopRadius=.25f};
                watch.Restart();
                // The spare slot is unreachable by old commands. If either upload fails the old GPU state is untouched.
                flowBuffer.SetData(plan.Directions,0,(slot-1)*cells,cells);maskBuffer.SetData(plan.Navigation.CopyWalkable(),0,(slot-1)*cells,cells);
                UploadReplacement(next);orders=next;receipts[p.receipt.Sequence]=p.receipt;Recount();
                p.receipt.Target=plan.ResolvedTarget;p.receipt.UploadMilliseconds=watch.Elapsed.TotalMilliseconds;
                if(p.refresh){p.receipt.NavigationRefreshes++;p.receipt.NavigationError=null;}else p.receipt.SubmittedMembers=plan.Members.Length;
                p.receipt.Status=LocalOrderStatus.Committed;
            }
            catch(Exception ex){if(p.task!=null&&!p.task.IsCompleted)retiredPlanTask=p.task;if(p.refresh)p.receipt.NavigationError=ex.Message;else{p.receipt.Status=LocalOrderStatus.Rejected;p.receipt.Error=ex.Message;}}
            pending=null;
        }
        private void TryRefreshAttack()
        {
            if(Time.realtimeSinceStartup<nextAttackRefresh||(canRefreshAttack!=null&&!canRefreshAttack())||(retiredPlanTask!=null&&!retiredPlanTask.IsCompleted))return;
            nextAttackRefresh=Time.realtimeSinceStartup+.25f;retiredPlanTask=null;
            LocalOrderReceipt chosen=null,first=null;
            foreach(var r in receipts.Values)if(r.Kind==LocalOrderKind.Attack)
            {if(first==null||r.Sequence<first.Sequence)first=r;if(r.Sequence>lastAttackSequence&&(chosen==null||r.Sequence<chosen.Sequence))chosen=r;}
            chosen=chosen??first;if(chosen==null)return;lastAttackSequence=chosen.Sequence;
            var members=new List<int>();for(int i=0;i<orders.Length;i++)if(orders[i].sequence==chosen.Sequence&&orders[i].slotPlusOne>0)members.Add(i);
            if(members.Count==0)return;
            var p=new Pending{refresh=true,members=members.ToArray(),kind=LocalOrderKind.Attack,target=chosen.Target,receipt=chosen,watch=Stopwatch.StartNew()};
            try{p.positionsRequest=AsyncGPUReadback.Request(buffers.agentPositionReadBuffer);p.hpRequest=AsyncGPUReadback.Request(buffers.combatBuffers.hpReadBuffer);pending=p;SnapshotRequests++;}
            catch(Exception ex){chosen.NavigationError=ex.Message;}
        }
        private void UploadReplacement(LocalAgentOrder[] next)
        {
            spareOrderBuffer.SetData(next);var old=orderBuffer;orderBuffer=spareOrderBuffer;spareOrderBuffer=old;version++;
            foreach(var r in receipts.Values){r.Status=LocalOrderStatus.Committed;r.ObservedAlive=r.GpuExecutingMembers=r.GpuBlockedMembers=0;}
        }
        private void Recount()
        {var used=new HashSet<int>();ActiveMembers=0;var sequences=new HashSet<int>();foreach(var o in orders)if(o.slotPlusOne!=0){used.Add(o.slotPlusOne);sequences.Add(o.sequence);ActiveMembers++;}ActiveGroups=used.Count;
         var remove=new List<int>();foreach(var k in receipts.Keys)if(!sequences.Contains(k))remove.Add(k);foreach(int k in remove){receipts[k].Status=LocalOrderStatus.Invalidated;receipts[k].Error="Members replaced, cleared or no longer alive.";receipts[k].ObservedAlive=receipts[k].GpuExecutingMembers=receipts[k].GpuBlockedMembers=0;receipts.Remove(k);}}
        public bool TryGetReceipt(int sequence,out LocalOrderReceipt receipt)=>receipts.TryGetValue(sequence,out receipt);
        public LocalAgentOrder OrderAt(int index)=>orders[index];
        public void ClearTeam(int team)
        {
            if(disposed)return;if(pending!=null && pending.receipt.Team==team){pending.receipt.Status=LocalOrderStatus.Invalidated;pending.receipt.Error="Superseded by a whole-army command.";retiredPlanTask=pending.task;pending=null;}
            var next=(LocalAgentOrder[])orders.Clone();bool changed=false;for(int i=0;i<next.Length;i++)if(teams[i]==team && next[i].slotPlusOne!=0){next[i]=default;changed=true;}
            if(changed){UploadReplacement(next);orders=next;Recount();}
        }
        public void AfterDispatch(MassGpuShaderSet shaders)
        {
            if(disposed||ackPending||ActiveGroups==0||Time.realtimeSinceStartup<nextAckTime)return;
            var shader=shaders.CombatSimulationShader;if(shader==null)return;shader.EnableKeyword(Keyword);int kernel=shader.FindKernel("ObserveLocalOrders");
            shader.SetBuffer(kernel,"_LocalOrders",orderBuffer);shader.SetBuffer(kernel,"_LocalOrderMask",maskBuffer);shader.SetBuffer(kernel,"_LocalOrderAck",ackBuffer);
            shader.SetBuffer(kernel,"hpReadBuffer",buffers.combatBuffers.hpReadBuffer);shader.SetBuffer(kernel,"agentPositionReadBuffer",buffers.agentPositionReadBuffer);
            shader.SetBuffer(kernel,"engagementSlotAssignmentBuffer",buffers.combatBuffers.engagementSlotAssignmentBuffer);
            shader.Dispatch(kernel,Mathf.Max(1,(orders.Length+63)/64),1,1);
            ackRequest=AsyncGPUReadback.Request(ackBuffer);ackVersion=version;ackPending=true;nextAckTime=Time.realtimeSinceStartup+.2f;AckRequests++;
        }
        // Admission already limits each of four groups to 4096 members. This table is sorted by
        // scanning orders only after a committed version change; agent IDs are not bitmask-limited.
        private const int PartitionCapacity=GroupBudget*4096;
        private int[] partitionIds;private int partitionVersion=-1,partitionCount,partitionStances;
        internal bool TryBindPartition(MassGpuShaderSet shaders)
        {
            if(disposed||ActiveGroups==0||!shaders.HasLocalPartitions||buffers.LocalPartitionTypeMask==0)return false;
            if(partitionIds==null)partitionIds=new int[PartitionCapacity];
            if(partitionVersion!=version) {
                partitionCount=partitionStances=0;
                for(int i=0;i<orders.Length;i++) {
                    var o=orders[i];if(o.slotPlusOne<=0||o.epoch!=Epoch)continue;
                    int role=o.stance==1?0:o.stance==3?1:o.stance==4?2:-1;
                    if(role<0||partitionCount>=PartitionCapacity){partitionVersion=-1;return false;}
                    partitionIds[partitionCount++]=i;partitionStances|=1<<role;
                }
                partitionVersion=version;
            }
            if(partitionCount==0)return false;
            var shader=shaders.CombatSimulationShader;
            shader.DisableKeyword(Keyword);
            shader.SetInts("_LocalPartitionIds",partitionIds);
            shader.SetInt("_LocalPartitionCount",partitionCount);
            return true;
        }
        internal bool UsesPartition(int role) => (partitionStances&(1<<(role/2)))!=0 && (buffers.LocalPartitionTypeMask&(1<<(role%2)))!=0;
        internal void BindPartitionKernel(ComputeShader shader,int kernel) {
            shader.SetInt("_LocalOrderEpoch",Epoch);shader.SetInt("_LocalOrderCellCount",cells);
            shader.SetBuffer(kernel,"_LocalOrders",orderBuffer);shader.SetBuffer(kernel,"_LocalOrderFlow",flowBuffer);shader.SetBuffer(kernel,"_LocalOrderMask",maskBuffer);
        }
        public long PartitionCpuBytes => partitionIds==null?0:(long)partitionIds.Length*4;
        // Shared compute-asset constant storage, not an additional channel-owned ComputeBuffer.
        public const int PartitionConstantBytes=65536;

        public void Bind(MassGpuShaderSet shaders)
        {
            var shader=shaders.CombatSimulationShader;if(shader==null)return;
            if(disposed||ActiveGroups==0){shader.DisableKeyword(Keyword);return;}shader.EnableKeyword(Keyword);int k=shaders.SimulateCombatAndAccumulateDamage;
            shader.SetInt("_LocalOrderEpoch",Epoch);shader.SetInt("_LocalOrderCellCount",cells);shader.SetBuffer(k,"_LocalOrders",orderBuffer);
            shader.SetBuffer(k,"_LocalOrderFlow",flowBuffer);shader.SetBuffer(k,"_LocalOrderMask",maskBuffer);
        }
        public bool IsDisposed=>disposed;
        public void Dispose()
        {
            if(disposed)return;disposed=true;partitionIds=null;partitionCount=partitionStances=0;partitionVersion=-1;
            if(pending!=null){pending.receipt.Status=LocalOrderStatus.Invalidated;pending.receipt.Error="Battle/context reset; snapshot discarded.";pending=null;}
            foreach(var r in receipts.Values)r.Status=LocalOrderStatus.Invalidated;receipts.Clear();cache.Clear();
            orderBuffer?.Release();spareOrderBuffer?.Release();flowBuffer?.Release();maskBuffer?.Release();ackBuffer?.Release();ActiveGroups=ActiveMembers=0;
            // Default ComputeBuffer mode and ordered graphics dispatches retain in-flight native uses. No async-compute queue is used.
        }
    }
}


