using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game
{
    /// <summary>
    /// Keeps existing WarSandbox scenes forward-compatible without rewriting an open
    /// scene asset behind the designer's back. Explicitly authored components win.
    /// </summary>
    public static class WarSandboxRuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureVerticalSlice()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.name.StartsWith("WarSandbox", System.StringComparison.Ordinal))
                return;

            MassEngineManager manager = Object.FindFirstObjectByType<MassEngineManager>();
            if (manager == null)
                return;

            WarSandboxBattleController controller = manager.GetComponent<WarSandboxBattleController>();
            if (controller == null)
                controller = manager.gameObject.AddComponent<WarSandboxBattleController>();
            controller.manager = manager;

            WarSandboxCommandHUD hud = manager.GetComponent<WarSandboxCommandHUD>();
            if (hud == null)
                hud = manager.gameObject.AddComponent<WarSandboxCommandHUD>();
            hud.controller = controller;
            if (hud.commandCamera == null)
                hud.commandCamera = manager.cullingCamera;
        }
    }
}
