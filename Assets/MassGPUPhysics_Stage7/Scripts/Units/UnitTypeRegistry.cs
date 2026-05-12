using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MassGPUPhysics.Stage7
{
    public sealed class UnitTypeRegistry
    {
        private readonly List<IUnitType> registeredTypes = new List<IUnitType>();

        public IReadOnlyList<IUnitType> RegisteredTypes { get { return registeredTypes; } }

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

        public void RegisterFromScenario(ScenarioConfig_Stage7 scenarioConfig)
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
                        Debug.LogError(validation.Errors[errorIndex], config);
                    continue;
                }

                for (int warningIndex = 0; warningIndex < validation.Warnings.Count; warningIndex++)
                    Debug.LogWarning(validation.Warnings[warningIndex], config);

                Register(CreateUnit(config));
            }
        }

        public void InitializeAll(MassGpuBufferManager_Stage7 buffers, ComputePipelineOrchestrator pipeline)
        {
            int offset = 0;
            int total = TotalAgentCount;

            for (int i = 0; i < registeredTypes.Count; i++)
            {
                IUnitType unitType = registeredTypes[i];
                UnitTypeInitContext context = new UnitTypeInitContext
                {
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

        public void FillCombatArrays(int[] teamIds, int[] hpValues)
        {
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                IUnitType unitType = registeredTypes[i];
                int maxHp = unitType.Config != null && unitType.Config.combatConfig != null
                    ? Mathf.Max(1, unitType.Config.combatConfig.maxHp)
                    : 100;

                for (int agentIndex = 0; agentIndex < unitType.UnitCount; agentIndex++)
                {
                    int bufferIndex = unitType.BufferOffset + agentIndex;
                    if (teamIds != null && bufferIndex < teamIds.Length)
                        teamIds[bufferIndex] = unitType.TeamId;
                    if (hpValues != null && bufferIndex < hpValues.Length)
                        hpValues[bufferIndex] = maxHp;
                }
            }
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

        public UnitTypeConfig FindFirstConfigForTeam(int teamId)
        {
            for (int i = 0; i < registeredTypes.Count; i++)
            {
                if (registeredTypes[i].TeamId == teamId)
                    return registeredTypes[i].Config;
            }

            return registeredTypes.Count > 0 ? registeredTypes[0].Config : null;
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
                ? "MassGPUPhysics.Stage7.DefaultSwordUnit"
                : config.unitTypeClassName;
            System.Type type = UnitTypeTypeResolver.Resolve(className);

            if (type == null || !typeof(IUnitType).IsAssignableFrom(type))
            {
                Debug.LogError("Invalid Stage7 UnitType class: " + className, config);
                return new DefaultSwordUnit(config);
            }

            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(UnitTypeConfig) },
                null);
            if (constructor == null)
            {
                Debug.LogError("Stage7 UnitType class must expose a constructor taking UnitTypeConfig: " + className, config);
                return new DefaultSwordUnit(config);
            }

            return (IUnitType)constructor.Invoke(new object[] { config });
        }
    }
}
