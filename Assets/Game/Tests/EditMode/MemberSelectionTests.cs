using System;
using NUnit.Framework;
using UnityEngine;
namespace MassEngine.Game.Tests
{
    public sealed class MemberSelectionTests
    {
        [Test] public void PendingDoesNotExposePreviousMembers()
        {var s=new MemberSelectionState(1,0);var a=s.Begin();Assert.True(s.Apply(a,new[]{1,4},9));var b=s.Begin();Assert.AreEqual(MemberSelectionScope.Pending,s.Scope);Assert.False(s.TrySnapshot(out var ids,out _));Assert.IsEmpty(ids);Assert.False(s.Apply(a,new[]{2},9));Assert.True(s.Apply(b,new[]{3},9));}
        [Test] public void EmptyNeverMeansWholeArmy()
        {var s=new MemberSelectionState(1,0);s.Apply(s.Begin(),Array.Empty<int>(),3);Assert.AreEqual(MemberSelectionScope.Empty,s.Scope);Assert.False(s.TrySnapshot(out _,out _));}
        [TestCase(2,0,1)][TestCase(1,1,1)][TestCase(1,0,0)][TestCase(1,0,2)]
        public void RejectDifferentIdentity(long epoch,int team,long request)
        {var s=new MemberSelectionState(1,0);s.Begin();Assert.False(s.Apply(new MemberSelectionState.Ticket(epoch,team,request),new[]{1},5));}
        [Test] public void TeamSwitchClearsOnlySelection()
        {var s=new MemberSelectionState(1,0);var old=s.Begin();s.Clear(2);Assert.AreEqual(MemberSelectionScope.WholeArmy,s.Scope);Assert.False(s.Apply(old,new[]{0},1));Assert.AreEqual(2,s.Team);}
        [Test] public void SnapshotsAreImmutableAndVersioned()
        {var s=new MemberSelectionState(1,0);var ids=new[]{1,2};s.Apply(s.Begin(),ids,9);ids[0]=6;Assert.True(s.TrySnapshot(out var a,out int v));a[0]=8;s.TrySnapshot(out var b,out int same);Assert.AreEqual(1,b[0]);Assert.AreEqual(v,same);s.Apply(s.Current,new[]{2},8);s.TrySnapshot(out _,out int next);Assert.Greater(next,v);}
        [Test] public void InvalidationRejectsOutstandingResult()
        {var s=new MemberSelectionState(5,1);var t=s.Begin();s.Invalidate("reset");Assert.False(s.Apply(t,new[]{0},1));Assert.AreEqual(MemberSelectionScope.Unavailable,s.Scope);}
        [TestCase(-1,2)][TestCase(2,2)][TestCase(3,1)] public void RejectInvalidIndices(int a,int b)
        {var s=new MemberSelectionState(1,0);var t=s.Begin();Assert.Throws<ArgumentException>(()=>s.Apply(t,new[]{a,b},4));}
        [Test] public void RejectImpossibleCounts()
        {var s=new MemberSelectionState(1,0);var t=s.Begin();Assert.Throws<ArgumentException>(()=>s.Apply(t,new[]{0,1},1));}
        [TestCase(false)][TestCase(true)] public void CapturedProjectionSurvivesCameraMotion(bool orthographic)
        {
            var go=new GameObject("projection");try{var cam=go.AddComponent<Camera>();cam.enabled=false;cam.orthographic=orthographic;cam.orthographicSize=10;cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.pixelRect=new Rect(30,20,200,100);go.transform.position=new Vector3(0,10,-10);go.transform.LookAt(Vector3.zero);
            var snap=new MemberSelectionProjection(cam,new Vector2(230,120),new Vector2(30,20));Assert.True(snap.Contains(Vector3.zero));Assert.False(snap.Contains(go.transform.position-go.transform.forward*2));Assert.False(snap.Contains(go.transform.position+go.transform.forward*200));go.transform.position+=Vector3.right*100;Assert.True(snap.Contains(Vector3.zero));
            var empty=new MemberSelectionProjection(cam,new Vector2(50,50),new Vector2(50,50));Assert.False(empty.Contains(Vector3.zero));}finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
