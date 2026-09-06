using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Asset-safe roster editing for the War Sandbox editor. A new entry gets its own
    /// SpawnConfig, while movement/combat/animation/render templates remain shared.
    /// Removing an entry only changes the scenario roster; it never deletes an asset.
    /// </summary>
    public static class WarSandboxRosterEditor
    {
        public static bool TryAddUnitType(
            ScenarioConfig scenario,
            UnitTypeConfig template,
            int teamId,
            string displayName,
            out UnitTypeConfig added,
            out string error)
        {
            added = null;
            error = null;
            if (scenario == null)
            {
                error = "Assign a ScenarioConfig before adding a unit type.";
                return false;
            }
            if (template == null || template.spawnConfig == null)
            {
                error = "Choose a unit type template with a SpawnConfig.";
                return false;
            }
            if (teamId < 0)
            {
                error = "Team ID must be zero or greater.";
                return false;
            }
            if (!AssetDatabase.Contains(template) || !AssetDatabase.Contains(template.spawnConfig))
            {
                error = "Save the unit type template and its SpawnConfig before copying it.";
                return false;
            }

            string scenarioPath = AssetDatabase.GetAssetPath(scenario);
            if (string.IsNullOrEmpty(scenarioPath))
            {
                error = "Save the ScenarioConfig asset before adding a unit type.";
                return false;
            }

            string directory = Path.GetDirectoryName(scenarioPath);
            if (string.IsNullOrEmpty(directory))
                directory = "Assets/Game/Settings";
            directory = directory.Replace((char)92, '/');

            string baseName = SanitizeName(displayName);
            if (string.IsNullOrEmpty(baseName))
                baseName = "WarSandboxUnit";
            string unitPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + baseName + ".asset");
            string spawnPath = AssetDatabase.GenerateUniqueAssetPath(
                directory + "/" + Path.GetFileNameWithoutExtension(unitPath) + "_Spawn.asset");

            SpawnConfig spawn = Object.Instantiate(template.spawnConfig);
            spawn.name = Path.GetFileNameWithoutExtension(spawnPath);
            UnitTypeConfig unit = Object.Instantiate(template);
            unit.name = Path.GetFileNameWithoutExtension(unitPath);
            unit.unitTypeName = string.IsNullOrEmpty(displayName) ? template.unitTypeName : displayName;
            unit.teamId = teamId;
            unit.spawnConfig = spawn;

            AssetDatabase.CreateAsset(spawn, spawnPath);
            AssetDatabase.CreateAsset(unit, unitPath);

            UnitTypeConfig[] current = scenario.unitTypes ?? new UnitTypeConfig[0];
            var next = new UnitTypeConfig[current.Length + 1];
            for (int i = 0; i < current.Length; i++)
                next[i] = current[i];
            next[current.Length] = unit;

            Undo.RecordObject(scenario, "Add War Sandbox Unit Type");
            scenario.unitTypes = next;
            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();
            added = unit;
            return true;
        }

        public static bool RemoveUnitType(ScenarioConfig scenario, int index, out string error)
        {
            error = null;
            if (scenario == null || scenario.unitTypes == null || index < 0 || index >= scenario.unitTypes.Length)
            {
                error = "The selected roster entry no longer exists.";
                return false;
            }

            UnitTypeConfig[] current = scenario.unitTypes;
            var next = new UnitTypeConfig[current.Length - 1];
            for (int source = 0, target = 0; source < current.Length; source++)
            {
                if (source == index)
                    continue;
                next[target++] = current[source];
            }

            Undo.RecordObject(scenario, "Remove War Sandbox Unit Type");
            scenario.unitTypes = next;
            EditorUtility.SetDirty(scenario);
            return true;
        }

        public static int ResolveNextTeamId(ScenarioConfig scenario)
        {
            if (scenario == null || scenario.unitTypes == null || scenario.unitTypes.Length == 0)
                return 0;

            int highestTeamId = -1;
            for (int i = 0; i < scenario.unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = scenario.unitTypes[i];
                if (unitType != null)
                    highestTeamId = Mathf.Max(highestTeamId, unitType.teamId);
            }
            return highestTeamId + 1;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var chars = new List<char>(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char valueChar = value[i];
                if (char.IsLetterOrDigit(valueChar) || valueChar == '_' || valueChar == '-')
                    chars.Add(valueChar);
                else if (chars.Count == 0 || chars[chars.Count - 1] != '_')
                    chars.Add('_');
            }
            return new string(chars.ToArray()).Trim('_');
        }
    }
}
