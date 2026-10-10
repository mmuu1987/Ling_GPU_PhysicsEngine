using NUnit.Framework;
using UnityEngine;
namespace MassEngine.Game.Tests
{
    public sealed class DeploymentResizeTests
    {
        private WarSandboxDeploymentEntry E(int n=1024)=>new WarSandboxDeploymentEntry{count=n,center=new Vector3(-80,3,0),density=.4f,aspect=2.2f};
        [Test] public void Sample1024IsDepth34Front75AndRoundTrips()
        {var e=E();Assert.That(e.Size.x,Is.EqualTo(34.1121146).Within(.001));Assert.That(e.Size.z,Is.EqualTo(75.04665).Within(.001));Assert.IsTrue(WarSandboxDeploymentResize.TryFromSize(e,e.center,new Vector2(e.Size.x,e.Size.z),out var n,out var error),error);Assert.That(n.density,Is.EqualTo(.4f).Within(.000001));Assert.That(n.aspect,Is.EqualTo(2.2f).Within(.000001));Assert.AreEqual(Vector3.zero,n.manualSize);}
        [TestCase(1)] [TestCase(2)] [TestCase(4)] [TestCase(8)] [TestCase(5)] [TestCase(6)] [TestCase(9)] [TestCase(10)]
        public void AllEdgesCornersAnchorOppositeAndKeepNHeight(int h)
        {var e=E();int x=(h&1)!=0?-1:(h&2)!=0?1:0,z=(h&4)!=0?-1:(h&8)!=0?1:0;
         Assert.IsTrue(WarSandboxDeploymentResize.TryResize(e,(DeploymentResizeHandle)h,new Vector2(x*12,z*16),false,.55f,.08f,out var n,out var error),error);
         Assert.That(n.Size.x,Is.EqualTo(e.Size.x+(x!=0?12:0)).Within(.001));Assert.That(n.Size.z,Is.EqualTo(e.Size.z+(z!=0?16:0)).Within(.001));
         Assert.That(n.center.x-x*n.Size.x/2,Is.EqualTo(e.center.x-x*e.Size.x/2).Within(.001));Assert.That(n.center.z-z*n.Size.z/2,Is.EqualTo(e.center.z-z*e.Size.z/2).Within(.001));Assert.AreEqual(e.count,n.count);Assert.AreEqual(3,n.center.y);}
        [TestCase(1)] [TestCase(2)] [TestCase(4)] [TestCase(8)] [TestCase(5)] [TestCase(6)] [TestCase(9)] [TestCase(10)]
        public void ShiftMaintainsInitialAspect(int h)
        {var e=E();Assert.IsTrue(WarSandboxDeploymentResize.TryResize(e,(DeploymentResizeHandle)h,new Vector2(12,16),true,.55f,.08f,out var n,out var error),error);Assert.That(n.aspect,Is.EqualTo(e.aspect).Within(.00001));}
        [TestCase(-10000)] [TestCase(10000)] public void ExtremeDragHasPositiveRepresentableActualShape(float amount)
        {var e=E();Assert.IsTrue(WarSandboxDeploymentResize.TryResize(e,DeploymentResizeHandle.MaxX|DeploymentResizeHandle.MaxZ,Vector2.one*amount,false,.55f,.08f,out var n,out var error),error);Assert.That(n.Size.x,Is.GreaterThan(0));Assert.That(n.density,Is.InRange(.05f,1.5f));Assert.That(n.aspect,Is.InRange(.1f,10));if(amount<0)Assert.IsTrue(WarSandboxDeploymentResize.Fits(n,.55f,.08f,out error),error);}
        [Test] public void LegacyManualOnlyChangesAfterExplicitConversionAndUndoRestoresExactRecord()
        {var e=E();e.manualSize=new Vector3(40,7,80);e.density=.7f;e.aspect=.5f;var draft=new WarSandboxDeploymentDraft(new[]{e});Assert.AreEqual(e,draft[0]);Assert.IsTrue(WarSandboxDeploymentResize.TryResize(e,DeploymentResizeHandle.MaxX,new Vector2(10,0),false,.55f,.08f,out var n,out _));Assert.AreEqual(Vector3.zero,n.manualSize);Assert.That(n.density,Is.EqualTo(1024f/4000).Within(.000001));draft.Set(0,n);draft.Undo();Assert.AreEqual(e,draft[0]);draft.Redo();Assert.AreEqual(n,draft[0]);}
        [TestCase(0)] [TestCase(-1)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] public void BadDimensionsRejected(float v)
        {Assert.IsFalse(WarSandboxDeploymentResize.TryFromSize(E(),Vector3.zero,new Vector2(v,10),out _,out _));}
        [Test] public void CountChangeBeforeResizeUsesCurrentN()
        {var e=E(2048);Assert.IsTrue(WarSandboxDeploymentResize.TryResize(e,DeploymentResizeHandle.MaxX,new Vector2(10,0),false,.55f,.08f,out var n,out _));Assert.That(n.density,Is.EqualTo(2048/(n.Size.x*n.Size.z)).Within(.000001));}
        [Test] public void RealLatticeRejectsAreaOnlyFalsePositiveAndKeepsRadius()
        {var e=E();e.density=1.5f;Assert.IsFalse(WarSandboxDeploymentResize.Fits(e,.55f,.08f,out _));e.density=.4f;Assert.IsTrue(WarSandboxDeploymentResize.Fits(e,.55f,.08f,out _));Assert.IsFalse(WarSandboxDeploymentResize.Fits(e,1.1f,.08f,out _));}
        [Test] public void GeneratedLatticeMatchesBoundsAndUsesScaleOne()
        {var e=E(101);e.manualSize=new Vector3(30,0,60);var config=ScriptableObject.CreateInstance<SpawnConfig>();try{config.unitCount=e.count;config.spawnCenter=e.center;config.spawnSize=e.manualSize;var agents=new AgentData[e.count];new DefaultSpawnModule(config).GenerateAgents(agents,0,e.count,0);foreach(var a in agents){Assert.That(a.position.x,Is.InRange(e.Bounds.xMin-.001f,e.Bounds.xMax+.001f));Assert.That(a.position.z,Is.InRange(e.Bounds.yMin-.001f,e.Bounds.yMax+.001f));Assert.AreEqual(Vector3.one,a.scale);}}finally{Object.DestroyImmediate(config);}}
    }
    public sealed class DeploymentResizeCommitTests
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
        [Test] public void ResizeCommitsOnceAndKeepsOtherEntryAndGpu()
        {var draft=d.Draft;var e=draft[0];var other=draft[1];var gpu=m.Buffers;Assert.IsTrue(WarSandboxDeploymentResize.TryResize(e,DeploymentResizeHandle.MaxX,new Vector2(5,0),false,.45f,.08f,out var n,out var error),error);Assert.IsTrue(d.TryResizeDraft(draft,draft.Revision,0,e,n,out error),error);Assert.AreEqual(1,draft.Revision);Assert.AreEqual(other,draft[1]);Assert.AreSame(gpu,m.Buffers);Assert.IsTrue(draft.Undo());Assert.AreEqual(e,draft[0]);Assert.IsFalse(draft.CanUndo);Assert.IsTrue(draft.Redo());Assert.AreEqual(n,draft[0]);}
        [Test] public void RadiusPackingRejectedEvenWithoutCustomizedRadius()
        {var draft=d.Draft;var e=draft[0];var n=e;n.density=1.5f;Assert.IsFalse(d.TryResizeDraft(draft,0,0,e,n,out var error));StringAssert.Contains("间距",error);Assert.IsFalse(draft.CanUndo);}
        [Test] public void StaleNoOpAndChangedCountCannotBypassToken()
        {var draft=d.Draft;var e=draft[0];var n=e;n.count++;Assert.IsFalse(d.TryResizeDraft(draft,0,0,e,n,out _));Assert.IsTrue(d.TryResizeDraft(draft,0,0,e,e,out _));Assert.IsFalse(draft.CanUndo);n=e;n.center.x--;draft.Set(0,n);draft.Undo();Assert.IsFalse(d.TryResizeDraft(draft,0,0,e,e,out _));}
        [Test] public void FullFootprintAndOverlapStillRejected()
        {var draft=d.Draft;var e=draft[0];var n=e;n.center.x=99;Assert.IsFalse(d.TryResizeDraft(draft,0,0,e,n,out _));n.center=draft[1].center;Assert.IsFalse(d.TryResizeDraft(draft,0,0,e,n,out _));Assert.IsFalse(draft.CanUndo);}
    }
}
