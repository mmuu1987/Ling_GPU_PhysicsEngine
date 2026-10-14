using UnityEngine;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine.UI;
#endif
namespace MassEngine.Game
{
    // Opt-in standalone verification. Without the exact flag no object is created and no files are written.
    public sealed class WarSandboxToyUiPlayerSmoke : MonoBehaviour
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] private class Receipt { public bool passed; public string buildGuid, stage, error; public bool osInputTest = false; public int livePreviews; }
        private readonly Receipt receipt = new Receipt(); private string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--toy-ui-smoke")) return;
            string path = args.FirstOrDefault(x => x.StartsWith("--toy-ui-output="));
            if (path == null) { Debug.LogError("Missing isolated toy UI output"); Application.Quit(2); return; }
            var go = new GameObject("Opt-in toy UI player verification"); DontDestroyOnLoad(go);
            var smoke = go.AddComponent<WarSandboxToyUiPlayerSmoke>(); smoke.output = path.Substring("--toy-ui-output=".Length);
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(output); receipt.buildGuid = Application.buildGUID;
            var check = Check();
            while (true)
            {
                object next;
                try { if (!check.MoveNext()) break; next = check.Current; }
                catch (Exception ex) { receipt.error = ex.ToString(); Finish(1); yield break; }
                yield return next;
            }
            receipt.passed = true; receipt.stage = "complete"; Finish(0);
        }
        private void Finish(int code)
        {
            File.WriteAllText(Path.Combine(output, "receipt.json"), JsonUtility.ToJson(receipt, true));
            Debug.Log("TOY_UI_PLAYER_RESULT " + JsonUtility.ToJson(receipt)); Application.Quit(code);
        }
        private static Button Button(string name) => FindObjectsByType<Button>(FindObjectsSortMode.None).Single(x => x.gameObject.activeInHierarchy && x.name == name);
        private static void Require(bool value, string note) { if (!value) throw new InvalidOperationException(note); }
        private void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
        private static void CheckPreview(string name)
        {
            var widget = FindObjectsByType<UnitModelPreviewWidget>(FindObjectsSortMode.None).Single(x => x.gameObject.activeInHierarchy && x.name == name);
            Require(widget.IsLive && widget.Renderer.Texture != null, "Missing live preview: " + name);
            var target = widget.Renderer.Texture; var old = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                var pixels = image.GetPixels32(); int solid = pixels.Count(p => p.a > 127), pink = pixels.Count(p => p.a > 127 && p.r > 200 && p.b > 200 && p.g < 75);
                Require(pixels.Count(p => p.a > 2 && p.a < 100) > 5, "Missing soft ground shadow: " + name);
                Require(solid > 100 && pink < solid * .9f, "Blank/error shader preview: " + name);
            }
            finally { RenderTexture.active = old; Destroy(image); }
        }
        private IEnumerator Check()
        {
            receipt.stage = "main-menu"; yield return new WaitForSecondsRealtime(3);
            var session = WarSandboxSceneSession.Instance;
            Require(session != null && session.State == WarSandboxEntryState.Menu, "Ordinary startup must show menu");
            Button("home-play"); Shot("01-main-menu"); yield return new WaitForSecondsRealtime(1);
            Button("menu-library").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            Require(FindObjectsByType<Button>(FindObjectsSortMode.None).Count(x => x.gameObject.activeInHierarchy && x.name.StartsWith("lib-item-")) == 33, "Library must show 33 representatives");
            for (int n = 0; n < 33; n++)
            {
                Button("lib-item-" + n).onClick.Invoke(); yield return new WaitForSecondsRealtime(.25f);
                CheckPreview("lib-unit"); receipt.livePreviews++;
            }
            Button("lib-item-0").onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
            Shot("00-deduplicated-library"); yield return new WaitForSecondsRealtime(1);
            Button("lib-back").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            Button("home-play").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            int index = Array.FindIndex(session.catalog.entries, x => x.id == "robot-expressive");
            Button("card-" + index).onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            Button("card-" + index + "-enter").onClick.Invoke();
            receipt.stage = "load-deployment"; float deadline = Time.realtimeSinceStartup + 120;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Require(session.State == WarSandboxEntryState.Battle && session.Controller != null, "Battle load failed: " + session.Error);
            yield return new WaitForSecondsRealtime(2);
            var controller = session.Controller; var deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            Require(deployment.IsEditing, "Deployment must open on entry");
            int team = deployment.Draft[0].teamId; Button("army-details-" + team).onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            receipt.stage = "legion"; CheckPreview("legion-unit-preview"); Button("legion-done"); Shot("02-legion"); yield return new WaitForSecondsRealtime(1);
            Button("legion-done").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            Require(deployment.IsEditing && controller.Phase == WarSandboxBattlePhase.Setup, "Returning must not apply/start");
            Button("deployment-start").onClick.Invoke(); yield return new WaitForSecondsRealtime(3);
            receipt.stage = "battle"; Require(controller.Phase == WarSandboxBattlePhase.Running, "Battle failed to start");
            Button("start").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            Require(controller.Phase == WarSandboxBattlePhase.Paused, "Pause failed");
            Shot("03-battle"); yield return new WaitForSecondsRealtime(1);
            Button("battle-tools").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            Button("end-battle").onClick.Invoke(); yield return new WaitForSecondsRealtime(.5f);
            receipt.stage = "result"; Require(controller.BattleResult.valid, "Result missing"); Button("result-edit");
            Require(!FindObjectsByType<Button>(FindObjectsSortMode.None).Any(x => x.gameObject.activeInHierarchy && x.name == "attack"), "Combat commands remain under result");
            Shot("04-result"); yield return new WaitForSecondsRealtime(1);
            foreach (var name in new[] { "01-main-menu", "02-legion", "03-battle", "04-result" })
                Require(File.Exists(Path.Combine(output, name + ".png")), "Missing frame " + name);
        }
#endif
    }
}
