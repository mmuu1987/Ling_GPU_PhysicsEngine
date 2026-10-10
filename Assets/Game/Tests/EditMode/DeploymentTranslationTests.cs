using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Tests
{
    public sealed class DeploymentTranslationTests
    {
        private static WarSandboxDeploymentDraft Draft() => new WarSandboxDeploymentDraft(new[]{
            new WarSandboxDeploymentEntry{center=new Vector3(-20,3,0),count=100,density=.4f,aspect=2.2f},
            new WarSandboxDeploymentEntry{center=new Vector3(50,3,0),count=80,density=.4f,aspect=2.2f}});
        private readonly Rect map=new Rect(100,20,200,100);
        private readonly Vector2 world=new Vector2(200,100);
        private WarSandboxDeploymentTranslation Begin(WarSandboxDeploymentDraft d)
        {var s=new WarSandboxDeploymentTranslation();Assert.IsTrue(s.Begin(d,0,world,map,new Vector2(183,70),new Vector2(500,300)));return s;}
        [Test] public void PreservesGrabOffsetAndAuthoringHeightWithoutMutation()
        {var d=Draft();var s=Begin(d);var before=d.Snapshot();Assert.IsTrue(s.Move(d,0,world,map,new Vector2(193,60),new Vector2(510,310)));Assert.IsTrue(s.Dragging);Assert.That(Vector3.Distance(new Vector3(-10,3,10),s.PreviewCenter),Is.LessThan(.0001f),"Projection float round-off must stay below 0.1mm");Assert.AreEqual(before[0],d[0]);Assert.AreEqual(before[1],d[1]);Assert.AreEqual(0,d.Revision);Assert.IsFalse(d.CanUndo);}
        [TestCase(0)] [TestCase(5)] [TestCase(6)] public void PhysicalPixelThreshold(int pixels)
        {var d=Draft();var s=Begin(d);s.Move(d,0,world,map,new Vector2(183+pixels,70),new Vector2(500+pixels,300));Assert.AreEqual(pixels>=6,s.Dragging);}
        [Test] public void HundredsOfPreviewMovesNeverWriteDraftOrHistory()
        {var d=Draft();var s=Begin(d);for(int i=0;i<200;i++)Assert.IsTrue(s.Move(d,0,world,map,new Vector2(183+i*.1f,70),new Vector2(500+i,300)));Assert.AreEqual(0,d.Revision);Assert.IsFalse(d.CanUndo);Assert.AreEqual(100,d[0].count);Assert.AreEqual(.4f,d[0].density);Assert.AreEqual(2.2f,d[0].aspect);}
        [Test] public void AnyRevisionIncludingUndoRejectsCapturedIdentity()
        {var d=Draft();var s=Begin(d);var e=d[1];e.count++;d.Set(1,e);d.Undo();Assert.AreEqual(100,d[0].count);Assert.IsFalse(s.Move(d,0,world,map,new Vector2(193,70),new Vector2(520,300)));Assert.IsFalse(s.Active);}
        [Test] public void IdenticalReplacementDraftCannotReuseToken()
        {var d=Draft();var s=Begin(d);Assert.IsFalse(s.IsCurrent(Draft(),0,world,map));}
        [Test] public void SelectionSwitchRejectsEvenWhenEntriesLookTheSame()
        {var d=Draft();var s=Begin(d);Assert.IsFalse(s.IsCurrent(d,1,world,map));}
        [Test] public void LayoutOrWorldChangeRejects()
        {var d=Draft();var s=Begin(d);Assert.IsFalse(s.IsCurrent(d,0,world,new Rect(101,20,200,100)));Assert.IsFalse(s.IsCurrent(d,0,world*2,map));}
        [Test] public void LeavingContentCancelsAndCannotResumeOnReentry()
        {var d=Draft();var s=Begin(d);Assert.IsFalse(s.Move(d,0,world,map,new Vector2(99,70),new Vector2(520,300)));Assert.IsFalse(s.Move(d,0,world,map,new Vector2(190,70),new Vector2(520,300)));Assert.IsFalse(d.CanUndo);}
        [Test] public void CancelClearsDraftReferenceAndDoesNotWrite()
        {var d=Draft();var s=Begin(d);s.Cancel();Assert.IsFalse(s.Active);Assert.IsNull(s.Draft);Assert.IsFalse(d.CanUndo);}
        [TestCase(-1)] [TestCase(2)] public void InvalidIndexRejected(int index)
        {Assert.IsFalse(new WarSandboxDeploymentTranslation().Begin(Draft(),index,world,map,map.center,Vector2.zero));}
        [Test] public void InvalidProjectionOrPointerRejected()
        {var s=new WarSandboxDeploymentTranslation();Assert.IsFalse(s.Begin(Draft(),0,Vector2.zero,map,map.center,Vector2.zero));Assert.IsFalse(s.Begin(Draft(),0,world,new Rect(),map.center,Vector2.zero));Assert.IsFalse(s.Begin(Draft(),0,world,map,new Vector2(float.NaN,0),Vector2.zero));}
    }
    public sealed class DeploymentTranslationCommitTests
    {
        private GameObject root; private MassEngineManager m; private WarSandboxRuntimeDeployment d;
        private ScenarioConfig source; private MassEngineSystemConfig system;private UnitTypeConfig a,b;
        private UnitTypeConfig Unit(int team,float x)
        {var u=ScriptableObject.CreateInstance<UnitTypeConfig>();u.unitTypeName="P4 test";u.teamId=team;u.spawnConfig=ScriptableObject.CreateInstance<SpawnConfig>();u.spawnConfig.unitCount=100;u.spawnConfig.spawnCenter=new Vector3(x,0,0);return u;}
        [SetUp] public void Setup()
        {
            a=Unit(0,-40);b=Unit(1,40);source=ScriptableObject.CreateInstance<ScenarioConfig>();source.unitTypes=new[]{a,b};
            root=new GameObject("P4 isolated draft");root.SetActive(false);m=root.AddComponent<MassEngineManager>();m.scenarioConfig=source;
            system=ScriptableObject.CreateInstance<MassEngineSystemConfig>();system.simulationConfig=ScriptableObject.CreateInstance<SimulationConfig>();system.simulationConfig.simulationWorldSize=new Vector2(200,200);system.runtimeFlowConfig=ScriptableObject.CreateInstance<RuntimeFlowConfig>();m.systemConfig=system;
            var c=root.AddComponent<WarSandboxBattleController>();c.manager=m;c.RebuildArmyStates();d=root.AddComponent<WarSandboxRuntimeDeployment>();d.controller=c;
            Assert.IsTrue(d.TryBeginEdit(false,out var error),error);
        }
        [TearDown] public void Cleanup()
        {Object.DestroyImmediate(root);foreach(var u in new[]{a,b}){Object.DestroyImmediate(u.spawnConfig);Object.DestroyImmediate(u);}Object.DestroyImmediate(source);Object.DestroyImmediate(system.simulationConfig);Object.DestroyImmediate(system.runtimeFlowConfig);Object.DestroyImmediate(system);Time.timeScale=1;}
        [Test] public void ValidMoveCommitsOnceAndUndoRedoPreserveEverythingElse()
        {var draft=d.Draft;var e=draft[0];var other=draft[1];var buffer=m.Buffers;int rev=draft.Revision;Assert.IsTrue(d.TryTranslateDraft(draft,rev,0,e,e.center+new Vector3(-5,99,0),out var error),error);Assert.AreEqual(rev+1,draft.Revision);var expected=e;expected.center.x-=5;Assert.AreEqual(expected,draft[0]);Assert.AreEqual(other,draft[1]);Assert.AreSame(buffer,m.Buffers);Assert.AreEqual(e.center,a.spawnConfig.spawnCenter);Assert.IsTrue(draft.Undo());Assert.AreEqual(e,draft[0]);Assert.IsFalse(draft.CanUndo);Assert.IsTrue(draft.Redo());Assert.AreEqual(expected,draft[0]);}
        [Test] public void BoundsViolationKeepsDraftHistoryAndGpu()
        {var draft=d.Draft;var e=draft[0];var buffer=m.Buffers;Assert.IsFalse(d.TryTranslateDraft(draft,draft.Revision,0,e,new Vector3(99,0,0),out var error));Assert.IsNotEmpty(error);Assert.AreEqual(e,draft[0]);Assert.IsFalse(draft.CanUndo);Assert.AreSame(buffer,m.Buffers);}
        [Test] public void OtherFormationOverlapRejected()
        {var draft=d.Draft;var e=draft[0];Assert.IsFalse(d.TryTranslateDraft(draft,draft.Revision,0,e,draft[1].center,out var error));StringAssert.Contains("重叠",error);Assert.IsFalse(draft.CanUndo);}
        [Test] public void StaleRevisionCannotMoveAnotherEntryAfterRemoval()
        {var draft=d.Draft;var e=draft[0];int rev=draft.Revision;draft.Remove(0);var remaining=draft[0];Assert.IsFalse(d.TryTranslateDraft(draft,rev,0,e,new Vector3(-60,0,0),out _));Assert.AreEqual(remaining,draft[0]);}
        [Test] public void NoOpHasNoUndoAndNaNIsRejected()
        {var draft=d.Draft;var e=draft[0];Assert.IsTrue(d.TryTranslateDraft(draft,draft.Revision,0,e,e.center,out _));Assert.IsFalse(draft.CanUndo);Assert.IsFalse(d.TryTranslateDraft(draft,draft.Revision,0,e,new Vector3(float.NaN,0,0),out _));Assert.IsFalse(draft.CanUndo);}
        [Test] public void ClosedDraftRejectsGesture()
        {var draft=d.Draft;var e=draft[0];d.CancelEditing();Assert.IsFalse(d.TryTranslateDraft(draft,draft.Revision,0,e,e.center+Vector3.right,out _));}
    }
}
