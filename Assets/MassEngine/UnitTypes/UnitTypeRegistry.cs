using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MassEngine
{
    /// <summary>
    /// Registry of all unit types in the running scenario. Owns buffer offset / unit
    /// type index assignment and aggregates per-unit-type GPU settings for upload.
    /// Team layout in the agent buffer carries NO semantic meaning: every kernel resolves
    /// team and unit type through teamIdBuffer / unitTypeIndexBuffer.
    /// </summary>
    public sealed class UnitTypeRegistry
    {
        private readonly List<IUnitType> registeredTypes = new List<IUnitType>();

        public IReadOnlyList<IUnitType> RegisteredTypes { get { return registeredTypes; } }

        public int UnitTypeCount { get { return registeredTypes.Count; } }

        public int TotalAgentCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < registeredTypes.Count; i++)
                    total += Mathf.Max(0, registeredTypes[i].UnitCount);
                return total;
            }
        }

        /// <summary>
        /// Engine-internal: types must be registered through RegisterFromScenario so
        /// that buffer slices, GPU settings cache and allocation signature stay in
        /// sync. To add unit types at runtime, edit ScenarioConfig.unitTypes and call
        /// MassEngineManager.ResetScenario()/Initialize().
        /// </summary>
        internal void Register(IUnitType unitType)
        {
            if (unitType == null)
                return;

            registeredTypes.Add(unitType);
        }

        public void RegisterFromScenario(ScenarioConfig scenarioConfig)
        {
            registeredTypes.Clear();

            if (scenarioConfig == null || scenarioConfig.unitTypes == null)
                return;

            for (int i = 0; i < scenarioConfig.unitTypes.Length; i++)
            {
                UnitTypeConfig config = scenarioConfig.unitTypes[i];
                ValidationResult validation = ConfigValidator.Validate(config);
                if (!validation.IsValid)
                {
                    for (int errorIndex = 0; errorIndex < validation.Errors.Count; errorIndex++)
                        Debug.LogError("MassEngine unit type skipped: " + validation.Errors[errorIndex], config);
                    continue;
                }

                for (int warningIndex = 0; warningIndex < validation.Warnings.Count; warningIndex++)
                    Debug.LogWarning(validation.Warnings[warningIndex], config);

                Register(CreateUnit(config));
            }
        }

        public void InitializeAll(MassGpuBufferManager buffers, ComputePipelineOrchestrator pipeline)
        {
            int offset = 0;
            int total = TotalAgentCount;

            for (int i = 0; i < registeredTypes.Count; i++)
            {
                IUnitType unitType = registeredTypes[i];
                UnitTypeInitContext context = new UnitTypeInitContext
                {
                    unitTypeIndex = i,
                    bufferOffset = offset,
                    totalAgentCount = total,
                    bufferManager = buffers,
                    pipeline = pipeline
                };

                unitType.Initialize(context);
                unitType.OnBuffersBound(buffers);
                offset += unitType.UnitCount;
            }
        }

        public void GenerateAgents(AgentData[] agents)
        {
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                IUnitType unitType = registeredTypes[i];
                if (unitType.SpawnModule != null)
                    unitType.SpawnModule.GenerateAgents(agents, unitType.BufferOffset, unitType.UnitCount, unitType.TeamId);
            }
        }

        public void FillCombatArrays(int[] teamIds, int[] hpValues, int[] unitTypeIndices)
        {
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                IUnitType unitType = registeredTypes[i];
                int maxHp = unitType.CombatModule != null ? unitType.CombatModule.MaxHp : 100;

                for (int agentIndex = 0; agentIndex < unitType.UnitCount; agentIndex++)
                {
                    int bufferIndex = unitType.BufferOffset + agentIndex;
                    if (teamIds != null && bufferIndex < teamIds.Length)
                        teamIds[bufferIndex] = unitType.TeamId;
                    if (hpValues != null && bufferIndex < hpValues.Length)
                        hpValues[bufferIndex] = maxHp;
                    if (unitTypeIndices != null && bufferIndex < unitTypeIndices.Length)
                        unitTypeIndices[bufferIndex] = unitType.UnitTypeIndex;
                }
            }
        }

        /// <summary>
        /// Refreshes the per-unit-type GPU settings into a caller-owned array
        /// (allocation-free per frame). Returns false when the array size mismatches.
        /// </summary>
        public bool FillGpuSettings(UnitTypeGpuSettings[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            for (int i = 0; i < registeredTypes.Count; i++)
                target[i] = registeredTypes[i].BuildGpuSettings();

            return true;
        }

        /// <summary>
        /// CPU-only projectile impact splash radius per unit type (0 = single target).
        /// Only DefaultCombatModule configs declare it; custom combat modules stay single-target.
        /// </summary>
        public bool FillProjectileSplashRadii(float[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                float radius = config != null ? config.projectileSplashRadius : 0f;
                target[i] = float.IsNaN(radius) || float.IsInfinity(radius) ? 0f : Mathf.Max(0f, radius);
            }

            return true;
        }

        /// <summary>
        /// 炸飞 (2026-10-17, render only): knock-fly apex height per unit type; only splash shooters count.
        /// Returns true only when at least one unit type knocks bodies flying.
        /// </summary>
        public bool FillKnockFlyHeights(float[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            bool any = false;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                float height = config != null && config.projectileSplashRadius > 0f ? config.knockFlyHeight : 0f;
                height = float.IsNaN(height) || float.IsInfinity(height) ? 0f : Mathf.Clamp(height, 0f, 50f);
                target[i] = height;
                any |= height > 0f;
            }

            return any;
        }

        /// <summary>
        /// Melee charge parameters per unit type: x = damage multiplier (1 = off), y = minimum speed fraction.
        /// Returns true only when at least one unit type charges, so callers can keep the legacy kernel otherwise.
        /// </summary>
        public bool FillMeleeCharge(Vector2[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            bool any = false;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                float multiplier = config != null ? config.chargeDamageMultiplier : 1f;
                float fraction = config != null ? config.chargeMinSpeedFraction : 0.6f;
                if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1f) multiplier = 1f;
                if (float.IsNaN(fraction) || float.IsInfinity(fraction)) fraction = 0.6f;
                bool melee = config == null || config.projectileRange <= 0.01f;
                if (!melee) multiplier = 1f;
                target[i] = new Vector2(multiplier, Mathf.Clamp(fraction, 0f, 2f));
                any |= multiplier > 1f;
            }

            return any;
        }

        /// <summary>
        /// Flight altitude per unit type in metres (0 = ground). Returns true only when at least one type flies,
        /// so callers can keep the legacy kernels otherwise.
        /// </summary>
        public bool FillProjectileFx(Vector4[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            bool any = false;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                Color color = config != null ? config.projectileFxColor : Color.clear;
                bool on = color.a > 0f && !(float.IsNaN(color.r) || float.IsNaN(color.g) || float.IsNaN(color.b));
                Color rgb = linear ? new Color(color.r, color.g, color.b, 1f).linear : color;
                target[i] = on ? new Vector4(rgb.r, rgb.g, rgb.b, 1f) : Vector4.zero;
                any |= on;
            }

            return any;
        }

        /// <summary>
        /// Flight altitude per unit type (see FillFlightAltitudes).
        /// </summary>
        public bool FillFlightAltitudes(float[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            bool any = false;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                float altitude = config != null ? config.flightAltitude : 0f;
                if (float.IsNaN(altitude) || float.IsInfinity(altitude) || altitude < 0f) altitude = 0f;
                target[i] = altitude;
                any |= altitude > 0f;
            }

            return any;
        }

        /// <summary>
        /// Melee splash (stomp) radius per unit type (0 = single target). Returns true only when at least one
        /// melee unit type has a radius, so callers can keep the legacy kernel otherwise.
        /// </summary>
        public bool FillMeleeSplash(float[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            bool any = false;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                float radius = config != null ? config.meleeSplashRadius : 0f;
                if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0f) radius = 0f;
                if (config != null && config.projectileRange > 0.01f) radius = 0f;
                target[i] = radius;
                any |= radius > 0f;
            }

            return any;
        }

        /// <summary>
        /// Trample per unit type: x = damage multiplier (0 = off), y = speed loss per trampled enemy, z = minimum trample
        /// speed fraction, w = share of incoming damage taken while at trample speed. Returns true only when at least one melee unit type tramples.
        /// </summary>
        public bool FillTrample(Vector4[] target)
        {
            if (target == null || target.Length != registeredTypes.Count)
                return false;

            bool any = false;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                CombatConfig config = (registeredTypes[i].CombatModule as DefaultCombatModule)?.Config;
                float multiplier = config != null ? config.trampleDamageMultiplier : 0f;
                if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) multiplier = 0f;
                if (config != null && config.projectileRange > 0.01f) multiplier = 0f;
                float loss = config != null ? Mathf.Clamp01(config.trampleSpeedLoss) : 0f;
                float fraction = config != null ? Mathf.Clamp(config.trampleMinSpeedFraction, 0.05f, 2f) : 0.45f;
                if (float.IsNaN(loss)) loss = 0f;
                if (float.IsNaN(fraction)) fraction = 0.45f;
                float taken = config != null ? config.trampleDamageTaken : 1f;
                taken = float.IsNaN(taken) ? 1f : Mathf.Clamp01(taken);
                target[i] = multiplier > 0f ? new Vector4(multiplier, loss, fraction, taken) : Vector4.zero;
                any |= multiplier > 0f;
            }

            return any;
        }

        public int CountAgentsForTeam(int teamId)
        {
            int count = 0;
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                if (registeredTypes[i].TeamId == teamId)
                    count += registeredTypes[i].UnitCount;
            }

            return count;
        }

        /// <summary>
        /// Resolves the configured flow target for a team: the first registered unit type
        /// on that team whose MovementModule declares an active target wins.
        /// </summary>
        public bool TryGetConfiguredFlowTarget(int teamId, out FlowFieldTarget target)
        {
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                IUnitType unitType = registeredTypes[i];
                if (unitType.TeamId != teamId || unitType.MovementModule == null)
                    continue;

                FlowFieldTarget candidate = unitType.MovementModule.Target;
                if (candidate.active)
                {
                    WarnShadowedTargets(teamId, i);
                    target = candidate;
                    return true;
                }
            }

            target = default;
            return false;
        }

        // The engine keeps ONE flow field per team, so only the first active target on
        // a team wins. A second configured target would otherwise be ignored with no
        // trace - warn once per (team, shadowed type).
        private readonly System.Collections.Generic.HashSet<int> warnedShadowedTargets = new System.Collections.Generic.HashSet<int>();

        private void WarnShadowedTargets(int teamId, int winnerIndex)
        {
            for (int i = winnerIndex + 1; i < registeredTypes.Count; i++)
            {
                IUnitType other = registeredTypes[i];
                if (other.TeamId != teamId || other.MovementModule == null || !other.MovementModule.Target.active)
                    continue;
                if (warnedShadowedTargets.Add(teamId * 1024 + i))
                    Debug.LogWarning("MassEngine: unit type " + i + " (team " + teamId + ") declares a flow target but is shadowed by unit type " + winnerIndex + " - only the first active target per team takes effect. Use MassEngineManager.SetFlowTargetOverride for runtime orders.");
            }
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < registeredTypes.Count; i++)
                registeredTypes[i].Release();

            registeredTypes.Clear();
        }

        private static IUnitType CreateUnit(UnitTypeConfig config)
        {
            string className = string.IsNullOrWhiteSpace(config.unitTypeClassName)
                ? "MassEngine.DefaultSwordUnit"
                : config.unitTypeClassName;
            System.Type type = UnitTypeTypeResolver.Resolve(className);

            if (type == null || !typeof(IUnitType).IsAssignableFrom(type))
            {
                Debug.LogError("Invalid MassEngine UnitType class: " + className, config);
                return new DefaultSwordUnit(config);
            }

            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(UnitTypeConfig) },
                null);
            if (constructor == null)
            {
                Debug.LogError("MassEngine UnitType class must expose a constructor taking UnitTypeConfig: " + className, config);
                return new DefaultSwordUnit(config);
            }

            return (IUnitType)constructor.Invoke(new object[] { config });
        }
    }
}

