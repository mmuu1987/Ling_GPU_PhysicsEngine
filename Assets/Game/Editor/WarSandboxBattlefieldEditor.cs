using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Editor
{
    public static class WarSandboxBattlefieldEditor
    {
        public static bool TryEnsureController(MassEngineManager manager,
            out WarSandboxBattleController controller, out string error)
        {
            controller = null;
            if (!CanWriteScene(manager, out error)) return false;
            controller = manager.GetComponent<WarSandboxBattleController>();
            if (controller != null && controller.manager != null && controller.manager != manager)
            {
                error = "The battlefield Controller is bound to another manager.";
                return false;
            }
            int group = BeginUndo("Create War Sandbox Rule Controls");
            if (controller == null) controller = Undo.AddComponent<WarSandboxBattleController>(manager.gameObject);
            Undo.RecordObject(controller, "Bind Battlefield Manager");
            controller.manager = manager;
            var hud = manager.GetComponent<WarSandboxCommandHUD>();
            if (hud == null) hud = Undo.AddComponent<WarSandboxCommandHUD>(manager.gameObject);
            Undo.RecordObject(hud, "Bind Battlefield HUD");
            hud.controller = controller;
            if (hud.commandCamera == null) hud.commandCamera = manager.cullingCamera;
            MarkSceneObject(controller);
            MarkSceneObject(hud);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        public static bool TryCapture(WarSandboxBattleController controller,
            WarSandboxBattlefieldConfig config, out string error)
        {
            if (!CanWriteController(controller, out error) || !HasSavedAsset(config, out error)) return false;
            WarSandboxBattlefieldRules snapshot = controller.CaptureBattlefieldRules();
            if (!snapshot.TryValidate(out error)) return false;

            int group = BeginUndo("Save War Sandbox Battlefield Rules");
            Undo.RecordObjects(new Object[] { controller, config }, "Save War Sandbox Battlefield Rules");
            config.rules = snapshot;
            controller.battlefieldConfig = config;
            EditorUtility.SetDirty(config);
            MarkSceneObject(controller);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        public static bool TryApply(WarSandboxBattleController controller,
            WarSandboxBattlefieldConfig config, out string error)
        {
            if (!CanWriteController(controller, out error) || !HasSavedAsset(config, out error)) return false;
            if (!config.TryCreateSnapshot(out WarSandboxBattlefieldRules rules, out error)) return false;

            int group = BeginUndo("Load War Sandbox Battlefield Rules");
            Undo.RecordObject(controller, "Load War Sandbox Battlefield Rules");
            // Authoring changes serialized fields only. Runtime snapshots start in Awake,
            // so Undo never has to restore hidden, nonserialized battle state.
            controller.battlefieldConfig = config;
            controller.gameMode = rules.gameMode;
            controller.controlPointCenter = rules.controlPointCenter;
            controller.controlPointRadius = rules.controlPointRadius;
            controller.controlPointCaptureSeconds = rules.controlPointCaptureSeconds;
            controller.staticObstaclesEnabled = rules.staticObstaclesEnabled;
            controller.staticObstacleClearance = rules.staticObstacleClearance;
            controller.useCustomStaticObstacleLayout = true;
            controller.staticObstacles = rules.staticObstacles;
            MarkSceneObject(controller);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        public static bool TryCaptureSnapshot(WarSandboxBattleController controller,
            out WarSandboxBattlefieldRules rules, out string error)
        {
            rules = default;
            if (!CanWriteController(controller, out error)) return false;
            rules = controller.CaptureBattlefieldRules();
            return rules.TryValidate(out error);
        }

        private static bool HasSavedAsset(WarSandboxBattlefieldConfig config, out string error)
        {
            error = config != null && AssetDatabase.Contains(config)
                ? null : "Choose a saved battlefield rules asset.";
            return error == null;
        }

        private static bool CanWriteController(WarSandboxBattleController controller, out string error)
        {
            if (!CanWriteScene(controller, out error)) return false;
            if (controller.Phase != WarSandboxBattlePhase.Setup)
            {
                error = "Battlefield rules can only be authored during Setup.";
                return false;
            }
            return true;
        }

        private static bool CanWriteScene(Component target, out string error)
        {
            error = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                error = "Battlefield rules can only be authored outside Play Mode.";
            else if (target == null || !target.gameObject.scene.IsValid() || !target.gameObject.scene.isLoaded ||
                     EditorUtility.IsPersistent(target))
                error = "Choose a component in an open scene.";
            return error == null;
        }

        private static int BeginUndo(string action)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(action);
            return Undo.GetCurrentGroup();
        }

        internal static void MarkSceneObject(Component target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }
    }
}
