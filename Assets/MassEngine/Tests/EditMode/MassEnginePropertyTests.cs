using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MassEngine.Projectiles;

namespace MassEngine.Tests
{
    /// <summary>
    /// EditMode tests for the CPU-side contracts of MassEngine: data layout invariants,
    /// registry/module composition, GPU settings channel, state model mirror, config
    /// validation, and pipeline dispatch order (via the orchestrator's dispatch hook).
    /// GPU kernel behaviour (damage accrual, death, movement) is covered by the PlayMode
    /// suite in Tests/PlayMode/MassEngineGpuKernelTests.cs which runs real dispatches.
    /// </summary>
    public sealed class MassEnginePropertyTests
    {
        // ------------------------------------------------------------------
        // Property 1: GPU data layout invariants
        // ------------------------------------------------------------------

        [Test]
        public void AgentDataStrideRemains56Bytes()
        {
            Assert.AreEqual(56, Marshal.SizeOf<AgentData>());
        }

        [Test]
        public void UnitTypeGpuSettingsStrideMatchesHlslStruct()
        {
            Assert.AreEqual(UnitTypeGpuSettings.StrideBytes, Marshal.SizeOf<UnitTypeGpuSettings>());
            Assert.AreEqual(0, UnitTypeGpuSettings.StrideBytes % 16, "StructuredBuffer element size should stay 16-byte aligned for cross-platform safety.");
        }

        [Test]
        public void ProjectileGpuDataStrideIs64Bytes()
        {
            Assert.AreEqual(64, Projectiles.ProjectileGpuData.Stride);
            Assert.AreEqual(64, Marshal.SizeOf<Projectiles.ProjectileGpuData>());
        }

        [Test]
        public void ProjectileBufferAllocationTest()
        {
            MassGpuBufferManager bufferManager = new MassGpuBufferManager();
            int agentCount = 1000;
            int expectedMaxProjectiles = agentCount / 4;

            bufferManager.Allocate(agentCount, 256, 32, 128, 128, 1);

            Assert.IsNotNull(bufferManager.projectileBuffer, "projectileBuffer should be allocated");
            Assert.AreEqual(expectedMaxProjectiles, bufferManager.MaxProjectiles, "MaxProjectiles should be agentCount / 4");
            Assert.IsNotNull(bufferManager.combatBuffers.launchRequestBuffer, "launchRequestBuffer should be allocated");

            bufferManager.ReleaseAll();

            Assert.IsNull(bufferManager.projectileBuffer, "projectileBuffer should be released");
            Assert.AreEqual(0, bufferManager.MaxProjectiles, "MaxProjectiles should be reset to 0");
        }

        [Test]
        public void TeamSpatialTelemetryDecodesCentroidAndBounds()
        {
            int[] values = new int[16];
            values[0] = 4;
            values[1] = 40;
            values[2] = 20;
            values[3] = 2;
            values[4] = -3;
            values[5] = 18;
            values[6] = 11;
            values[7] = 3;

            Assert.That(BattleTelemetry.TryDecodeTeamSpatialStats(values, 0, out TeamSpatialTelemetry team), Is.True);
            Assert.That(team.aliveCount, Is.EqualTo(4));
            Assert.That(team.centroid, Is.EqualTo(new Vector3(10f, 0f, 5f)));
            Assert.That(team.bounds.center, Is.EqualTo(team.centroid));
            Assert.That(team.bounds.min.x, Is.LessThanOrEqualTo(2f));
            Assert.That(team.bounds.max.x, Is.GreaterThanOrEqualTo(18f));
            Assert.That(team.observationZoneCount, Is.EqualTo(3));
            Assert.That(BattleTelemetry.TryDecodeTeamSpatialStats(values, 1, out _), Is.False);
        }

        // ------------------------------------------------------------------
        // Property 2: public field budget — every type in the namespace, no
        // suffix filtering, so new god-objects cannot hide from this test.
        // Alignment padding does not count; see CountDataFields.
        // ------------------------------------------------------------------

        [Test]
        public void NoTypeInNamespaceExceedsPublicFieldBudget()
        {
            const int budget = 30;
            Type[] offenders = typeof(MassEngineManager).Assembly.GetTypes()
                .Where(type => type.Namespace == "MassEngine")
                .Where(type => !type.IsEnum && !type.IsInterface)
                .Where(type => CountDataFields(type) > budget)
                .ToArray();

            Assert.IsEmpty(offenders,
                "Types over the public field budget: " + string.Join(", ", offenders.Select(t =>
                    t.FullName + "(" + CountDataFields(t) + ")")));
        }

        /// <summary>
        /// Public instance fields that actually carry data. Alignment padding is excluded
        /// because it is forced by the GPU contract rather than by design weight:
        /// UnitTypeGpuSettings has to stay 144 bytes and 16-byte aligned - see
        /// UnitTypeGpuSettingsStrideMatchesHlslStruct above, and MassGpuBufferManager,
        /// which refuses to allocate at all when the stride drifts - which costs it six
        /// padding ints that no line of C# or HLSL ever reads. Counting those made the
        /// budget rule contradict the alignment rule; the 30 fields that do carry data
        /// were always inside the budget. A real god-object still cannot hide: field
        /// number 31 fails the test whatever it is named.
        /// </summary>
        private static int CountDataFields(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Count(field => !field.Name.StartsWith("padding", StringComparison.Ordinal));
        }

        // ------------------------------------------------------------------
        // Property 3: spawn containment
        // ------------------------------------------------------------------

