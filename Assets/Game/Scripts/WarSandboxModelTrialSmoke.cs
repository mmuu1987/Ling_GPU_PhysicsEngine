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
    /// <summary>
    /// Attach to an independent root in the trial menu, not to the shipping entry smoke.
    /// Development player only: --model-trial-smoke --model-trial-phase=seed|reload
    /// --model-trial-output=<absolute directory> --model-trial-plan-directory=<isolated directory>.
    /// No argument means no navigation, persistence, file writes, camera or simulation changes.
    /// Evidence is automated API/UI-callback verification, never human visual acceptance.
    /// </summary>
    [DefaultExecutionOrder(-150)] // Session.Awake (-200) exists before us; all Awake calls precede Session.Start.
    [DisallowMultipleComponent]
    public sealed class WarSandboxModelTrialSmoke : MonoBehaviour
    {
        public WarSandboxSceneSession session;
        public WarSandboxBattlefieldCatalog expectedCatalog;
        public ScenarioConfig expectedSourceScenario;
        public UnitTypeConfig expectedTemplate;
        public string battlefieldId = "m54-model-trial";
        public string templateId = "m54-unitychan";
        public int templateRevision = 1;
        public string planSlot = "M54ModelTrial";
        [Tooltip("Real-time watchdog; supported values are 120 or 180 seconds. Never forces a result.")]
        public int watchdogSeconds = 180;
        [Tooltip("Fixture counts only; legacy defaults preserve64/96. Large-model scenes author8/12.")]
        public int initialTrialCount = 64, savedTrialCount = 96;
        [Tooltip("Optional isolated pre-battle validation; must reset before normal natural battle.")]
        public MonoBehaviour preBattleCheck;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private const string ReceiptName = "model-trial-seed-receipt.json";
        private static WarSandboxModelTrialSmoke instance;
        private readonly Report report = new Report();
        private string output, planDirectory, planPath, planHash;
        private bool finished;
        private ScenarioConfig source;
        private UnitTypeConfig trialTemplate;
        private WarSandboxRuntimeDeployment deployment;
        private WarSandboxBattleController controller;
        private SeedReceipt seedReceipt;

        [Serializable]
        private sealed class Report
        {
            public int schemaVersion = 1;
            public bool completed, passed;
            public bool humanAcceptance = false;
            public string phase, stage = "startup", error, startedUtc, finishedUtc, runId;
            public int processId;
            public string processStartedUtc, seedRunId, seedProcessStartedUtc;
            public int seedProcessId;
            public string unity, buildGuid, gpu, graphicsApi, outputDirectory, planDirectory;
            public bool developmentBuild, editor;
            public string battlefieldId, templateId, planSlot, planSha256;
            public int templateRevision, watchdogSeconds;
            public string sourceJsonBefore, sourceJsonAfter;
            public bool sourceJsonUnchanged, gpuReleased, runtimeCopiesReleased, planUnchanged;
            public string failureCleanup;
            public string method = "Real retained uGUI Button.onClick/InputField callbacks (not OS pointer input), real SceneSession/RuntimeDeployment/PlanStore. Invalid Apply is disabled in the UI, so its rejection is additionally tested via RuntimeDeployment.TryApply. Normal speed, default battle orders, natural GPU telemetry settlement only; no HP, damage, GPU state or result injection. Screenshots only check dimensions/nonblank pixels, not model appearance. One full battle plus a short full-strength restart. Human model/animation/LOD acceptance remains pending.";
            public WarSandboxPlanEntry[] savedDeployment;
            public BattleEvidence battle;
            public List<string> checks = new List<string>();
            public List<string> actions = new List<string>();
            public List<string> errors = new List<string>();
            public List<SourceEvidence> sources = new List<SourceEvidence>();
            public List<ModelEvidence> models = new List<ModelEvidence>();
            public List<ImageEvidence> screenshots = new List<ImageEvidence>();
        }

        [Serializable]
        private sealed class SeedReceipt
        {
            public bool passed;
            public string runId, processStartedUtc, buildGuid, battlefieldId, templateId, slot, planSha256;
            public int processId, templateRevision;
        }

        [Serializable]
        private sealed class BattleEvidence
        {
            public string mode, phase, victoryReason;
            public bool naturalSettlement;
            public int winnerTeamId = -1;
            public float simulationSeconds, realSeconds;
            // Array index is the engine teamId, including any unused army slots.
            public int[] initial, survivors, restartAlive;
        }

        [Serializable]
        private sealed class SourceEvidence
        {
            public string name, type, beforeSha256, afterSha256;
            public bool unchanged;
            [NonSerialized] public UnityEngine.Object asset;
        }

        [Serializable]
        private sealed class ModelEvidence
        {
            public string stage, templateId, unitName, renderConfig, profile, nearMesh, nearMaterial;
            public int formation, teamId, count, nearVertices;
        }

        [Serializable]
        private sealed class ImageEvidence
        {
            public string file, phase;
            public int width, height;
            public bool nonblank, resultValid;
            public float simulationSeconds;
            public int[] alive;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Awake()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--model-trial-smoke") < 0)
            { enabled = false; return; }
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            if (session == null) session = WarSandboxSceneSession.Instance;
            if (session != null) session.enterDefaultOnStart = false;
            // The builder must provide a separate root; do not reparent any gameplay objects.
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (instance != this) return;
            Application.logMessageReceived += TrackError;
            StartCoroutine(Guarded(Run()));
        }

        private IEnumerator Run()
        {
            InitializeArguments();
            Require(transform.parent == null, "Attach the smoke to an independent menu root.");
            Require(session != null && session == WarSandboxSceneSession.Instance && session.State == WarSandboxEntryState.Menu,
                "Trial smoke must start in its independent menu, before default entry.");
            Require(expectedCatalog == null || session.catalog == expectedCatalog, "Wrong trial catalog wiring.");
            Require(session.catalog != null && session.catalog.TryResolve(battlefieldId, WarSandboxSceneSession.CanLoadScene,
                out _, out _), "Trial battlefield is absent or invalid.");
            Require(session.catalog.TryResolveTemplate(templateId, templateRevision, out trialTemplate, out string error), error);
            Require(expectedTemplate == null || trialTemplate == expectedTemplate, "Stable template ID resolved to a different asset.");
            Require(trialTemplate.renderConfig != null && trialTemplate.renderConfig.vatProfile != null, "Trial template has no model profile.");
            CaptureSource(session.catalog);
            foreach (var entry in session.catalog.entries) CaptureSource(entry.rules);
            foreach (var template in session.catalog.templates) CaptureUnit(template.config);
            Application.runInBackground = true;
            float deadline = Time.realtimeSinceStartup + 30;
            while (!SplashScreen.isFinished && Time.realtimeSinceStartup < deadline) yield return null;
            Require(SplashScreen.isFinished, "Startup splash timed out.");
            Screen.SetResolution(1024, 768, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.7f);
            Stage("catalog");
            yield return Screenshot("catalog.png");
            int catalogIndex = Array.FindIndex(session.catalog.entries, e => e.id == battlefieldId);
            yield return ClickUI("card-" + catalogIndex + "-enter");
            yield return WaitForLoad(WarSandboxEntryState.Battle);
            Require(session.CurrentEntryId == battlefieldId, "UI entered a different battlefield.");
            controller = session.Controller;
            var manager = controller.manager;
            deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            Require(deployment != null && controller.GetComponent<WarSandboxDeploymentHUD>() != null, "Runtime deployment/uGUI is missing.");
            Require(manager.Buffers != null && manager.Buffers.IsAllocated && manager.UnitTypes != null, "Initial GPU deployment is unavailable.");
            Require(controller.Phase == WarSandboxBattlePhase.Setup && !manager.IsBattleRunning, "Trial auto-started before player orders.");
            source = manager.scenarioConfig;
            Require(source != null && (expectedSourceScenario == null || source == expectedSourceScenario), "Wrong trial source Scenario.");
            report.sourceJsonBefore = JsonUtility.ToJson(source);
            CaptureSource(source);
            foreach (var unit in source.unitTypes) CaptureUnit(unit);
            deployment.PlanStore = new WarSandboxLocalPlanStore(planDirectory);
            report.actions.Add("API: assign isolated RuntimeDeployment.PlanStore (no default player save directory).");
            Stage("template-selection");
            yield return ClickUI("edit");
            Require(deployment.IsEditing && deployment.SourceScenario == source, "UI did not create a source-isolated draft.");
            int trialIndex = -1, templateIndex = -1, enemyTemplateIndex = -1;
            for (int i = 0; i < deployment.Draft.Count; i++)
                if (deployment.Draft[i].template == trialTemplate) { Require(trialIndex < 0, "Expected one trial formation."); trialIndex = i; }
            for (int i = 0; i < deployment.Templates.Count; i++)
                if (deployment.Templates[i] == trialTemplate) templateIndex = i;
                else if (enemyTemplateIndex < 0) enemyTemplateIndex = i;
            Require(trialIndex >= 0 && templateIndex >= 0 && enemyTemplateIndex >= 0, "Trial/enemy must both be initial Scenario templates, not just Catalog registrations.");
            var baseline = deployment.Draft[trialIndex];
            Require(initialTrialCount > 0 && savedTrialCount > 0 && initialTrialCount != savedTrialCount && (baseline.count == initialTrialCount || baseline.count == savedTrialCount), "Unexpected authored trial count.");
            bool enemyPresent = false;
            for (int i = 0; i < deployment.Draft.Count; i++)
            {
                var entry = deployment.Draft[i];
                if (entry.teamId != baseline.teamId && entry.count > 0)
                {
                    enemyPresent = true;
                    Require(entry.template != trialTemplate && entry.template.renderConfig != trialTemplate.renderConfig &&
                        entry.template.renderConfig != null && entry.template.renderConfig.vatProfile != trialTemplate.renderConfig.vatProfile,
                        "Enemy must retain its distinct built-in model, not the trial RenderConfig/profile.");
                }
            }
            Require(enemyPresent, "No opposing army was deployed.");
            yield return ClickUI("roster-item-" + trialIndex);
            // Select away and back, exercising the actual template callback rather than only reading the catalog.
            yield return ClickUI("field-template");
            yield return ClickUI("template-" + enemyTemplateIndex);
            Require(deployment.Draft[trialIndex].template == deployment.Templates[enemyTemplateIndex], "Template UI did not change the selection.");
            yield return ClickUI("field-template");
            yield return ClickUI("template-" + templateIndex);
            Require(deployment.Draft[trialIndex].template == trialTemplate, "Trial model is not selectable through the roster UI.");
            report.checks.Add("New stable-ID template is selectable in the real roster UI; source Scenario supplies available templates.");

            if (report.phase == "seed")
            {
                Stage("invalid-deployment");
                var buffers = manager.Buffers;
                SetUIField("input-count", "0");
                yield return ClickUI("field-update");
                Require(deployment.Draft[trialIndex].count == 0, "Invalid input did not reach the draft.");
                var applyButton = FindButton("deployment-apply");
                Require(applyButton != null && !applyButton.IsInteractable(), "Invalid deployment did not disable UI Apply.");
                report.actions.Add("API: RuntimeDeployment.TryApply for invalid count=0; disabled UI cannot legitimately be clicked.");
                Require(!deployment.TryApply(out error) && !string.IsNullOrEmpty(error), "Business API accepted the invalid deployment.");
                Require(manager.Buffers == buffers && buffers.IsAllocated && manager.scenarioConfig == source && deployment.IsEditing,
                    "Rejected deployment changed GPU allocation/source/transaction state.");
                report.checks.Add("Invalid count rejected without GPU rebuild or partial Scenario replacement: " + error);
                yield return Screenshot("deployment-invalid.png");
                yield return ClickUI("roster-undo");
                Require(deployment.Draft[trialIndex].count == baseline.count, "Undo failed to restore the baseline count.");
                // A different legitimate count makes cross-process load distinguishable from scene defaults.
                SetUIField("input-count", (baseline.count == initialTrialCount ? savedTrialCount : initialTrialCount).ToString(CultureInfo.InvariantCulture));
                yield return ClickUI("field-update");
                Require(deployment.TryValidate(out error), error);
                Stage("save-plan");
                yield return Screenshot("deployment-edited.png");
                yield return ClickUI("deployment-plans");
                SetUIField("plans-slot", planSlot);
                SetUIField("plans-name", "M5.4 试验模型独立方案");
                yield return ClickUI("plans-save");
                Require(deployment.PlanStore.Exists(planSlot), "UI failed to save the trial slot.");
                yield return Screenshot("plan-saved.png");
                // Discard the working draft; even seed must really reload its saved plan.
                yield return ClickUI("plans-back");
                yield return ClickUI("deployment-cancel");
                Require(!deployment.IsEditing && manager.scenarioConfig == source, "Cancel unexpectedly applied the draft.");
                yield return ClickUI("edit");
            }

            Stage("load-plan");
            Require(deployment.PlanStore.TryLoad(planSlot, out var plan, out error), error);
            Require(plan.battlefieldId == battlefieldId, "Saved plan belongs to a different battlefield.");
            planHash = HashFile(planPath);
            if (seedReceipt != null) Require(planHash == seedReceipt.planSha256, "Seed plan file changed before reload.");
            report.planSha256 = planHash;
            report.savedDeployment = plan.entries;
            yield return ClickUI("deployment-plans");
            Require(deployment.PlanStore.TryListSlots(out var slots, out error), error);
            int slotIndex = Array.IndexOf(slots, planSlot);
            Require(slotIndex >= 0, "Trial slot missing from PlanStore list.");
            yield return ClickUI("plan-item-" + slotIndex);
            yield return ClickUI("plans-load");
            yield return ClickUI("plans-confirm-yes");
            Require(deployment.IsEditing && deployment.Draft.Count == plan.entries.Length, "UI load did not restore the saved draft.");
            var expected = deployment.Draft.Snapshot();
            bool distinctCount = false;
            for (int i = 0; i < expected.Length; i++)
            {
                Require(session.catalog.TryGetTemplateId(expected[i].template, out string id, out int revision), "Loaded template lost its stable identity.");
                var actual = WarSandboxPlanEntry.From(expected[i], id, revision);
                Require(JsonUtility.ToJson(actual) == JsonUtility.ToJson(plan.entries[i]), "Loaded draft differs from saved formation " + i);
                if (id == templateId) distinctCount |= expected[i].count != baseline.count;
            }
            Require(distinctCount, "Loaded trial count still equals scene defaults; seed evidence was not restored.");
            Require(deployment.TryValidate(out error), error);
            yield return Screenshot("deployment.png");
            var initialBuffers = manager.Buffers;
            Stage("apply-deployment");
            yield return ClickUI("deployment-apply");
            Require(!deployment.IsEditing && !initialBuffers.IsAllocated, "Apply failed to replace the old GPU allocation.");
            VerifyDeployment(expected, "applied");
            VerifySources();
            if (preBattleCheck != null)
            {
                Require(preBattleCheck is IModelTrialPreBattleCheck, "Invalid pre-battle fixture hook.");
                yield return ((IModelTrialPreBattleCheck)preBattleCheck).Run(controller);
                VerifyDeployment(expected, "after-prebattle-check"); VerifySources();
                Require(HashFile(planPath) == planHash, "Pre-battle fixture changed saved plan.");
                report.checks.Add("Optional authored small-scene movement/turn check completed and reset before natural combat.");
            }
            yield return ClickUI("panorama");
            yield return Screenshot("setup.png");
            report.checks.Add("Saved plan loaded/applied via uGUI; distinct runtime Scenario/UnitType/Spawn copies, count, stable ID and live VAT mesh binding verified.");

            Stage("natural-battle");
            Require(controller.SimulationSpeed == 1 && Time.timeScale == 1, "Natural battle must start at normal speed.");
            report.battle = new BattleEvidence { mode = controller.gameMode.ToString(), initial = ArmyCounts(true) };
            float started = Time.realtimeSinceStartup;
            yield return ClickUI("start");
            Require(manager.IsBattleRunning && !controller.BattleResult.valid, "UI failed to start a normal battle.");
            yield return Screenshot("combat.png");
            float nextLog = Time.realtimeSinceStartup;
            bool contactCaptured = false;
            while (!controller.BattleResult.valid && Time.realtimeSinceStartup - started < watchdogSeconds)
            {
                Require(manager.IsBattleRunning && controller.SimulationSpeed == 1 && Time.timeScale == 1,
                    "Battle was stopped or accelerated before natural settlement.");
                Require(report.errors.Count == 0, string.Join("\n", report.errors));
                if (!contactCaptured && HasCasualties(report.battle.initial))
                { contactCaptured = true; yield return Screenshot("combat-contact.png"); }
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    Debug.Log("MODEL_TRIAL " + report.phase + " simulation=" + controller.TelemetrySnapshot.battleSeconds.ToString("F1", CultureInfo.InvariantCulture));
                    UpdateBattleEvidence(started);
                    WriteProgress();
                    nextLog = Time.realtimeSinceStartup + 20;
                }
                yield return null;
            }
            UpdateBattleEvidence(started);
            Require(controller.BattleResult.valid && report.battle.realSeconds <= watchdogSeconds,
                "No natural settlement observed within " + watchdogSeconds + " real seconds; no result was injected.");
            VerifyNaturalResult();
            Stage("settlement");
            yield return Screenshot("settlement.png");
            string settled = JsonUtility.ToJson(controller.BattleResult);
            yield return new WaitForSecondsRealtime(0.4f);
            Require(!manager.IsBattleRunning && JsonUtility.ToJson(controller.BattleResult) == settled, "Settlement did not remain frozen.");
            report.checks.Add("Normal-speed default orders produced a real natural settlement; winner/time/all-army initial and survivor counts recorded.");

            Stage("restart");
            yield return ClickUI("reset"); // Real '原样重开' callback resets to Setup before any new losses.
            Require(controller.Phase == WarSandboxBattlePhase.Setup && !controller.BattleResult.valid && !manager.IsBattleRunning,
                "Restart retained the terminal state.");
            VerifyDeployment(expected, "restart");
            report.battle.restartAlive = ArmyCounts(false);
            for (int team = 0; team < report.battle.initial.Length; team++)
                Require(report.battle.restartAlive[team] == report.battle.initial[team] && !controller.GetArmy(team).hasOrder &&
                    controller.GetMoveRoutePointCount(team) == 0, "Restart retained casualties or commands for team " + team);
            yield return Screenshot("restart.png");
            yield return ClickUI("start");
            Require(manager.IsBattleRunning && !controller.BattleResult.valid, "Full-strength deployment was not playable after restart.");
            report.checks.Add("UI restart restored full counts, fresh orders and the same trial model binding; normal start works again.");
            VerifySources();
            Require(HashFile(planPath) == planHash, "Battle/restart rewrote the saved plan.");
            var runtimeObjects = CollectRuntimeCopies(manager.scenarioConfig);
            var finalBuffers = manager.Buffers;
            Stage("return-to-catalog");
            yield return ClickUI("nav-menu");
            if (session.ConfirmationOpen) yield return ClickUI("modal-confirm");
            yield return WaitForLoad(WarSandboxEntryState.Menu);
            report.gpuReleased = !finalBuffers.IsAllocated && session.Controller == null && manager == null &&
                FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None).Length == 0;
            report.runtimeCopiesReleased = runtimeObjects.TrueForAll(value => value == null);
            Require(report.gpuReleased && report.runtimeCopiesReleased, "Returning retained GPU resources or owned Scenario/UnitType/Spawn copies.");
            VerifySources();
            report.planUnchanged = HashFile(planPath) == planHash;
            Require(report.planUnchanged, "Returning rewrote the saved plan.");
            yield return Screenshot("returned-catalog.png");
            report.checks.Add("Returned through real navigation/confirmation UI; all runtime copies and scene GPU allocation released; source JSON and plan bytes unchanged.");
            Stage("complete");
        }

        private void InitializeArguments()
        {
            report.startedUtc = DateTime.UtcNow.ToString("O");
            report.runId = Guid.NewGuid().ToString("N");
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            { report.processId = process.Id; report.processStartedUtc = process.StartTime.ToUniversalTime().ToString("O"); }
            report.phase = Argument("--model-trial-phase");
            report.unity = Application.unityVersion; report.buildGuid = Application.buildGUID;
            report.developmentBuild = Debug.isDebugBuild; report.editor = Application.isEditor;
            report.gpu = SystemInfo.graphicsDeviceName; report.graphicsApi = SystemInfo.graphicsDeviceVersion;
            report.battlefieldId = battlefieldId; report.templateId = templateId; report.templateRevision = templateRevision;
            report.planSlot = planSlot; report.watchdogSeconds = watchdogSeconds;
            Require(report.phase == "seed" || report.phase == "reload", "Specify --model-trial-phase=seed or reload.");
            Require(watchdogSeconds == 120 || watchdogSeconds == 180, "watchdogSeconds must be 120 or 180.");
            // Slot validity is enforced by the real PlanStore save/load APIs before any slot file is read here.
            string requestedOutput = AbsoluteDirectory(Argument("--model-trial-output"));
            planDirectory = AbsoluteDirectory(Argument("--model-trial-plan-directory"));
            string playerPlans = AbsoluteDirectory(new WarSandboxLocalPlanStore().DirectoryPath);
            Require(!IsWithin(planDirectory, playerPlans) && !IsWithin(playerPlans, planDirectory), "Do not use the default player plan directory or its ancestors/children.");
            Require(!IsWithin(requestedOutput, planDirectory) && !IsWithin(planDirectory, requestedOutput), "Output and plan directories must be separate siblings, not nested.");
            Require(!File.Exists(Path.Combine(requestedOutput, "report.json")) && !File.Exists(Path.Combine(requestedOutput, "progress.json")),
                "Output contains prior smoke evidence; choose a fresh output directory.");
            // Assign output only after overwrite checks, so even an argument failure cannot overwrite an old report.
            output = requestedOutput;
            Directory.CreateDirectory(output);
            report.outputDirectory = output; report.planDirectory = planDirectory;
            planPath = Path.Combine(planDirectory, planSlot + ".json");
            if (report.phase == "seed")
                Require(!Directory.Exists(planDirectory) && !File.Exists(planDirectory), "Seed requires a new, nonexistent isolated plan directory (no overwrite).");
            else
            {
                Require(Directory.Exists(planDirectory) && File.Exists(Path.Combine(planDirectory, ReceiptName)), "Reload requires the successful seed receipt in its isolated directory.");
                seedReceipt = JsonUtility.FromJson<SeedReceipt>(File.ReadAllText(Path.Combine(planDirectory, ReceiptName)));
                Require(seedReceipt != null && seedReceipt.passed && seedReceipt.battlefieldId == battlefieldId && seedReceipt.templateId == templateId &&
                    seedReceipt.templateRevision == templateRevision && seedReceipt.slot == planSlot && seedReceipt.buildGuid == Application.buildGUID,
                    "Seed receipt is incomplete or belongs to another build/trial/template/slot.");
                Require(seedReceipt.processId != report.processId || seedReceipt.processStartedUtc != report.processStartedUtc,
                    "Reload must run in a separate process, not a second coroutine in the seed process.");
                report.seedRunId = seedReceipt.runId; report.seedProcessId = seedReceipt.processId; report.seedProcessStartedUtc = seedReceipt.processStartedUtc;
            }
            WriteProgress();
        }

        private void VerifyDeployment(WarSandboxDeploymentEntry[] expected, string atStage)
        {
            var manager = controller.manager;
            var owned = manager.scenarioConfig;
            Require(owned != source && owned != null && owned.unitTypes.Length == expected.Length && manager.Buffers.IsAllocated,
                "No independent applied Scenario/GPU allocation.");
            Require(manager.UnitTypes.RegisteredTypes.Count == expected.Length, "A deployed unit type was skipped by the engine.");
            Require(VatProfileReader.TryRead(trialTemplate.renderConfig.vatProfile, out var profile, out string error), error);
            int total = 0, trialCount = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                var e = expected[i]; var unit = owned.unitTypes[i]; var live = manager.UnitTypes.RegisteredTypes[i];
                Require(unit != e.template && unit.spawnConfig != e.template.spawnConfig && live.Config == unit, "Unit/Spawn not owned by this deployment.");
                var actual = WarSandboxDeploymentEntry.From(unit);
                Require(actual.teamId == e.teamId && actual.count == e.count && actual.center == e.center && actual.density == e.density &&
                    actual.aspect == e.aspect && actual.manualSize == e.manualSize && live.UnitCount == e.count, "Applied formation values/counts differ from the saved draft.");
                Require(unit.renderConfig == e.template.renderConfig && unit.combatConfig == e.template.combatConfig &&
                    unit.movementConfig == e.template.movementConfig && unit.animationConfig == e.template.animationConfig &&
                    unit.flockingConfig == e.template.flockingConfig, "Deployment replaced shared model/combat/movement configuration.");
                total += e.count;
                if (e.template != trialTemplate) continue;
                trialCount++;
                var render = live.RenderRuntime;
                Require(render != null && render.nearMesh != null && render.nearMesh == profile.cleanMesh && render.nearMaterial != null &&
                    render.nearMaterial.shader != null && render.nearMaterial.shader.isSupported && render.nearBlock != null,
                    "Live renderer did not resolve the new trial VAT mesh/material/block.");
                report.models.Add(new ModelEvidence { stage = atStage, templateId = templateId, formation = i, teamId = e.teamId, count = live.UnitCount,
                    unitName = unit.unitTypeName, renderConfig = unit.renderConfig.name, profile = unit.renderConfig.vatProfile.name,
                    nearMesh = render.nearMesh.name, nearVertices = render.nearMesh.vertexCount, nearMaterial = render.nearMaterial.name });
            }
            Require(trialCount == 1 && manager.UnitTypes.TotalAgentCount == total, "Applied trial formation/total counts are wrong.");
        }

        private void UpdateBattleEvidence(float started)
        {
            var result = controller.BattleResult;
            report.battle.phase = controller.Phase.ToString();
            report.battle.realSeconds = Time.realtimeSinceStartup - started;
            report.battle.simulationSeconds = result.valid ? result.battleSeconds : controller.TelemetrySnapshot.battleSeconds;
            report.battle.winnerTeamId = result.valid ? result.winnerTeamId : -1;
            report.battle.victoryReason = result.valid ? result.victoryReason.ToString() : null;
            report.battle.survivors = ArmyCounts(false);
        }

        private void VerifyNaturalResult()
        {
            var result = controller.BattleResult;
            var counts = report.battle.survivors;
            Require(result.valid && result.battleSeconds > 0 && controller.TelemetrySnapshot.valid && !controller.manager.IsBattleRunning,
                "Natural telemetry result is incomplete or simulation did not stop.");
            for (int team = 0; team < counts.Length; team++)
                Require(counts[team] >= 0 && counts[team] <= report.battle.initial[team], "Invalid survivor count for team " + team);
            if (result.phase == WarSandboxBattlePhase.Draw)
                Require(result.winnerTeamId == -1 && Array.TrueForAll(counts, n => n == 0), "Invalid draw result.");
            else
            {
                Require(result.winnerTeamId >= 0 && result.winnerTeamId < counts.Length && counts[result.winnerTeamId] > 0, "Winner is not a surviving army.");
                if (result.victoryReason == WarSandboxVictoryReason.Annihilation)
                    for (int team = 0; team < counts.Length; team++) Require(team == result.winnerTeamId || counts[team] == 0, "Annihilation left an enemy alive.");
                else Require(controller.gameMode == WarSandboxGameMode.ControlPoint && controller.ControlPointCaptureProgress >= 1 &&
                    controller.ControlPointOwnerTeamId == result.winnerTeamId, "Invalid control point victory.");
            }
            report.battle.naturalSettlement = true;
        }

        private int[] ArmyCounts(bool initial)
        {
            var counts = new int[controller.ArmyCount];
            for (int team = 0; team < counts.Length; team++) counts[team] = initial ? controller.GetArmy(team).initialUnitCount : controller.GetAliveUnitCount(team);
            return counts;
        }

        private bool HasCasualties(int[] initial)
        {
            for (int team = 0; team < initial.Length; team++) if (controller.GetAliveUnitCount(team) < initial[team]) return true;
            return false;
        }

        private void CaptureSource(UnityEngine.Object value)
        {
            if (value == null || report.sources.Exists(e => e.asset == value)) return;
            report.sources.Add(new SourceEvidence { asset = value, name = value.name, type = value.GetType().Name,
                beforeSha256 = HashBytes(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(value))) });
        }

        private void CaptureUnit(UnitTypeConfig unit)
        {
            if (unit == null) return;
            CaptureSource(unit); CaptureSource(unit.spawnConfig); CaptureSource(unit.renderConfig); CaptureSource(unit.combatConfig);
            CaptureSource(unit.animationConfig); CaptureSource(unit.movementConfig); CaptureSource(unit.flockingConfig);
            if (unit.renderConfig != null) CaptureSource(unit.renderConfig.vatProfile);
        }

        private void VerifySources()
        {
            foreach (var evidence in report.sources)
            {
                Require(evidence.asset != null, "Source asset was destroyed: " + evidence.name);
                evidence.afterSha256 = HashBytes(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(evidence.asset)));
                evidence.unchanged = evidence.beforeSha256 == evidence.afterSha256;
                Require(evidence.unchanged, "Source asset JSON changed: " + evidence.name);
            }
            report.sourceJsonAfter = JsonUtility.ToJson(source);
            report.sourceJsonUnchanged = report.sourceJsonBefore == report.sourceJsonAfter;
            Require(report.sourceJsonUnchanged, "sourceJson changed.");
        }

        private static List<UnityEngine.Object> CollectRuntimeCopies(ScenarioConfig scenario)
        {
            var owned = new List<UnityEngine.Object> { scenario };
            foreach (var unit in scenario.unitTypes) { owned.Add(unit); owned.Add(unit.spawnConfig); }
            return owned;
        }

        private IEnumerator ClickUI(string name)
        {
            float deadline = Time.realtimeSinceStartup + 5;
            Button button;
            while ((button = FindButton(name)) == null && Time.realtimeSinceStartup < deadline) yield return null;
            Require(button != null && button.IsInteractable(), "UI button missing/disabled: " + name);
            report.actions.Add("uGUI Button.onClick: " + name);
            button.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.25f);
        }

        private static Button FindButton(string name)
        {
            Button found = null;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name && button.isActiveAndEnabled)
                { Require(found == null, "Ambiguous active UI button: " + name); found = button; }
            return found;
        }

        private void SetUIField(string name, string value)
        {
            InputField found = null;
            foreach (var field in FindObjectsByType<InputField>(FindObjectsSortMode.None))
                if (field.name == name && field.isActiveAndEnabled)
                { Require(found == null, "Ambiguous active UI input: " + name); found = field; }
            Require(found != null && found.IsInteractable(), "UI input missing/disabled: " + name);
            report.actions.Add("uGUI InputField.text/onValueChanged: " + name + "=" + value);
            found.text = value;
        }

        private IEnumerator WaitForLoad(WarSandboxEntryState expected)
        {
            float deadline = Time.realtimeSinceStartup + 60;
            while (session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            Require(!session.IsLoading && session.State == expected, "Scene transition failed/timed out: " + session.Error);
            yield return null; yield return null;
        }

        private IEnumerator Screenshot(string filename)
        {
            string path = Path.Combine(output, filename);
            Require(!File.Exists(path), "Refusing to overwrite a screenshot: " + filename);
            yield return new WaitForSecondsRealtime(0.2f); // Let retained UI publish its latest state.
            var evidence = new ImageEvidence { file = filename, width = Screen.width, height = Screen.height,
                phase = controller != null ? controller.Phase.ToString() : session.State.ToString(),
                resultValid = controller != null && controller.BattleResult.valid,
                simulationSeconds = controller != null ? controller.TelemetrySnapshot.battleSeconds : 0,
                alive = controller != null ? ArmyCounts(false) : new int[0] };
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 10;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Require(File.Exists(path), "Screenshot was not produced: " + filename);
            yield return new WaitForSecondsRealtime(0.2f);
            var image = new Texture2D(2, 2);
            try
            {
                Require(image.LoadImage(File.ReadAllBytes(path)), "Screenshot decode failed: " + filename);
                Require(image.width == evidence.width && image.height == evidence.height, "Stale screenshot dimensions: " + filename);
                var pixels = image.GetPixels32(); var background = pixels[0];
                for (int i = 0; i < pixels.Length; i += Mathf.Max(1, pixels.Length / 8192))
                {
                    var p = pixels[i];
                    if (Mathf.Abs(p.r - background.r) + Mathf.Abs(p.g - background.g) + Mathf.Abs(p.b - background.b) > 24)
                    { evidence.nonblank = true; break; }
                }
                Require(evidence.nonblank, "Blank screenshot: " + filename);
                report.screenshots.Add(evidence);
            }
            finally { Destroy(image); }
        }

        private void Stage(string value) { report.stage = value; WriteProgress(); }
        private void WriteProgress()
        {
            if (output != null) File.WriteAllText(Path.Combine(output, "progress.json"), JsonUtility.ToJson(report, true));
        }

        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            string failure = null;
            while (stack.Count > 0 && failure == null)
            {
                object next = null; bool moved = false;
                try
                {
                    Require(report.errors.Count == 0, string.Join("\n", report.errors));
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                }
                catch (Exception ex) { failure = ex.ToString(); }
                if (failure != null) break;
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            if (failure != null) yield return CleanupFailure();
            Finish(failure == null && report.errors.Count == 0, failure ?? string.Join("\n", report.errors));
        }

        private IEnumerator CleanupFailure()
        {
            // Cleanup uses the normal scene lifecycle, never direct buffer/state replacement.
            bool returning = false;
            try
            {
                if (session != null && !session.IsLoading && session.State != WarSandboxEntryState.Menu)
                {
                    report.actions.Add("API failure cleanup: SceneSession.TryReturnToMenu(true).");
                    returning = session.TryReturnToMenu(true, out string error);
                    report.failureCleanup = returning ? "Return requested after failure." : error;
                }
            }
            catch (Exception ex) { report.failureCleanup = ex.Message; }
            float deadline = Time.realtimeSinceStartup + 15;
            while (returning && session != null && session.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
            if (returning) report.failureCleanup = session != null && session.State == WarSandboxEntryState.Menu ? "Returned via SceneSession after failure." : "Cleanup return did not complete before timeout.";
        }

        private void Finish(bool passed, string error)
        {
            if (finished) return;
            finished = true;
            report.completed = true; report.passed = passed; report.error = error; report.finishedUtc = DateTime.UtcNow.ToString("O");
            Application.logMessageReceived -= TrackError;
            try
            {
                if (passed && report.phase == "seed")
                {
                    var receipt = new SeedReceipt { passed = true, runId = report.runId, processId = report.processId,
                        processStartedUtc = report.processStartedUtc, buildGuid = report.buildGuid, battlefieldId = battlefieldId,
                        templateId = templateId, templateRevision = templateRevision, slot = planSlot, planSha256 = planHash };
                    using (var stream = new FileStream(Path.Combine(planDirectory, ReceiptName), FileMode.CreateNew, FileAccess.Write))
                    using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(receipt, true));
                }
            }
            catch (Exception ex) { report.passed = false; report.error = (report.error ?? "") + "\nSeed receipt failed: " + ex; }
            try
            {
                if (output != null)
                { File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true)); WriteProgress(); }
            }
            catch (Exception ex) { report.passed = false; Debug.LogError("MODEL_TRIAL report write failed: " + ex); }
            Debug.Log("MODEL_TRIAL " + (report.passed ? "PASS" : "FAIL") + " phase=" + report.phase + " stage=" + report.stage + " " + report.error);
            Application.Quit(report.passed ? 0 : 1);
        }

        private void TrackError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= TrackError;
            if (instance == this) instance = null;
        }

        private static string Argument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(key + "=", StringComparison.Ordinal)) return args[i].Substring(key.Length + 1);
                if (args[i] == key && i + 1 < args.Length) return args[i + 1];
            }
            return null;
        }

        private static string AbsoluteDirectory(string value)
        {
            Require(!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value), "Smoke output/plan directories must be explicit absolute paths.");
            string full = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Windows Mono preserves '/' in the supplied root but GetFullPath uses '\\'.
            // Compare normalized roots so E:/... is as absolute as E:\\...; still reject E:relative and \\root-relative.
            Require(string.Equals(Path.GetPathRoot(value).Replace('\\', '/'), Path.GetPathRoot(Path.GetFullPath(value)).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase),
                "Drive-relative/root-relative paths are not explicit absolute directories.");
            Require(full.Length > Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length,
                "A drive/root directory is not an isolated smoke directory.");
            return full;
        }

        private static bool IsWithin(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));
        private static string HashBytes(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error ?? "Model trial assertion failed."); }
#else
        private void Awake() { enabled = false; }
#endif
    }
}
