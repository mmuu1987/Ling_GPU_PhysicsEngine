using System;using System.Collections.Generic;using UnityEngine;
namespace MassEngine {
 /// <summary>Opt-in dynamic-enemy approach. Only substitutes a clear cardinal route to a REAL target in the same lane; never makes synthetic goals.</summary>
 public static class TerrainLaneApproach33 {
  public static void Apply(TerrainNavigationGrid nav,IReadOnlyList<Vector2> goals,Vector2[] directions,float stopRadius){
   if(goals==null||goals.Count==0)return;
   var rows=new List<int>[nav.ResolutionZ];var columns=new List<int>[nav.ResolutionX];Vector2 mean=Vector2.zero;int total=0;
   foreach(var p in goals){if(!nav.TryGetCell(p,out int cell)||!nav.IsWalkable(p))continue;int x=cell%nav.ResolutionX,z=cell/nav.ResolutionX;if(rows[z]==null)rows[z]=new List<int>();if(columns[x]==null)columns[x]=new List<int>();rows[z].Add(cell);columns[x].Add(cell);mean+=nav.CellCenter(cell);total++;}
   if(total==0)return;mean/=total;foreach(var list in rows)list?.Sort();foreach(var list in columns)list?.Sort();
   float near=Mathf.Max(nav.CellSize*4,stopRadius+nav.CellSize);
   for(int cell=0;cell<directions.Length;cell++){
    Vector2 original=directions[cell];if(original.sqrMagnitude<.0001f)continue; // Never override stop/unreachable.
    // Only skip valid cells: retain CellCenter exceptions for active excess entries.
    if(cell<nav.CellCount && rows[cell/nav.ResolutionX]==null && columns[cell%nav.ResolutionX]==null)continue;
    Vector2 here=nav.CellCenter(cell),toward=mean-here;bool horizontal=Mathf.Abs(toward.x)>=Mathf.Abs(toward.y);int sign=(horizontal?toward.x:toward.y)>=0?1:-1;
    Vector2 proposed=horizontal?new Vector2(sign,0):new Vector2(0,sign);
    // Both possible outcomes already have these exact bits. Do not conflate signed zero.
    if(BitConverter.SingleToInt32Bits(original.x)==BitConverter.SingleToInt32Bits(proposed.x) &&
       BitConverter.SingleToInt32Bits(original.y)==BitConverter.SingleToInt32Bits(proposed.y))continue;
    if(Vector2.Dot(proposed,original)<.5f)continue;
    var list=horizontal?rows[cell/nav.ResolutionX]:columns[cell%nav.ResolutionX];if(list==null)continue;int at=list.BinarySearch(cell);if(at<0)at=~at;else continue;int pick=sign>0?at:at-1;if(pick<0||pick>=list.Count)continue;int target=list[pick];
    if(Vector2.Distance(here,nav.CellCenter(target))<near||!nav.HasClearCardinalRoute33(cell,target))continue;
    directions[cell]=proposed;
   }
  }
 }
}

