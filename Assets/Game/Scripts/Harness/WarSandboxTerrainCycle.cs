using UnityEngine;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEngine.Rendering;
using UnityEngine.UI;
#endif

namespace MassEngine.Game
{
    /// <summary>Opt-in independent-player terrain and launch-preset evidence. Normal gameplay never starts this probe.</summary>
    [DefaultExecutionOrder(-150)]
    public sealed partial class WarSandboxTerrainCycle : MonoBehaviour
    {
        public WarSandboxSceneSession session;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable] private sealed class BattleEvidence
        {
            public string slot;
            public int[] initial, survivors, restart;
            public WarSandboxBattleResult result;
            public float realSeconds;
            public int projectiles, flowFields;
            public double flowCpuMilliseconds;
        }
        [Serializable] private sealed class Report
        {
            public bool completed, passed, sourceAssetsUnchanged, plansUnchanged, crossProcess, lightLoop;
            public string phase, stage, error, runId, startedUtc, processStartedUtc, buildGuid, unity, cpu, gpu, api;
            public int processId, gpuCapacityMB, releases, appliedPlans, renderedFrames, targetFrameRate;
            public List<string> actions = new List<string>();
            public List<string> errors = new List<string>();
            public List<BattleEvidence> battles = new List<BattleEvidence>();
            public List<PerformanceSample> performance = new List<PerformanceSample>();
            public List<GpuWorkloadSnapshot> gpuWorkloads = new List<GpuWorkloadSnapshot>();
            public CongestionEvidence congestion;
            public CommandEvidence commands;
            public ReadabilityEvidence readability;
            public RangedEvidence ranged;
            public PlayabilityEvidence playability;
            public ReleaseEvidence release;
        }
        [Serializable] private sealed class Receipt
        {
            public bool passed;
            public string runId, processStartedUtc, buildGuid, hashA, hashB;
            public int processId;
        }
        private readonly Report report = new Report();
        private readonly Dictionary<UnityEngine.Object, string> sources = new Dictionary<UnityEngine.Object, string>();
        private readonly Dictionary<string, string> planHashes = new Dictionary<string, string>();
        private static WarSandboxTerrainCycle instance;
        private WarSandboxBattleController controller;
        private MassEngineManager manager;
        private WarSandboxRuntimeDeployment deployment;
        private ScenarioConfig sourceScenario;
        private string output, planDirectory;
        private Camera renderCamera;
        private RenderTexture renderTarget;
        private int renderedFrames;
        private bool finished, ownsOutput;
        private const string SlotA = "terrain-a", SlotB = "terrain-b", ReceiptName = "seed-receipt.json";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Awake()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--terrain-cycle") < 0) { enabled = false; return; }
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            string phase = Argument("--terrain-cycle-phase=");
            if (phase != "presets" && phase != "presets-ui" && phase != "motion-review" && phase != "congestion" && phase != "commands" && phase != "readability" && phase != "ranged" && phase != "playability-seed" && phase != "playability-reload" && phase != "official-catalog") session.enterDefaultOnStart = false;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            // This functional smoke mode is deliberately capped, never FPS evidence.
            Application.targetFrameRate = phase == "motion-review" || phase == "congestion" || phase == "commands" || phase == "readability" || phase == "ranged" || phase == "official-catalog" || IsLightLoop ? 30 : -1;
        }

        private void Start()
        {
            if (instance != this) return;
            Application.logMessageReceived += TrackError;
            RenderPipelineManager.endCameraRendering += CameraRendered;
            StartCoroutine(Guard(Run()));
        }

        private IEnumerator Run()
        {
            InitializeArguments();
            Capture(session.catalog);
            foreach (var entry in session.catalog.entries) { Capture(entry.rules); Capture(entry.terrainSurface); }
            foreach (var entry in session.catalog.templates) CaptureUnit(entry.config);
            if (report.phase == "performance") { yield return RunPerformance(); yield break; }
            if (IsOfficialRun) { yield return RunOfficialCatalog(); yield break; }
            if (IsPresetRun) { yield return RunPresets(); yield break; }
            if (IsPlayabilityRun) { yield return RunPlayability(); yield break; }
            if (IsReleaseRun) { yield return RunReleaseValidation(); yield break; }

            if (report.phase == "seed")
            {
                yield return Enter(IsLightLoop ? "launch-mountain" : "mountain-battle", true);
                yield return Click("edit");
                Require(deployment.IsEditing, "Deployment UI did not open.");
                var before = deployment.Draft[0];
                var invalid = before; invalid.center = new Vector3(-80, 0, 73);
                deployment.Draft.Set(0, invalid);
                var buffers = manager.Buffers;
                Require(!deployment.TryApply(out string error) && !string.IsNullOrEmpty(error), "Cliff footprint accepted.");
                Require(ReferenceEquals(manager.Buffers, buffers) && buffers.IsAllocated && deployment.IsEditing,
                    "Rejected deployment replaced the live GPU world.");
                Require(deployment.Draft.Undo() && deployment.TryValidate(out error), error);
                report.actions.Add("Rejected a cliff footprint before GPU mutation; Undo restored a valid draft.");
                foreach (string slot in new[] { SlotA, SlotB })
                {
                    yield return Click("roster-item-0");
                    Field("input-count", slot == SlotA ? "80" : "48");
                    yield return Click("field-update");
                    Require(deployment.Draft[0].count == CountFor(slot), "UI count did not update.");
                    if (IsLightLoop)
                    {
                        var rules = deployment.Draft.Rules; rules.controlPointCaptureSeconds = slot == SlotA ? 17.5f : 23.5f;
                        deployment.Draft.Replace(deployment.Draft.Snapshot(), rules);
                        report.actions.Add("Draft API: set distinct persisted rule value for " + slot);
                    }
                    yield return Click("deployment-plans");
                    Field("plans-slot", slot); Field("plans-name", "M6.3 山地方案 " + slot);
                    yield return Click("plans-save");
                    Require(deployment.PlanStore.TryLoad(slot, out var plan, out error), error);
                    Require(plan.entries[0].count == CountFor(slot), "Saved count is wrong.");
                    planHashes.Add(slot, HashFile(PlanPath(slot)));
                    yield return Click("plans-back");
                }
                yield return Click("deployment-cancel");
                yield return Leave(false);
            }
            else
            {
                var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(planDirectory, ReceiptName)));
                Require(receipt != null && receipt.passed && receipt.buildGuid == report.buildGuid,
                    "Reload requires a passed seed receipt from this build.");
                Require(receipt.runId != report.runId && (receipt.processId != report.processId || receipt.processStartedUtc != report.processStartedUtc),
                    "Reload must run in a separate process.");
                planHashes.Add(SlotA, receipt.hashA); planHashes.Add(SlotB, receipt.hashB);
                VerifyPlans(); report.crossProcess = true;
            }

            string naturalSlot = report.phase == "seed" ? SlotA : SlotB;
            foreach (string slot in new[] { naturalSlot, naturalSlot == SlotA ? SlotB : SlotA })
            {
                Stage("load-" + slot);
                yield return Enter(IsLightLoop ? "launch-mountain" : "mountain-battle", true);
                yield return LoadPlan(slot);
                if (slot == naturalSlot) yield return NaturalBattle(slot);
                else
                {
                    yield return Click("reset"); VerifyFullStrength();
                    yield return Click("start");
                    Require(manager.IsBattleRunning, "The second plan cannot start.");
                    yield return new WaitForSecondsRealtime(1);
                }
                yield return Leave(true);
                VerifySources(); VerifyPlans();
            }
            // Historical mode keeps its 110k check; the explicit light loop never enters that scene.
            int flatPopulation = IsLightLoop ? 512 : 110000;
            Stage(IsLightLoop ? "flat-512-switch" : "flat-110k-switch");
            yield return Enter(IsLightLoop ? "launch-open" : "open-battle", false);
            Require(manager.UnitTypes.TotalAgentCount == flatPopulation && !manager.TerrainGpuAllocated && manager.TerrainSurface == null,
                "Flat scene inherited terrain or changed the shipped population.");
            yield return Click("start");
            yield return new WaitForSecondsRealtime(3);
            Require(manager.IsBattleRunning, "Flat battle did not run.");
            yield return CaptureWorld(IsLightLoop ? "flat-512.png" : "flat-110k.png");
            yield return Click("reset"); VerifyFullStrength();
            Require(manager.UnitTypes.TotalAgentCount == flatPopulation, "Flat restart changed its population.");
            yield return Leave(false);
            yield return Enter(IsLightLoop ? "launch-mountain" : "mountain-battle", true);
            Require(manager.UnitTypes.TotalAgentCount == 384 && manager.TerrainGpuAllocated, "Fresh terrain inherited a saved deployment.");
            yield return CaptureWorld("fresh-terrain.png");
            yield return Leave(false);
            VerifySources(); VerifyPlans();
            Stage("complete");
        }

        private void InitializeArguments()
        {
            report.phase = Argument("--terrain-cycle-phase=");
            report.lightLoop = IsLightLoop;
            Require(!IsLightLoop || report.phase == "seed" || report.phase == "reload" || IsPlayabilityRun, "Light mode only supports persistence loops.");
            Require(report.phase == "seed" || report.phase == "reload" || report.phase == "performance" || IsPresetRun || IsPlayabilityRun || IsReleaseRun,
                "Specify seed, reload, performance, presets, presets-ui, motion-review, congestion, commands, readability, ranged, official-catalog, playability-seed, playability-reload, release-performance, desktop-performance, gpu-breakdown or stability phase.");
            output = AbsoluteArgument("--terrain-cycle-output=");
            Require(!Directory.Exists(output) && !File.Exists(output), "Evidence directory must be fresh.");
            string defaultPlans = Path.GetFullPath(new WarSandboxLocalPlanStore().DirectoryPath);
            Require(Disjoint(output, defaultPlans), "Evidence cannot overlap player plans.");
            if (report.phase != "performance" && !IsReleaseRun)
            {
                planDirectory = AbsoluteArgument("--terrain-cycle-plans=");
                Require(Disjoint(planDirectory, defaultPlans) && Disjoint(output, planDirectory), "Validation plans must be isolated.");
                if (report.phase == "seed" || IsPresetRun || report.phase == "playability-seed") Require(!Directory.Exists(planDirectory) && !File.Exists(planDirectory), "Seed plans directory must be fresh.");
                else Require(File.Exists(Path.Combine(planDirectory, IsPlayabilityRun ? PlayabilityReceiptName : ReceiptName)), "Seed receipt missing.");
            }
            if (IsPlayabilityRun) InitializePlayabilitySettings(defaultPlans);
            else if (IsLightLoop)
            {
                settingsFile = AbsoluteArgument("--war-sandbox-settings-file=");
                Require(Disjoint(settingsFile, Application.persistentDataPath) && Disjoint(settingsFile, defaultPlans) &&
                    Disjoint(settingsFile, output) && Disjoint(settingsFile, planDirectory) && !File.Exists(settingsFile), "Light terrain mode requires fresh isolated settings.");
            }
            if (IsReleaseRun) InitializeReleaseSettings();
            if (report.phase == "motion-review" || report.phase == "congestion" || report.phase == "commands" || report.phase == "readability" || report.phase == "ranged" || IsOfficialRun)
            {
                settingsFile = AbsoluteArgument("--war-sandbox-settings-file=");
                Require(Disjoint(settingsFile, Application.persistentDataPath) && Disjoint(settingsFile, output) &&
                    Disjoint(settingsFile, planDirectory) && !File.Exists(settingsFile) && !Directory.Exists(settingsFile),
                    "Motion review needs fresh isolated settings.");
            }
            report.targetFrameRate = Application.targetFrameRate;
            Directory.CreateDirectory(output);
            ownsOutput = true;
            report.runId = Guid.NewGuid().ToString("N"); report.startedUtc = DateTime.UtcNow.ToString("O");
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            { report.processId = process.Id; report.processStartedUtc = process.StartTime.ToUniversalTime().ToString("O"); }
            report.buildGuid = Application.buildGUID; report.unity = Application.unityVersion;
            report.cpu = SystemInfo.processorType; report.gpu = SystemInfo.graphicsDeviceName;
            report.gpuCapacityMB = SystemInfo.graphicsMemorySize; report.api = SystemInfo.graphicsDeviceType.ToString();
            Stage("initialized");
        }

        private IEnumerator Enter(string id, bool terrain)
        {
            Require(session.TryEnterBattlefield(id, false, out string error), error);
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            BindBattle(terrain);
            yield return null;
        }

        private void BindBattle(bool terrain)
        {
            controller = session.Controller; manager = controller.manager;
            deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            Require(deployment != null && manager.TerrainGpuAllocated == terrain, "Incorrect scene/provider.");
            Require(!IsLightLoop || manager.UnitTypes.TotalAgentCount <= 1000, "Light validation must not enter a large battlefield.");
            if (planDirectory != null) deployment.PlanStore = new WarSandboxLocalPlanStore(planDirectory);
            sourceScenario = manager.scenarioConfig; Capture(sourceScenario); Capture(manager.systemConfig);
            foreach (var unit in sourceScenario.unitTypes) CaptureUnit(unit);
            ConfigureCamera(terrain);
        }

        private IEnumerator LoadPlan(string slot)
        {
            yield return Click("edit");
            Require(deployment.PlanStore.TryLoad(slot, out var plan, out string error), error);
            VerifyFailedPlanLoads(plan);
            yield return Click("deployment-plans");
            Require(deployment.PlanStore.TryListSlots(out var slots, out error), error);
            int index = Array.IndexOf(slots, slot); Require(index >= 0, "Missing saved slot.");
            yield return Click("plan-item-" + index);
            yield return Click("plans-load"); yield return Click("plans-confirm-yes");
            VerifyLoadedPlan(plan);
            var expected = deployment.Draft.Snapshot();
            Require(expected.Length == plan.entries.Length && expected[0].count == CountFor(slot), "Saved draft was not restored.");
            for (int i = 0; i < expected.Length; i++)
            {
                Require(session.catalog.TryGetTemplateId(expected[i].template, out string id, out int revision), "Template ID missing.");
                Require(JsonUtility.ToJson(WarSandboxPlanEntry.From(expected[i], id, revision)) == JsonUtility.ToJson(plan.entries[i]),
                    "Saved formation differs at " + i);
            }
            var oldBuffers = manager.Buffers;
            yield return Click("deployment-apply");
            Require(!deployment.IsEditing && !oldBuffers.IsAllocated && manager.scenarioConfig != sourceScenario,
                "Apply did not replace the GPU allocation with an owned scenario.");
            int total = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                var actual = manager.scenarioConfig.unitTypes[i]; var entry = expected[i];
                Require(actual != entry.template && actual.spawnConfig != entry.template.spawnConfig &&
                    actual.teamId == entry.teamId && actual.spawnConfig.unitCount == entry.count, "Runtime formation isolation failed.");
                Require(manager.TerrainSurface.TrySample(new Vector2(entry.center.x, entry.center.z), out var sample) &&
                    Mathf.Abs(actual.spawnConfig.spawnCenter.y - sample.Position.y) < .001f, "Runtime spawn was not grounded.");
                total += entry.count;
            }
            Require(manager.UnitTypes.TotalAgentCount == total, "Applied population differs.");
            VerifyAppliedPlan(plan);
            VerifyFullStrength(); VerifySources(); VerifyPlans(); report.appliedPlans++;
            yield return CaptureWorld(slot + "-setup.png");
        }

        private IEnumerator NaturalBattle(string slot)
        {
            Stage("natural-battle-" + slot);
            var evidence = new BattleEvidence { slot = slot, initial = ArmyCounts(true) };
            report.battles.Add(evidence);
            float started = Time.realtimeSinceStartup, nextLog = started;
            yield return Click("start");
            while (!controller.BattleResult.valid && Time.realtimeSinceStartup - started < 480)
            {
                Require(manager.IsBattleRunning && controller.SimulationSpeed == 1 && Time.timeScale == 1,
                    "Normal battle stopped or changed speed before settlement.");
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    evidence.survivors = ArmyCounts(false); evidence.realSeconds = Time.realtimeSinceStartup - started;
                    Debug.Log("TERRAIN_CYCLE " + slot + " seconds=" + evidence.realSeconds.ToString("F1") + " alive=" + string.Join(",", evidence.survivors));
                    WriteReport(); nextLog = Time.realtimeSinceStartup + 20;
                }
                yield return null;
            }
            evidence.result = controller.BattleResult; evidence.survivors = ArmyCounts(false);
            evidence.realSeconds = Time.realtimeSinceStartup - started; evidence.projectiles = manager.TotalLaunchedProjectiles;
            evidence.flowFields = manager.TerrainCompletedFields; evidence.flowCpuMilliseconds = manager.TerrainTotalSolveMilliseconds;
            Require(evidence.result.valid && evidence.result.battleSeconds > 0 && controller.TelemetrySnapshot.valid && !manager.IsBattleRunning,
                "No natural settlement in 480 seconds; no result was injected.");
            bool annihilation = evidence.result.victoryReason == WarSandboxVictoryReason.Annihilation;
            if (!annihilation) Require(controller.gameMode == WarSandboxGameMode.ControlPoint &&
                controller.ControlPointOwnerTeamId == evidence.result.winnerTeamId && controller.ControlPointCaptureProgress >= 1,
                "Winner does not match completed point capture.");
            for (int team = 0; team < evidence.initial.Length; team++)
            {
                int alive = evidence.survivors[team];
                Require(alive >= 0 && alive <= evidence.initial[team], "Invalid survivor count.");
                Require(team == evidence.result.winnerTeamId ? alive > 0 : (!annihilation || alive == 0), "Winner does not match survivors.");
            }
            string result = JsonUtility.ToJson(evidence.result);
            yield return new WaitForSecondsRealtime(.4f);
            Require(JsonUtility.ToJson(controller.BattleResult) == result && !manager.IsBattleRunning, "Result did not freeze.");
            if (IsPlayabilityRun)
            {
                VerifyResultRows(evidence.initial.Length, false);
                yield return CapturePresetUI(slot + "-settled-ui.png");
            }
            yield return CaptureWorld(slot + "-settled.png");
            yield return Click("reset"); VerifyFullStrength(); evidence.restart = ArmyCounts(false);
            for (int team = 0; team < evidence.initial.Length; team++)
                Require(evidence.restart[team] == evidence.initial[team], "Restart lost troops.");
            yield return Click("start"); Require(manager.IsBattleRunning, "Restart cannot start again.");
            yield return new WaitForSecondsRealtime(1);
        }

        private IEnumerator Leave(bool ownedScenario, string button = "nav-menu")
        {
            var buffers = manager.Buffers; var previousManager = manager;
            var owned = new List<UnityEngine.Object>();
            if (ownedScenario)
            {
                owned.Add(manager.scenarioConfig);
                foreach (var unit in manager.scenarioConfig.unitTypes) { owned.Add(unit); owned.Add(unit.spawnConfig); }
            }
            ReleaseCamera();
            yield return Click(button);
            if (session.ConfirmationOpen) yield return Click("modal-confirm");
            yield return WaitForLoad(WarSandboxEntryState.Menu);
            Require(!buffers.IsAllocated && !previousManager.TerrainGpuAllocated && previousManager == null && session.Controller == null &&
                FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length == 0, "Scene/GPU resources survived return.");
            Require(owned.TrueForAll(value => value == null), "Runtime scenario copies survived return.");
            report.releases++; manager = null; controller = null; deployment = null;
        }

        private void VerifyFullStrength()
        {
            Require(controller.Phase == WarSandboxBattlePhase.Setup && !controller.BattleResult.valid && !manager.IsBattleRunning,
                "Reset retained battle state.");
            for (int team = 0; team < controller.ArmyCount; team++)
                Require(controller.GetAliveUnitCount(team) == controller.GetArmy(team).initialUnitCount &&
                    !controller.GetArmy(team).hasOrder && controller.GetMoveRoutePointCount(team) == 0, "Reset retained casualties/orders.");
        }
        private int[] ArmyCounts(bool initial)
        {
            var counts = new int[controller.ArmyCount];
            for (int team = 0; team < counts.Length; team++) counts[team] = initial ? controller.GetArmy(team).initialUnitCount : controller.GetAliveUnitCount(team);
            return counts;
        }
        private void Capture(UnityEngine.Object value) { if (value != null && !sources.ContainsKey(value)) sources.Add(value, JsonUtility.ToJson(value)); }
        private void CaptureUnit(UnitTypeConfig unit)
        {
            Capture(unit); Capture(unit.spawnConfig); Capture(unit.combatConfig); Capture(unit.renderConfig);
            Capture(unit.movementConfig); Capture(unit.animationConfig); Capture(unit.flockingConfig);
            if (unit.renderConfig != null) Capture(unit.renderConfig.vatProfile);
        }
        private void VerifySources()
        {
            foreach (var pair in sources) Require(pair.Key != null && JsonUtility.ToJson(pair.Key) == pair.Value, "Source asset changed: " + pair.Key);
            report.sourceAssetsUnchanged = true;
        }
        private void VerifyPlans()
        {
            foreach (var pair in planHashes) Require(HashFile(PlanPath(pair.Key)) == pair.Value, "Saved plan changed: " + pair.Key);
            report.plansUnchanged = true;
        }
        private IEnumerator Click(string name)
        {
            yield return null;
            Button found = null;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name && button.isActiveAndEnabled) { Require(found == null, "Ambiguous button " + name); found = button; }
            Require(found != null && found.IsInteractable(), "Missing/disabled UI button: " + name);
            report.actions.Add("uGUI Button.onClick: " + name); found.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.25f);
        }
        private void Field(string name, string value)
        {
            InputField found = null;
            foreach (var field in FindObjectsByType<InputField>(FindObjectsSortMode.None))
                if (field.name == name && field.isActiveAndEnabled) { Require(found == null, "Ambiguous field " + name); found = field; }
            Require(found != null && found.IsInteractable(), "Missing UI field " + name);
            report.actions.Add("uGUI InputField.text: " + name + "=" + value); found.text = value;
        }
        private IEnumerator WaitForLoad(WarSandboxEntryState expected)
        {
            float deadline = Time.realtimeSinceStartup + 90;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!session.IsLoading && session.State == expected, "Scene transition failed: " + session.Error);
        }
        private IEnumerator Guard(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine); string failure = null;
            while (stack.Count > 0 && failure == null)
            {
                object next = null; bool moved = false;
                try
                {
                    Require(report.errors.Count == 0, string.Join("\n", report.errors));
                    moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current;
                }
                catch (Exception ex) { failure = ex.ToString(); }
                if (failure != null) break;
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            ReleaseCamera();
            if (failure != null && session != null && !session.IsLoading && session.State == WarSandboxEntryState.Battle)
            {
                session.TryReturnToMenu(true, out _);
                float deadline = Time.realtimeSinceStartup + 20;
                while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            }
            Finish(failure);
        }
        private void Finish(string error)
        {
            if (finished) return; finished = true;
            report.completed = true; report.error = error; report.passed = error == null && report.errors.Count == 0;
            report.renderedFrames = renderedFrames;
            Application.logMessageReceived -= TrackError; RenderPipelineManager.endCameraRendering -= CameraRendered;
            if (report.passed && report.phase == "seed")
            {
                var receipt = new Receipt { passed = true, runId = report.runId, processId = report.processId,
                    processStartedUtc = report.processStartedUtc, buildGuid = report.buildGuid, hashA = planHashes[SlotA], hashB = planHashes[SlotB] };
                try { using (var writer = new StreamWriter(new FileStream(Path.Combine(planDirectory, ReceiptName), FileMode.CreateNew))) writer.Write(JsonUtility.ToJson(receipt, true)); }
                catch (Exception ex) { report.passed = false; report.error = ex.ToString(); }
            }
            if (report.passed && report.phase == "playability-seed")
            {
                try { WritePlayabilityReceipt(); }
                catch (Exception ex) { report.passed = false; report.error = ex.ToString(); }
            }
            WriteReport();
            Debug.Log("TERRAIN_CYCLE " + (report.passed ? "PASS" : "FAIL") + " " + report.phase + " " + report.stage + " " + report.error);
            Application.Quit(report.passed ? 0 : 1);
        }
        private void TrackError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || message.Contains("AsyncGPUReadback failed"))
                report.errors.Add(message + "\n" + trace);
        }
        private void Stage(string stage) { report.stage = stage; Debug.Log("TERRAIN_CYCLE stage=" + stage); WriteReport(); }
        private void WriteReport() { if (ownsOutput) File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true)); }
        private string PlanPath(string slot) => Path.Combine(planDirectory, slot + ".json");
        private static int CountFor(string slot) => slot == SlotA ? 80 : 48;
        private static string HashFile(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        private static string Argument(string prefix) { foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length); return null; }
        private static string AbsoluteArgument(string prefix)
        {
            string value = Argument(prefix);
            Require(!string.IsNullOrEmpty(value) && (value.StartsWith(@"\\", StringComparison.Ordinal) ||
                (value.Length > 3 && value[1] == ':' && (value[2] == '/' || value[2] == '\\'))), "Required absolute Windows directory: " + prefix);
            string full = Path.GetFullPath(value);
            Require(full.TrimEnd('\\', '/') != Path.GetPathRoot(full).TrimEnd('\\', '/'), "A volume root is not an isolated directory.");
            return full;
        }
        private static bool Disjoint(string a, string b)
        {
            a = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            b = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return !a.StartsWith(b, StringComparison.OrdinalIgnoreCase) && !b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }
        private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
#endif
    }
}
