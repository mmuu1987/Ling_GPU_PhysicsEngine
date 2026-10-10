using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
namespace MassEngine.Tests
{
    public sealed class LocalOrderPlanTests
    {
        private TerrainSurface Surface(bool wall=false)
        {var blocked=new bool[256];if(wall)for(int z=0;z<16;z++)blocked[z*16+7]=true;return new TerrainSurface("P6",1,17,17,new Vector2(-8,-8),new Vector2(16,16),45,new float[289],blocked);}
        private TerrainNavigationGrid Nav(float r,bool wall=false)=>new TerrainNavigationGrid(Surface(wall),new Vector2(-8,-8),1,16,16,r,.1f);
        private bool Plan(int[] ids,Vector2[] p,int[] hp,out LocalOrderPlan plan,out string error,bool wall=false,float[] radii=null,int[] teams=null)
        =>LocalOrderPlan.TryCreate(0,ids,teams??new[]{0,0,1},radii??new[]{.1f,.1f,4f},p,hp,LocalOrderKind.Move,new Vector2(4.5f,0.5f),r=>Nav(r,wall),out plan,out error);
        [Test] public void StableGpuLayoutsRemainIndependent(){Assert.AreEqual(64,Marshal.SizeOf<AgentData>());Assert.AreEqual(32,Marshal.SizeOf<LocalAgentOrder>());}
        [Test] public void SelectionSnapshotCopiedAndUnselectedLargeRadiusIgnored()
        {var ids=new[]{0};var p=new[]{new Vector2(3.5f,.5f),new Vector2(-4,.5f),Vector2.zero};Assert.IsTrue(Plan(ids,p,new[]{1,1,1},out var plan,out var error,true),error);ids[0]=2;Assert.AreEqual(0,plan.Members[0]);Assert.AreEqual(.1f,plan.Radius);}
        [Test] public void CurrentPositionNotBirthOrWholeArmyControlsReachability()
        {var p=new[]{new Vector2(3.5f,.5f),new Vector2(-4,.5f),Vector2.zero};Assert.IsTrue(Plan(new[]{0},p,new[]{1,1,1},out _,out _,true));Assert.IsFalse(Plan(new[]{0,1},p,new[]{1,1,1},out _,out _,true));p[0]=p[1];Assert.IsFalse(Plan(new[]{0},p,new[]{1,1,1},out _,out _,true));}
        [Test] public void DeadMembersFilteredButAllDeadRejected()
        {var p=new[]{new Vector2(3.5f,.5f),new Vector2(3.5f,2.5f),Vector2.zero};Assert.IsTrue(Plan(new[]{0,1},p,new[]{1,0,1},out var plan,out _));Assert.AreEqual(1,plan.Members.Length);Assert.IsFalse(Plan(new[]{0,1},p,new[]{0,0,1},out _,out _));}
        [TestCase(-1)] [TestCase(3)] [TestCase(2)] public void InvalidOrForeignIndexRejected(int id)
        {Assert.IsFalse(Plan(new[]{id},new Vector2[3],new[]{1,1,1},out _,out _));}
        [Test] public void DuplicateAndEmptyRejected(){Assert.IsFalse(Plan(new[]{0,0},new Vector2[3],new[]{1,1,1},out _,out _));Assert.IsFalse(Plan(new int[0],new Vector2[3],new[]{1,1,1},out _,out _));}
        [Test] public void NonfinitePositionFailsClosed(){Assert.IsFalse(Plan(new[]{0},new[]{new Vector2(float.NaN,0),Vector2.zero,Vector2.zero},new[]{1,1,1},out _,out _));}
        [Test] public void DistributedArrivalsHaveSpaceAndFieldNotOnePerSoldier()
        {var p=new[]{new Vector2(3.5f,.5f),new Vector2(3.5f,2.5f),Vector2.zero};Assert.IsTrue(Plan(new[]{0,1},p,new[]{1,1,1},out var plan,out var error),error);Assert.Greater(Vector2.Distance(plan.Arrivals[0],plan.Arrivals[1]),.2f);Assert.AreEqual(256,plan.Directions.Length);}
        [Test] public void MixedSelectedRadiusUsesLargestSelectedNotWholeTeam()
        {var p=new[]{new Vector2(3.5f,.5f),new Vector2(3.5f,2.5f),Vector2.zero};Assert.IsTrue(Plan(new[]{0,1},p,new[]{1,1,1},out var plan,out _,false,new[]{.1f,.6f,4f}));Assert.AreEqual(.6f,plan.Radius);}
        [Test] public void HoldRequiresLiveCurrentWalkablePosition()
        {Assert.IsFalse(LocalOrderPlan.TryCreate(0,new[]{0},new[]{0},new[]{.1f},new[]{new Vector2(50,0)},new[]{1},LocalOrderKind.Hold,Vector2.zero,r=>Nav(r),out _,out _));}
    }
}
