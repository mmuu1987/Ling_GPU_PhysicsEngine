using System;
using System.IO;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>Only the independent Knight menu contains this component. No engine/global saved defaults change.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class WarSandboxCharacterPilotRuntime : MonoBehaviour
    {
        private static WarSandboxCharacterPilotRuntime instance;
        private bool automated;
        private WarSandboxRuntimeDeployment lastDeployment;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() => instance = null;
        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
            automated = Array.IndexOf(Environment.GetCommandLineArgs(), "--model-trial-smoke") >= 0;
            Debug.Log("CHARACTER_PILOT_FRAME_CAP target=30 vSync=0; functional preview, not performance evidence.");
        }
        private void Update()
        {
            if (automated) return; // The smoke explicitly owns separate seed/reload stores.
            var session = WarSandboxSceneSession.Instance;
            var controller = session != null ? session.Controller : null;
            if (controller == null) { lastDeployment = null; return; }
            var deployment = controller.GetComponent<WarSandboxRuntimeDeployment>();
            if (deployment != null && deployment != lastDeployment)
            {
                deployment.PlanStore = new WarSandboxLocalPlanStore(Path.Combine(Application.persistentDataPath, "KnightPilotPlans"));
                lastDeployment = deployment;
            }
        }
        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
