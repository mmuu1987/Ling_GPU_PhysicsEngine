using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game
{
    public enum WarSandboxEntryState { Menu, Loading, Battle, Failed }

    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class WarSandboxSceneSession : MonoBehaviour
    {
        public WarSandboxBattlefieldCatalog catalog;
        public bool enterDefaultOnStart = true;
        public static WarSandboxSceneSession Instance { get; private set; }
        public WarSandboxEntryState State { get; private set; } = WarSandboxEntryState.Menu;
        public WarSandboxBattleController Controller { get; private set; }
        public string CurrentEntryId { get; private set; }
        public string CurrentDisplayName { get; private set; }
        public string Error { get; private set; }
        public float LoadingProgress { get; private set; }
        /// <summary>Non-fatal note from the global unit stat layer for the current battlefield (null = none).</summary>
        public string StatsWarning { get; private set; }
        public bool ConfirmationOpen { get; private set; }
        public bool SettingsOpen { get; private set; }
        public bool HelpOpen { get; private set; }
        private bool resumeAfterHelp30;
        private int helpClosedFrame30 = -1;
        public bool HelpInputBlocked30 => HelpOpen || Time.frameCount == helpClosedFrame30;
        public bool IsLoading => transitionInFlight || State == WarSandboxEntryState.Loading;
        public bool InputBlocked => IsLoading || State != WarSandboxEntryState.Battle || ConfirmationOpen || SettingsOpen || HelpInputBlocked30;
        public bool RequiresEndConfirmation => Controller != null &&
            (Controller.Phase == WarSandboxBattlePhase.Running || Controller.Phase == WarSandboxBattlePhase.Paused ||
             WarSandboxRuntimeDeployment.BlocksCommands(Controller) ||
             (PlanState28.For(Controller) != null && PlanState28.For(Controller).NeedsLeaveWarning));

        private string menuScenePath;
        private string pendingScenePath;
        private string pendingEntryId;
        private string pendingDisplayName;
        private WarSandboxBattlefieldConfig pendingRules;
        private WarSandboxBattlefieldConfig activeRules;
        private WarSandboxBattlefieldEntry pendingEntry;
        private WarSandboxBattlefieldEntry activeEntry;
        public WarSandboxBattlefieldEntry CurrentBattlefield => activeEntry?.CopyIdentity();
        private bool returningToMenu;
        private bool resumeAfterConfirmation;
        private bool resumeAfterSettings;
        private bool receivedScene;
        private bool transitionInFlight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            menuScenePath = gameObject.scene.path;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            if (Instance != this || !enterDefaultOnStart || HasArgument("--war-sandbox-menu") || HasArgument("--war-sandbox-smoke")) return;
            if (catalog == null) { Error = "No battlefield catalog is assigned."; return; }
            if (!HasArgument("--war-sandbox-auto-enter") && !HasArgument("--terrain-cycle") && !HasArgument("--model-trial-smoke")) return;
            TryEnterBattlefield(catalog.defaultEntryId, false, out _);
        }

        public static bool AllowsBattleCommands(WarSandboxBattleController controller)
        {
            if (WarSandboxDeploymentHUD.BlocksInput(controller)) return false;
            return Instance == null || (!Instance.InputBlocked && Instance.Controller == controller);
        }

        public bool TryEnterBattlefield(string id, bool confirmEndBattle, out string error)
        {
            error = null;
            if (!CanNavigate(confirmEndBattle, out error)) return false;
            if (catalog == null) return Reject("No battlefield catalog is assigned.", out error);
            if (!catalog.TryResolve(id, CanLoadScene, out var entry, out error)) { Error = error; return false; }
            if (!CanLoadScene(menuScenePath)) return Reject("The battlefield menu scene is not included in the build.", out error);

            // Freeze the request before unloading anything. Later asset edits or selections
            // must not change the definition already being loaded.
            var snapshot = ScriptableObject.CreateInstance<WarSandboxBattlefieldConfig>();
            snapshot.name = entry.rules.name + " (Runtime)";
            snapshot.rules = entry.rules.rules.Copy();
            snapshot.hideFlags = HideFlags.DontSave;
            BeginTransition();
            pendingRules = snapshot;
            pendingEntry = entry.CopyIdentity();
            pendingEntry.rules = snapshot;
            pendingEntryId = entry.id;
            pendingDisplayName = entry.displayName;
            returningToMenu = false;
            StartCoroutine(LoadScene(entry.scenePath));
            return true;
        }

        public bool TryReturnToMenu(bool confirmEndBattle, out string error)
        {
            if (!CanNavigate(confirmEndBattle, out error)) return false;
            if (!CanLoadScene(menuScenePath)) return Reject("The battlefield menu scene is unavailable.", out error);
            BeginTransition();
            returningToMenu = true;
            StartCoroutine(LoadScene(menuScenePath));
            return true;
        }

        private bool CanNavigate(bool confirmed, out string error)
        {
            error = null;
            if (IsLoading) { error = "A scene is already loading."; return false; }
            if (HelpOpen) { error = "Close help before changing battlefields."; return false; }
            if (SettingsOpen) { error = "Close settings before changing battlefields."; return false; }
            if (RequiresEndConfirmation && !confirmed) { error = "Confirm ending the current battle first."; return false; }
            return true;
        }

        public void BeginConfirmation()
        {
            if (IsLoading || ConfirmationOpen || SettingsOpen || HelpOpen) return;
            resumeAfterConfirmation = Controller != null && Controller.Phase == WarSandboxBattlePhase.Running;
            if (resumeAfterConfirmation) Controller.PauseBattle();
            ConfirmationOpen = true;
            SetCameraInput(false);
        }

        public void CancelConfirmation()
        {
            if (!ConfirmationOpen) return;
            ConfirmationOpen = false;
            SetCameraInput(true);
            if (resumeAfterConfirmation && Controller != null) Controller.StartOrResumeBattle();
            resumeAfterConfirmation = false;
        }

        public void OpenSettings()
        {
            if (IsLoading || ConfirmationOpen || SettingsOpen || HelpOpen) return;
            resumeAfterSettings = Controller != null && Controller.Phase == WarSandboxBattlePhase.Running;
            if (resumeAfterSettings) Controller.PauseBattle();
            SettingsOpen = true; SetCameraInput(false);
        }

        public void CloseSettings()
        {
            if (!SettingsOpen) return;
            SettingsOpen = false; SetCameraInput(true);
            if (resumeAfterSettings && Controller != null) Controller.StartOrResumeBattle();
            resumeAfterSettings = false;
        }

        public void OpenHelp30()
        {
            if (Guide30.For(this) == null || IsLoading || ConfirmationOpen || SettingsOpen || HelpOpen) return;
            resumeAfterHelp30 = Controller != null && Controller.Phase == WarSandboxBattlePhase.Running;
            if (resumeAfterHelp30) Controller.PauseBattle();
            HelpOpen = true; SetCameraInput(false);
        }
        public void CloseHelp30()
        {
            if (!HelpOpen) return;
            HelpOpen = false; SetCameraInput(true);
            if (resumeAfterHelp30 && Controller != null) Controller.StartOrResumeBattle();
            helpClosedFrame30 = Time.frameCount; // Protect subsequent input, not the deliberate resume above.
            resumeAfterHelp30 = false;
        }

        public void ClearError() => Error = null;

        private void BeginTransition()
        {
            if (Controller != null && Controller.manager != null) Controller.manager.PauseBattle();
            SetCameraInput(false);
            State = WarSandboxEntryState.Loading;
            Error = null;
            LoadingProgress = 0;
            ConfirmationOpen = false;
            resumeAfterConfirmation = false;
            receivedScene = false;
            SettingsOpen = false; resumeAfterSettings = false; HelpOpen = false; resumeAfterHelp30 = false;
            transitionInFlight = true;
            Time.timeScale = 1;
        }

        private IEnumerator LoadScene(string path)
        {
            pendingScenePath = path;
            AsyncOperation operation = null;
            string loadError = null;
            try { operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single); }
            catch (Exception ex) { loadError = ex.Message; }
            if (operation == null)
            {
                transitionInFlight = false;
                Fail("Scene loading failed: " + (loadError ?? path));
                yield break;
            }
            while (!operation.isDone)
            {
                LoadingProgress = Mathf.Clamp01(operation.progress / 0.9f);
                yield return null;
            }
            if (!receivedScene) Fail("The loaded scene did not complete battlefield setup: " + path);
            transitionInFlight = false;
            LoadingProgress = 1;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsLoading)
            {
                // A direct scene load is not a catalog request. Do not carry the previous
                // rules or persistent session into an independently opened battlefield.
                if (mode == LoadSceneMode.Single && scene.path != menuScenePath)
                {
                    var directManager = FindManager(scene, out _);
                    if (directManager != null) WarSandboxRuntimeBootstrap.EnsureControls(directManager);
                    Destroy(gameObject);
                }
                return;
            }
            if (scene.path != pendingScenePath) return;
            receivedScene = true;
            try { ApplyLoadedScene(scene); }
            catch (Exception ex) { Fail("Battlefield setup failed: " + ex.Message); }
        }

        private void ApplyLoadedScene(Scene scene)
        {
            Controller = null;
            if (activeRules != null) Destroy(activeRules);
            activeRules = null;
            activeEntry = null;
            CurrentEntryId = null;
            CurrentDisplayName = null;
            if (returningToMenu)
            {
                ClearPending();
                State = WarSandboxEntryState.Menu;
                return;
            }

            MassEngineManager manager = FindManager(scene, out string error);
            if (manager == null) { Fail(error); return; }
            manager.PauseBattle();
            if (!TryValidateTerrainProvider(manager, pendingEntry, out error) ||
                !TryValidateScene(manager, out error, false)) { Fail(error); return; }
            Controller = WarSandboxRuntimeBootstrap.EnsureControls(manager);
            Controller.GetComponent<WarSandboxRuntimeDeployment>().battlefieldCatalog = catalog;
            Controller.pauseOnStart = true;
            if (!Controller.TryApplyBattlefieldConfig(pendingRules, out error)) { Fail(error); return; }
            // Awake may have rejected the scene's authored obstacles. The already-validated
            // catalog rules, not that obsolete layout, are the requested initialization contract.
            if (manager.terrainSurfaceAsset != null && (manager.Buffers == null || !manager.Buffers.IsAllocated))
            { manager.ResetScenario(); manager.PauseBattle(); }
            // Player-wide unit stat overrides (兵种库). No applicable override = authored scenario untouched.
            Controller.GetComponent<WarSandboxRuntimeDeployment>().TryApplyGlobalStatsOnLoad(out string statsWarning);
            StatsWarning = statsWarning;
            if (!TryValidateScene(manager, out error)) { Fail(error); return; }
            Controller.RebuildArmyStates();
            Controller.selectedTeam = FirstDeployedTeam(Controller);
            activeRules = pendingRules;
            activeEntry = pendingEntry;
            pendingRules = null;
            CurrentEntryId = pendingEntryId;
            CurrentDisplayName = pendingDisplayName;
            ClearPending();
            State = WarSandboxEntryState.Battle;
            SetCameraInput(true);
        }

        private void Fail(string message)
        {
            Error = message;
            ClearPending();
            State = WarSandboxEntryState.Failed;
            ConfirmationOpen = false;
            // Prevent legacy click scripts and Inspector flags from simulating a failed scene.
            foreach (var manager in FindObjectsByType<MassEngineManager>(FindObjectsSortMode.None))
            {
                manager.PauseBattle();
                manager.enabled = false;
            }
            Time.timeScale = 1;
        }

        private void ClearPending()
        {
            if (pendingRules != null) Destroy(pendingRules);
            pendingRules = null;
            pendingEntry = null;
            pendingEntryId = null;
            pendingDisplayName = null;
            pendingScenePath = null;
            returningToMenu = false;
        }

        private bool Reject(string message, out string error) { error = message; Error = message; return false; }

        public static bool CanLoadScene(string path)
        {
            if (!WarSandboxBattlefieldEntry.IsScenePath(path)) return false;
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                if (string.Equals(SceneUtility.GetScenePathByBuildIndex(i), path, StringComparison.Ordinal)) return true;
            return false;
        }

        public static MassEngineManager FindManager(Scene scene, out string error)
        {
            error = null;
            MassEngineManager found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (MassEngineManager manager in root.GetComponentsInChildren<MassEngineManager>(true))
                {
                    if (found != null) { error = "Battlefield scene contains more than one manager."; return null; }
                    found = manager;
                }
            if (found == null) error = "Battlefield scene has no MassEngineManager.";
            return found;
        }

        // CPU-only contract check: never treats an absent/broken mountain provider as flat ground.
        public static bool TryValidateTerrainProvider(MassEngineManager manager,
            WarSandboxBattlefieldEntry entry, out string error)
        {
            error = null;
            if (manager == null || entry == null) { error = "Terrain battlefield request is missing."; return false; }
            if (manager.terrainSurfaceAsset != entry.terrainSurface)
            { error = "Loaded terrain provider is not the requested asset."; return false; }
            if (entry.terrainId == "flat-ground")
            {
                if (entry.terrainVersion != 1 || entry.terrainSurface != null)
                { error = "flat-ground@1 requires a null terrain provider."; return false; }
            }
            else if (entry.terrainSurface == null || entry.terrainSurface.Id != entry.terrainId ||
                entry.terrainSurface.Version != entry.terrainVersion)
            { error = "Requested terrain provider identity/version changed or is missing."; return false; }
            if (!manager.TryGetTerrainContext(out var surface, out var navigation, out error)) return false;
            if (entry.terrainId == "flat-ground")
            {
                if (surface == null && navigation == null) return true;
                error = "Flat battlefield unexpectedly has a terrain snapshot."; return false;
            }
            if (surface == null || navigation == null || surface.Id != entry.terrainId || surface.Version != entry.terrainVersion)
            { error = "Loaded terrain snapshot identity/version does not match the request."; return false; }
            var simulation = manager.systemConfig != null ? manager.systemConfig.simulationConfig : null;
            if (simulation == null || surface.Origin != -simulation.simulationWorldSize * .5f ||
                surface.Size != simulation.simulationWorldSize)
            { error = "Terrain origin/size does not match the battlefield world."; return false; }
            return true;
        }

        public static bool TryValidateScene(MassEngineManager manager, out string error, bool requireGpu = true)
        {
            error = null;
            if (manager == null || !manager.isActiveAndEnabled) error = "Battlefield manager is missing or disabled.";
            else if (!SystemInfo.supportsComputeShaders) error = "This device does not support compute shaders.";
            else if (!manager.enableGpuDispatch) error = "GPU dispatch is disabled in this battlefield.";
            else if (manager.systemConfig == null || manager.systemConfig.simulationConfig == null || manager.systemConfig.runtimeFlowConfig == null)
                error = "Battlefield simulation/flow configuration is incomplete.";
            else if (manager.cullingCamera == null || !manager.cullingCamera.isActiveAndEnabled)
                error = "Battlefield camera is missing or disabled.";
            else if (manager.scenarioConfig == null || manager.scenarioConfig.unitTypes == null || manager.scenarioConfig.unitTypes.Length == 0)
                error = "Battlefield has no default deployment.";
            if (error != null) return false;
            var teams = new HashSet<int>();
            foreach (var unit in manager.scenarioConfig.unitTypes)
            {
                var validation = ConfigValidator.Validate(unit);
                if (!validation.IsValid) { error = string.Join("\n", validation.Errors); return false; }
                teams.Add(unit.teamId);
            }
            if (teams.Count < 2) error = "A battlefield needs at least two deployed armies.";
            else if (requireGpu && (manager.Buffers == null || !manager.Buffers.IsAllocated || manager.UnitTypes == null))
                error = string.IsNullOrEmpty(manager.TerrainError)
                    ? "Battlefield GPU initialization failed. Check its shader and deployment configuration."
                    : "Battlefield terrain initialization failed: " + manager.TerrainError;
            return error == null;
        }

        private static int FirstDeployedTeam(WarSandboxBattleController controller)
        {
            for (int i = 0; i < controller.ArmyCount; i++)
                if (controller.GetArmy(i).initialUnitCount > 0) return i;
            return 0;
        }

        private void SetCameraInput(bool enabled)
        {
            if (Controller == null || Controller.manager == null || Controller.manager.cullingCamera == null) return;
            var camera = Controller.manager.cullingCamera.GetComponent<MyCameraManager>();
            if (camera == null) camera = Controller.manager.cullingCamera.GetComponentInParent<MyCameraManager>();
            if (camera != null) { if (enabled) camera.UnlockInput(); else camera.LockInput(); }
        }

        private static bool HasArgument(string value) => Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ClearPending();
            if (activeRules != null) Destroy(activeRules);
            Instance = null;
            Time.timeScale = 1;
        }
    }
}
