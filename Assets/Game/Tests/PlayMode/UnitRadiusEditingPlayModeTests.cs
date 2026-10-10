#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    public class UnitRadiusEditingPlayModeTests
    {
        private const string Green = "Assets/Game/Scenes/Green.unity";
        private Scene battleScene;
        private GameObject uiOwner, cameraOwner;
        private static string Output
        {
            get
            {
                const string prefix = "--interaction-p2-output=";
                string a = Environment.GetCommandLineArgs().FirstOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
                string p = Path.Combine(a != null ? a.Substring(prefix.Length) : Application.temporaryCachePath, "p2-evidence"); Directory.CreateDirectory(p); return p;
            }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (uiOwner != null) Object.DestroyImmediate(uiOwner);
            if (cameraOwner != null) Object.DestroyImmediate(cameraOwner);
            if (battleScene.IsValid() && battleScene.isLoaded) yield return SceneManager.UnloadSceneAsync(battleScene);
            yield return null;
        }
        private IEnumerator LoadBattle()
        {
            yield return SceneManager.LoadSceneAsync(Green, LoadSceneMode.Additive);
            battleScene = SceneManager.GetSceneByPath(Green); Assert.That(battleScene.IsValid() && battleScene.isLoaded);
            yield return null; yield return null;
        }
        private MassEngineManager Manager() => WarSandboxSceneSession.FindManager(battleScene, out _);
        private WarSandboxRuntimeDeployment Deployment(WarSandboxUnitStatStore store)
        {
            var m = Manager(); Assert.That(m, Is.Not.Null);
            var controller = WarSandboxRuntimeBootstrap.EnsureControls(m); controller.RebuildArmyStates(); m.PauseBattle();
            var d = m.GetComponent<WarSandboxRuntimeDeployment>(); d.battlefieldCatalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset"); d.StatStore = store; d.PlanStore = new WarSandboxLocalPlanStore(Path.Combine(Output, "Plans")); return d;
        }
        private static UnitTypeGpuSettings[] Settings(MassEngineManager m)
        {
            Assert.That(m.Buffers != null && m.Buffers.IsAllocated, Is.True);
            var data = new UnitTypeGpuSettings[m.Buffers.UnitTypeCount]; m.Buffers.unitTypeSettingsBuffer.GetData(data); return data;
        }
        private static void Check(MassEngineManager m, UnitTypeGpuSettings[] baseline, float radius, int count, string tag)
        {
            var settings = Settings(m); Assert.That(settings.Length, Is.EqualTo(baseline.Length)); Assert.That(m.Buffers.AgentCount, Is.EqualTo(count));
            Assert.That(settings[0].agentRadius, Is.EqualTo(radius));
            for (int i = 0; i < settings.Length; i++)
            {
                if (i == 0) settings[i].agentRadius = baseline[i].agentRadius;
                Assert.That(JsonUtility.ToJson(settings[i]), Is.EqualTo(JsonUtility.ToJson(baseline[i])), "Unrelated GPU fields changed: " + i);
            }
            var agents = new AgentData[count]; m.Buffers.agentBuffer.GetData(agents);
            Assert.That(agents.All(a => a.scale == Vector3.one), Is.True);
            if (m.DeploymentRadiusClearance.HasValue)
            { Assert.That(m.TerrainNavigation, Is.Not.Null); Assert.That(m.TerrainNavigation.Clearance, Is.EqualTo(m.DeploymentRadiusClearance.Value)); }
            string line = $"P2_GPU_CHAIN {tag} radius={radius:R} count={count} clearance={m.TerrainNavigation?.Clearance} unrelatedSettingsEqual=true scaleOne=true";
            Debug.Log(line); File.AppendAllText(Path.Combine(Output, "gpu-chain.txt"), line + "\n");
        }
        private static void Save(WarSandboxUnitStatStore store, string id, float? radius)
        {
            var g = new WarSandboxGlobalStats(); var s = new WarSandboxStatSet(); if (radius.HasValue) s.Set(WarSandboxUnitStat.AgentRadius, radius.Value); g.Replace(id, 1, s);
            Assert.That(store.TrySave(g, "P2 isolated radius", out var error), Is.True, error);
        }
        [UnityTest] public IEnumerator SavedRadiusReachesGpuOnlyAtBoundariesAndRestoresAcrossResetAndSceneReentry()
        {
            Assert.That(WarSandboxSceneSession.Instance, Is.Null);
            yield return LoadBattle();
            var store = new WarSandboxUnitStatStore(Path.Combine(Output, "gpu-stats.json")); var d = Deployment(store); var m = Manager(); var c = d.controller;
            var original = m.scenarioConfig.unitTypes[0]; string before = EditorJsonUtility.ToJson(original.flockingConfig);
            Assert.That(d.battlefieldCatalog.TryGetTemplateId(original, out string id, out _), Is.True);
            var baseline = Settings(m); int count = m.Buffers.AgentCount; Assert.That(baseline[0].agentRadius, Is.EqualTo(.55f));
            Save(store, id, .6f); File.Copy(store.FilePath, Path.Combine(Output, "global-radius-0.6.json")); Assert.That(d.TryApplyGlobalStatsOnLoad(out string error), Is.True, error);
            Check(m, baseline, .6f, count, "global-load"); Assert.That(m.scenarioConfig.unitTypes[0].flockingConfig, Is.Not.SameAs(original.flockingConfig));
            Assert.Throws<InvalidOperationException>(() => m.SetDeploymentRadiusClearance(.7f), "Cannot mutate a live/paused allocated world in place");
            Assert.That(c.StartDefaultBattle(), Is.True); yield return null; yield return null;
            Save(store, id, .65f); Assert.That(d.TryApplyGlobalStatsOnLoad(out error), Is.False); Assert.That(error, Is.Not.Null);
            Check(m, baseline, .6f, count, "running-no-hot-apply");
            c.ResetBattle(); Check(m, baseline, .6f, count, "reset-keeps-applied");
            var unchangedBuffer = m.Buffers.unitTypeSettingsBuffer;
            Assert.That(d.TryBeginEdit(false, out error), Is.True, error); d.SetStat(original, WarSandboxUnitStat.AgentRadius, 1.6f);
            Assert.That(d.TryApply(out error), Is.False); StringAssert.Contains("网格", error);
            Assert.That(m.Buffers.unitTypeSettingsBuffer, Is.SameAs(unchangedBuffer));
            d.SetStat(original, WarSandboxUnitStat.AgentRadius, 1.4f); Assert.That(d.TryApply(out error), Is.False); StringAssert.Contains("间距", error);
            Assert.That(m.Buffers.unitTypeSettingsBuffer, Is.SameAs(unchangedBuffer)); Assert.That(d.CancelEditing(), Is.True);
            Check(m, baseline, .6f, count, "rejected-grid-density-keeps-gpu");
            Assert.That(d.TryBeginEdit(false, out error), Is.True, error); Assert.That(d.SetStat(original, WarSandboxUnitStat.AgentRadius, .7f), Is.True);
            Assert.That(d.CancelEditing(), Is.True); Check(m, baseline, .6f, count, "cancel-keeps-applied");
            Assert.That(d.TryBeginEdit(false, out error), Is.True, error); d.SetStat(original, WarSandboxUnitStat.AgentRadius, .7f);
            var battlefield = d.battlefieldCatalog.entries.Single(e => e.scenePath == Green);
            var sample = WarSandboxLocalPlanStore.Create("P2Radius", "P2 isolated local radius", battlefield.id, battlefield.contentVersion, battlefield.terrainId, battlefield.terrainVersion, d.WorldSize, m.systemConfig.simulationConfig.boundaryPadding, d.Draft.Rules, d.Draft.Snapshot(), d.battlefieldCatalog, d.Draft.Stats, out error);
            Assert.That(d.PlanStore.TrySave(sample, false, out error), Is.True, error);
            Assert.That(d.PlanStore.TryLoad("P2Radius", out var reloaded, out error), Is.True, error);
            Assert.That(WarSandboxLocalPlanStore.TryResolve(reloaded, battlefield, d.battlefieldCatalog, d.WorldSize, m.systemConfig.simulationConfig.boundaryPadding, out var reloadedDraft, out error), Is.True, error);
            Assert.That(reloadedDraft.TryGetStat(original, WarSandboxUnitStat.AgentRadius, out float local) && local == .7f);
            Assert.That(d.TryReplaceDraft(reloadedDraft, out error), Is.True, error);
            Assert.That(d.TryApply(out error), Is.True, error); Check(m, baseline, .7f, count, "local-beats-global");
            Assert.That(d.TryBeginEdit(false, out error), Is.True, error); d.Draft.ClearStat(original, WarSandboxUnitStat.AgentRadius);
            Assert.That(d.TryApply(out error), Is.True, error); Check(m, baseline, .65f, count, "clear-local-follows-global");
            Save(store, id, null); Assert.That(d.TryBeginEdit(false, out error), Is.True, error); Assert.That(d.TryApply(out error), Is.True, error);
            Check(m, baseline, .55f, count, "restore-official"); Assert.That(m.DeploymentRadiusClearance, Is.EqualTo(.55f));
            c.ResetBattle(); Check(m, baseline, .55f, count, "reset-after-restoration");
            Assert.That(m.scenarioConfig.unitTypes[0].flockingConfig, Is.SameAs(original.flockingConfig));
            Assert.That(EditorJsonUtility.ToJson(original.flockingConfig), Is.EqualTo(before));
            yield return SceneManager.UnloadSceneAsync(battleScene); yield return LoadBattle();
            d = Deployment(new WarSandboxUnitStatStore(store.FilePath)); m = Manager();
            Assert.That(d.TryApplyGlobalStatsOnLoad(out error), Is.False); Assert.That(error, Is.Null); Assert.That(m.DeploymentRadiusClearance, Is.Null);
            Check(m, baseline, .55f, count, "reenter-official");
            Save(store, id, .6f); yield return SceneManager.UnloadSceneAsync(battleScene); yield return LoadBattle();
            d = Deployment(new WarSandboxUnitStatStore(store.FilePath)); m = Manager();
            Assert.That(d.TryApplyGlobalStatsOnLoad(out error), Is.True, error); Check(m, baseline, .6f, count, "reenter-saved");
            Save(store, id, null);
        }
        private static void Field(WarSandboxFrontEnd f, string n, object v) => typeof(WarSandboxFrontEnd).GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(f, v);
        private static object Call(WarSandboxFrontEnd f, string n, params object[] v) => typeof(WarSandboxFrontEnd).GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, v);
        private static UnitModelPreviewWidget Draw(WarSandboxFrontEnd f, WarSandboxUGUI ui, GameObject owner, bool expanded = false)
        {
            ui.Begin(); Call(f, "DrawLibrary"); if (expanded) Call(f, "DrawExpandedUnitPreview"); ui.End(); Canvas.ForceUpdateCanvases();
            foreach (var t in owner.GetComponentsInChildren<Transform>()) t.gameObject.layer = 31;
            return owner.GetComponentsInChildren<UnitModelPreviewWidget>().Single();
        }
        private void Shot(Camera camera, RenderTexture target, string name)
        {
            Canvas.ForceUpdateCanvases(); camera.Render(); var previous = RenderTexture.active; var t = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = target; t.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); t.Apply(); Assert.That(t.GetPixels32().Count(p => p.r + p.g + p.b > 10), Is.GreaterThan(target.width * target.height / 2)); File.WriteAllBytes(Path.Combine(Output, name + ".png"), t.EncodeToPNG()); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(t); }
        }
        [UnityTest] public IEnumerator LibraryCallbacksPreviewPendingValuesButOnlyExplicitSavePersists()
        {
            Assert.That(WarSandboxSceneSession.Instance, Is.Null); var oldSize = WarSandboxUGUI.ScreenSizeOverride;
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            uiOwner = new GameObject("P2 actual library"); var session = uiOwner.AddComponent<WarSandboxSceneSession>(); session.enterDefaultOnStart = false; session.enabled = false;
            session.catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>("Assets/Game/Scenes/Catalog.asset");
            var entry = WarSandboxRosterChoices.Library(session.catalog)[0]; var before = EditorJsonUtility.ToJson(entry.config.flockingConfig);
            var front = uiOwner.AddComponent<WarSandboxFrontEnd>(); front.enabled = false; var ui = new WarSandboxUGUI(uiOwner.transform, "P2 canvas", 200); Field(front, "ui", ui);
            front.LibraryStore = new WarSandboxUnitStatStore(Path.Combine(Output, "ui-stats.json")); front.OpenLibrary();
            cameraOwner = new GameObject("P2 screenshot camera"); var camera = cameraOwner.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            var canvas = uiOwner.GetComponentInChildren<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            try
            {
                WarSandboxUGUI.ScreenSizeOverride = new Vector2(1280, 720); var widget = Draw(front, ui, uiOwner); yield return null;
                Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.55f)); Assert.That(File.Exists(front.LibraryStore.FilePath), Is.False);
                var input = uiOwner.GetComponentsInChildren<InputField>().Single(x => x.name == "lib-r-agentRadius-field"); input.onEndEdit.Invoke("0.6");
                widget = Draw(front, ui, uiOwner); yield return null; Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.6f)); Assert.That(File.Exists(front.LibraryStore.FilePath), Is.False);
                Assert.That(widget.Renderer.SubjectScale, Is.EqualTo(Vector3.one)); Shot(camera, target, "library-pending-0.6");
                input.onEndEdit.Invoke("NaN"); input.onEndEdit.Invoke("9"); widget = Draw(front, ui, uiOwner); Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.6f));
                Assert.That((bool)Call(front, "SaveLibraryPending", entry), Is.True);
                Assert.That(new WarSandboxUnitStatStore(front.LibraryStore.FilePath).Current.TryGet(entry.templateId, WarSandboxUnitStat.AgentRadius, out float saved) && saved == .6f);
                byte[] file = File.ReadAllBytes(front.LibraryStore.FilePath);
                File.WriteAllBytes(Path.Combine(Output, "library-saved-0.6.json"), file);
                input.onEndEdit.Invoke("0.7"); widget = Draw(front, ui, uiOwner); Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.7f));
                uiOwner.GetComponentsInChildren<Button>().Single(x => x.name == "lib-discard").onClick.Invoke(); widget = Draw(front, ui, uiOwner);
                Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.6f)); Assert.That(File.ReadAllBytes(front.LibraryStore.FilePath), Is.EqualTo(file));
                Field(front, "enlargedUnit", entry); Field(front, "unitPreviewOpen", true); widget = Draw(front, ui, uiOwner, true); yield return null;
                Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.6f)); Shot(camera, target, "expanded-saved-0.6");
                Field(front, "unitPreviewOpen", false); Draw(front, ui, uiOwner);
                uiOwner.GetComponentsInChildren<Button>().Single(x => x.name == "lib-r-agentRadius-clear").onClick.Invoke(); widget = Draw(front, ui, uiOwner); yield return null;
                Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.55f)); Assert.That(File.ReadAllBytes(front.LibraryStore.FilePath), Is.EqualTo(file));
                Shot(camera, target, "library-restore-pending-0.55"); Assert.That((bool)Call(front, "SaveLibraryPending", entry), Is.True);
                front.OpenLibrary(); widget = Draw(front, ui, uiOwner); Assert.That(widget.Renderer.ContactRadius, Is.EqualTo(.55f));
                Assert.That(EditorJsonUtility.ToJson(entry.config.flockingConfig), Is.EqualTo(before));
                Debug.Log("P2_UI_CHAIN pending=.6 invalidRejected=true saved=.6 cancelled=.7 restored=.55 sourceAssetUnchanged=true");
            }
            finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); WarSandboxUGUI.ScreenSizeOverride = oldSize; ShaderUtil.allowAsyncCompilation = async; }
            // Close the real library, then consume the exact bytes produced by its Save callback.
            // This is intentionally not a second independently constructed Global(.6) fixture.
            Object.DestroyImmediate(uiOwner); uiOwner = null; Object.DestroyImmediate(cameraOwner); cameraOwner = null;
            yield return null; yield return LoadBattle();
            var store = new WarSandboxUnitStatStore(Path.Combine(Output, "library-saved-0.6.json")); var deployment = Deployment(store); var manager = Manager();
            var baseline = Settings(manager); int count = manager.Buffers.AgentCount;
            Assert.That(deployment.TryApplyGlobalStatsOnLoad(out string error), Is.True, error);
            Check(manager, baseline, .6f, count, "actual-ui-saved-file-new-battle");
            yield return null;
        }
    }
}
#endif
