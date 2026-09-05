using System.Collections.Generic;

namespace MassEngine
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

    /// <summary>
    /// Pure validation. This class never mutates configuration assets: missing optional
    /// sub-configs fall back to hard-coded defaults inside UnitTypeGpuSettings /
    /// the default modules at runtime, without writing anything back.
    /// </summary>
    public static class ConfigValidator
    {
        /// <summary>
        /// Highest teamId a scenario may use. Every team-partitioned buffer is indexed by raw
        /// teamId, so this ceiling is about memory rather than doctrine: a mistyped teamId would
        /// otherwise reserve a flow field slice and a grid partition for a team that has no units.
        /// </summary>
        public const int MaxTeamId = 7;

        public static ValidationResult Validate(UnitTypeConfig config)
        {
            ValidationResult result = new ValidationResult();

            if (config == null)
            {
                result.AddError("UnitTypeConfig is null.");
                return result;
            }

            if (config.spawnConfig == null)
                result.AddError("SpawnConfig is null; the unit type cannot be spawned and will be skipped.");
            else if (config.spawnConfig.unitCount <= 0)
                result.AddError("SpawnConfig.unitCount must be greater than 0.");

            if (config.teamId < 0 || config.teamId > MaxTeamId)
                result.AddError(
                    "teamId must be in [0, " + MaxTeamId + "] (0 = attacker, 1 = defender, 2+ = extra armies); " +
                    "each team owns a flow field slice and a grid partition indexed by this id. Unit type will be skipped.");

            if (config.movementConfig == null)
                result.AddWarning("MovementConfig is null; built-in defaults will be used (asset is not modified).");
            if (config.flockingConfig == null)
                result.AddWarning("FlockingConfig is null; built-in defaults will be used (asset is not modified).");
            if (config.animationConfig == null)
                result.AddWarning("AnimationConfig is null; built-in defaults will be used (asset is not modified).");
            if (config.combatConfig == null)
                result.AddWarning("CombatConfig is null; built-in defaults will be used (asset is not modified).");
            if (config.renderConfig == null)
                result.AddWarning("RenderConfig is null; the unit type will simulate but not render.");

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
    }
}
