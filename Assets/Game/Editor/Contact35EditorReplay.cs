using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;
namespace MassEngine.Game.Editor {
 [InitializeOnLoad]public static class Contact35EditorReplay {
  static Contact35EditorReplay(){EditorApplication.update+=Watch;}
  public static void Begin(){string output=Environment.GetCommandLineArgs().First(s=>s.StartsWith("--terrain-output=")).Substring(17);if(Directory.Exists(output))throw new Exception("Refuse existing replay evidence");if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Refuse running editor");SessionState.SetString("Contact35ReplayOutput",output);EditorBuildSettings.scenes=new[]{"MainMenu","Green","Autumn","Winter"}.Select(n=>new EditorBuildSettingsScene("Assets/Game/Scenes/"+n+".unity",true)).ToArray();EditorSceneManager.OpenScene("Assets/Game/Scenes/MainMenu.unity",OpenSceneMode.Single);EditorApplication.EnterPlaymode();}
  static void Watch(){string p=SessionState.GetString("Contact35ReplayOutput","");if(string.IsNullOrEmpty(p)||!File.Exists(Path.Combine(p,"receipt.json")))return;SessionState.EraseString("Contact35ReplayOutput");string s=File.ReadAllText(Path.Combine(p,"receipt.json"));EditorApplication.Exit(s.Contains("\"passed\": true")?0:1);}
 }
}
