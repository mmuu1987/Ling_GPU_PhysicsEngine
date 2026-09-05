using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    public static class WarSandboxScenarioPresetAuthoring
    {
        public static bool Capture(
            WarSandboxScenarioPreset preset,
            MassEngineManager manager,
            WarSandboxBattleController controller)
        {
            if (preset == null || manager == null || controller == null)
                return false;

            Undo.RecordObject(preset, "Capture War Sandbox Scenario");
            if (!preset.CaptureFrom(manager, controller))
                return false;

            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            return true;
        }

        public static bool Apply(
            WarSandboxScenarioPreset preset,
            MassEngineManager manager,
            WarSandboxBattleController controller)
        {
            if (preset == null || manager == null || controller == null)
                return false;

            Object[] targets = CollectApplyTargets(preset, manager, controller).ToArray();
            Undo.RecordObjects(targets, "Apply War Sandbox Scenario");
            if (!preset.ApplyTo(manager, controller))
                return false;

            for (int i = 0; i < targets.Length; i++)
                EditorUtility.SetDirty(targets[i]);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            AssetDatabase.SaveAssets();
            return true;
        }

        private static List<Object> CollectApplyTargets(
            WarSandboxScenarioPreset preset,
            MassEngineManager manager,
            WarSandboxBattleController controller)
        {
            var result = new List<Object> { manager, controller };
            var unique = new HashSet<Object> { manager, controller };

            Add(unique, result, preset.systemConfig != null ? preset.systemConfig.simulationConfig : null);
            Add(unique, result, preset.systemConfig != null ? preset.systemConfig.runtimeFlowConfig : null);
            Add(unique, result, preset.systemConfig != null ? preset.systemConfig.runtimeCombatConfig : null);

            if (preset.deployments != null)
            {
                for (int i = 0; i < preset.deployments.Length; i++)
                {
                    WarSandboxDeploymentSnapshot deployment = preset.deployments[i];
                    UnitTypeConfig unitType = deployment != null ? deployment.unitType : null;
                    Add(unique, result, unitType);
                    Add(unique, result, unitType != null ? unitType.spawnConfig : null);
                }
            }

            return result;
        }

        private static void Add(HashSet<Object> unique, List<Object> result, Object value)
        {
            if (value != null && unique.Add(value))
                result.Add(value);
        }
    }
}
