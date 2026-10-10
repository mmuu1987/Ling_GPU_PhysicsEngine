using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;
namespace MassEngine.Game.Editor {
 [InitializeOnLoad]public static class Contact34EditorReplay {
  static Contact34EditorReplay(){EditorApplication.update+=Watch;}
  public static void Begin(){string output=Environment.GetCommandLineArgs().First(s=>s.StartsWith("--terrain-output=")).Substring(17);if(Directory.Exists(output))throw new Exception("Refuse existing replay evidence");if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Refuse running editor");SessionState.SetString("Contact34ReplayOutput",output);EditorBuildSettings.scenes=new[]{"LaunchMenu","Green","Autumn","Winter"}.Select(n=>new EditorBuildSettingsScene("Assets/Game/Experiments/NavigationApproach/"+n+".unity",true)).ToArray();EditorSceneManager.OpenScene("Assets/Game/Experiments/NavigationApproach/LaunchMenu.unity",OpenSceneMode.Single);EditorApplication.EnterPlaymode();}
  static void Watch(){string p=SessionState.GetString("Contact34ReplayOutput","");if(string.IsNullOrEmpty(p)||!File.Exists(Path.Combine(p,"receipt.json")))return;SessionState.EraseString("Contact34ReplayOutput");string s=File.ReadAllText(Path.Combine(p,"receipt.json"));EditorApplication.Exit(s.Contains("\"passed\": true")?0:1);}
 }
}
