using System.Collections.Generic;
using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class ValidationResult
    {
        private readonly List<string> warnings = new List<string>();
        private readonly List<string> errors = new List<string>();

        public IReadOnlyList<string> Warnings { get { return warnings; } }
        public IReadOnlyList<string> Errors { get { return errors; } }
        public bool IsValid { get { return errors.Count == 0; } }

        public void AddWarning(string message)
        {
            warnings.Add(message);
        }

        public void AddError(string message)
        {
            errors.Add(message);
        }
    }

    public static class ConfigValidator
    {
        public static ValidationResult Validate(UnitTypeConfig config)
        {
            ValidationResult result = new ValidationResult();

            if (config == null)
            {
                result.AddError("UnitTypeConfig is null.");
                return result;
            }

            if (config.spawnConfig == null)
                result.AddWarning("SpawnConfig is null; runtime defaults will be used.");
            else if (config.spawnConfig.unitCount <= 0)
                result.AddError("SpawnConfig.unitCount must be greater than 0.");

            if (config.movementConfig == null)
                result.AddWarning("MovementConfig is null; runtime defaults will be used.");
            if (config.flockingConfig == null)
                result.AddWarning("FlockingConfig is null; runtime defaults will be used.");
            if (config.animationConfig == null)
                result.AddWarning("AnimationConfig is null; runtime defaults will be used.");
            if (config.combatConfig == null)
                result.AddWarning("CombatConfig is null; runtime defaults will be used.");
            if (config.renderConfig == null)
                result.AddWarning("RenderConfig is null; rendering must be configured separately.");
            if (!string.IsNullOrWhiteSpace(config.unitTypeClassName))
            {
                System.Type unitType = UnitTypeTypeResolver.Resolve(config.unitTypeClassName);
                if (unitType == null)
                    result.AddError("UnitType class was not found: " + config.unitTypeClassName);
                else if (!typeof(IUnitType).IsAssignableFrom(unitType))
                    result.AddError("UnitType class must implement IUnitType: " + config.unitTypeClassName);
            }

            return result;
        }

        public static void EnsureRuntimeDefaults(UnitTypeConfig config)
        {
            if (config == null)
                return;

            if (config.spawnConfig == null)
                config.spawnConfig = CreateRuntimeConfig<SpawnConfig>("Runtime SpawnConfig");
            if (config.movementConfig == null)
                config.movementConfig = CreateRuntimeConfig<MovementConfig>("Runtime MovementConfig");
            if (config.flockingConfig == null)
                config.flockingConfig = CreateRuntimeConfig<FlockingConfig>("Runtime FlockingConfig");
            if (config.animationConfig == null)
                config.animationConfig = CreateRuntimeConfig<AnimationConfig>("Runtime AnimationConfig");
            if (config.combatConfig == null)
                config.combatConfig = CreateRuntimeConfig<CombatConfig>("Runtime CombatConfig");
            if (config.renderConfig == null)
                config.renderConfig = CreateRuntimeConfig<RenderConfig>("Runtime RenderConfig");
            if (string.IsNullOrWhiteSpace(config.unitTypeClassName))
                config.unitTypeClassName = "MassGPUPhysics.Stage7.DefaultSwordUnit";
        }

        private static T CreateRuntimeConfig<T>(string name) where T : ScriptableObject
        {
            T config = ScriptableObject.CreateInstance<T>();
            config.name = name;
            config.hideFlags = HideFlags.DontSave;
            return config;
        }
    }
}
