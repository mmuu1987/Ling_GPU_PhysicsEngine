using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace MassGPUPhysics.Stage7.Tests
{
    public sealed class Stage7PropertyTests
    {
        [Test]
        public void AgentDataStrideRemains56Bytes()
        {
            Assert.AreEqual(56, Marshal.SizeOf<AgentData>());
        }

        [Test]
        public void PublicConfigurationFieldCountStaysBelowLimit()
        {
            Type[] checkedTypes = typeof(MassGpuSystemManager_Stage7).Assembly.GetTypes()
                .Where(type => type.Namespace == "MassGPUPhysics.Stage7")
                .Where(type => type.IsClass && (type.Name.EndsWith("Module") || type.Name.EndsWith("Config") || type.Name.EndsWith("_Stage7")))
                .ToArray();

            foreach (Type type in checkedTypes)
            {
                int publicFieldCount = type.GetFields(BindingFlags.Instance | BindingFlags.Public).Length;
                Assert.LessOrEqual(publicFieldCount, 30, type.FullName);
            }
        }

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
        public void FlowVelocityDirectionAndWeightAreConsistent()
        {
            MovementConfig config = ScriptableObject.CreateInstance<MovementConfig>();
            config.maxSpeed = 10f;
            config.flowFieldWeight = 1f;
            DefaultMovementModule module = new DefaultMovementModule(config);
            module.SetTargetArea(new Vector3(10f, 0f, 5f), new Vector3(4f, 0f, 8f));

            Vector3 low = module.ComputeDesiredVelocity(Vector3.zero, new Vector2(1f, 1f), 0.25f);
            Vector3 high = module.ComputeDesiredVelocity(Vector3.zero, new Vector2(1f, 1f), 0.75f);
            Vector3 fallback = module.ComputeDesiredVelocity(Vector3.zero, Vector2.zero, 1f);
            Vector2 lowDirection = new Vector2(low.x, low.z).normalized;

            Assert.AreEqual(FlowFieldTargetMode.Area, module.Target.mode);
            Assert.Greater(Vector2.Dot(lowDirection, new Vector2(1f, 1f).normalized), 0.99f);
            Assert.GreaterOrEqual(high.magnitude, low.magnitude);
            Assert.Greater(fallback.magnitude, 0f);

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void FlockingForcesSatisfySeparationAndAttractionProperties()
        {
            FlockingConfig config = ScriptableObject.CreateInstance<FlockingConfig>();
            DefaultFlockingModule module = new DefaultFlockingModule(config);

            Vector3 weak = module.ComputeSeparationForce(Vector3.zero, new Vector3(0.5f, 0f, 0f), 0.5f, 2f);
            Vector3 strong = module.ComputeSeparationForce(Vector3.zero, new Vector3(0.5f, 0f, 0f), 0.5f, 4f);
            Vector3 attraction = module.ComputeAttractionForce(Vector3.zero, new Vector3(4f, 0f, 1f), 3f);

            Assert.GreaterOrEqual(strong.magnitude, weak.magnitude);
            Assert.Greater(Vector3.Dot(attraction, new Vector3(4f, 0f, 1f)), 0f);

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void DensityMapAccumulationCountsAliveAgentsPerFlowCell()
        {
            Vector3[] positions =
            {
                new Vector3(-1.25f, 0f, -1.25f),
                new Vector3(-1.1f, 0f, -1.2f),
                new Vector3(1.8f, 0f, 1.8f)
            };
            int[] hp = { 10, 0, 20 };

            uint[,] density = BuildDensityMap(positions, hp, 4, new Vector2(-2f, -2f), 1f);

            Assert.AreEqual(1u, density[0, 0]);
            Assert.AreEqual(1u, density[3, 3]);
            Assert.AreEqual(0u, density[0, 1]);
        }

        [Test]
        public void DensityPressureActivatesOnlyAboveComfortAndSlowsCrowds()
        {
            uint[,] density = new uint[3, 3];
            density[1, 1] = 2;
            density[0, 1] = 2;
            density[2, 1] = 2;

            DensityPressure pressure = ComputeDensityPressure(density, new Vector2(1.5f, 1.5f), new Vector2(1f, 0f), Vector2.zero, 1f, 2, 6f, 0.85f, 0.45f);
            Assert.AreEqual(Vector2.zero, pressure.avoidance);
            Assert.AreEqual(1f, pressure.speedScale);
            Assert.AreEqual(0f, pressure.pressure);

            density[1, 1] = 5;
            density[0, 1] = 7;
            density[2, 1] = 1;

            pressure = ComputeDensityPressure(density, new Vector2(1.5f, 1.5f), new Vector2(1f, 0f), Vector2.zero, 1f, 2, 6f, 0.85f, 0.45f);
            Vector2 expectedAvoidance = Vector2.right * 0.425f;

            Assert.That(pressure.avoidance.x, Is.EqualTo(expectedAvoidance.x).Within(0.0001f));
            Assert.That(pressure.avoidance.y, Is.EqualTo(expectedAvoidance.y).Within(0.0001f));
            Assert.That(pressure.pressure, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(pressure.speedScale, Is.EqualTo(0.775f).Within(0.0001f));
        }

        [Test]
        public void SeparationSkipFrameAppliesOnlyOnModuloZeroFrames()
        {
            Vector2 separation = new Vector2(0.5f, 0.25f);
            Vector2 applied = ApplySeparationSkip(separation, 4f, 0.1f, 6u, 3u);
            Vector2 skipped = ApplySeparationSkip(separation, 4f, 0.1f, 7u, 3u);
            Vector2 intervalOne = ApplySeparationSkip(separation, 4f, 0.1f, 7u, 1u);

            Assert.AreEqual(separation * 0.4f, applied);
            Assert.AreEqual(Vector2.zero, skipped);
            Assert.AreEqual(separation * 0.4f, intervalOne);
        }

        [Test]
        public void StableSpeedVariationIsDeterministicAndBounded()
        {
            float first = AgentSpeedMultiplier(17u, 0.08f);
            float second = AgentSpeedMultiplier(17u, 0.08f);

            Assert.AreEqual(first, second);
            Assert.That(first, Is.InRange(0.92f, 1.08f));
            Assert.AreEqual(1f, AgentSpeedMultiplier(17u, 0f));
        }

        [Test]
        public void StableLaneBiasIsDeterministicAndNormalized()
        {
            Vector2 direction = new Vector2(0.35f, 0.94f).normalized;
            Vector2 first = ApplyStableLaneBias(4u, direction, 0.25f, 0.5f);
            Vector2 second = ApplyStableLaneBias(4u, direction, 0.25f, 0.5f);

            Assert.That(first.x, Is.EqualTo(second.x).Within(0.0001f));
            Assert.That(first.y, Is.EqualTo(second.y).Within(0.0001f));
            Assert.That(first.magnitude, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void FlockingConfigMovementOptimizationDefaultsAndClamps()
        {
            FlockingConfig defaults = ScriptableObject.CreateInstance<FlockingConfig>();
            Assert.AreEqual(1, defaults.separationSkipInterval);
            Assert.AreEqual(2f, defaults.densityAvoidanceStrength);
            Assert.AreEqual(2, defaults.densityComfortCount);
            Assert.AreEqual(6f, defaults.densityPressureRange);
            Assert.AreEqual(0.45f, defaults.densitySpeedPenalty);
            Assert.AreEqual(0.08f, defaults.speedVariation);
            Assert.AreEqual(0.25f, defaults.laneBiasStrength);

            FlockingConfig invalid = ScriptableObject.CreateInstance<FlockingConfig>();
            invalid.separationSkipInterval = -4;
            invalid.densityAvoidanceStrength = -2f;
            invalid.densityComfortCount = -3;
            invalid.densityPressureRange = -1f;
            invalid.densitySpeedPenalty = 3f;
            invalid.speedVariation = 2f;
            invalid.laneBiasStrength = -1f;
            UnitTypeConfig unit = ScriptableObject.CreateInstance<UnitTypeConfig>();
            unit.flockingConfig = invalid;

            UnitTypeGpuSettings settings = UnitTypeGpuSettings.FromConfig(unit);

            Assert.AreEqual(1, settings.separationSkipInterval);
            Assert.AreEqual(0f, settings.densityAvoidanceStrength);
            Assert.AreEqual(0, settings.densityComfortCount);
            Assert.AreEqual(0.01f, settings.densityPressureRange);
            Assert.AreEqual(1f, settings.densitySpeedPenalty);
            Assert.AreEqual(0.5f, settings.speedVariation);
            Assert.AreEqual(0f, settings.laneBiasStrength);

            UnityEngine.Object.DestroyImmediate(unit);
            UnityEngine.Object.DestroyImmediate(invalid);
            UnityEngine.Object.DestroyImmediate(defaults);
        }

        [Test]
        public void AnimationMapsStatesAndClampsDeathTime()
        {
            AnimationConfig config = ScriptableObject.CreateInstance<AnimationConfig>();
            config.deathClipFrameRange = new Vector2(0f, 30f);
            config.deathClipFrameRate = 30f;
            DefaultAnimationModule module = new DefaultAnimationModule(config);

            Assert.IsTrue(module.GetClipForState(AgentState.Idle).loop);
            Assert.IsTrue(module.GetClipForState(AgentState.Move).loop);
            Assert.IsTrue(module.GetClipForState(AgentState.Engage).loop);
            Assert.IsTrue(module.GetClipForState(AgentState.Attack).loop);
            Assert.IsFalse(module.GetClipForState(AgentState.Dead).loop);
            Assert.AreEqual(1f, module.AdvanceAnimationTime(0.95f, AgentState.Dead, 5f, 0), 0.0001f);

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void CombatExcludesDeadAndSameTeamAndComputesDamage()
        {
            CombatConfig config = ScriptableObject.CreateInstance<CombatConfig>();
            config.attackDamage = 7;
            DefaultCombatModule module = new DefaultCombatModule(config);

            SpatialHashQuery query = new SpatialHashQuery
            {
                positions = new[] { Vector3.zero, Vector3.right, Vector3.forward },
                candidateIndices = new[] { 1, 2 }
            };
            int[] teams = { 0, 0, 1 };
            int[] hp = { 100, 100, 20 };

            int target = module.FindNearestEnemy(0, 0, query, teams, hp);
            int accumulated = Enumerable.Range(0, 4).Sum(_ => module.ComputeDamage(0f, 0.8f, config.attackDamage));

            Assert.AreEqual(2, target);
            Assert.AreEqual(28, accumulated);
            Assert.AreEqual(AgentState.Dead, module.ResolvePostDamageState(0));

            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void StateMachineAcceptsOnlyLegalTransitionsAndResolvesPriority()
        {
            AgentState result;

            Assert.IsTrue(AgentStateMachine.TryTransition(AgentState.Idle, AgentState.Move, out result));
            Assert.AreEqual(AgentState.Move, result);
            Assert.IsFalse(AgentStateMachine.TryTransition(AgentState.Idle, AgentState.Attack, out result));
            Assert.AreEqual(AgentState.Idle, result);
            Assert.IsFalse(AgentStateMachine.TryTransition(AgentState.Move, AgentState.Idle, out result));
            Assert.AreEqual(AgentState.Move, result);
            Assert.IsTrue(AgentStateMachine.TryTransition(AgentState.Attack, AgentState.Dead, out result));
            Assert.AreEqual(AgentState.Dead, result);
            Assert.IsFalse(AgentStateMachine.TryTransition(AgentState.Attack, AgentState.Engage, out result));
            Assert.AreEqual(AgentState.Attack, result);
            Assert.IsFalse(AgentStateMachine.TryTransition(AgentState.Dead, AgentState.Move, out result));
            Assert.AreEqual(AgentState.Dead, result);
            Assert.AreEqual(AgentState.Attack, AgentStateMachine.ResolveConflict(AgentState.Engage, AgentState.Move, AgentState.Attack));
        }

        [Test]
        public void UnitTypeRegistryCreatesConfiguredSubclassWithoutCoreChanges()
        {
            UnitTypeConfig config = ScriptableObject.CreateInstance<UnitTypeConfig>();
            SpawnConfig spawn = ScriptableObject.CreateInstance<SpawnConfig>();
            spawn.unitCount = 3;
            config.spawnConfig = spawn;
            config.unitTypeClassName = typeof(TestUnitType).FullName;

            ScenarioConfig_Stage7 scenario = ScriptableObject.CreateInstance<ScenarioConfig_Stage7>();
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

        private static uint[,] BuildDensityMap(Vector3[] positions, int[] hp, int resolution, Vector2 origin, float cellSize)
        {
            uint[,] density = new uint[resolution, resolution];
            for (int i = 0; i < positions.Length; i++)
            {
                if (hp[i] <= 0)
                    continue;

                Vector2Int cell = PositionToFlowCell(new Vector2(positions[i].x, positions[i].z), resolution, origin, cellSize);
                density[cell.x, cell.y]++;
            }

            return density;
        }

        private struct DensityPressure
        {
            public Vector2 avoidance;
            public float speedScale;
            public float pressure;
        }

        private static DensityPressure ComputeDensityPressure(uint[,] density, Vector2 position, Vector2 desiredDirection, Vector2 origin, float cellSize, int comfort, float range, float strength, float speedPenalty)
        {
            int resolution = density.GetLength(0);
            Vector2Int cell = PositionToFlowCell(position, resolution, origin, cellSize);
            float safeRange = Mathf.Max(0.01f, range);
            float centerPressure = Mathf.Clamp01(((float)density[cell.x, cell.y] - Mathf.Max(0, comfort)) / safeRange);
            float aheadPressure = Mathf.Clamp01((SampleAheadDensity(density, cell, desiredDirection) - Mathf.Max(0, comfort)) / safeRange);
            float pressure = Mathf.Max(centerPressure, aheadPressure);
            Vector2 gradient = SampleDensityGradient(density, cell);

            return new DensityPressure
            {
                avoidance = pressure > 0f && gradient.sqrMagnitude > 0.0001f ? -gradient.normalized * Mathf.Max(0f, strength) * pressure : Vector2.zero,
                speedScale = Mathf.Clamp01(1f - pressure * Mathf.Clamp01(speedPenalty)),
                pressure = pressure
            };
        }

        private static Vector2 ApplySeparationSkip(Vector2 separation, float strength, float dt, uint frameIndex, uint skipInterval)
        {
            uint safeInterval = Math.Max(1u, skipInterval);
            return frameIndex % safeInterval == 0u ? separation * strength * dt : Vector2.zero;
        }

        private static float AgentSpeedMultiplier(uint agentId, float variation)
        {
            return 1f + SignedHash01(agentId ^ 0x9E3779B9u) * Mathf.Clamp(variation, 0f, 0.5f);
        }

        private static Vector2 ApplyStableLaneBias(uint agentId, Vector2 direction, float strength, float pressure)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return direction;

            Vector2 unitDirection = direction.normalized;
            Vector2 side = new Vector2(-unitDirection.y, unitDirection.x);
            float lane = SignedHash01(agentId ^ 0x85EBCA6Bu);
            float laneScale = Mathf.Clamp01(strength) * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(pressure));
            return (unitDirection + side * lane * laneScale).normalized;
        }

        private static float Hash01(uint seed)
        {
            unchecked
            {
                seed ^= seed >> 16;
                seed *= 2246822519u;
                seed ^= seed >> 13;
                seed *= 3266489917u;
                seed ^= seed >> 16;
                return (seed & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float SignedHash01(uint seed)
        {
            return Hash01(seed) * 2f - 1f;
        }

        private static float SampleAheadDensity(uint[,] density, Vector2Int cell, Vector2 desiredDirection)
        {
            Vector2Int ahead = DirectionToCellStep(desiredDirection);
            if (ahead == Vector2Int.zero)
                return density[cell.x, cell.y];

            Vector2Int side = DirectionToCellStep(new Vector2(-desiredDirection.y, desiredDirection.x));
            float aheadCenter = SampleDensityCell(density, cell + ahead);
            float aheadLeft = SampleDensityCell(density, cell + ahead + side);
            float aheadRight = SampleDensityCell(density, cell + ahead - side);
            return (aheadCenter * 2f + aheadLeft + aheadRight) * 0.25f;
        }

        private static Vector2 SampleDensityGradient(uint[,] density, Vector2Int cell)
        {
            float left = SampleDensityCell(density, cell + Vector2Int.left);
            float right = SampleDensityCell(density, cell + Vector2Int.right);
            float down = SampleDensityCell(density, cell + Vector2Int.down);
            float up = SampleDensityCell(density, cell + Vector2Int.up);
            return new Vector2(right - left, up - down) * 0.5f;
        }

        private static float SampleDensityCell(uint[,] density, Vector2Int cell)
        {
            int resolutionX = density.GetLength(0);
            int resolutionY = density.GetLength(1);
            int x = Mathf.Clamp(cell.x, 0, resolutionX - 1);
            int y = Mathf.Clamp(cell.y, 0, resolutionY - 1);
            return density[x, y];
        }

        private static Vector2Int DirectionToCellStep(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return Vector2Int.zero;

            Vector2 unit = direction.normalized;
            int x = Mathf.RoundToInt(unit.x);
            int y = Mathf.RoundToInt(unit.y);
            if (x == 0 && y == 0)
            {
                if (Mathf.Abs(unit.x) > Mathf.Abs(unit.y))
                    x = unit.x >= 0f ? 1 : -1;
                else
                    y = unit.y >= 0f ? 1 : -1;
            }

            return new Vector2Int(x, y);
        }

        private static Vector2Int PositionToFlowCell(Vector2 position, int resolution, Vector2 origin, float cellSize)
        {
            Vector2 local = (position - origin) / Mathf.Max(cellSize, 0.0001f);
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, resolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, resolution - 1);
            return new Vector2Int(x, y);
        }

        private sealed class TestUnitType : UnitTypeBase
        {
            public TestUnitType(UnitTypeConfig config) : base(config)
            {
            }
        }
    }
}
