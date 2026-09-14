#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MassEngine.Game
{
    // Opt-in development-player smoke test. No NUnit or UnityEditor dependency.
    public sealed partial class WarSandboxEntrySmoke : MonoBehaviour
    {
        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string stage;
            public string error;
            public string[] checks;
            public FullBattle[] battles;
        }

        [Serializable]
        private sealed class FullBattle
        {
            public string entryId;
            public string mode;
            public string phase;
            public string victoryReason;
            public int winnerTeamId;
            public float simulationSeconds;
            public float realSeconds;
            public int[] initial;
            public int[] survivors;
        }

        private readonly List<string> checks = new List<string>();
        private readonly List<string> errors = new List<string>();
        private readonly List<FullBattle> battles = new List<FullBattle>();
        private string stage = "startup";
        private string output;
        private WarSandboxSceneSession session;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-smoke") < 0) return;
            var root = new GameObject("Entry Build Smoke"); DontDestroyOnLoad(root);
            root.AddComponent<WarSandboxEntrySmoke>();
        }

        private void Start()
        {
            Application.runInBackground = true;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--war-sandbox-smoke-output");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "EntrySmoke");
            Directory.CreateDirectory(output);
            Application.logMessageReceived += TrackError;
            StartCoroutine(Guarded(Run()));
        }

        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            while (stack.Count > 0)
            {
                object next = null;
                bool moved = false;
                string failure = null;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception ex) { failure = ex.ToString(); }
                if (failure != null) { Finish(false, failure); yield break; }
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            Finish(errors.Count == 0, string.Join("\n", errors));
        }

        private IEnumerator Run()
        {
            for (int i = 0; i < 300 && WarSandboxSceneSession.Instance == null; i++) yield return null;
            session = WarSandboxSceneSession.Instance;
            Require(session != null && session.State == WarSandboxEntryState.Menu, "Player did not start in its entry scene.");
            var sourceCatalog = session.catalog;
            string sourceJson = JsonUtility.ToJson(sourceCatalog);
            session.catalog = Instantiate(sourceCatalog);
            var rulesA = sourceCatalog.entries[0].rules; var rulesB = sourceCatalog.entries[1].rules;
            string aJson = JsonUtility.ToJson(rulesA), bJson = JsonUtility.ToJson(rulesB);
            stage = "startup-splash";
            float splashDeadline = Time.realtimeSinceStartup + 30f;
            while (!SplashScreen.isFinished && Time.realtimeSinceStartup < splashDeadline) yield return null;
            Require(SplashScreen.isFinished, "Startup splash did not finish before UI capture.");
            stage = "menu-desktop";
            // A hidden Windows launch needs a real resize before its first presentation.
            Screen.SetResolution(1024, 768, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-plan-seed") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-plan-reload") >= 0)
            {
                yield return RunLocalPlans(Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-plan-seed") >= 0);
                yield break;
            }
            yield return Screenshot("menu-desktop.png");
            stage = "menu-narrow";
            Screen.SetResolution(560, 800, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Screenshot("menu-narrow.png");
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);

            foreach (string id in new[] { "open-battle", "walled-point", "open-battle" })
            {
                stage = "load-" + id;
                Require(session.TryEnterBattlefield(id, false, out string error), error);
                Require(!session.TryEnterBattlefield(id, false, out _), "Duplicate load was accepted.");
                yield return WaitForLoad();
                Require(session.State == WarSandboxEntryState.Battle, session.Error);
                var controller = session.Controller; var manager = controller.manager;
                Require(!manager.IsBattleRunning && controller.Phase == WarSandboxBattlePhase.Setup, "Battle started before player orders.");
                Require(FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length == 1, "Multiple simulations survived the transition.");
                Require(manager.Buffers != null && manager.Buffers.IsAllocated, "GPU initialization failed in the player.");
                var mode = id == "walled-point" ? WarSandboxGameMode.ControlPoint : WarSandboxGameMode.Annihilation;
                Require(controller.gameMode == mode, "Wrong selected rule definition.");
                Require(controller.SimulationSpeed == 1 && controller.selectedTeam == 0, "State leaked from the previous battle.");
                for (int i = 0; i < 8; i++) yield return null;
                yield return Screenshot(id + ".png");
                yield return PreviewScreenshot(id + "-preview.png", manager);
                Require(controller.StartDefaultBattle(), "Default orders were rejected.");
                for (int i = 0; i < 40; i++) yield return null;
                Require(!session.TryReturnToMenu(false, out _), "Battle abandoned without confirmation.");
                session.BeginConfirmation();
                Require(!manager.IsBattleRunning && !controller.StartDefaultBattle(), "Confirmation did not block commands.");
                yield return Screenshot("confirmation.png");
                session.CancelConfirmation();
                Require(manager.IsBattleRunning, "Cancel did not resume the battle.");
                controller.ResetBattle();
                Require(controller.gameMode == mode && controller.GetMoveRoutePointCount(0) == 0, "Restart did not restore selected rules.");
                controller.SetGameMode(mode == WarSandboxGameMode.ControlPoint ? WarSandboxGameMode.Annihilation : WarSandboxGameMode.ControlPoint);
                controller.ResetBattle();
                Require(controller.gameMode == mode, "Temporary Setup edits survived restart.");
                controller.StartDefaultBattle(); controller.SetSimulationSpeed(4); controller.selectedTeam = 2;
                var buffers = manager.Buffers;
                Require(session.TryReturnToMenu(true, out error), error);
                yield return WaitForLoad();
                Require(session.State == WarSandboxEntryState.Menu, session.Error);
                Require(controller == null && !buffers.IsAllocated, "Old scene/GPU buffers were retained.");
                Require(FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length == 0, "Simulation remained on the menu.");
                Require(Time.timeScale == 1 && session.CurrentEntryId == null, "Return retained speed or selection.");
                checks.Add(id + ": load, fight, confirmation, restart, return, GPU release");
            }

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-full-battles") >= 0)
                yield return RunFullBattles();

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--war-sandbox-deployment") >= 0)
                yield return RunDeployment();

            stage = "failure-recovery";
            var goodEntries = session.catalog.entries;
            session.catalog.entries = new[] { new WarSandboxBattlefieldEntry
            { id = "bad", displayName = "Invalid battlefield", scenePath = "Assets/Missing.unity", rules = rulesA } };
            session.catalog.defaultEntryId = "bad";
            Require(!session.TryEnterBattlefield("bad", false, out _) && session.State == WarSandboxEntryState.Menu,
                "Pre-load failure did not preserve the menu.");
            session.catalog.entries[0].scenePath = "Assets/Game/Scenes/WarSandboxMenu.unity";
            Require(session.TryEnterBattlefield("bad", false, out _), "Post-load failure fixture did not load.");
            yield return WaitForLoad();
            Require(session.State == WarSandboxEntryState.Failed && session.InputBlocked, "Missing manager was not rejected.");
            yield return Screenshot("failure.png");
            Require(session.TryReturnToMenu(true, out _), "Cannot leave failed scene.");
            yield return WaitForLoad();
            Require(session.State == WarSandboxEntryState.Menu, "Did not return after failure.");
            session.catalog.entries = goodEntries; session.catalog.defaultEntryId = sourceCatalog.defaultEntryId;
            Require(session.TryEnterBattlefield("walled-point", false, out _), "Cannot retry after failure.");
            yield return WaitForLoad();
            Require(session.State == WarSandboxEntryState.Battle, session.Error);
            Require(JsonUtility.ToJson(sourceCatalog) == sourceJson && JsonUtility.ToJson(rulesA) == aJson && JsonUtility.ToJson(rulesB) == bJson,
                "Source catalog/rules were modified.");
            checks.Add("pre-load rejection, post-load failure, return, retry, source assets unchanged");

            stage = "direct-scene-load";
            var runtimeCatalog = session.catalog;
            yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/WarSandbox.unity", LoadSceneMode.Single);
            yield return null; yield return null;
            Require(WarSandboxSceneSession.Instance == null, "Direct load retained a stale session.");
            var direct = FindFirstObjectByType<WarSandboxBattleController>();
            Require(direct != null && direct.battlefieldConfig == null && direct.gameMode == WarSandboxGameMode.Annihilation,
                "Direct load inherited the previous catalog rules.");
            Destroy(runtimeCatalog);
            checks.Add("direct scene startup remains independent");
            stage = "complete";
        }

        private IEnumerator RunDeployment()
        {
            stage = "deployment-open";
            Require(session.TryEnterBattlefield("open-battle", false, out string error), error);
            yield return WaitForLoad();
            var controller = session.Controller; var manager = controller.manager;
            var source = manager.scenarioConfig;
            var original = new List<string>();
            foreach (var unit in source.unitTypes) { original.Add(JsonUtility.ToJson(unit)); original.Add(JsonUtility.ToJson(unit.spawnConfig)); }
            var editor = manager.GetComponent<WarSandboxRuntimeDeployment>();
            var hud = manager.GetComponent<WarSandboxDeploymentHUD>();
            hud.RequestEdit(); yield return null;
            Require(editor.IsEditing, editor.Error);
            yield return Screenshot("deployment-desktop.png");
            Screen.SetResolution(560, 800, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Screenshot("deployment-narrow.png");
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            var invalid = editor.Draft[0]; invalid.center = editor.Draft[1].center; editor.Draft.Set(0, invalid);
            var oldBuffers = manager.Buffers;
            Require(!editor.TryApply(out _) && manager.Buffers == oldBuffers && oldBuffers.IsAllocated, "Invalid deployment changed GPU state.");
            Require(!controller.StartDefaultBattle(), "Editing did not block battle commands.");
            editor.Draft.Undo();
            for (int i = 0; i < editor.Draft.Count; i++)
            {
                var entry = editor.Draft[i]; entry.count = 64; entry.manualSize = Vector3.zero; entry.density = 0.5f; entry.aspect = 2;
                entry.center = entry.teamId == 0 ? new Vector3(i == 0 ? -50 : -28, 0, 0) :
                    entry.teamId == 1 ? new Vector3(i == 2 ? 50 : 28, 0, 0) : new Vector3(0, 0, i == 4 ? 70 : 44);
                editor.Draft.Set(i, entry);
            }
            Require(editor.TryValidate(out error), error);
            yield return Screenshot("deployment-compact.png");
            Require(editor.TryApply(out error), error);
            yield return null;
            Require(!oldBuffers.IsAllocated && manager.UnitTypes.TotalAgentCount == 384, "Edited army did not rebuild GPU buffers.");
            Require(manager.scenarioConfig != source, "Player deployment still uses the source scenario.");
            stage = "deployment-natural-battle";
            var camera = manager.cullingCamera;
            camera.transform.position = new Vector3(0, 160, -120); camera.transform.LookAt(new Vector3(0, 0, 20));
            Require(controller.StartDefaultBattle(), "Edited battle did not start.");
            float start = Time.realtimeSinceStartup;
            while (!controller.BattleResult.valid && Time.realtimeSinceStartup - start < 180)
            { Require(errors.Count == 0, string.Join("\n", errors)); yield return null; }
            Require(controller.BattleResult.valid, "Edited battle did not settle naturally within 180 seconds.");
            yield return Screenshot("deployment-settlement.png");
            battles.Add(new FullBattle { entryId = "player-deployment", mode = controller.gameMode.ToString(),
                phase = controller.Phase.ToString(), victoryReason = controller.BattleResult.victoryReason.ToString(),
                winnerTeamId = controller.BattleResult.winnerTeamId, simulationSeconds = controller.BattleResult.battleSeconds,
                realSeconds = Time.realtimeSinceStartup - start, initial = new[] { 128, 128, 128 },
                survivors = new[] { controller.GetAliveUnitCount(0), controller.GetAliveUnitCount(1), controller.GetAliveUnitCount(2) } });
            controller.ResetBattle();
            Require(!controller.BattleResult.valid && controller.GetAliveUnitCount(0) == 128 && controller.GetAliveUnitCount(2) == 128,
                "Restart retained losses or results.");
            Require(controller.StartDefaultBattle(), "Edited deployment could not restart.");
            yield return new WaitForSecondsRealtime(0.5f);
            Require(!editor.TryBeginEdit(false, out _), "Running battle returned to edit without confirmation.");
            Require(editor.TryBeginEdit(true, out error), error);
            var before = manager.scenarioConfig;
            var changed = editor.Draft[0]; changed.count = 32; editor.Draft.Set(0, changed);
            Require(editor.TryApply(out error), error); yield return null;
            Require(before == null && manager.UnitTypes.TotalAgentCount == 352, "Second deployment retained old runtime configs.");
            Require(controller.StartDefaultBattle(), "Second edited battle could not start.");
            yield return new WaitForSecondsRealtime(0.5f);
            int index = 0;
            foreach (var unit in source.unitTypes)
            { Require(JsonUtility.ToJson(unit) == original[index++], "Source unit was modified."); Require(JsonUtility.ToJson(unit.spawnConfig) == original[index++], "Source spawn was modified."); }
            var owned = manager.scenarioConfig; var buffers = manager.Buffers;
            Require(session.TryReturnToMenu(true, out error), error); yield return WaitForLoad();
            Require(!buffers.IsAllocated && owned == null, "Return leaked runtime deployment resources.");
            checks.Add("deployment: edit, validation, natural battle, full-strength restart, reedit, second battle, source isolation, GPU release");
        }

        private IEnumerator RunFullBattles()
        {
            foreach (string id in new[] { "open-battle", "walled-point" })
            {
                stage = "full-battle-" + id;
                Require(session.TryEnterBattlefield(id, false, out string error), error);
                yield return WaitForLoad();
                Require(session.State == WarSandboxEntryState.Battle, session.Error);
                var controller = session.Controller;
                var manager = controller.manager;
                var initial = new int[controller.ArmyCount];
                for (int team = 0; team < initial.Length; team++) initial[team] = controller.GetArmy(team).initialUnitCount;
                string scenarioBefore = JsonUtility.ToJson(manager.scenarioConfig);
                var unitJson = new string[manager.scenarioConfig.unitTypes.Length];
                var spawnJson = new string[unitJson.Length];
                for (int i = 0; i < unitJson.Length; i++)
                {
                    unitJson[i] = JsonUtility.ToJson(manager.scenarioConfig.unitTypes[i]);
                    spawnJson[i] = JsonUtility.ToJson(manager.scenarioConfig.unitTypes[i].spawnConfig);
                }
                Require(controller.StartDefaultBattle(), "Full battle could not start.");
                float started = Time.realtimeSinceStartup, nextLog = started;
                while (!controller.BattleResult.valid && Time.realtimeSinceStartup - started < 600f)
                {
                    Require(errors.Count == 0, string.Join("\n", errors));
                    if (Time.realtimeSinceStartup >= nextLog)
                    {
                        int[] live = new int[initial.Length];
                        for (int team = 0; team < live.Length; team++) live[team] = controller.GetAliveUnitCount(team);
                        Debug.Log("FULL_BATTLE " + id + " simulation=" + controller.TelemetrySnapshot.battleSeconds.ToString("F1") +
                            " alive=" + string.Join(",", live) + " capture=" + controller.ControlPointCaptureProgress.ToString("F2"));
                        nextLog = Time.realtimeSinceStartup + 15f;
                    }
                    yield return null;
                }
                var result = controller.BattleResult;
                Require(result.valid, "Full battle did not reach a natural settlement within 600 real seconds: " + id);
                Require(!manager.IsBattleRunning && controller.Phase != WarSandboxBattlePhase.Running, "Settlement did not pause simulation.");
                Require(result.battleSeconds > 0, "Settlement has no elapsed battle time.");
                var survivors = new int[initial.Length];
                for (int team = 0; team < survivors.Length; team++)
                {
                    survivors[team] = controller.GetAliveUnitCount(team);
                    Require(survivors[team] >= 0 && survivors[team] <= initial[team], "Invalid surviving army count.");
                }
                if (result.phase == WarSandboxBattlePhase.Draw)
                    Require(result.winnerTeamId == -1 && Array.TrueForAll(survivors, count => count == 0), "Invalid draw.");
                else
                {
                    Require(result.winnerTeamId >= 0 && result.winnerTeamId < initial.Length && survivors[result.winnerTeamId] > 0,
                        "Winner is not a surviving deployed army.");
                    if (result.victoryReason == WarSandboxVictoryReason.Annihilation)
                        for (int team = 0; team < survivors.Length; team++)
                            Require(team == result.winnerTeamId || survivors[team] == 0, "Annihilation has a surviving enemy.");
                    else
                        Require(controller.gameMode == WarSandboxGameMode.ControlPoint && controller.ControlPointCaptureProgress >= 1 &&
                            controller.ControlPointOwnerTeamId == result.winnerTeamId, "Capture winner does not own the objective.");
                }
                battles.Add(new FullBattle
                {
                    entryId = id, mode = controller.gameMode.ToString(), phase = result.phase.ToString(),
                    victoryReason = result.victoryReason.ToString(), winnerTeamId = result.winnerTeamId,
                    simulationSeconds = result.battleSeconds, realSeconds = Time.realtimeSinceStartup - started,
                    initial = initial, survivors = survivors
                });
                string settled = JsonUtility.ToJson(result);
                yield return Screenshot(id + "-settlement.png");
                Require(!controller.IssueOrder(ArmyOrder.Attack(0)) && !controller.IssueMoveOrder(0, Vector3.zero, true),
                    "Ordinary orders reopened a finished battle.");
                controller.StartOrResumeBattle();
                yield return new WaitForSecondsRealtime(0.6f);
                Require(!manager.IsBattleRunning && JsonUtility.ToJson(controller.BattleResult) == settled, "Settlement changed after completion.");
                Require(!session.RequiresEndConfirmation, "Finished battle still requires abandon confirmation.");
                controller.ResetBattle();
                Require(!controller.BattleResult.valid && controller.Phase == WarSandboxBattlePhase.Setup &&
                    controller.SimulationSpeed == 1 && controller.ControlPointCaptureProgress == 0, "Settlement state survived reset.");
                for (int team = 0; team < initial.Length; team++)
                    Require(controller.GetAliveUnitCount(team) == initial[team] && !controller.GetArmy(team).hasOrder &&
                        controller.GetMoveRoutePointCount(team) == 0, "Restart retained losses or orders.");
                Require(controller.StartDefaultBattle(), "Battle cannot restart after a natural settlement.");
                yield return new WaitForSecondsRealtime(1f);
                Require(!controller.BattleResult.valid && manager.IsBattleRunning, "Restart reused the previous settlement.");
                controller.ResetBattle();
                Require(JsonUtility.ToJson(manager.scenarioConfig) == scenarioBefore, "Scenario changed during a full battle.");
                for (int i = 0; i < unitJson.Length; i++)
                    Require(JsonUtility.ToJson(manager.scenarioConfig.unitTypes[i]) == unitJson[i] &&
                        JsonUtility.ToJson(manager.scenarioConfig.unitTypes[i].spawnConfig) == spawnJson[i], "Full battle rewrote deployment assets.");
                var buffers = manager.Buffers;
                Require(session.TryReturnToMenu(false, out error), error);
                yield return WaitForLoad();
                Require(session.State == WarSandboxEntryState.Menu && !buffers.IsAllocated && session.Controller == null,
                    "Completed battle did not release its scene and GPU resources.");
                checks.Add(id + ": natural settlement, frozen result, restart, source isolation, return");
            }
        }

        private IEnumerator WaitForLoad()
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!session.IsLoading, "Scene loading timed out.");
            yield return null; yield return null;
            Require(errors.Count == 0, string.Join("\n", errors));
        }

        private IEnumerator PreviewScreenshot(string name, MassEngineManager manager)
        {
            Camera camera = manager.cullingCamera;
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            var ui = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            var hidden = new List<MonoBehaviour>();
            foreach (var component in ui)
                if (component.enabled && (component is WarSandboxCommandHUD || component is WarSandboxFrontEnd || component is BattleTelemetryHUD))
                { component.enabled = false; hidden.Add(component); }
            camera.transform.position = new Vector3(0, 480, -380);
            camera.transform.LookAt(new Vector3(0, 0, 80));
            for (int i = 0; i < 4; i++) yield return null;
            yield return Screenshot(name);
            camera.transform.SetPositionAndRotation(position, rotation);
            foreach (var component in hidden) if (component != null) component.enabled = true;
        }

        private IEnumerator Screenshot(string name)
        {
            string path = Path.Combine(output, name);
            if (File.Exists(path)) File.Delete(path);
            yield return new WaitForSecondsRealtime(0.15f);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(0.1f);
            Require(File.Exists(path), "Screenshot was not produced: " + name);
            var image = new Texture2D(2, 2);
            try
            {
                Require(image.LoadImage(File.ReadAllBytes(path)), "Screenshot could not be decoded: " + name);
                Require(image.width == Screen.width && image.height == Screen.height, "Screenshot resolution is stale: " + name);
                Color32[] pixels = image.GetPixels32();
                Color32 background = pixels[0];
                bool nonBlank = false;
                int stepX = Mathf.Max(1, image.width / 96), stepY = Mathf.Max(1, image.height / 96);
                for (int y = 0; y < image.height && !nonBlank; y += stepY)
                    for (int x = 0; x < image.width && !nonBlank; x += stepX)
                    {
                        Color32 pixel = pixels[y * image.width + x];
                        nonBlank = Mathf.Abs(pixel.r - background.r) + Mathf.Abs(pixel.g - background.g) +
                            Mathf.Abs(pixel.b - background.b) > 24;
                    }
                Require(nonBlank, "Screenshot is blank: " + name);
                checks.Add("nonblank screenshot: " + name + " (" + image.width + "x" + image.height + ")");
            }
            finally { Destroy(image); }
        }

        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error ?? "Entry smoke assertion failed.");
        }

        private void TrackError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
        }

        private void Finish(bool passed, string error)
        {
            var report = new Report { passed = passed, stage = stage, error = error, checks = checks.ToArray(), battles = battles.ToArray() };
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("ENTRY_SMOKE " + (passed ? "PASS" : "FAIL") + " " + stage + " " + error);
            Application.logMessageReceived -= TrackError;
            Application.Quit(passed ? 0 : 1);
        }
    }
}
#endif
