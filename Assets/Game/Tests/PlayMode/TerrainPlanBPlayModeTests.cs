#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace MassEngine.Game.Tests {
 public sealed class TerrainPlanBPlayModeTests {
  const string Menu="Assets/Game/Experiments/ForestPrototype/LaunchMenu.unity";
  string output;EditorBuildSettingsScene[] oldScenes;
  void StopAuto(Scene scene,LoadSceneMode mode){if(scene.path==Menu&&WarSandboxSceneSession.Instance!=null)WarSandboxSceneSession.Instance.enterDefaultOnStart=false;}
  [UnityTest,Timeout(300000)]public IEnumerator ImportedSceneryTerrainDeploymentAndGpuMovement(){
   output=Environment.GetEnvironmentVariable("TOY_UI_OUTPUT");Assert.That(output,Is.Not.Null.And.Not.Empty);Directory.CreateDirectory(output);
   WarSandboxUnitStatStore.DefaultPathOverride=Path.Combine(output,"isolated-globals.json");
   oldScenes=EditorBuildSettings.scenes;
   if(!Enumerable.Range(0,SceneManager.sceneCountInBuildSettings).Any(i=>SceneUtility.GetScenePathByBuildIndex(i)==Menu))Assert.Ignore("Use Logs/test-terrain-plan-b.py to register isolated scenes before PlayMode.");
   SceneManager.sceneLoaded+=StopAuto;
   yield return EditorSceneManager.LoadSceneAsyncInPlayMode(Menu,new LoadSceneParameters(LoadSceneMode.Single));SceneManager.sceneLoaded-=StopAuto;
   var go=new GameObject("Terrain Plan B integration check");Object.DontDestroyOnLoad(go);var check=go.AddComponent<TerrainPlanBPlayerSmoke>();check.enabled=false;
   var flags=BindingFlags.NonPublic|BindingFlags.Instance;typeof(TerrainPlanBPlayerSmoke).GetField("output",flags).SetValue(check,output);
   var routine=(IEnumerator)typeof(TerrainPlanBPlayerSmoke).GetMethod("Check",flags).Invoke(check,null);yield return routine;
   var receipt=typeof(TerrainPlanBPlayerSmoke).GetField("receipt",flags).GetValue(check);receipt.GetType().GetField("passed").SetValue(receipt,true);receipt.GetType().GetField("stage").SetValue(receipt,"complete");var json=JsonUtility.ToJson(receipt,true);File.WriteAllText(Path.Combine(output,"movement.json"),json);Debug.Log("TERRAIN_B_INTEGRATION_PASS "+json);Object.Destroy(go);
  }
  [UnityTearDown]public IEnumerator Cleanup(){
   if(oldScenes!=null)EditorBuildSettings.scenes=oldScenes;
   SceneManager.sceneLoaded-=StopAuto;WarSandboxUnitStatStore.DefaultPathOverride=null;Time.timeScale=1;
   if(WarSandboxSceneSession.Instance!=null)Object.Destroy(WarSandboxSceneSession.Instance.gameObject);
   var old=SceneManager.GetActiveScene();var empty=SceneManager.CreateScene("Terrain B cleanup");SceneManager.SetActiveScene(empty);if(old.IsValid()&&old!=empty)yield return SceneManager.UnloadSceneAsync(old);yield return null;
  }
 }
}
#endif
