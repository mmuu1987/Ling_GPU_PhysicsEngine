using NUnit.Framework;
using UnityEngine;
namespace MassEngine.Game.Tests
{
 public sealed class PlayerCommandScopeTests
 {
  [TestCase(ArmyOrderType.Move)][TestCase(ArmyOrderType.Attack)][TestCase(ArmyOrderType.Hold)][TestCase(ArmyOrderType.Retreat)]
  public void LocalFourKindsUseFrozenMembers(ArmyOrderType kind)
  {var ids=new[]{1,3};var s=new PlayerCommandScope(MemberSelectionScope.Local,0,2,3,4,ids);ids[0]=9;Assert.True(s.Allows(kind,false,out _));Assert.AreEqual(1,s.CopyMembers()[0]);var copy=s.CopyMembers();copy[0]=8;Assert.AreEqual(1,s.CopyMembers()[0]);}
  [TestCase(MemberSelectionScope.Empty)][TestCase(MemberSelectionScope.Pending)][TestCase(MemberSelectionScope.Unavailable)]
  public void InvalidScopeNeverBecomesWholeArmy(MemberSelectionScope scope)
  {foreach(var kind in new[]{ArmyOrderType.Move,ArmyOrderType.Attack,ArmyOrderType.Hold,ArmyOrderType.Retreat})Assert.False(new PlayerCommandScope(scope,0,2,3,4,null).Allows(kind,false,out _));}
  [Test]public void LocalAppendRefusedWholeAppendRetained()
  {Assert.False(new PlayerCommandScope(MemberSelectionScope.Local,0,2,3,4,new[]{1}).Allows(ArmyOrderType.Move,true,out _));Assert.True(new PlayerCommandScope(MemberSelectionScope.WholeArmy,0,0,0,0,null).Allows(ArmyOrderType.Move,true,out _));}
  [TestCase(0)][TestCase(4097)]public void BadLocalCountRejected(int count)
  {Assert.False(new PlayerCommandScope(MemberSelectionScope.Local,0,1,1,1,new int[count]).Allows(ArmyOrderType.Hold,false,out _));}
  [TestCase(1,0,1,1)][TestCase(0,1,1,1)][TestCase(0,0,2,1)][TestCase(0,0,1,2)]
  public void ChangedIntentRejected(int team,long epoch,long request,int version)
  {var a=new PlayerCommandScope(MemberSelectionScope.Local,0,0,1,1,new[]{1});var b=new PlayerCommandScope(MemberSelectionScope.Local,team,epoch,request,version,new[]{1});Assert.False(a.SameIntent(b));}
  TerrainNavigationGrid Nav(float radius,bool wall=false)
  {var blocked=new bool[256];if(wall)for(int z=0;z<16;z++)blocked[z*16+7]=true;var surface=new TerrainSurface("P8",1,17,17,new Vector2(-8,-8),new Vector2(16,16),45,new float[289],blocked);return new TerrainNavigationGrid(surface,new Vector2(-8,-8),1,16,16,radius,.1f);}
  [Test]public void AttackUsesReachableEnemiesPerComponentNotNamedVictim()
  {var positions=new[]{new Vector2(-4.5f,.5f),new Vector2(3.5f,.5f),new Vector2(-5.5f,.5f),new Vector2(4.5f,.5f)};Assert.True(LocalOrderPlan.TryCreate(0,new[]{0,1},new[]{0,0,1,1},new[]{.1f,.1f,.1f,.1f},positions,new[]{1,1,1,1},LocalOrderKind.Attack,Vector2.zero,r=>Nav(r,true),out var plan,out var error),error);Assert.AreEqual(2,plan.Members.Length);Assert.AreEqual(256,plan.Directions.Length);Assert.False(LocalOrderPlan.TryCreate(0,new[]{0,1},new[]{0,0,1,1},new[]{.1f,.1f,.1f,.1f},positions,new[]{1,1,0,1},LocalOrderKind.Attack,Vector2.zero,r=>Nav(r,true),out _,out _));}
  [Test]public void RetreatUsesExistingAnchorButRequiresCurrentReachability()
  {var p=new[]{new Vector2(-4.5f,.5f)};Assert.True(LocalOrderPlan.TryCreate(0,new[]{0},new[]{0},new[]{.1f},p,new[]{1},LocalOrderKind.Retreat,new Vector2(-5.5f,.5f),r=>Nav(r,true),out var plan,out var error),error);Assert.AreEqual(new Vector2(-5.5f,.5f),plan.ResolvedTarget);Assert.False(LocalOrderPlan.TryCreate(0,new[]{0},new[]{0},new[]{.1f},p,new[]{1},LocalOrderKind.Retreat,new Vector2(4.5f,.5f),r=>Nav(r,true),out _,out _));}
  [Test]public void NoArmyOrUnknownKindRejected()
  {Assert.False(new PlayerCommandScope(MemberSelectionScope.WholeArmy,-1,0,0,0,null).Allows(ArmyOrderType.Move,false,out _));Assert.False(new PlayerCommandScope(MemberSelectionScope.WholeArmy,0,0,0,0,null).Allows(ArmyOrderType.None,false,out _));}
 }
}
