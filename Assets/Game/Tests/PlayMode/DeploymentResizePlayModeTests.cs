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
    public sealed class DeploymentResizePlayModeTests
    {
        private WarSandboxDeploymentMapPointer pressed;private Scene scene;private WarSandboxRuntimeDeployment d;private WarSandboxDeploymentHUD hud;private MassEngineManager m;
        private bool prepared;private float oldDelta;private string output;private Vector2? oldScreen;
        private static FieldInfo Field(string n)=>typeof(WarSandboxDeploymentHUD).GetField(n,BindingFlags.Instance|BindingFlags.NonPublic);
        private static MethodInfo Method(string n)=>typeof(WarSandboxDeploymentHUD).GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic);
        private IEnumerator Frames(int n=10){for(int i=0;i<n;i++)yield return null;}
        [UnitySetUp] public IEnumerator Setup()
        {
            var arg=Environment.GetCommandLineArgs().FirstOrDefault(x=>(x.StartsWith("--interaction-p5-output=")||((x.StartsWith("--interaction-p6-output=")||x.StartsWith("--interaction-p8-output="))||x.StartsWith("--interaction-p7-output="))));
            if(arg==null)Assert.Ignore("Owned P5/P6 runner opt-in required");
            output=Path.Combine(arg.Substring("--interaction-p5-output=".Length),"p5-evidence");Directory.CreateDirectory(output);
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
        private void Evidence(string name,string value){File.WriteAllText(Path.Combine(output,name+".txt"),value);Debug.Log("P5_EVIDENCE "+name+" "+value);}
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
        private IEnumerator SelectSpatial()
        {Field("spatialSelection").SetValue(hud,true);Field("nextUiRefresh").SetValue(hud,0f);yield return Frames(3);Canvas.ForceUpdateCanvases();}
        private Vector3 Corner(WarSandboxDeploymentEntry e)=>new Vector3(e.Bounds.xMax,e.center.y,e.Bounds.yMax);
        [UnityTest] public IEnumerator CornerResizePreviewUndoAndActualGpuFootprint()
        {
            yield return SelectSpatial();var draft=d.Draft;var before=draft[0];var other=draft[1];var gpu=m.Buffers.agentBuffer;int rev=draft.Revision;
            var start=Corner(before);var ev=Event(start);Capture("01-before.png");Down(ev);
            Assert.AreEqual(DeploymentResizeHandle.MaxX|DeploymentResizeHandle.MaxZ,Field("resizeHandle").GetValue(hud));
            ev.position=ScreenPoint(start+new Vector3(14,0,18));for(int i=0;i<80;i++)Drag(ev);
            Assert.AreEqual(before,draft[0]);Assert.AreEqual(rev,draft.Revision);Assert.AreSame(gpu,m.Buffers.agentBuffer);
            var preview=(WarSandboxDeploymentEntry)Field("resizePreview").GetValue(hud);Assert.That(preview.Size.x,Is.EqualTo(before.Size.x+14).Within(.001));Assert.That(preview.Size.z,Is.EqualTo(before.Size.z+18).Within(.001));
            var input=Active<InputField>().Single(x=>x.name=="input-density");Assert.IsTrue(input.readOnly);Assert.AreEqual(preview.density.ToString("R",System.Globalization.CultureInfo.InvariantCulture),input.text);
            Capture("02-resize-preview.png");Up(ev);Assert.AreEqual(rev+1,draft.Revision);Assert.AreEqual(other,draft[1]);Assert.AreEqual(before.count,draft[0].count);Assert.AreSame(gpu,m.Buffers.agentBuffer);Assert.IsFalse(input.readOnly);
            var final=draft[0];Assert.That(final.Bounds.xMin,Is.EqualTo(before.Bounds.xMin).Within(.001));Assert.That(final.Bounds.yMin,Is.EqualTo(before.Bounds.yMin).Within(.001));
            Assert.IsTrue(draft.Undo());Assert.AreEqual(before,draft[0]);Assert.IsFalse(draft.CanUndo);Assert.IsTrue(draft.Redo());yield return Frames();Capture("03-resize-committed.png");
            Assert.IsTrue(d.TryApply(out var error),error);var agents=new AgentData[m.Buffers.AgentCount];m.Buffers.agentBuffer.GetData(agents);float xmin=float.MaxValue,xmax=float.MinValue,zmin=float.MaxValue,zmax=float.MinValue;
            for(int i=0;i<final.count;i++){var a=agents[i];Assert.AreEqual(Vector3.one,a.scale);Assert.That(a.position.x,Is.InRange(final.Bounds.xMin-.01f,final.Bounds.xMax+.01f));Assert.That(a.position.z,Is.InRange(final.Bounds.yMin-.01f,final.Bounds.yMax+.01f));xmin=Mathf.Min(xmin,a.position.x);xmax=Mathf.Max(xmax,a.position.x);zmin=Mathf.Min(zmin,a.position.z);zmax=Mathf.Max(zmax,a.position.z);}
            Assert.That(xmin,Is.EqualTo(final.Bounds.xMin).Within(.01));Assert.That(xmax,Is.EqualTo(final.Bounds.xMax).Within(.01));Assert.That(zmin,Is.EqualTo(final.Bounds.yMin).Within(.01));Assert.That(zmax,Is.EqualTo(final.Bounds.yMax).Within(.01));Assert.AreEqual(2048,agents.Length);
            Evidence("resize-gpu","80 previews;one commit;undo redo;N=1024 total=2048 scale=1;GPU extrema match new D/W;D="+final.Size.x+" W="+final.Size.z+" density="+final.density+" aspect="+final.aspect);
            Assert.IsTrue(d.controller.StartDefaultBattle());yield return Frames(2);Assert.AreEqual(WarSandboxBattlePhase.Running,d.controller.Phase);d.controller.PauseBattle();
        }
        [UnityTest] public IEnumerator NumericAndPointerRejectTightShapesWithoutOverwritingText()
        {
            yield return SelectSpatial();var draft=d.Draft;var original=draft[0];int rev=draft.Revision;
            var densityInput=Active<InputField>().Single(x=>x.name=="input-density");densityInput.text="1.5";
            Assert.IsFalse((bool)Method("CommitFields").Invoke(hud,null));Assert.AreEqual("1.5",densityInput.text);Assert.AreEqual(original,draft[0]);
            var ev=Event(Corner(original));Down(ev);ev.position=ScreenPoint(Corner(original)+new Vector3(14,0,18));Drag(ev);Up(ev);Assert.AreEqual(rev,draft.Revision);Assert.AreEqual("1.5",Field("density").GetValue(hud));
            Method("ReadFields").Invoke(hud,null);Field("nextUiRefresh").SetValue(hud,0f);yield return Frames(3);
            ev=Event(Corner(original));Down(ev);ev.position=ScreenPoint(Corner(original)+new Vector3(14,0,18));Drag(ev);hud.SendMessage("OnApplicationFocus",false);Up(ev);Assert.AreEqual(rev,draft.Revision);
            Assert.IsTrue(d.SetStat(original.template,WarSandboxUnitStat.AgentRadius,1.1f));Method("ReadFields").Invoke(hud,null);var proposed=original;proposed.center.z+=1;
            Assert.IsFalse(d.TryResizeDraft(draft,draft.Revision,0,original,proposed,out var reason));StringAssert.Contains("间距",reason);Assert.AreEqual(original,draft[0]);
            Evidence("resize-rejection","numeric 1.5 retained;pending input blocks pointer;focus cancels;effective local radius 1.1 rejects packing");
        }
        [UnityTest] public IEnumerator LegacyManualResizeAt1080AndCenterRemainsMoveTarget()
        {
            var draft=d.Draft;var old=draft[0];old.manualSize=new Vector3(old.Size.x,7,old.Size.z);old.density=.7f;old.aspect=.5f;draft.Set(0,old);Method("ReadFields").Invoke(hud,null);
            Assert.IsTrue((bool)Method("CommitFields").Invoke(hud,null));Assert.AreEqual(old,draft[0]);
            WarSandboxUGUI.ScreenSizeOverride=new Vector2(1920,1080);yield return SelectSpatial();
            var centerEvent=Event(old.center);Down(centerEvent);Assert.AreEqual(DeploymentResizeHandle.None,Field("resizeHandle").GetValue(hud));hud.SendMessage("OnApplicationFocus",false);Up(centerEvent);
            int rev=draft.Revision;var ev=Event(Corner(old));Down(ev);ev.position=ScreenPoint(Corner(old)+new Vector3(14,0,18));Drag(ev);Up(ev);
            Assert.AreEqual(rev+1,draft.Revision);Assert.AreEqual(Vector3.zero,draft[0].manualSize);Assert.AreEqual(old.count,draft[0].count);Assert.IsTrue(draft.Undo());Assert.AreEqual(old,draft[0]);
            Evidence("resize-legacy","1080 logical layout;untouched manual retained;body center still moves;explicit resize converts to auto;one undo restores exact legacy manual including Y");
        }
    }
}
#endif



