#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Tests
{
    public sealed class DeploymentTranslationPlayModeTests
    {
        private WarSandboxDeploymentMapPointer pressed;private Scene scene;private WarSandboxRuntimeDeployment d;private WarSandboxDeploymentHUD hud;private MassEngineManager m;
        private bool prepared;private float oldDelta;private string output;private Vector2? oldScreen;
        private static FieldInfo Field(string n)=>typeof(WarSandboxDeploymentHUD).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic);
        private static MethodInfo Method(string n)=>typeof(WarSandboxDeploymentHUD).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic);
        private IEnumerator Frames(int n=10){for(int i=0;i<n;i++)yield return null;}
        [UnitySetUp] public IEnumerator Setup()
        {
            var arg=Environment.GetCommandLineArgs().FirstOrDefault(x=>(x.StartsWith("--interaction-p4-output=")||(x.StartsWith("--interaction-p5-output=")||((x.StartsWith("--interaction-p6-output=")||x.StartsWith("--interaction-p8-output="))||x.StartsWith("--interaction-p7-output=")))));
            if(arg==null)Assert.Ignore("Owned P4/P6 runner opt-in required");
            output=Path.Combine(arg.Substring("--interaction-p4-output=".Length),"p4-evidence");Directory.CreateDirectory(output);
            oldDelta=Time.captureDeltaTime;oldScreen=WarSandboxUGUI.ScreenSizeOverride;prepared=true;Time.captureDeltaTime=1f/60;WarSandboxUGUI.ScreenSizeOverride=new Vector2(1280,720);
            Assert.IsNull(WarSandboxSceneSession.Instance);
            yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Green.unity",LoadSceneMode.Additive);scene=SceneManager.GetSceneByPath("Assets/Game/Scenes/Green.unity");yield return Frames(2);
            m=WarSandboxSceneSession.FindManager(scene,out _);Assert.IsNotNull(m);var c=WarSandboxRuntimeBootstrap.EnsureControls(m);c.RebuildArmyStates();m.PauseBattle();
            d=m.GetComponent<WarSandboxRuntimeDeployment>();d.battlefieldCatalog=AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");d.StatStore=new WarSandboxUnitStatStore(Path.Combine(output,"globals.json"));d.PlanStore=new WarSandboxLocalPlanStore(Path.Combine(output,"plans"));hud=m.GetComponent<WarSandboxDeploymentHUD>();
            Assert.IsTrue(d.TryBeginEdit(false,out var error),error);yield return Frames();Canvas.ForceUpdateCanvases();Assert.IsNotNull(Pointer());
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(!prepared)yield break;
            if(hud!=null)Method("CancelTranslation").Invoke(hud,new object[]{null});
            if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            Time.captureDeltaTime=oldDelta;WarSandboxUGUI.ScreenSizeOverride=oldScreen;Time.timeScale=1;yield return null;
        }
        private WarSandboxDeploymentMapPointer Pointer()=>Object.FindObjectsByType<WarSandboxDeploymentMapPointer>(FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy);
        private Vector2 ScreenPoint(Vector3 worldPoint)
        {
            var p=Pointer();var world=d.WorldSize;float u=worldPoint.x/world.x+.5f,v=.5f-worldPoint.z/world.y;
            return RectTransformUtility.WorldToScreenPoint(null,p.Rect.TransformPoint(new Vector3(u*p.Rect.rect.width,-v*p.Rect.rect.height,0)));
        }
        private PointerEventData Event(Vector3 center)=>new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,pointerId=-1,position=ScreenPoint(center)};
        private void Down(PointerEventData e){pressed=Pointer();ExecuteEvents.Execute(pressed.gameObject,e,ExecuteEvents.pointerDownHandler);}
        private void Drag(PointerEventData e){if(pressed!=null)ExecuteEvents.Execute(pressed.gameObject,e,ExecuteEvents.dragHandler);}
        private void Up(PointerEventData e){if(pressed!=null)ExecuteEvents.Execute(pressed.gameObject,e,ExecuteEvents.pointerUpHandler);pressed=null;}
        private Vector3 LegalMove(int index)
        {
            var validate=typeof(WarSandboxRuntimeDeployment).GetMethod("ValidateDraft",BindingFlags.NonPublic|BindingFlags.Instance);
            foreach(var delta in new[]{new Vector3(0,0,24),new Vector3(0,0,-24),new Vector3(-24,0,0),new Vector3(24,0,0)})
            {var entries=d.Draft.Snapshot();entries[index].center+=delta;var candidate=new WarSandboxDeploymentDraft(entries,d.Draft.Rules,d.Draft.Stats);object[] args={candidate,null};if((bool)validate.Invoke(d,args))return entries[index].center;}
            Assert.Fail("No legal local 24m translation fixture");return default;
        }
        private void Evidence(string name,string value){File.WriteAllText(Path.Combine(output,name+".txt"),value);Debug.Log("P4_EVIDENCE "+name+" "+value);}
        private static T[] Active<T>() where T:Component => Object.FindObjectsByType<T>(FindObjectsSortMode.None).Where(x=>x.gameObject.activeInHierarchy).ToArray();
        private void Capture(string name)
        {
            string path = Path.Combine(output, name); Assert.That(File.Exists(path), Is.False, "Do not overwrite evidence.");
            var size = WarSandboxUGUI.ScreenSizeOverride ?? new Vector2(Screen.width, Screen.height); int w = (int)size.x, h = (int)size.y;
            var target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            var oldTarget = RenderTexture.active;
            var camera = new GameObject("Quality PNG UI Camera").AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.Nothing; camera.cullingMask = 1 << 31; camera.targetTexture = target;
            var canvases = Active<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray(); var layers = new Dictionary<GameObject, int>();
            try
            {
                foreach (var real in Active<Camera>().Where(c => c != camera).OrderBy(c => c.depth))
                { var old = real.targetTexture; try { real.targetTexture = target; real.Render(); } finally { real.targetTexture = old; } }
                foreach (var canvas in canvases)
                {
                    foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) { layers[t.gameObject] = t.gameObject.layer; t.gameObject.layer = 31; }
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                Canvas.ForceUpdateCanvases();
                foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value;
                RenderTexture.active = oldTarget; Object.Destroy(texture); Object.Destroy(camera.gameObject); target.Release(); Object.Destroy(target);
            }
        }
        [UnityTest] public IEnumerator DragCommitsOnceUndoRedoAndActualGpuSpawnMatchesDraft()
        {
            var draft=d.Draft;var original=draft[0];var other=draft[1];var buffer=m.Buffers.agentBuffer;int rev=draft.Revision;var target=LegalMove(0);
            // Press off-centre to exercise the grab offset rather than centre snapping.
            Capture("01-before.png");var grab=new Vector3(2,0,2);var e=Event(original.center+grab);Down(e);
            e.position=ScreenPoint(target+grab);for(int i=0;i<80;i++)Drag(e);
            Assert.AreEqual(rev,draft.Revision);Assert.AreEqual(original,draft[0]);Assert.IsFalse(draft.CanUndo);Assert.AreSame(buffer,m.Buffers.agentBuffer);
            Assert.IsNotNull(GameObject.Find("deployment-translation-preview"));Capture("02-drag-preview.png");Up(e);
            Assert.AreEqual(rev+1,draft.Revision);Assert.That(Vector3.Distance(target,draft[0].center),Is.LessThan(.001f));Assert.AreEqual(other,draft[1]);Assert.AreSame(buffer,m.Buffers.agentBuffer);
            Assert.IsTrue(draft.Undo());Assert.AreEqual(original,draft[0]);Assert.IsFalse(draft.CanUndo);Assert.IsTrue(draft.Redo());yield return Frames();Capture("03-committed.png");
            var final=draft[0];
            Assert.IsTrue(d.TryApply(out var error),error);var agents=new AgentData[m.Buffers.AgentCount];m.Buffers.agentBuffer.GetData(agents);
            Assert.AreEqual(original.count+other.count,agents.Length);int matched=0;
            for(int i=0;i<final.count;i++){Assert.That(agents[i].position.x,Is.InRange(final.Bounds.xMin-.01f,final.Bounds.xMax+.01f));Assert.That(agents[i].position.z,Is.InRange(final.Bounds.yMin-.01f,final.Bounds.yMax+.01f));Assert.AreEqual(Vector3.one,agents[i].scale);matched++;}
            Assert.That(m.scenarioConfig.unitTypes[0].spawnConfig.spawnCenter.x,Is.EqualTo(final.center.x).Within(.001f));Assert.That(m.scenarioConfig.unitTypes[0].spawnConfig.spawnCenter.z,Is.EqualTo(final.center.z).Within(.001f));Assert.AreEqual(final.count,m.scenarioConfig.unitTypes[0].spawnConfig.unitCount);
            Evidence("drag-undo-gpu","one revision;80 previews;other entry unchanged;GPU buffer preserved until explicit apply;spawn checked="+matched+";units="+agents.Length);
            Assert.IsTrue(d.controller.StartDefaultBattle());yield return Frames(2);Assert.AreEqual(WarSandboxBattlePhase.Running,d.controller.Phase);d.controller.PauseBattle();
        }
        [UnityTest] public IEnumerator CancellationsAndInvalidInputDoNotCommitOrReplayClick()
        {
            var draft=d.Draft;var original=draft[0];var target=LegalMove(0);int rev=draft.Revision;
            var e=Event(original.center);Down(e);e.position=ScreenPoint(target);Drag(e);hud.SendMessage("OnApplicationFocus",false);Up(e);Assert.AreEqual(rev,draft.Revision);
            e=Event(original.center);Down(e);e.position=ScreenPoint(new Vector3(-d.WorldSize.x,0,0));Drag(e);e.position=ScreenPoint(target);Up(e);Assert.AreEqual(original,draft[0]);
            e=Event(original.center);Down(e);e.position=ScreenPoint(target);Drag(e);WarSandboxUGUI.ScreenSizeOverride=new Vector2(1920,1080);yield return Frames(2);Up(e);Assert.AreEqual(rev,draft.Revision);WarSandboxUGUI.ScreenSizeOverride=new Vector2(1280,720);yield return Frames();
            e=Event(original.center);Down(e);e.position=ScreenPoint(target);Drag(e);Field("plansOpen").SetValue(hud,true);yield return Frames(2);Field("plansOpen").SetValue(hud,false);Field("nextUiRefresh").SetValue(hud,0f);yield return Frames();Up(e);Assert.AreEqual(rev,draft.Revision);
            Field("x").SetValue(hud,"not-a-number");e=Event(original.center);Down(e);e.position=ScreenPoint(target);Drag(e);Up(e);Assert.AreEqual("not-a-number",Field("x").GetValue(hud));Assert.AreEqual(rev,draft.Revision);Method("ReadFields").Invoke(hud,null);
            e=Event(original.center);Down(e);Up(e);yield return Frames();Assert.IsTrue((bool)Field("spatialSelection").GetValue(hud));Assert.AreEqual(rev,draft.Revision);
            Evidence("cancel-input","focus,outside/reentry,resize,modal,invalid text and short click: no draft writes");
        }
        [UnityTest] public IEnumerator FullFootprintAndLegacyClickPlacementAreExclusive()
        {
            var draft=d.Draft;var original=draft[0];int rev=draft.Revision;
            // Centre stays inside map but footprint extends over boundary.
            var e=Event(original.center);Down(e);e.position=ScreenPoint(new Vector3(d.WorldSize.x*.5f-1,0,0));Drag(e);Up(e);Assert.AreEqual(rev,draft.Revision);Assert.AreEqual(original,draft[0]);Assert.IsNotEmpty((string)Field("inputError").GetValue(hud));
            // Search a terrain-only rejection while centre lies within the map; no commit/rebuild during probing.
            bool terrainRejected=false;var nav=m.TerrainNavigation;
            if(nav!=null)
            {for(float z=-d.WorldSize.y*.4f;z<d.WorldSize.y*.4f&&!terrainRejected;z+=16)for(float x=-d.WorldSize.x*.4f;x<d.WorldSize.x*.4f&&!terrainRejected;x+=16)
             {var p=new Vector3(x,0,z);if(nav.IsFootprintWalkable(new Vector2(x,z),new Vector2(original.Size.x,original.Size.z)))continue;
              Assert.IsFalse(d.TryTranslateDraft(draft,rev,0,original,p,out _));Assert.AreEqual(rev,draft.Revision);terrainRejected=true;}}
            Assert.IsTrue(terrainRejected,"Authored Green terrain contains a rejected footprint");
            Field("placing").SetValue(hud,true);var target=LegalMove(0);e=Event(target);Down(e);Up(e);Assert.AreEqual(rev+1,draft.Revision);Assert.That(Vector3.Distance(target,draft[0].center),Is.LessThan(.001f));
            // Stray PointerClick cannot invoke a second placement: only the new gesture component is bound.
            ExecuteEvents.Execute(Pointer().gameObject,e,ExecuteEvents.pointerClickHandler);Assert.AreEqual(rev+1,draft.Revision);Assert.IsNull(Pointer().GetComponent<EventTrigger>());
            Evidence("validation-click","full-footprint boundary/terrain rejected;click placement commits once;no old click listener");yield return null;
        }
    }
}
#endif




