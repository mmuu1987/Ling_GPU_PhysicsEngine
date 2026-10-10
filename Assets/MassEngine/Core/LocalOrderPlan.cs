using System;
using System.Collections.Generic;
using UnityEngine;
namespace MassEngine
{
    public enum LocalOrderKind { Move, Hold, Attack, Retreat }
    public enum LocalOrderStatus { Rejected, AwaitingSnapshot, Committed, GpuExecuting, Invalidated }
    public sealed class LocalOrderReceipt
    {
        public int Epoch {get;internal set;} public int Sequence {get;internal set;} public int Team {get;internal set;}
        public int NavigationRefreshes {get;internal set;} public string NavigationError {get;internal set;}
        public int MemberVersion {get;internal set;} public LocalOrderStatus Status {get;internal set;}
        public LocalOrderKind Kind {get;internal set;} public Vector2 Target {get;internal set;}
        public IReadOnlyList<int> MemberSnapshot {get;internal set;}=Array.Empty<int>();
        public string Error {get;internal set;} public int SubmittedMembers {get;internal set;} public int ObservedAlive {get;internal set;}
        public int GpuExecutingMembers {get;internal set;} public int GpuBlockedMembers {get;internal set;}
        public double ReadbackMilliseconds {get;internal set;} public double PlanMilliseconds {get;internal set;}
        public double UploadMilliseconds {get;internal set;}
    }
    // 32-byte side buffer. AgentData stays 64 bytes; neither team nor unit-type ids are repurposed.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LocalAgentOrder
    {
        public int slotPlusOne, sequence, stance, epoch;
        public Vector2 arrival; public float arrivalRegionRadius, stopRadius;
    }
    public sealed class LocalOrderPlan
    {
        public int[] Members {get;private set;} public Vector2[] Arrivals {get;private set;}
        public Vector2[] Directions {get;private set;} public TerrainNavigationGrid Navigation {get;private set;}
        public Vector2 ResolvedTarget {get;private set;}
        public float Radius {get;private set;} public float ArrivalRegionRadius {get;private set;}
        public static bool Finite(Vector2 p)=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y);
        public static bool TryCreate(int team,int[] snapshot,int[] teams,float[] radii,Vector2[] positions,int[] hp,
            LocalOrderKind kind,Vector2 target,Func<float,TerrainNavigationGrid> navigationFactory,out LocalOrderPlan plan,out string error)
        {
            plan=null;error=null;
            if(snapshot==null||snapshot.Length==0||snapshot.Length>4096||teams==null||radii==null||positions==null||hp==null||
                teams.Length!=radii.Length||teams.Length!=positions.Length||teams.Length!=hp.Length||!Finite(target)||
                (kind!=LocalOrderKind.Move&&kind!=LocalOrderKind.Hold&&kind!=LocalOrderKind.Attack&&kind!=LocalOrderKind.Retreat)) {error="Invalid member snapshot, target or local command.";return false;}
            var members=new List<int>();var unique=new HashSet<int>();float maxRadius=.05f;
            foreach(int id in snapshot)
            {
                if(id<0||id>=teams.Length||teams[id]!=team||!unique.Add(id)){error="Members must be unique indices of the requested team.";return false;}
                if(hp[id]<=0)continue;
                if(!Finite(positions[id])||float.IsNaN(radii[id])||float.IsInfinity(radii[id])||radii[id]<=0){error="Invalid current position or effective radius.";return false;}
                maxRadius=Mathf.Max(maxRadius,radii[id]);members.Add(id);
            }
            if(members.Count==0){error="No surviving selected members.";return false;}
            members.Sort();var nav=navigationFactory(maxRadius);if(nav==null){error="Prototype requires an authored terrain navigation context.";return false;}
            if(kind==LocalOrderKind.Attack)
            {
                // Multi-source field to live enemy positions, not a chosen victim. Existing GPU target acquisition still decides combat.
                var targets=new List<Vector2>();var targetCells=new HashSet<int>();var components=new HashSet<int>();
                foreach(int id in members){int component=nav.ComponentAt(positions[id]);if(component<0){error="Selected live member is outside its navigation mask.";return false;}components.Add(component);}
                var reachableComponents=new HashSet<int>();Vector2 centre=Vector2.zero;foreach(int id in members)centre+=positions[id]/members.Count;
                float nearest=float.PositiveInfinity;Vector2 displayTarget=Vector2.zero;
                for(int i=0;i<teams.Length;i++)if(teams[i]!=team&&hp[i]>0&&Finite(positions[i])&&nav.IsWalkable(positions[i]))
                {
                    int component=nav.ComponentAt(positions[i]);if(!components.Contains(component))continue;
                    nav.TryGetCell(positions[i],out int cell);if(targetCells.Add(cell))targets.Add(nav.CellCenter(cell));reachableComponents.Add(component);
                    float distance=(positions[i]-centre).sqrMagnitude;if(distance<nearest){nearest=distance;displayTarget=positions[i];}
                }
                if(targets.Count==0||!components.IsSubsetOf(reachableComponents)){error="No reachable live enemy for every selected navigation component; old command retained.";return false;}
                var attackField=nav.CreateFlowField(targets,0);var positionsAtAdmission=new Vector2[members.Count];for(int i=0;i<members.Count;i++)positionsAtAdmission[i]=positions[members[i]];
                plan=new LocalOrderPlan{Members=members.ToArray(),Arrivals=positionsAtAdmission,Directions=attackField,Navigation=nav,Radius=maxRadius,ArrivalRegionRadius=0,ResolvedTarget=displayTarget};return true;
            }
            var goals=new Vector2[members.Count];float region=0;
            if(kind==LocalOrderKind.Move||kind==LocalOrderKind.Retreat)
            {
                if(!nav.IsWalkable(target)){error="Target is blocked or outside the selected-radius navigation mask.";return false;}
                nav.TryGetCell(target,out int targetCell);Vector2 center=nav.CellCenter(targetCell);
                int cols=Mathf.CeilToInt(Mathf.Sqrt(members.Count)),rows=Mathf.CeilToInt(members.Count/(float)cols);
                float step=Mathf.Ceil((2*maxRadius+.1f)/nav.CellSize)*nav.CellSize;
                // A clear rectangular arrival region guarantees safe local distribution after the shared obstacle-aware path.
                Vector2 extent=new Vector2((cols-1)*step,(rows-1)*step);
                if(!nav.IsFootprintWalkable(center,extent)){error="Arrival region cannot fit selected live members; old commands retained.";return false;}
                for(int i=0;i<members.Count;i++)
                {
                    goals[i]=center+new Vector2(i%cols-(cols-1)*.5f,i/cols-(rows-1)*.5f)*step;
                    if(!nav.AreConnected(positions[members[i]],goals[i])){error="A selected current live position cannot reach the arrival region.";return false;}
                }
                region=extent.magnitude+nav.CellSize*2;
            }
            else
            {
                for(int i=0;i<members.Count;i++){goals[i]=positions[members[i]];if(!nav.IsWalkable(goals[i])){error="Current selected position is not walkable at its effective radius.";return false;}}
            }
            var field=(kind==LocalOrderKind.Move||kind==LocalOrderKind.Retreat)?nav.CreateFlowField(new[]{target},0):new Vector2[nav.CellCount];
            plan=new LocalOrderPlan{Members=members.ToArray(),Arrivals=goals,Directions=field,Navigation=nav,Radius=maxRadius,ArrivalRegionRadius=region,ResolvedTarget=target};return true;
        }
    }
}

