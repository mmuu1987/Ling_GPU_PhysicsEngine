using System;using System.IO;using System.Collections.Generic;using UnityEngine;
namespace MassEngine.Game.Editor {
 public static class StripeSafety33 {
  static void Check(bool b,string text){if(!b)throw new Exception(text);}
  public static void Run(){int checkedCells=0,changedCells=0,paths=0;var rows=new List<string>();
   foreach(int mode in new[]{0,1,2,3}){
    const int N=32;var heights=new float[(N+1)*(N+1)];var blocked=new bool[N*N];
    if(mode==1||mode==2)for(int z=0;z<N;z++)if(mode==2||z<12||z>19)blocked[z*N+15]=true;
    if(mode==3)for(int z=8;z<24;z++)for(int x=10;x<13;x++)blocked[z*N+x]=true;
    var surface=new TerrainSurface("stripe-safety-"+mode,1,N+1,N+1,Vector2.zero,new Vector2(N,N),45,heights,blocked);
    var nav=new TerrainNavigationGrid(surface,Vector2.zero,1,N,N,.1f,0);
    var goals=new List<Vector2>();for(int z=7;z<25;z++)goals.Add(new Vector2(z%5==0?24.5f:27.5f,z+.5f));
    Vector2[] original=nav.CreateFlowField(goals,0),candidate=(Vector2[])original.Clone();TerrainLaneApproach33.Apply(nav,goals,candidate,0);
    var terminal=new HashSet<int>();foreach(var p in goals)if(nav.TryGetCell(p,out int i)&&nav.IsWalkable(p))terminal.Add(i);
    for(int cell=0;cell<candidate.Length;cell++){
     checkedCells++;if(original[cell]==Vector2.zero)Check(candidate[cell]==Vector2.zero,"Changed stopped/unreachable cell");
     if(candidate[cell]!=original[cell]){changedCells++;int to=cell+(int)Mathf.Sign(candidate[cell].x)*(candidate[cell].x==0?0:1)+(int)Mathf.Sign(candidate[cell].y)*(candidate[cell].y==0?0:N);Check(nav.HasClearCardinalRoute33(cell,to),"Introduced illegal next edge");}
     if(candidate[cell]==Vector2.zero)continue;var seen=new HashSet<int>();int at=cell;
     for(int hops=0;hops<N*N;hops++){
      Check(seen.Add(at),"Routing cycle in mode "+mode+" starting "+cell);var v=candidate[at];if(v==Vector2.zero){Check(terminal.Contains(at),"Stopped at non-target");break;}
      int dx=v.x==0?0:(v.x>0?1:-1),dz=v.y==0?0:(v.y>0?1:-1),x=at%N,z=at/N;
      Check(x+dx>=0&&x+dx<N&&z+dz>=0&&z+dz<N,"Out of grid");int next=at+dx+dz*N;Check(nav.IsWalkable(nav.CellCenter(next)),"Entered blocked cell");
      if(dx!=0&&dz!=0){Check(nav.IsWalkable(nav.CellCenter(at+dx))&&nav.IsWalkable(nav.CellCenter(at+dz*N)),"Cut diagonal corner");}
      at=next;Check(hops<N*N-1,"Did not reach target");
     }paths++;
    }
    if(mode==2){int left=16*N+5,right=16*N+27;Check(!nav.HasClearCardinalRoute33(left,right),"Crossed closed wall");Check(candidate[left]==Vector2.zero,"Unreachable area received synthetic target");}
    var noGoals=nav.CreateFlowField(new List<Vector2>(),0);TerrainLaneApproach33.Apply(nav,new List<Vector2>(),noGoals,0);foreach(var v in noGoals)Check(v==Vector2.zero,"No-goal field changed");
    rows.Add("mode"+mode+": legal edges, no corner cut/cycles, reachable paths terminate at real goals, stop/unreachable preserved");
   }
   Check(changedCells>0,"Candidate path branch never exercised");string text="{\"passed\":true,\"checkedCells\":"+checkedCells+",\"changedCells\":"+changedCells+",\"paths\":"+paths+",\"proceduralCases\":4,\"gpuObstacleTest\":false}";File.WriteAllText("Logs/StripeFix-20261005/safety.json",text);Debug.Log("STRIPE33_SAFETY "+text);
  }
 }
}
