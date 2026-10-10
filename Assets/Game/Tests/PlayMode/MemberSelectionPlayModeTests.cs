using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace MassEngine.Game.Tests
{
    public sealed class MemberSelectionPlayModeTests
    {
        Scene scene;MassEngineManager m;WarSandboxBattleController c;WarSandboxCommandHUD hud;Camera cam;GpuMemberSelection selection;
        float oldDelta;bool started;string output;
        IEnumerator Frames(int n){for(int i=0;i<n;i++)yield return null;}
        [UnitySetUp] public IEnumerator Setup()
        {
            var a=Environment.GetCommandLineArgs().FirstOrDefault(x=>(x.StartsWith("--interaction-p7-output=")||x.StartsWith("--interaction-p8-output=")));if(a==null)Assert.Ignore("P7 owned opt-in required");output=a.Substring("--interaction-p7-output=".Length);
            oldDelta=Time.captureDeltaTime;started=true;Time.captureDeltaTime=1f/60;
            Assert.IsNull(WarSandboxSceneSession.Instance);yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Green.unity",LoadSceneMode.Additive);scene=SceneManager.GetSceneByPath("Assets/Game/Scenes/Green.unity");yield return Frames(3);
            m=WarSandboxSceneSession.FindManager(scene,out _);c=WarSandboxRuntimeBootstrap.EnsureControls(m);hud=c.GetComponent<WarSandboxCommandHUD>();c.RebuildArmyStates();Assert.True(c.IssueOrder(ArmyOrder.Hold(0)));Assert.True(c.IssueOrder(ArmyOrder.Hold(1)));yield return Frames(20);
            var go=new GameObject("P7 Test camera");SceneManager.MoveGameObjectToScene(go,scene);cam=go.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=10;cam.nearClipPlane=.1f;cam.farClipPlane=100;cam.pixelRect=new Rect(0,0,256,256);cam.transform.position=new Vector3(0,20,0);cam.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {if(!started)yield break;hud?.CloseSelectionPreview();selection?.Dispose();selection=null;if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);Time.captureDeltaTime=oldDelta;Time.timeScale=1;yield return null;}
        void SyntheticGpuPopulation()
        {
            m.enableGpuDispatch=false;int n=m.Buffers.AgentCount;var agents=new AgentData[n];var hp=new int[n];var teams=new int[n];var positions=new Vector2[n];
            Vector3[] p={new Vector3(-4,0,-4),new Vector3(4,0,-4),new Vector3(-4,0,4),new Vector3(4,0,4),Vector3.zero,new Vector3(0,30,0),new Vector3(0,-500,0),Vector3.zero};
            for(int i=0;i<n;i++){agents[i].scale=Vector3.one;agents[i].position=i<p.Length?p[i]:new Vector3(500,0,500);positions[i]=new Vector2(agents[i].position.x,agents[i].position.z);teams[i]=i==1||i==3?1:0;hp[i]=i<p.Length&&i!=4?100:0;}
            m.Buffers.agentBuffer.SetData(agents);m.Buffers.agentPositionReadBuffer.SetData(positions);m.Buffers.agentPositionWriteBuffer.SetData(positions);m.Buffers.combatBuffers.teamIdBuffer.SetData(teams);m.Buffers.combatBuffers.hpReadBuffer.SetData(hp);m.Buffers.combatBuffers.hpWriteBuffer.SetData(hp);
        }
        MemberSelectionProjection Full()=>new MemberSelectionProjection(cam,Vector2.zero,new Vector2(256,256));
        IEnumerator Ready(GpuMemberSelection q)
        {
            double end=Time.realtimeSinceStartupAsDouble+12;
            do{q.Tick();if(!q.Busy&&q.State.Scope!=MemberSelectionScope.Pending)break;yield return null;}while(Time.realtimeSinceStartupAsDouble<end);
            Assert.False(q.Busy,"GPU confirmation deadline");Assert.AreNotEqual(MemberSelectionScope.Unavailable,q.State.Scope,q.State.Error);
        }
        int[] Members(GpuMemberSelection q){Assert.True(q.TrySnapshot(out var ids,out _));return ids;}
        void Evidence(string name,string value){File.WriteAllText(Path.Combine(output,name+".txt"),value);}
        [UnityTest] public IEnumerator P7GpuProjectionTeamLifeAndCapturedCamera()
        {
            SyntheticGpuPopulation();selection=new GpuMemberSelection(m,0);var projection=Full();selection.Request(projection);cam.transform.position+=Vector3.right*100;yield return Ready(selection);
            CollectionAssert.AreEqual(new[]{0,2,7},Members(selection));Assert.AreEqual(5,selection.State.Alive);
            Evidence("p7-projection","Interleaved team0/team1 in actual GPU buffers; dead, behind-camera, beyond-far rejected; captured matrix survives camera move before dispatch; indices=0,2,7; team alive=5. Controlled fixture, not authored-roster gameplay performance.");
        }
        [UnityTest] public IEnumerator P7NewestRequestSwitchAndResetDiscardOldReadbacks()
        {
            SyntheticGpuPopulation();selection=new GpuMemberSelection(m,0);long epoch=selection.State.Epoch;selection.Request(Full());selection.Tick();
            selection.Request(new MemberSelectionProjection(cam,new Vector2(240,240),new Vector2(256,256)));selection.Request(new MemberSelectionProjection(cam,Vector2.zero,new Vector2(120,256)));yield return Ready(selection);CollectionAssert.AreEqual(new[]{0,2},Members(selection));
            selection.Request(Full());selection.Tick();selection.Clear(1);yield return Ready(selection);Assert.AreEqual(MemberSelectionScope.WholeArmy,selection.State.Scope);Assert.AreEqual(2,selection.State.Alive);Assert.False(selection.TrySnapshot(out _,out _));
            selection.Request(Full());selection.Tick();m.ResetScenario();selection.Tick();Assert.False(selection.IsCurrent);Assert.AreEqual(MemberSelectionScope.Unavailable,selection.State.Scope);selection.Dispose();selection=new GpuMemberSelection(m,0);Assert.AreNotEqual(epoch,selection.State.Epoch);
            Evidence("p7-generation","Latest queued rectangle wins; team switch clears selection and discards old request; reset invalidates old buffer identity and uses a new epoch. No stale result accepted.");
        }
        [UnityTest] public IEnumerator P7DeathPrunesWithoutSelectingNewArrivalsAndEmptyIsExplicit()
        {
            SyntheticGpuPopulation();selection=new GpuMemberSelection(m,0);selection.Request(Full());yield return Ready(selection);int version=selection.State.Version;
            var hp=new int[m.Buffers.AgentCount];m.Buffers.combatBuffers.hpReadBuffer.GetData(hp);hp[0]=0;m.Buffers.combatBuffers.hpReadBuffer.SetData(hp);
            yield return new WaitForSecondsRealtime(.3f);selection.Tick();yield return Ready(selection);CollectionAssert.AreEqual(new[]{2,7},Members(selection));Assert.Greater(selection.State.Version,version);
            hp[0]=100;m.Buffers.combatBuffers.hpReadBuffer.SetData(hp);yield return new WaitForSecondsRealtime(.3f);selection.Tick();yield return Ready(selection);CollectionAssert.AreEqual(new[]{2,7},Members(selection));
            selection.Request(new MemberSelectionProjection(cam,new Vector2(240,240),new Vector2(256,256)));yield return Ready(selection);Assert.AreEqual(MemberSelectionScope.Empty,selection.State.Scope);Assert.False(selection.TrySnapshot(out _,out _));
            int before=selection.ReadbackPairs;double until=Time.realtimeSinceStartupAsDouble+1.1;while(Time.realtimeSinceStartupAsDouble<until){selection.Tick();yield return null;}int pairs=selection.ReadbackPairs-before;Assert.LessOrEqual(pairs,5,"Liveness readbacks throttled, not per-frame");
            Evidence("p7-liveness-cost","Observed "+pairs+" async mask/count pairs in 1.1s; each pair="+(m.Buffers.AgentCount*4+8)+" logical bytes; no synchronous agent data readback in production selector.");
            Evidence("p7-liveness","Dead member removed and version advanced; liveness refresh does not add revived/newly-entered members; empty rectangle produces Empty, never WholeArmy.");
        }
        [UnityTest] public IEnumerator P7BatchedRingsRenderFromLiveGpuPositions()
        {
            SyntheticGpuPopulation();selection=new GpuMemberSelection(m,0);selection.Request(Full());yield return Ready(selection);
            var rt=new RenderTexture(256,256,24);var texture=new Texture2D(256,256,TextureFormat.RGB24,false);rt.Create();cam.targetTexture=rt;cam.rect=new Rect(0,0,1,1);cam.aspect=1;cam.cullingMask=1<<30;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;cam.enabled=true;
            try
            {
                for(int i=0;i<5;i++){selection.Draw(cam,30);yield return null;}
                var old=RenderTexture.active;RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();RenderTexture.active=old;
                int cyan=texture.GetPixels32().Count(p=>p.g>80&&p.b>70&&p.r<p.g*.6f);Assert.Greater(cyan,10,"Actual ring pixels, not just an issued Draw call");
                File.WriteAllBytes(Path.Combine(output,"p7-gpu-rings.png"),texture.EncodeToPNG());
                var agents=new AgentData[m.Buffers.AgentCount];m.Buffers.agentBuffer.GetData(agents);agents[0].position=new Vector3(4,0,4);m.Buffers.agentBuffer.SetData(agents);
                for(int i=0;i<5;i++){selection.Draw(cam,30);yield return null;}
                old=RenderTexture.active;RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,256,256),0,0);texture.Apply();RenderTexture.active=old;
                int moved=0;for(int y=168;y<191;y++)for(int x=168;x<191;x++){var pixel=texture.GetPixel(x,y);if(pixel.g>.3f&&pixel.b>.25f&&pixel.r<pixel.g*.6f)moved++;}File.WriteAllBytes(Path.Combine(output,"p7-gpu-rings-moved.png"),texture.EncodeToPNG());Assert.Greater(moved,3,"Ring follows GPU agent position without reselection");
                Assert.Greater(selection.DrawCalls,0);Assert.AreEqual(m.Buffers.AgentCount*8L+28,selection.LogicalGpuBytes);
                Evidence("p7-render","Automatic camera to isolated RenderTexture; actual cyan ring pixels="+cyan+"; no per-member GameObjects; logical GPU bytes="+selection.LogicalGpuBytes+"; no FPS claim.");
            }
            finally{cam.targetTexture=null;cam.enabled=false;UnityEngine.Object.Destroy(texture);rt.Release();UnityEngine.Object.Destroy(rt);}
        }
        [UnityTest] public IEnumerator P7PointerCaptureUiCameraFocusAndTeamCancellation()
        {
            SyntheticGpuPopulation();hud.commandCamera=cam;Assert.True(hud.BeginSelectionPreviewForDevelopment(out var error),error);hud.TestSelectionPointerP7(new Vector2(20,20),false,false,false,true);
            hud.TestSelectionPointerP7(new Vector2(10,10),true,true,false,true);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));
            hud.TestSelectionPointerP7(new Vector2(10,10),true,true,false,false,true);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));
            hud.TestSelectionPointerP7(new Vector2(10,10),true,true,false);Assert.True(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));hud.TestFocus26(false);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));
            hud.TestSelectionPointerP7(new Vector2(1,1),true,true,false);hud.TestSelectionPointerP7(new Vector2(255,255),false,false,true);yield return Ready(hud.SelectionPreview);CollectionAssert.AreEqual(new[]{0,2,7},Members(hud.SelectionPreview));
            hud.TestSelectionPointerP7(new Vector2(10,10),true,true,false);c.SelectArmy(1);yield return Frames(2);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));Assert.AreEqual(1,hud.SelectionPreview.State.Team);
            hud.TestSelectionPointerP7(new Vector2(1,1),true,true,false);hud.TestSelectionPointerP7(new Vector2(-1,100),false,true,false);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));
            hud.TestSelectionPointerP7(new Vector2(1,1),true,true,false);cam.pixelRect=new Rect(0,0,200,200);hud.TestSelectionPointerP7(new Vector2(150,150),false,false,true);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));
            cam.pixelRect=new Rect(0,0,256,256);hud.TestSelectionPointerP7(new Vector2(1,1),true,true,false);
            typeof(WarSandboxCommandHUD).GetField("helpOpen",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(hud,true);yield return Frames(2);Assert.False(WarSandboxCommandHUD.IsSelectionCameraCaptured(cam));Assert.AreEqual(MemberSelectionScope.WholeArmy,hud.SelectionPreview.State.Scope);
            typeof(WarSandboxCommandHUD).GetField("helpOpen",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(hud,false);
            hud.CloseSelectionPreview();Assert.False(hud.SelectionPreviewActive);Evidence("p7-input","Production pointer handler driven with explicit synthetic frames: UI and camera priority, focus loss, mouse-up projection, team switch, leaving/resizing viewport and help-open cancellation. Not OS input acceptance.");
        }
        [UnityTest] public IEnumerator P7SelectionKeepsHistoricalCommandsAndBlocksLegacyPlayerOrders()
        {
            Assert.True(m.TryEnableLocalOrderPrototype(out var error),error);var local=m.LocalOrders;
            foreach(int i in new[]{0,1})
            {var receipt=local.Submit(local.Epoch,0,i+1,new[]{i},LocalOrderKind.Hold,Vector2.zero);double until=Time.realtimeSinceStartupAsDouble+12;while(receipt.Status==LocalOrderStatus.AwaitingSnapshot&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.That(receipt.Status,Is.EqualTo(LocalOrderStatus.Committed).Or.EqualTo(LocalOrderStatus.GpuExecuting),receipt.Error);}
            int seq0=local.OrderAt(0).sequence,seq1=local.OrderAt(1).sequence;Assert.AreNotEqual(seq0,seq1);
            cam.orthographicSize=500;cam.farClipPlane=2000;cam.transform.position=new Vector3(0,800,0);hud.commandCamera=cam;
            Assert.True(hud.BeginSelectionPreviewForDevelopment(out error),error);hud.TestSelectionPointerP7(new Vector2(1,1),false,false,false,true);
            hud.SelectionPreview.Request(Full());yield return Ready(hud.SelectionPreview);Assert.Greater(hud.SelectionPreview.State.Count,2);StringAssert.Contains("混合命令",hud.TestSelectionStatusP7());
            yield return new WaitForSecondsRealtime(.2f);
            var buttons=hud.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            foreach(string key in new[]{"attack","move","hold","retreat"})
            {var button=buttons.Single(b=>b.gameObject.name==key);Assert.False(button.interactable,key+" visibly disabled");button.onClick.Invoke();}
            Assert.False(hud.TestMove25(new Vector3(20,0,20)));Assert.False(hud.TestMove25(new Vector3(20,0,20),true));
            foreach(string method in new[]{"IssueAttack","IssueHold","IssueRetreat","BeginMoveOrder"})typeof(WarSandboxCommandHUD).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(hud,null);
            Assert.False(hud.AwaitingMove25);Assert.AreEqual(seq0,local.OrderAt(0).sequence);Assert.AreEqual(seq1,local.OrderAt(1).sequence);
            hud.TestSelectionPointerP7(new Vector2(20,20),false,false,false,escape:true);c.SelectArmy(1);yield return Frames(2);Assert.AreEqual(seq0,local.OrderAt(0).sequence);Assert.AreEqual(seq1,local.OrderAt(1).sequence);
            // Explicit developer bridge only, not a P8 player order entry. Count/version/IDs must reach the P6 receipt unchanged.
            c.SelectArmy(0);yield return Frames(2);hud.SelectionPreview.Request(Full());yield return Ready(hud.SelectionPreview);
            Assert.True(hud.SelectionPreview.TrySnapshot(out var snapshot,out int memberVersion));int selectedCount=hud.SelectionPreview.State.Count;Assert.AreEqual(selectedCount,snapshot.Length);
            var expected=(int[])snapshot.Clone();var bridged=local.Submit(local.Epoch,0,memberVersion,snapshot,LocalOrderKind.Hold,Vector2.zero);snapshot[0]=-1;
            double deadline=Time.realtimeSinceStartupAsDouble+12;while((bridged.Status==LocalOrderStatus.AwaitingSnapshot||bridged.GpuExecutingMembers<selectedCount)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.AreEqual(LocalOrderStatus.GpuExecuting,bridged.Status,bridged.Error);Assert.AreEqual(memberVersion,bridged.MemberVersion);CollectionAssert.AreEqual(expected,bridged.MemberSnapshot);Assert.AreEqual(selectedCount,bridged.SubmittedMembers);Assert.AreEqual(selectedCount,bridged.GpuExecutingMembers);
            hud.TestSelectionPointerP7(new Vector2(20,20),false,false,false,escape:true);Assert.AreEqual(bridged.Sequence,local.OrderAt(expected[0]).sequence);
            Evidence("p7-selection-command-snapshot","Selected="+selectedCount+"; receipt snapshot="+bridged.MemberSnapshot.Count+"; submitted="+bridged.SubmittedMembers+"; GPU executing="+bridged.GpuExecutingMembers+"; memberVersion="+memberVersion+"; immutable caller copy; clearing selection preserves receipt. Explicit developer bridge, no P8 player entry.");
            hud.CloseSelectionPreview();Assert.True(c.IssueOrder(ArmyOrder.Hold(0)));Assert.AreEqual(0,local.ActiveGroups);
            Evidence("p7-orders","Two real P6 local Hold receipts; broad live GPU selection displays mixed commands; four real uGUI buttons are disabled and forced onClick invokes remain safe; all five legacy HUD order methods reject during preview (including append/minimap common move path). Esc and team change preserve both sequences. Explicit Controller whole-army API retains original semantics and clears local groups.");
        }
    }
}