        [Test]
        public void SpawnedAgentsRemainInsideSpawnArea()
        {
            SpawnConfig config = ScriptableObject.CreateInstance<SpawnConfig>();
            config.spawnCenter = new Vector3(10f, 2f, -4f);
            config.spawnSize = new Vector3(8f, 0f, 12f);
            DefaultSpawnModule module = new DefaultSpawnModule(config);
            AgentData[] agents = new AgentData[128];

            module.GenerateAgents(agents, 0, agents.Length, 0);

            for (int i = 0; i < agents.Length; i++)
            {
                Assert.That(agents[i].position.x, Is.InRange(6f, 14f));
                Assert.AreEqual(2f, agents[i].position.y);
                Assert.That(agents[i].position.z, Is.InRange(-10f, 2f));
                Assert.AreEqual((int)AgentState.Idle, agents[i].currentState);
            }

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void SpawnFormationIsDeterministicAndAvoidsDefaultDensityOverlap()
        {
            SpawnConfig config = ScriptableObject.CreateInstance<SpawnConfig>();
            config.unitCount = 400;
            config.formationDensity = 0.5f;
            config.formationAspect = 2f;
            config.spawnSize = Vector3.zero;
            DefaultSpawnModule module = new DefaultSpawnModule(config);
            AgentData[] first = new AgentData[config.unitCount];
            AgentData[] second = new AgentData[config.unitCount];

            module.GenerateAgents(first, 0, first.Length, 0);
            module.GenerateAgents(second, 0, second.Length, 0);

            float minimumDistanceSqr = float.MaxValue;
            for (int i = 0; i < first.Length; i++)
            {
                Assert.AreEqual(first[i].position, second[i].position, "formation must be reproducible at index " + i);
                Assert.AreEqual(first[i].currentAnimationTime, second[i].currentAnimationTime, 0.000001f);
                for (int j = i + 1; j < first.Length; j++)
                    minimumDistanceSqr = Mathf.Min(minimumDistanceSqr, (first[i].position - first[j].position).sqrMagnitude);
            }

            // Shipped sword units use radius 0.55m (1.10m contact diameter). The
            // default 0.5 agents/m2 formation must begin outside that contact range.
            Assert.Greater(Mathf.Sqrt(minimumDistanceSqr), 1.1f);
            UnityEngine.Object.DestroyImmediate(config);
        }

        // ------------------------------------------------------------------
        // Per-unit-type GPU parameter channel (Requirement 1.5 / 4.4 / 5.4)
        // ------------------------------------------------------------------

        [Test]
        public void ThreeUnitTypesEachKeepTheirOwnGpuSettings()
        {
            // Two attacker unit types with different tuning plus one defender: every unit
            // type must surface its OWN values in the uploaded settings array — nothing
            // may collapse onto "first config of the team".
            var swords = MakeUnitTypeConfig(teamId: 0, unitCount: 4, maxSpeed: 6f, attackDamage: 10, agentRadius: 0.45f);
            var archers = MakeUnitTypeConfig(teamId: 0, unitCount: 2, maxSpeed: 4f, attackDamage: 25, agentRadius: 0.6f);
            var guards = MakeUnitTypeConfig(teamId: 1, unitCount: 3, maxSpeed: 5f, attackDamage: 12, agentRadius: 0.5f);

            ScenarioConfig scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[] { swords.config, archers.config, guards.config };

            UnitTypeRegistry registry = new UnitTypeRegistry();
            registry.RegisterFromScenario(scenario);

            Assert.AreEqual(3, registry.UnitTypeCount);
            Assert.AreEqual(9, registry.TotalAgentCount);
            Assert.AreEqual(6, registry.CountAgentsForTeam(0));
            Assert.AreEqual(3, registry.CountAgentsForTeam(1));

            registry.InitializeAll(null, null);

            UnitTypeGpuSettings[] settings = new UnitTypeGpuSettings[3];
            Assert.IsTrue(registry.FillGpuSettings(settings));

            Assert.AreEqual(6f, settings[0].maxSpeed);
            Assert.AreEqual(4f, settings[1].maxSpeed);
            Assert.AreEqual(5f, settings[2].maxSpeed);
            Assert.AreEqual(10, settings[0].attackDamage);
            Assert.AreEqual(25, settings[1].attackDamage);
            Assert.AreEqual(12, settings[2].attackDamage);
            Assert.AreEqual(0, settings[0].teamId);
            Assert.AreEqual(0, settings[1].teamId);
            Assert.AreEqual(1, settings[2].teamId);

            // Buffer offsets are contiguous in registration order and unitTypeIndices map
            // every agent to its type.
            Assert.AreEqual(0, registry.RegisteredTypes[0].BufferOffset);
            Assert.AreEqual(4, registry.RegisteredTypes[1].BufferOffset);
            Assert.AreEqual(6, registry.RegisteredTypes[2].BufferOffset);

            int[] teamIds = new int[9];
            int[] hp = new int[9];
            int[] unitTypeIndices = new int[9];
            registry.FillCombatArrays(teamIds, hp, unitTypeIndices);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 1, 1, 2, 2, 2 }, unitTypeIndices);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0, 0, 1, 1, 1 }, teamIds);
            Assert.IsTrue(hp.All(value => value > 0));

            registry.ReleaseAll();
            swords.Destroy();
            archers.Destroy();
            guards.Destroy();
            UnityEngine.Object.DestroyImmediate(scenario);
        }

        [Test]
        public void RuntimeConfigEditsReachSettingsNextRefresh()
        {
            var unit = MakeUnitTypeConfig(teamId: 0, unitCount: 1, maxSpeed: 6f, attackDamage: 10, agentRadius: 0.45f);
            ScenarioConfig scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[] { unit.config };
            UnitTypeRegistry registry = new UnitTypeRegistry();
            registry.RegisterFromScenario(scenario);
            registry.InitializeAll(null, null);

            UnitTypeGpuSettings[] settings = new UnitTypeGpuSettings[1];
            registry.FillGpuSettings(settings);
            Assert.AreEqual(18f, settings[0].separationStrength);

            // Requirement 5.5: runtime parameter edits take effect on the next refresh.
            unit.flocking.separationStrength = 33f;
            registry.FillGpuSettings(settings);
            Assert.AreEqual(33f, settings[0].separationStrength);

            registry.ReleaseAll();
            unit.Destroy();
            UnityEngine.Object.DestroyImmediate(scenario);
        }

        [Test]
        public void GpuSettingsClampInvalidConfigValues()
        {
            FlockingConfig invalidFlocking = ScriptableObject.CreateInstance<FlockingConfig>();
            invalidFlocking.densityAvoidanceStrength = -2f;
            invalidFlocking.densityComfortPerSqm = -3f;
            invalidFlocking.densityPressureRangePerSqm = -1f;
            invalidFlocking.densitySpeedPenalty = 3f;
            invalidFlocking.speedVariation = 2f;
            invalidFlocking.laneBiasStrength = -1f;
            CombatConfig invalidCombat = ScriptableObject.CreateInstance<CombatConfig>();
            invalidCombat.targetAcquireRadius = -1f;
            invalidCombat.projectileTrailLength = -1f;
            UnitTypeConfig unit = ScriptableObject.CreateInstance<UnitTypeConfig>();
            unit.flockingConfig = invalidFlocking;
            unit.combatConfig = invalidCombat;

            UnitTypeGpuSettings settings = UnitTypeGpuSettings.FromConfig(unit);

            Assert.AreEqual(0f, settings.densityAvoidanceStrength);
            Assert.AreEqual(0f, settings.densityComfortPerSqm);
            Assert.AreEqual(0.01f, settings.densityPressureRangePerSqm);
            Assert.AreEqual(1f, settings.densitySpeedPenalty);
            Assert.AreEqual(0.5f, settings.speedVariation);
            Assert.AreEqual(0f, settings.laneBiasStrength);
            Assert.AreEqual(0.1f, settings.targetAcquireRadius);
            Assert.AreEqual(0f, settings.projectileTrailLength);

            UnityEngine.Object.DestroyImmediate(unit);
            UnityEngine.Object.DestroyImmediate(invalidCombat);
            UnityEngine.Object.DestroyImmediate(invalidFlocking);
        }

        [Test]
        public void ProjectileTrailLengthFlowsThroughCombatConfig()
        {
            CombatConfig combat = ScriptableObject.CreateInstance<CombatConfig>();
            combat.projectileTrailLength = 1.75f;
            UnitTypeConfig unit = ScriptableObject.CreateInstance<UnitTypeConfig>();
            unit.combatConfig = combat;

            UnitTypeGpuSettings settings = UnitTypeGpuSettings.FromConfig(unit);

            Assert.AreEqual(1.75f, settings.projectileTrailLength);

            UnityEngine.Object.DestroyImmediate(unit);
            UnityEngine.Object.DestroyImmediate(combat);
        }

        [Test]
        public void TracerPaletteResolvesPerTeamAndClampsPastItsEnd()
        {
            ProjectileRenderConfig config = ScriptableObject.CreateInstance<ProjectileRenderConfig>();
            config.teamColors = new[] { Color.red, Color.green, Color.blue };

            Assert.AreEqual(Color.red, config.ResolveTeamColor(0), "team 0 must use its own entry");
            Assert.AreEqual(Color.green, config.ResolveTeamColor(1), "team 1 must use its own entry");
            Assert.AreEqual(Color.blue, config.ResolveTeamColor(2), "team 2 must use its own entry");
            // Clamped, not wrapped: an unpainted army must not impersonate team 0's tracer.
            Assert.AreEqual(Color.blue, config.ResolveTeamColor(7), "a team past the palette reuses the last entry");
            Assert.AreEqual(Color.red, config.ResolveTeamColor(-1), "a negative team id cannot index out of range");

            // An empty palette is authoring in progress, not a reason to draw black tracers
            // on a black-blended pass - invisible projectiles read as a broken simulation.
            config.teamColors = new Color[0];
            Assert.AreEqual(Color.white, config.ResolveTeamColor(0), "an empty palette must fall back to white");
            config.teamColors = null;
            Assert.AreEqual(Color.white, config.ResolveTeamColor(0), "a null palette must fall back to white");

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void NullConfigsProduceUsableDefaults()
        {
            UnitTypeGpuSettings settings = UnitTypeGpuSettings.FromConfig(null);
            Assert.AreEqual(8f, settings.targetAcquireRadius);
            Assert.AreEqual(6f, settings.maxSpeed);
            Assert.AreEqual(1f, settings.projectileTrailLength);
            Assert.Greater(settings.deathClipDuration, 0f);
        }

        // ------------------------------------------------------------------
        // Registry guards (open/closed + explicit limits)
        // ------------------------------------------------------------------

        [Test]
        public void UnitTypeRegistryCreatesConfiguredSubclassWithoutCoreChanges()
        {
            UnitTypeConfig config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            SpawnConfig spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            spawn.unitCount = 3;
            config.spawnConfig = spawn;
            config.unitTypeClassName = typeof(TestUnitType).FullName;

            ScenarioConfig scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[] { config };
            UnitTypeRegistry registry = new UnitTypeRegistry();

            registry.RegisterFromScenario(scenario);

            Assert.AreEqual(1, registry.RegisteredTypes.Count);
            Assert.IsInstanceOf<TestUnitType>(registry.RegisteredTypes[0]);

            registry.ReleaseAll();
            UnityEngine.Object.DestroyImmediate(scenario);
            UnityEngine.Object.DestroyImmediate(spawn);
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void ExtraArmyTeamIdRegistersWhileOutOfRangeIdsAreRejectedLoudly()
        {
            // teamId 2 is a third army, not a typo: every team owns a flow field slice and a grid
            // partition indexed by its id, so the registry must let one through.
            var third = MakeUnitTypeConfig(teamId: 2, unitCount: 5, maxSpeed: 6f, attackDamage: 10, agentRadius: 0.45f);
            ScenarioConfig scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[] { third.config };
            UnitTypeRegistry registry = new UnitTypeRegistry();

            registry.RegisterFromScenario(scenario);
            Assert.AreEqual(1, registry.UnitTypeCount, "A third army must simulate, not be skipped.");
            Assert.AreEqual(2, registry.RegisteredTypes[0].TeamId);
            registry.ReleaseAll();

            // Past the ceiling it is a typo again: those buffers are sized from the widest teamId.
            var beyond = MakeUnitTypeConfig(
                teamId: ConfigValidator.MaxTeamId + 1, unitCount: 5, maxSpeed: 6f, attackDamage: 10, agentRadius: 0.45f);
            scenario.unitTypes = new[] { beyond.config };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("teamId must be in"));
            registry.RegisterFromScenario(scenario);

            Assert.AreEqual(0, registry.UnitTypeCount, "Out-of-range team ids must be rejected, not silently mis-simulated.");

            var negative = MakeUnitTypeConfig(teamId: -1, unitCount: 5, maxSpeed: 6f, attackDamage: 10, agentRadius: 0.45f);
            scenario.unitTypes = new[] { negative.config };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("teamId must be in"));
            registry.RegisterFromScenario(scenario);

            Assert.AreEqual(0, registry.UnitTypeCount);

            negative.Destroy();
            beyond.Destroy();
            third.Destroy();
            UnityEngine.Object.DestroyImmediate(scenario);
        }

        [Test]
        public void MissingSpawnConfigIsAnErrorAndUnitIsSkipped()
        {
            UnitTypeConfig config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            ValidationResult result = ConfigValidator.Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("SpawnConfig")));
            // Validation must not mutate the asset (no runtime defaults written back).
            Assert.IsNull(config.spawnConfig);
            Assert.IsNull(config.movementConfig);

            UnityEngine.Object.DestroyImmediate(config);
        }

        // ------------------------------------------------------------------
        // State model mirror (Requirement 10) — mirrors the HLSL semantics:
        // alive states re-derived by priority each frame, Dead terminal.
        // ------------------------------------------------------------------

        [Test]
        public void DeadIsTerminalAndAliveStatesAreFreelyRederived()
        {
            AgentState result;

            foreach (AgentState from in new[] { AgentState.Idle, AgentState.Move, AgentState.Engage, AgentState.Attack })
            {
                foreach (AgentState to in (AgentState[])Enum.GetValues(typeof(AgentState)))
                {
                    Assert.IsTrue(AgentStateMachine.TryTransition(from, to, out result), from + " -> " + to + " must be legal for alive agents");
                    Assert.AreEqual(to, result);
                }
            }

            foreach (AgentState to in new[] { AgentState.Idle, AgentState.Move, AgentState.Engage, AgentState.Attack })
            {
                Assert.IsFalse(AgentStateMachine.TryTransition(AgentState.Dead, to, out result), "Dead is terminal");
                Assert.AreEqual(AgentState.Dead, result);
            }

            Assert.IsTrue(AgentStateMachine.IsTerminal(AgentState.Dead));
        }

        [Test]
        public void ResolveMatchesGpuPriorityTable()
        {
            Assert.AreEqual(AgentState.Dead, AgentStateMachine.Resolve(true, true, true, true));
            Assert.AreEqual(AgentState.Attack, AgentStateMachine.Resolve(false, true, true, true));
            Assert.AreEqual(AgentState.Engage, AgentStateMachine.Resolve(false, false, true, true));
            Assert.AreEqual(AgentState.Move, AgentStateMachine.Resolve(false, false, false, true));
            Assert.AreEqual(AgentState.Idle, AgentStateMachine.Resolve(false, false, false, false));
        }

        [Test]
        public void ResolveConflictPicksHighestPriorityLegalRequest()
        {
            Assert.AreEqual(AgentState.Attack, AgentStateMachine.ResolveConflict(AgentState.Engage, AgentState.Move, AgentState.Attack));
            Assert.AreEqual(AgentState.Dead, AgentStateMachine.ResolveConflict(AgentState.Idle, AgentState.Move, AgentState.Dead));
            Assert.AreEqual(AgentState.Dead, AgentStateMachine.ResolveConflict(AgentState.Dead, AgentState.Move, AgentState.Attack));
        }

        // ------------------------------------------------------------------
        // Corpse lifetime mirror: the rule the classify kernel and both agent
        // vertex shaders implement (AgentDataCommon.hlsl / ResolveCorpseSink).
        // ------------------------------------------------------------------

        [Test]
        public void CorpseDespawnPointIsLingerPlusSink()
        {
            Assert.IsTrue(CorpseLifetime.DespawnEnabled(15f));
            Assert.AreEqual(16.5f, CorpseLifetime.DespawnSeconds(15f, 1.5f), 0.0001f);
            Assert.AreEqual(15f, CorpseLifetime.DespawnSeconds(15f, 0f), 0.0001f);
        }

        [Test]
        public void ZeroLingerKeepsCorpsesForever()
        {
            Assert.IsFalse(CorpseLifetime.DespawnEnabled(0f));
            Assert.AreEqual(0f, CorpseLifetime.DespawnSeconds(0f, 1.5f), 0.0001f);
            Assert.IsFalse(CorpseLifetime.IsDespawned(100000f, 0f, 1.5f), "0 linger must preserve the pre-despawn behaviour");
            Assert.AreEqual(0f, CorpseLifetime.SinkOffset(100000f, 0f, 1.5f, 2.2f), 0.0001f, "a corpse that never despawns must never sink");
        }

        [Test]
        public void CorpseSinksOnlyAfterTheLingerWindow()
        {
            Assert.AreEqual(0f, CorpseLifetime.SinkOffset(0f, 15f, 1.5f, 2.2f), 0.0001f);
            Assert.AreEqual(0f, CorpseLifetime.SinkOffset(14.9f, 15f, 1.5f, 2.2f), 0.0001f);
            Assert.AreEqual(1.1f, CorpseLifetime.SinkOffset(15.75f, 15f, 1.5f, 2.2f), 0.0001f, "halfway through the sink window");
            Assert.AreEqual(2.2f, CorpseLifetime.SinkOffset(16.5f, 15f, 1.5f, 2.2f), 0.0001f);
            Assert.AreEqual(2.2f, CorpseLifetime.SinkOffset(90f, 15f, 1.5f, 2.2f), 0.0001f, "the offset must clamp, not keep growing");
        }

        [Test]
        public void CorpseIsDespawnedExactlyAtTheSinkEnd()
        {
            Assert.IsFalse(CorpseLifetime.IsDespawned(16.49f, 15f, 1.5f));
            Assert.IsTrue(CorpseLifetime.IsDespawned(16.5f, 15f, 1.5f));
            Assert.IsTrue(CorpseLifetime.IsDespawned(20f, 15f, 1.5f));
        }

        [Test]
        public void CorpseAgeRunsPastTheDeathClipAndStopsAtDespawn()
        {
            const float deathClip = 1.5f;
            float age = 0f;
            for (int i = 0; i < 2000; i++)
                age = CorpseLifetime.Advance(age, 1f / 60f, 15f, 1.5f, deathClip);

            Assert.Greater(age, deathClip, "the accumulator doubles as corpse age, so it must outlive the death clip");
            Assert.AreEqual(CorpseLifetime.DespawnSeconds(15f, 1.5f), age, 0.0001f, "and must stop at the despawn point rather than grow without bound");
            Assert.IsTrue(CorpseLifetime.IsDespawned(age, 15f, 1.5f));
        }

        [Test]
        public void CorpseAgeStopsAtTheDeathClipWhenDespawnIsOff()
        {
            const float deathClip = 1.5f;
            float age = 0f;
            for (int i = 0; i < 600; i++)
                age = CorpseLifetime.Advance(age, 1f / 60f, 0f, 1.5f, deathClip);

            Assert.AreEqual(deathClip, age, 0.0001f, "with despawn off the death pose must hold at the last frame, as before");
        }

        [Test]
        public void LodConfigDefaultsRemoveCorpsesInUnderTwentySeconds()
        {
            LodConfig lod = ScriptableObject.CreateInstance<LodConfig>();
            try
            {
                Assert.IsTrue(CorpseLifetime.DespawnEnabled(lod.corpseLingerSeconds), "corpse despawn must be on by default");
                float despawn = CorpseLifetime.DespawnSeconds(lod.corpseLingerSeconds, lod.corpseSinkSeconds);
                Assert.GreaterOrEqual(despawn, 10f);
                Assert.LessOrEqual(despawn, 20f);
                Assert.Greater(lod.corpseSinkDepth, 0f, "the sink has to actually move the body below the ground plane");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(lod);
            }
        }

        // ------------------------------------------------------------------
        // Pipeline dispatch order (Requirement 9.1) via the dispatch hook.
        // ------------------------------------------------------------------

        private sealed class DispatchRecorder : IDispatchListener
        {
            public readonly List<string> Labels = new List<string>();

            public void OnDispatch(string kernelLabel)
            {
                Labels.Add(kernelLabel);
            }
        }

        [Test]
        public void DispatchOrderIsSpatialHashFlowDensityCombatThenPerUnitTypeLod()
        {
            MassGpuBufferManager buffers = new MassGpuBufferManager();
            buffers.Allocate(agentCount: 8, gridCellCount: 16, maxAgentsPerCell: 4, flowFieldResolutionX: 16, flowFieldResolutionZ: 16, unitTypeCount: 2);

            DispatchRecorder recorder = new DispatchRecorder();
            // Null shaders: dispatches are skipped but the recorder still sees intent,
            // and each missing kernel is reported exactly once.
            ComputePipelineOrchestrator orchestrator = new ComputePipelineOrchestrator(
                MassGpuShaderSet.Find(null, null, null, null, null), buffers, recorder);

            // 17 distinct kernel labels, each reported exactly once across both frames.
            for (int i = 0; i < 17; i++)
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("MassEngine skipped GPU dispatch"));

            PipelineFrameContext context = new PipelineFrameContext
            {
                totalAgentCount = 8,
                unitTypeCount = 2,
                agentThreadGroupsX = 1,
                gridThreadGroupsX = 1,
                rebuildDensityMap = true,
                densityMapThreadGroupsX = 1,
                densityMapThreadGroupsY = 1,
                teamFlows = new[]
                {
                    new TeamFlowFrameSettings { rebuildThisFrame = true, threadGroupsX = 1, resolutionX = 16, resolutionZ = 16 },
                    new TeamFlowFrameSettings { rebuildThisFrame = true, threadGroupsX = 1, resolutionX = 16, resolutionZ = 16 }
                }
            };

            orchestrator.DispatchFrame(context);
            // Second frame with identical missing shaders must not repeat the error spam.
            orchestrator.DispatchFrame(context);

            string[] expected =
            {
                "ClearGrid",
                "BuildSpatialHash",
                // Teams share the four kernels and are told apart by the label suffix, which is
                // also how the flowTeamId uniform is set - one team per dispatch group.
                "ClearRuntimeFlowResources[team0]",
                "BuildRuntimeFlowTargetDensity[team0]",
                "SelectRuntimeFlowTargets[team0]",
                "GenerateRuntimeFlowField[team0]",
                "ClearRuntimeFlowResources[team1]",
                "BuildRuntimeFlowTargetDensity[team1]",
                "SelectRuntimeFlowTargets[team1]",
                "GenerateRuntimeFlowField[team1]",
                "ClearDensityMap",
                "BuildDensityMap",
                "BuildEngagementSlotOccupancy",
                "ClearPendingDamage",
                "SimulateCombatAndAccumulateDamage",
                "ClassifyVisibleAgentsForUnitType[0]",
                "ClassifyVisibleAgentsForUnitType[1]"
            };

            CollectionAssert.AreEqual(expected, recorder.Labels.Take(expected.Length).ToArray());
            CollectionAssert.AreEqual(expected, recorder.Labels.Skip(expected.Length).ToArray(), "Dispatch order must be stable across frames.");

            // Gating OFF: no flow rebuild, no density — the throttle path must actually
            // skip those stages (a regression to per-frame rebuild is a real perf cliff).
            recorder.Labels.Clear();
            context.rebuildDensityMap = false;
            context.teamFlows = new[]
            {
                new TeamFlowFrameSettings { rebuildThisFrame = false, threadGroupsX = 1, resolutionX = 16, resolutionZ = 16 },
                new TeamFlowFrameSettings { rebuildThisFrame = false, threadGroupsX = 1, resolutionX = 16, resolutionZ = 16 }
            };
            orchestrator.DispatchFrame(context);
            string[] gatedExpected =
            {
                "ClearGrid",
                "BuildSpatialHash",
                "BuildEngagementSlotOccupancy",
                "ClearPendingDamage",
                "SimulateCombatAndAccumulateDamage",
                "ClassifyVisibleAgentsForUnitType[0]",
                "ClassifyVisibleAgentsForUnitType[1]"
            };
            CollectionAssert.AreEqual(gatedExpected, recorder.Labels, "gated-off frame must skip flow and density stages");

            buffers.ReleaseAll();
        }

        /// <summary>
        /// Three armies, each with its own flow field slice: the teams that asked for a rebuild
        /// get their own dispatch group, and the one that did not is skipped entirely. This is
        /// what a third army following its own orders looks like at the dispatch layer - before
        /// the fields were per team, team 2 had no field of its own to rebuild.
        /// </summary>
        [Test]
        public void EveryTeamThatRebuildsGetsItsOwnFlowDispatch()
        {
            MassGpuBufferManager buffers = new MassGpuBufferManager();
            buffers.Allocate(agentCount: 8, gridCellCount: 16, maxAgentsPerCell: 4, flowFieldResolutionX: 8, flowFieldResolutionZ: 8, unitTypeCount: 1, teamCount: 3);

            DispatchRecorder recorder = new DispatchRecorder();
            ComputePipelineOrchestrator orchestrator = new ComputePipelineOrchestrator(
                MassGpuShaderSet.Find(null, null, null, null, null), buffers, recorder);

            // 2 grid + 4 flow x 2 rebuilding teams + 3 combat + 1 LOD label, each reported once.
            for (int i = 0; i < 14; i++)
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("MassEngine skipped GPU dispatch"));

            PipelineFrameContext context = new PipelineFrameContext
            {
                totalAgentCount = 8,
                unitTypeCount = 1,
                agentThreadGroupsX = 1,
                gridThreadGroupsX = 1,
                rebuildDensityMap = false,
                teamFlows = new[]
                {
                    new TeamFlowFrameSettings { rebuildThisFrame = true, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 },
                    new TeamFlowFrameSettings { rebuildThisFrame = false, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 },
                    new TeamFlowFrameSettings { rebuildThisFrame = true, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 }
                }
            };

            orchestrator.DispatchFrame(context);

            string[] expectedFlow =
            {
                "ClearRuntimeFlowResources[team0]",
                "BuildRuntimeFlowTargetDensity[team0]",
                "SelectRuntimeFlowTargets[team0]",
                "GenerateRuntimeFlowField[team0]",
                "ClearRuntimeFlowResources[team2]",
                "BuildRuntimeFlowTargetDensity[team2]",
                "SelectRuntimeFlowTargets[team2]",
                "GenerateRuntimeFlowField[team2]"
            };
            CollectionAssert.AreEqual(
                expectedFlow,
                recorder.Labels.Where(label => label.Contains("RuntimeFlow")).ToArray(),
                "each rebuilding team needs its own dispatch group, and team 1 asked for none");

            // A frame context carrying more teams than the buffers were sized for must not
            // dispatch the extra team: its slice does not exist, so it would corrupt another's.
            recorder.Labels.Clear();
            context.teamFlows = new[]
            {
                new TeamFlowFrameSettings { rebuildThisFrame = false, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 },
                new TeamFlowFrameSettings { rebuildThisFrame = false, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 },
                new TeamFlowFrameSettings { rebuildThisFrame = false, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 },
                new TeamFlowFrameSettings { rebuildThisFrame = true, threadGroupsX = 1, resolutionX = 8, resolutionZ = 8, cellSize = 1f, sectorCount = 4 }
            };
            orchestrator.DispatchFrame(context);
            CollectionAssert.IsEmpty(
                recorder.Labels.Where(label => label.Contains("RuntimeFlow")).ToArray(),
                "a team beyond the allocated team count must never be dispatched");

            buffers.ReleaseAll();
        }

        [Test]
        public void CombatBuffersStaySeparateFromAgentDataAndDoubleBuffersSwap()
        {
            MassGpuBufferManager buffers = new MassGpuBufferManager();
            buffers.Allocate(4, 4, 4, 8, 8, 1);

            // Requirement 9.3: compute-only combat state never lives inside AgentData.
            Assert.AreEqual(56, buffers.agentBuffer.stride);
            Assert.NotNull(buffers.combatBuffers.hpReadBuffer);
            Assert.NotNull(buffers.combatBuffers.hpWriteBuffer);
            Assert.AreNotSame(buffers.combatBuffers.hpReadBuffer, buffers.combatBuffers.hpWriteBuffer);
            Assert.AreNotSame(buffers.combatBuffers.pendingDamageReadBuffer, buffers.combatBuffers.pendingDamageWriteBuffer);
            Assert.AreEqual(8, MassGpuBufferManager.EngagementSlotsPerTarget);
            Assert.AreEqual(4 * 8, buffers.combatBuffers.engagementSlotOccupancyBuffer.count);
            Assert.AreEqual(4, buffers.combatBuffers.engagementSlotAssignmentBuffer.count);
            Assert.That(buffers.teamGridCountsBuffer.count, Is.EqualTo(8));
            Assert.That(buffers.teamGridAgentIndicesBuffer.count, Is.EqualTo(32));

            ComputeBuffer hpReadBefore = buffers.combatBuffers.hpReadBuffer;
            ComputeBuffer damageReadBefore = buffers.combatBuffers.pendingDamageReadBuffer;
            ComputeBuffer positionReadBefore = buffers.agentPositionReadBuffer;
            buffers.SwapSimulationBuffers();
            Assert.AreSame(hpReadBefore, buffers.combatBuffers.hpWriteBuffer);
            Assert.AreSame(damageReadBefore, buffers.combatBuffers.pendingDamageWriteBuffer);
            Assert.AreSame(positionReadBefore, buffers.agentPositionWriteBuffer);

            buffers.ReleaseAll();
        }

        [Test]
        public void VisibleIndexAndArgsBuffersScaleWithUnitTypeCount()
        {
            MassGpuBufferManager buffers = new MassGpuBufferManager();
            buffers.Allocate(6, 4, 4, 8, 8, 3);

            for (int unitType = 0; unitType < 3; unitType++)
            {
                for (int lod = 0; lod < MassGpuBufferManager.LodLevels; lod++)
                {
                    Assert.NotNull(buffers.GetVisibleIndexBuffer(unitType, lod), "visible buffer " + unitType + "/" + lod);
                    Assert.NotNull(buffers.GetDrawArgsBuffer(unitType, lod), "args buffer " + unitType + "/" + lod);
                }
            }

            Assert.IsNull(buffers.GetVisibleIndexBuffer(3, 0));
            buffers.ReleaseAll();
        }

        [Test]
        public void TeamPartitionedBuffersScaleWithTeamCount()
        {
            // Two teams stay the default so every existing caller keeps its layout unchanged.
            MassGpuBufferManager legacy = new MassGpuBufferManager();
            legacy.Allocate(4, 4, 4, 8, 8, 1);
            Assert.That(legacy.TeamCount, Is.EqualTo(MassGpuBufferManager.DefaultTeamCount));
            Assert.That(legacy.TeamStatsSlotCount, Is.EqualTo(16));
            Assert.That(legacy.teamSpatialStatsBuffer.count, Is.EqualTo(16));
            legacy.ReleaseAll();

            // Deliberately odd and not a power of two: catches code that still assumes two
            // teams, or that indexes team records with a mask instead of a multiply.
            const int teamCount = 5;
            MassGpuBufferManager buffers = new MassGpuBufferManager();
            buffers.Allocate(4, 4, 4, 8, 8, 1, teamCount);

            Assert.That(buffers.TeamCount, Is.EqualTo(teamCount));
            Assert.That(buffers.teamGridCountsBuffer.count, Is.EqualTo(4 * teamCount));
            Assert.That(buffers.teamGridAgentIndicesBuffer.count, Is.EqualTo(4 * 4 * teamCount));
            Assert.That(buffers.TeamStatsSlotCount,
                Is.EqualTo(teamCount * MassGpuBufferManager.TeamStatsSlotsPerTeam));
            Assert.That(buffers.teamSpatialStatsBuffer.count, Is.EqualTo(buffers.TeamStatsSlotCount));

            // A TeamCount surviving release would size the next allocation from a dead value.
            buffers.ReleaseAll();
            Assert.That(buffers.TeamCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // Formation derivation + scenario physics ledger
        // ------------------------------------------------------------------

        [Test]
        public void SpawnFootprintDerivesFromCountDensityAndAspect()
        {
            SpawnConfig spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            spawn.unitCount = 200000;
            spawn.formationDensity = 0.5f;
            spawn.formationAspect = 2f;
            spawn.spawnSize = Vector3.zero; // auto

            Vector3 size = spawn.ResolveSpawnSize();
            float area = size.x * size.z;

            Assert.AreEqual(200000f / 0.5f, area, area * 0.01f, "footprint area must equal count / density");
            Assert.AreEqual(2f, size.z / size.x, 0.01f, "front(Z) : depth(X) must match formationAspect");
            Assert.AreEqual(0.5f, spawn.ResolveDensity(), 0.01f);
            Assert.IsFalse(spawn.HasManualFootprint);

            // Manual override wins verbatim.
            spawn.spawnSize = new Vector3(90f, 0f, 160f);
            Assert.IsTrue(spawn.HasManualFootprint);
            Assert.AreEqual(new Vector3(90f, 0f, 160f), spawn.ResolveSpawnSize());

            UnityEngine.Object.DestroyImmediate(spawn);
        }

        [Test]
        public void ScenarioPhysicsFlagsTheOverloadedBattleAndSuggestsAFit()
        {
            // Reproduces the real incident: 200k per side crammed into manual
            // 90x160m boxes inside a 220x220m world with a 2m/64 grid.
            var attacker = MakeSpawnScenarioType(0, 200000, new Vector3(-55f, 0f, 0f), new Vector3(90f, 0f, 160f));
            var defender = MakeSpawnScenarioType(1, 200000, new Vector3(55f, 0f, 0f), new Vector3(90f, 0f, 160f));
            SimulationConfig simulation = ScriptableObject.CreateInstance<SimulationConfig>();
            simulation.simulationWorldSize = new Vector2(220f, 220f);
            simulation.cellSize = 2f;
            simulation.maxAgentsPerCell = 64;
            RuntimeFlowConfig flow = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            flow.flowFieldResolution = 128;
            flow.flowFieldCellSize = 2f;
            flow.flowFieldOrigin = new Vector2(-110f, -110f);

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(
                new[] { attacker.config, defender.config }, simulation, flow);

            Assert.IsTrue(report.HasIssues, "the impossible scenario must be flagged");
            Assert.GreaterOrEqual(report.Issues.Count, 3, "over-dense spawns, world packing and grid overflow must all be reported");
            Assert.AreEqual(400000, report.TotalAgents);
            // The suggested world must satisfy global packing, not just spawn extents.
            Assert.GreaterOrEqual(report.SuggestedWorldSize.x, Mathf.Sqrt(400000f / 0.5f));
            Assert.That(report.SuggestedCellSize, Is.InRange(2f, 8f));
            Assert.That(report.SuggestedMaxAgentsPerCell, Is.InRange(16, 64));
            Assert.GreaterOrEqual(report.SuggestedFlowResolution * report.SuggestedFlowCellSize, report.SuggestedWorldSize.x,
                "suggested flow field must cover the suggested world");

            attacker.Destroy();
            defender.Destroy();
            UnityEngine.Object.DestroyImmediate(simulation);
            UnityEngine.Object.DestroyImmediate(flow);
        }

        [Test]
        public void ScenarioPhysicsAcceptsAHealthyScenario()
        {
            var attacker = MakeSpawnScenarioType(0, 10000, new Vector3(-55f, 0f, 0f), Vector3.zero);
            var defender = MakeSpawnScenarioType(1, 10000, new Vector3(55f, 0f, 0f), Vector3.zero);
            SimulationConfig simulation = ScriptableObject.CreateInstance<SimulationConfig>();
            simulation.simulationWorldSize = new Vector2(220f, 220f);
            simulation.cellSize = 2f;
            simulation.maxAgentsPerCell = 64;
            RuntimeFlowConfig flow = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            flow.flowFieldResolution = 128;
            flow.flowFieldCellSize = 2f;
            flow.flowFieldOrigin = new Vector2(-110f, -110f);

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(
                new[] { attacker.config, defender.config }, simulation, flow);

            Assert.IsFalse(report.HasIssues, string.Join(" | ", report.Issues));

            attacker.Destroy();
            defender.Destroy();
            UnityEngine.Object.DestroyImmediate(simulation);
            UnityEngine.Object.DestroyImmediate(flow);
        }

        [Test]
        public void ScenarioPhysicsFlagsOverlappingHostileSpawns()
        {
            // 200k auto footprints are ~447m deep; centers only 110m apart must be flagged.
            var attacker = MakeSpawnScenarioType(0, 200000, new Vector3(-55f, 0f, 0f), Vector3.zero);
            var defender = MakeSpawnScenarioType(1, 200000, new Vector3(55f, 0f, 0f), Vector3.zero);
            SimulationConfig simulation = ScriptableObject.CreateInstance<SimulationConfig>();
            simulation.simulationWorldSize = new Vector2(1280f, 1280f);
            simulation.cellSize = 3f;
            simulation.maxAgentsPerCell = 18;

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(
                new[] { attacker.config, defender.config }, simulation, null);

            Assert.IsTrue(report.Issues.Exists(issue => issue.Contains("重叠")),
                "interpenetrating hostile spawn zones must be flagged: " + string.Join(" | ", report.Issues));

            attacker.Destroy();
            defender.Destroy();
            UnityEngine.Object.DestroyImmediate(simulation);
        }

        [Test]
        public void ScenarioPhysicsFlagsFlowFieldOffsetInZ()
        {
            var attacker = MakeSpawnScenarioType(0, 10000, new Vector3(-55f, 0f, 0f), Vector3.zero);
            var defender = MakeSpawnScenarioType(1, 10000, new Vector3(55f, 0f, 0f), Vector3.zero);
            SimulationConfig simulation = ScriptableObject.CreateInstance<SimulationConfig>();
            simulation.simulationWorldSize = new Vector2(220f, 220f);
            simulation.cellSize = 2f;
            simulation.maxAgentsPerCell = 64;
            RuntimeFlowConfig flow = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            flow.flowFieldResolution = 128;
            flow.flowFieldCellSize = 2f;
            flow.flowFieldOrigin = new Vector2(-110f, 0f); // X fine, Z shifted half a map

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(
                new[] { attacker.config, defender.config }, simulation, flow);

            Assert.IsTrue(report.Issues.Exists(issue => issue.Contains("流场未覆盖")),
                "Z-shifted flow origin must be flagged: " + string.Join(" | ", report.Issues));

            attacker.Destroy();
            defender.Destroy();
            UnityEngine.Object.DestroyImmediate(simulation);
            UnityEngine.Object.DestroyImmediate(flow);
        }

        [Test]
        public void ScenarioPhysicsFlagsDynamicTargetingShortCircuit()
        {
            var attacker = MakeSpawnScenarioType(0, 10000, new Vector3(-55f, 0f, 0f), Vector3.zero);
            SimulationConfig simulation = ScriptableObject.CreateInstance<SimulationConfig>();
            simulation.simulationWorldSize = new Vector2(220f, 220f);
            simulation.cellSize = 2f;
            simulation.maxAgentsPerCell = 64;
            RuntimeFlowConfig flow = ScriptableObject.CreateInstance<RuntimeFlowConfig>();
            flow.flowFieldEnabled = false;                    // master off
            flow.runtimeDynamicAttackerFlowEnabled = true;    // dynamic on => silent standstill

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(new[] { attacker.config }, simulation, flow);

            Assert.IsTrue(report.Issues.Exists(issue => issue.Contains("短路")),
                "dynamic-on/master-off must be flagged: " + string.Join(" | ", report.Issues));

            attacker.Destroy();
            UnityEngine.Object.DestroyImmediate(simulation);
            UnityEngine.Object.DestroyImmediate(flow);
        }

        [Test]
        public void ScenarioPhysicsFlagsLodMisordering()
        {
            var attacker = MakeSpawnScenarioType(0, 10000, new Vector3(-55f, 0f, 0f), Vector3.zero);
            SimulationConfig simulation = ScriptableObject.CreateInstance<SimulationConfig>();
            simulation.simulationWorldSize = new Vector2(220f, 220f);
            simulation.cellSize = 2f;
            simulation.maxAgentsPerCell = 64;
            LodConfig lod = ScriptableObject.CreateInstance<LodConfig>();
            lod.nearLodRadius = 120f;
            lod.midLodRadius = 30f;                            // inverted
            lod.maxRenderDistance = 20f;                       // below mid

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(new[] { attacker.config }, simulation, null, lod);

            Assert.IsTrue(report.Issues.Exists(issue => issue.Contains("LOD 半径失序")), string.Join(" | ", report.Issues));
            Assert.IsTrue(report.Issues.Exists(issue => issue.Contains("maxRenderDistance")), string.Join(" | ", report.Issues));

            attacker.Destroy();
            UnityEngine.Object.DestroyImmediate(simulation);
            UnityEngine.Object.DestroyImmediate(lod);
        }

        [Test]
        public void AutoFootprintSpawnSpreadsAgentsAtFormationDensityAndAspect()
        {
            SpawnConfig config = ScriptableObject.CreateInstance<SpawnConfig>();
            config.unitCount = 2000;
            config.formationDensity = 0.5f;
            config.formationAspect = 4f;
            config.spawnSize = Vector3.zero; // auto
            Vector3 expected = config.ResolveSpawnSize();

            DefaultSpawnModule module = new DefaultSpawnModule(config);
            AgentData[] agents = new AgentData[2000];
            module.GenerateAgents(agents, 0, agents.Length, 0);

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < agents.Length; i++)
            {
                minX = Mathf.Min(minX, agents[i].position.x);
                maxX = Mathf.Max(maxX, agents[i].position.x);
                minZ = Mathf.Min(minZ, agents[i].position.z);
                maxZ = Mathf.Max(maxZ, agents[i].position.z);
            }

            // Integration guard: GenerateAgents must consume the RESOLVED footprint (a
            // revert to raw spawnSize collapses 200k agents onto a single point).
            Assert.AreEqual(expected.x, maxX - minX, expected.x * 0.1f, "X spread must match resolved depth");
            Assert.AreEqual(expected.z, maxZ - minZ, expected.z * 0.1f, "Z spread must match resolved front width");
            Assert.Greater((maxZ - minZ) / Mathf.Max(0.001f, maxX - minX), 3.0f, "front:depth ratio must follow formationAspect");

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void ShippedScenarioPassesPhysicsLedger()
        {
            ScenarioConfig scenario = UnityEditor.AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            SimulationConfig simulation = UnityEditor.AssetDatabase.LoadAssetAtPath<SimulationConfig>("Assets/Game/Settings/SimulationConfig.asset");
            RuntimeFlowConfig flow = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeFlowConfig>("Assets/Game/Settings/RuntimeFlowConfig.asset");
            LodConfig lod = UnityEditor.AssetDatabase.LoadAssetAtPath<LodConfig>("Assets/Game/Settings/LodConfig.asset");
            Assert.NotNull(scenario, "shipped ScenarioConfig missing");
            Assert.NotNull(simulation, "shipped SimulationConfig missing");

            ScenarioPhysicsReport report = ScenarioPhysics.Evaluate(scenario.unitTypes, simulation, flow, lod);

            Assert.IsFalse(report.HasIssues,
                "the shipped scenario must stay physically consistent:\n" + string.Join("\n", report.Issues));
        }

        [Test]
        public void ShippedScenarioFieldsMeleeAndRangedInEveryArmy()
        {
            ScenarioConfig scenario = UnityEditor.AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            Assert.NotNull(scenario, "shipped ScenarioConfig missing");

            // Mixed arms is the scenario's payload, not an engine feature that can be inferred:
            // one unit type per team silently reverts the battle to an all-ranged skirmish.
            var meleePerTeam = new System.Collections.Generic.Dictionary<int, int>();
            var rangedPerTeam = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < scenario.unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = scenario.unitTypes[i];
                Assert.NotNull(unitType, "scenario unit type " + i + " is missing");
                Assert.NotNull(unitType.spawnConfig, "scenario unit type " + i + " has no SpawnConfig");
                Assert.NotNull(unitType.combatConfig, "scenario unit type " + i + " has no CombatConfig");

                var bucket = unitType.combatConfig.projectileRange > 0.01f ? rangedPerTeam : meleePerTeam;
                int count;
                bucket.TryGetValue(unitType.teamId, out count);
                bucket[unitType.teamId] = count + Mathf.Max(0, unitType.spawnConfig.unitCount);
            }

            foreach (int teamId in rangedPerTeam.Keys)
            {
                Assert.IsTrue(meleePerTeam.ContainsKey(teamId) && meleePerTeam[teamId] > 0,
                    "team " + teamId + " fields ranged units but no melee ones");
                // A token melee escort would not change how the battle reads from the camera.
                Assert.That(meleePerTeam[teamId], Is.GreaterThan(rangedPerTeam[teamId] / 4),
                    "team " + teamId + " melee head count is too small to screen its ranged line");
            }

            Assert.That(meleePerTeam.Count, Is.GreaterThan(0), "no army fields melee units");
        }

        [Test]
        public void ShippedTracerPaletteGivesEveryRangedArmyItsOwnBrightColor()
        {
            ScenarioConfig scenario = UnityEditor.AssetDatabase.LoadAssetAtPath<ScenarioConfig>("Assets/Game/Settings/ScenarioConfig.asset");
            ProjectileRenderConfig render = UnityEditor.AssetDatabase.LoadAssetAtPath<ProjectileRenderConfig>("Assets/Game/Settings/ProjectileRenderConfig.asset");
            Assert.NotNull(scenario, "shipped ScenarioConfig missing");
            Assert.NotNull(render, "shipped ProjectileRenderConfig missing");

            var rangedTeams = new System.Collections.Generic.List<int>();
            for (int i = 0; i < scenario.unitTypes.Length; i++)
            {
                UnitTypeConfig unitType = scenario.unitTypes[i];
                if (unitType == null || unitType.combatConfig == null)
                    continue;
                if (unitType.combatConfig.projectileRange > 0.01f && !rangedTeams.Contains(unitType.teamId))
                    rangedTeams.Add(unitType.teamId);
            }

            Assert.That(rangedTeams.Count, Is.GreaterThan(1), "the palette guard needs at least two shooting armies");

            for (int i = 0; i < rangedTeams.Count; i++)
            {
                Color color = render.ResolveTeamColor(rangedTeams[i]);
                // The clamp fallback silently repaints every extra army with the last slot's
                // color, which is exactly the "whose shot was that?" bug this palette fixes.
                Assert.That(rangedTeams[i], Is.LessThan(render.teamColors.Length),
                    "team " + rangedTeams[i] + " shoots but has no palette entry of its own");
                // Tracers are thin alpha-blended lines over lit terrain and fog: a color whose
                // brightest channel is low reads as "nothing happened" from the battle camera.
                Assert.That(Mathf.Max(color.r, Mathf.Max(color.g, color.b)), Is.GreaterThanOrEqualTo(0.9f),
                    "team " + rangedTeams[i] + "'s tracer color is too dim to see");

                for (int j = i + 1; j < rangedTeams.Count; j++)
                {
                    Color other = render.ResolveTeamColor(rangedTeams[j]);
                    float spread = Mathf.Max(Mathf.Abs(color.r - other.r),
                        Mathf.Max(Mathf.Abs(color.g - other.g), Mathf.Abs(color.b - other.b)));
                    Assert.That(spread, Is.GreaterThan(0.2f),
                        "teams " + rangedTeams[i] + " and " + rangedTeams[j] + " fire near-identical tracers");
                }
            }
        }

        private static UnitTypeFixture MakeSpawnScenarioType(int teamId, int unitCount, Vector3 center, Vector3 manualSize)
        {
            UnitTypeFixture fixture = MakeUnitTypeConfig(teamId, unitCount, 6f, 10, 0.45f);
            fixture.spawn.spawnCenter = center;
            fixture.spawn.spawnSize = manualSize;
            fixture.spawn.formationDensity = 0.5f;
            return fixture;
        }

        [Test]
        public void StaticObstacleSegmentTestDetectsBlockedAndClearRoutes()
        {
            StaticObstacleRect obstacle = new StaticObstacleRect(Vector2.zero, new Vector2(4f, 8f));
            Assert.IsTrue(StaticObstacleMath.SegmentIntersects(obstacle, new Vector2(-10f, 0f), new Vector2(10f, 0f), 1f));
            Assert.IsFalse(StaticObstacleMath.SegmentIntersects(obstacle, new Vector2(-10f, 8f), new Vector2(10f, 8f), 1f));
        }

        [Test]
        public void StaticObstacleProjectsMoveTargetToNearestSafeEdge()
        {
            StaticObstacleRect obstacle = new StaticObstacleRect(Vector2.zero, new Vector2(4f, 8f));
            Vector3 projected = StaticObstacleMath.ResolvePointOutside(obstacle, new Vector3(1.5f, 0f, 0f), 1f);
            Assert.That(projected.x, Is.GreaterThan(3f));
            Assert.That(projected.z, Is.EqualTo(0f).Within(0.001f));
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private readonly struct UnitTypeFixture
        {
            public readonly UnitTypeConfig config;
            public readonly SpawnConfig spawn;
            public readonly MovementConfig movement;
            public readonly FlockingConfig flocking;
            public readonly CombatConfig combat;

            public UnitTypeFixture(UnitTypeConfig config, SpawnConfig spawn, MovementConfig movement, FlockingConfig flocking, CombatConfig combat)
            {
                this.config = config;
                this.spawn = spawn;
                this.movement = movement;
                this.flocking = flocking;
                this.combat = combat;
            }

            public void Destroy()
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(spawn);
                UnityEngine.Object.DestroyImmediate(movement);
                UnityEngine.Object.DestroyImmediate(flocking);
                UnityEngine.Object.DestroyImmediate(combat);
            }
        }

        private static UnitTypeFixture MakeUnitTypeConfig(int teamId, int unitCount, float maxSpeed, int attackDamage, float agentRadius)
        {
            SpawnConfig spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            spawn.unitCount = unitCount;

            MovementConfig movement = ScriptableObject.CreateInstance<MovementConfig>();
            movement.maxSpeed = maxSpeed;

            FlockingConfig flocking = ScriptableObject.CreateInstance<FlockingConfig>();
            flocking.agentRadius = agentRadius;

            CombatConfig combat = ScriptableObject.CreateInstance<CombatConfig>();
            combat.attackDamage = attackDamage;

            UnitTypeConfig config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            config.teamId = teamId;
            config.spawnConfig = spawn;
            config.movementConfig = movement;
            config.flockingConfig = flocking;
            config.combatConfig = combat;

            return new UnitTypeFixture(config, spawn, movement, flocking, combat);
        }

        private sealed class TestUnitType : UnitTypeBase
        {
            public TestUnitType(UnitTypeConfig config) : base(config)
            {
            }
        }
    }
}
