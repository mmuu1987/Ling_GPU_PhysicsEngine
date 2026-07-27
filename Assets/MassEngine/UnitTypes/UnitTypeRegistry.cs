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

        public void Register(IUnitType unitType)
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
                    target = candidate;
                    return true;
                }
            }

            target = default;
            return false;
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
