#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MassEngine.Tests
{
    /// <summary>
    /// PlayMode golden-value tests that dispatch the REAL MassEngine compute kernels on a
    /// tiny agent population and read the results back. This is the half of the test
    /// suite that observes shipped GPU behaviour rather than CPU mirrors.
    /// Requires a GPU with compute support; the fixture is skipped when compute shaders
    /// are unavailable (e.g. headless CI).
    /// </summary>
    public sealed class MassEngineGpuKernelTests
    {
        private const string ShaderRoot = "Assets/MassEngine/";

        private MassGpuBufferManager buffers;
        private ComputePipelineOrchestrator orchestrator;
        private UnitTypeRegistry registry;
        private ScenarioConfig scenario;
        private ScriptableObject[] createdConfigs;
        private UnitTypeGpuSettings[] settingsCache;

        // LOD/sim-cadence/flow overrides used by DispatchOneFrame (defaults = full rate, flow off).
        private float lodNearRadius = 100f;
        private float lodMidRadius = 200f;
        private int simFarInterval = 1;
        private float maxRenderDistance;
        private bool attackerFlowEnabled;
        private bool attackerFlowRebuild;
        private int attackerFlowTargetMode;
        private Vector3 attackerFlowTargetPoint;
        private AgentData[] initialAgents;
        private int[] initialTeamIds;
        private int[] initialHp;
        private int[] initialUnitTypeIndices;

        private const int AttackerCount = 4;
        private const int DefenderCount = 4;
        private const int TotalAgents = AttackerCount + DefenderCount;
        private const int AttackDamage = 10;
        private const float AttackInterval = 0.25f;
        private const float FrameDt = 0.02f;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders unavailable on this device; GPU kernel tests skipped.");

            ComputeShader spatialHash = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Spatial/Shaders/AgentSpatialHash.compute");
            ComputeShader runtimeFlow = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "FlowField/Shaders/AgentRuntimeFlow.compute");
            ComputeShader combat = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Simulation/Shaders/AgentCombatSimulation.compute");
            ComputeShader lod = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "VatRender/Shaders/AgentLodClassification.compute");
            Assert.NotNull(spatialHash, "spatial hash compute shader asset missing");
            Assert.NotNull(combat, "combat compute shader asset missing");

            MassGpuShaderSet shaders = MassGpuShaderSet.Find(spatialHash, runtimeFlow, combat, lod);
            Assert.IsTrue(shaders.IsValid);

            BuildScenario();
            buffers = new MassGpuBufferManager();
            orchestrator = new ComputePipelineOrchestrator(shaders, buffers);
            buffers.Allocate(TotalAgents, 64, 16, 16, 16, registry.UnitTypeCount);
            registry.InitializeAll(buffers, orchestrator);

            // Two opposing lines 1m apart around the origin: everyone is inside both
            // acquire radius and attack range of the nearest enemy.
            AgentData[] agents = new AgentData[TotalAgents];
            int[] teamIds = new int[TotalAgents];
            int[] hp = new int[TotalAgents];
            int[] unitTypeIndices = new int[TotalAgents];
            registry.GenerateAgents(agents);
            registry.FillCombatArrays(teamIds, hp, unitTypeIndices);

            for (int i = 0; i < TotalAgents; i++)
            {
                bool attacker = teamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                agents[i].position = new Vector3(attacker ? -0.5f : 0.5f, 0f, lane * 1.5f);
                agents[i].velocity = Vector3.zero;
            }

            buffers.UploadInitialData(agents, teamIds, hp, unitTypeIndices);
            initialAgents = agents;
            initialTeamIds = teamIds;
            initialHp = hp;
            initialUnitTypeIndices = unitTypeIndices;

            settingsCache = new UnitTypeGpuSettings[registry.UnitTypeCount];
            registry.FillGpuSettings(settingsCache);
            buffers.UploadUnitTypeSettings(settingsCache);
            dispatchedFrames = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if (buffers != null)
                buffers.ReleaseAll();
            buffers = null;
            orchestrator = null;

            if (registry != null)
                registry.ReleaseAll();
            registry = null;

            if (createdConfigs != null)
            {
                foreach (ScriptableObject asset in createdConfigs)
                {
                    if (asset != null)
                        Object.DestroyImmediate(asset);
                }
            }
            createdConfigs = null;

            if (scenario != null)
                Object.DestroyImmediate(scenario);
            scenario = null;
        }

        [UnityTest]
        public IEnumerator DamageAccruesAtAttackIntervalAndKillsAtZeroHp()
        {
            // 100 hp / 10 dmg / 0.25s interval => first kill needs 10 landed hits.
            // Frame budget: target acquisition is staggered over LOCAL_TARGET_SEARCH_INTERVAL
            // frames and cooldown ticks in dt steps, so allow generous headroom.
            int framesForFirstBlood = Mathf.CeilToInt((AttackInterval * 10f) / FrameDt) + 60;

            int[] hp = new int[TotalAgents];
            int[] states = ReadStates();
            bool sawDamage = false;
            bool sawDeath = false;
            int firstDeathFrame = -1;

            for (int frame = 0; frame < framesForFirstBlood && !sawDeath; frame++)
            {
                DispatchOneFrame(battleStarted: true);

                if ((frame & 7) == 0 || frame == framesForFirstBlood - 1)
                {
                    buffers.combatBuffers.hpReadBuffer.GetData(hp);
                    for (int i = 0; i < TotalAgents; i++)
                    {
                        if (hp[i] < 100)
                            sawDamage = true;
                        if (hp[i] <= 0 && !sawDeath)
                        {
                            sawDeath = true;
                            firstDeathFrame = frame;
                        }
                    }
                }

                if ((frame & 31) == 0)
                    yield return null;
            }

            Assert.IsTrue(sawDamage, "no damage was ever applied on the GPU");
            Assert.IsTrue(sawDeath, "no agent died although damage should be lethal within the frame budget");

            // Attack CADENCE guard: killing a 100 hp target with 10 dmg needs 10 hits =
            // 9 full cooldown periods after the first hit. A regression that ignores
            // attackInterval (hitting every frame) would kill within ~15 frames and MUST
            // fail here. Sampling every 8 frames only ever reports a LATER frame, so the
            // lower bound is safe.
            int framesPerCooldown = Mathf.FloorToInt(AttackInterval / FrameDt);
            int minKillFrame = 9 * framesPerCooldown;
            Assert.GreaterOrEqual(firstDeathFrame, minKillFrame,
                "first kill at frame " + firstDeathFrame + " is faster than the attack interval permits (" + minKillFrame + "); attackInterval is being ignored");

            // Damage is quantized to whole attacks: every hp value must be reachable by
            // subtracting N * attackDamage from maxHp.
            buffers.combatBuffers.hpReadBuffer.GetData(hp);
            for (int i = 0; i < TotalAgents; i++)
            {
                int lost = 100 - Mathf.Max(0, hp[i]);
                Assert.AreEqual(0, lost % AttackDamage, "agent " + i + " lost " + lost + " hp, not a multiple of attackDamage " + AttackDamage);
            }

            // Dead agents must be in state Dead with zeroed velocity; hp<=0 <=> Dead.
            states = ReadStates();
            AgentData[] agents = new AgentData[TotalAgents];
            buffers.agentBuffer.GetData(agents);
            for (int i = 0; i < TotalAgents; i++)
            {
                if (hp[i] <= 0)
                {
                    Assert.AreEqual((int)AgentState.Dead, states[i], "agent " + i + " has hp 0 but state " + (AgentState)states[i]);
                    Assert.AreEqual(0f, agents[i].velocity.magnitude, 0.0001f);
                }
                else
                {
                    Assert.AreNotEqual((int)AgentState.Dead, states[i], "agent " + i + " is alive but flagged Dead");
                }
            }
        }

        [UnityTest]
        public IEnumerator ObservedStateTransitionsAreLegalAndBattleProducesCombatStates()
        {
            int frames = 240;
            // First target-search burst happens within LOCAL_TARGET_SEARCH_INTERVAL
            // frames; give a margin before demanding combat states.
            const int combatSettleFrame = 32;
            int[] previousStates = ReadStates();
            bool sawAttack = false;
            int[] hp = new int[TotalAgents];

            for (int frame = 0; frame < frames; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                int[] states = ReadStates();
                buffers.combatBuffers.hpReadBuffer.GetData(hp);

                // The strict "must be attacking" guard only holds while everyone is
                // alive: after a kill, the attacker loses its target and legitimately
                // spends a few staggered-search frames before reacquiring.
                bool allAlive = true;
                for (int i = 0; i < TotalAgents; i++)
                {
                    if (hp[i] <= 0)
                    {
                        allAlive = false;
                        break;
                    }
                }

                for (int i = 0; i < TotalAgents; i++)
                {
                    // Value-range guard: a kernel regression writing garbage into
                    // currentState must fail loudly, not slip through as "legal".
                    Assert.That(states[i], Is.InRange((int)AgentState.Idle, (int)AgentState.Dead),
                        "agent " + i + " has out-of-range state " + states[i] + " at frame " + frame);

                    AgentState from = (AgentState)previousStates[i];
                    AgentState to = (AgentState)states[i];
                    Assert.IsTrue(AgentStateMachine.CanTransition(from, to),
                        "illegal transition " + from + " -> " + to + " on agent " + i + " at frame " + frame);
                    if (to == AgentState.Attack)
                        sawAttack = true;

                    // Semantic guard: in this fixture every live agent permanently has a
                    // live enemy inside attack range, so after the first search burst the
                    // GPU priority resolution must put every live agent in Attack.
                    if (frame >= combatSettleFrame && allAlive)
                        Assert.AreEqual((int)AgentState.Attack, states[i],
                            "live agent " + i + " with an in-range enemy is in state " + to + " at frame " + frame + " (priority resolution broken)");
                }

                previousStates = states;
                if ((frame & 31) == 0)
                    yield return null;
            }

            Assert.IsTrue(sawAttack, "opposing lines in attack range never entered Attack state");
        }

        [UnityTest]
        public IEnumerator BattleNotStartedFreezesAgentsInIdleWithNoDisplacement()
        {
            AgentData[] before = new AgentData[TotalAgents];
            buffers.agentBuffer.GetData(before);

            for (int frame = 0; frame < 30; frame++)
                DispatchOneFrame(battleStarted: false);
            yield return null;

            AgentData[] after = new AgentData[TotalAgents];
            buffers.agentBuffer.GetData(after);
            int[] hp = new int[TotalAgents];
            buffers.combatBuffers.hpReadBuffer.GetData(hp);

            for (int i = 0; i < TotalAgents; i++)
            {
                Assert.AreEqual(100, hp[i], "no damage may accrue before the battle starts");
                Assert.AreEqual((int)AgentState.Idle, after[i].currentState);
                Assert.Less((after[i].position - before[i].position).magnitude, 0.0001f, "agent " + i + " moved before battle start");
            }
        }

        [UnityTest]
        public IEnumerator LodScaledSimulationPreservesKillCadence()
        {
            // Golden guard for "lower cadence, same rates": the SAME duel must produce
            // its first kill at (almost) the same frame whether agents decide every
            // frame or every 4th frame. Catches both failure modes: missing dt
            // compensation (kill ~4x later) and reset-style cooldown quantization
            // (kill ~25% later).
            int killFull = -1;
            int killQuarter = -1;

            // Full rate: everyone in the near tier.
            lodNearRadius = 100f;
            lodMidRadius = 200f;
            simFarInterval = 1;
            yield return RunUntilFirstDeath(result => killFull = result);

            ResetBattlefield();

            // Quarter rate: tiny tier radii push every agent into the far tier.
            lodNearRadius = 0.01f;
            lodMidRadius = 0.02f;
            simFarInterval = 4;
            yield return RunUntilFirstDeath(result => killQuarter = result);

            Assert.Greater(killFull, 0, "full-rate duel never produced a kill");
            Assert.Greater(killQuarter, 0, "quarter-rate duel never produced a kill");
            Assert.LessOrEqual(Mathf.Abs(killQuarter - killFull), 20,
                "kill timing diverged between cadences: full=" + killFull + " quarter=" + killQuarter +
                " — dt compensation in the LOD-scaled simulation path is broken");
        }

        private IEnumerator RunUntilFirstDeath(System.Action<int> onResult)
        {
            int budget = Mathf.CeilToInt((AttackInterval * 10f) / FrameDt) + 120;
            int[] hp = new int[TotalAgents];

            for (int frame = 1; frame <= budget; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                buffers.combatBuffers.hpReadBuffer.GetData(hp);
                for (int i = 0; i < TotalAgents; i++)
                {
                    if (hp[i] <= 0)
                    {
                        onResult(frame);
                        yield break;
                    }
                }

                if ((frame & 31) == 0)
                    yield return null;
            }

            onResult(-1);
        }

        private void ResetBattlefield()
        {
            buffers.UploadInitialData(initialAgents, initialTeamIds, initialHp, initialUnitTypeIndices);
            dispatchedFrames = 0;
        }

        [UnityTest]
        public IEnumerator MaxRenderDistanceCapsVisibleInstanceCounts()
        {
            // Places one attacker per distance band and reads the indirect-args instance
            // counts back: the LOD classify stage is the single choke point deciding
            // whether anything renders, and it previously had zero observation.
            lodNearRadius = 10f;
            lodMidRadius = 100f;
            AgentData[] agents = (AgentData[])initialAgents.Clone();
            float[] distances = { 5f, 50f, 300f, 900f };
            for (int i = 0; i < TotalAgents; i++)
            {
                bool attacker = initialTeamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                agents[i].position = new Vector3(distances[lane % 4], 0f, attacker ? 0f : 2f);
                agents[i].velocity = Vector3.zero;
            }
            buffers.UploadInitialData(agents, initialTeamIds, initialHp, initialUnitTypeIndices);

            maxRenderDistance = 0f;
            DispatchOneFrame(battleStarted: false);
            yield return null;
            Assert.AreEqual(1, ReadInstanceCount(0, 0), "near bucket");
            Assert.AreEqual(1, ReadInstanceCount(0, 1), "mid bucket");
            Assert.AreEqual(2, ReadInstanceCount(0, 2), "far bucket unlimited");

            maxRenderDistance = 500f;
            DispatchOneFrame(battleStarted: false);
            yield return null;
            Assert.AreEqual(1, ReadInstanceCount(0, 2), "far bucket must drop the 900m agent at maxRenderDistance 500");
            Assert.AreEqual(1, ReadInstanceCount(0, 0), "near bucket unaffected by the cap");
        }

        private int ReadInstanceCount(int unitTypeIndex, int lodLevel)
        {
            uint[] args = new uint[5];
            buffers.GetDrawArgsBuffer(unitTypeIndex, lodLevel).GetData(args);
            return (int)args[1];
        }

        [UnityTest]
        public IEnumerator ClearedFlowTargetZeroFillsDirectionField()
        {
            // Ghost-target guard: removing the target must ZERO the direction field via
            // one clearing Generate pass — stale directions once marched armies to a
            // point that no longer existed.
            attackerFlowEnabled = true;
            attackerFlowRebuild = true;
            attackerFlowTargetMode = 1; // FLOW_TARGET_POINT
            attackerFlowTargetPoint = new Vector3(6f, 0f, 6f);
            DispatchOneFrame(battleStarted: false);
            yield return null;

            Vector2[] directions = new Vector2[16 * 16];
            buffers.flowFieldDirectionsBuffer.GetData(directions);
            int nonZero = 0;
            for (int i = 0; i < directions.Length; i++)
            {
                if (directions[i].sqrMagnitude > 0.0001f)
                    nonZero++;
            }
            Assert.Greater(nonZero, 200, "configured point target must fill most of the field with directions");

            attackerFlowTargetMode = 0; // target removed; dynamic off; battle stopped
            DispatchOneFrame(battleStarted: false);
            yield return null;

            buffers.flowFieldDirectionsBuffer.GetData(directions);
            for (int i = 0; i < directions.Length; i++)
                Assert.AreEqual(0f, directions[i].sqrMagnitude, 0.000001f, "cell " + i + " kept a ghost direction after target removal");
        }

        [UnityTest]
        public IEnumerator LodScaledSimulationPreservesTravelSpeed()
        {
            // March-speed twin of the kill-cadence guard: light frames integrate cached
            // velocity with REAL dt, so total displacement must match at any cadence.
            float displacementFull = 0f;
            float displacementQuarter = 0f;
            yield return RunMarch(1, value => displacementFull = value);
            yield return RunMarch(4, value => displacementQuarter = value);

            Assert.Greater(displacementFull, 3f, "full-rate march did not move — flow marching broken");
            Assert.Less(Mathf.Abs(displacementQuarter - displacementFull) / displacementFull, 0.15f,
                "march distance diverged between cadences: full=" + displacementFull + " quarter=" + displacementQuarter);
        }

        private IEnumerator RunMarch(int farInterval, System.Action<float> onDisplacement)
        {
            // Dead defenders => no combat; attackers follow the configured flow target.
            AgentData[] agents = (AgentData[])initialAgents.Clone();
            int[] hp = (int[])initialHp.Clone();
            for (int i = 0; i < TotalAgents; i++)
            {
                bool attacker = initialTeamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                agents[i].position = new Vector3(-6f, 0f, lane * 1.5f - 2f);
                agents[i].velocity = Vector3.zero;
                if (!attacker)
                    hp[i] = 0;
            }
            buffers.UploadInitialData(agents, initialTeamIds, hp, initialUnitTypeIndices);
            dispatchedFrames = 0;

            lodNearRadius = 0.01f;
            lodMidRadius = 0.02f;
            simFarInterval = farInterval;
            attackerFlowEnabled = true;
            attackerFlowRebuild = true;
            attackerFlowTargetMode = 1;
            attackerFlowTargetPoint = new Vector3(600f, 0f, 0f);

            for (int frame = 0; frame < 60; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                attackerFlowRebuild = false;
                if ((frame & 31) == 0)
                    yield return null;
            }

            AgentData[] result = new AgentData[TotalAgents];
            buffers.agentBuffer.GetData(result);
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < TotalAgents; i++)
            {
                if (initialTeamIds[i] != 0)
                    continue;
                sum += result[i].position.x - (-6f);
                count++;
            }
            onDisplacement(sum / Mathf.Max(1, count));

            // restore fixture defaults for subsequent tests
            attackerFlowEnabled = false;
            attackerFlowRebuild = false;
            attackerFlowTargetMode = 0;
            simFarInterval = 1;
            lodNearRadius = 100f;
            lodMidRadius = 200f;
        }

        [UnityTest]
        public IEnumerator PausingBattleFreezesHpAcrossBufferSwaps()
        {
            // The pause path must write hp through EVERY frame or the double-buffer swap
            // flip-flops between current and stale values (health flicker, corpse strobing).
            bool sawDamage = false;
            int[] hp = new int[TotalAgents];
            for (int frame = 0; frame < 200 && !sawDamage; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                buffers.combatBuffers.hpReadBuffer.GetData(hp);
                for (int i = 0; i < TotalAgents; i++)
                {
                    if (hp[i] < 100)
                        sawDamage = true;
                }
                if ((frame & 31) == 0)
                    yield return null;
            }
            Assert.IsTrue(sawDamage, "battle never produced damage");

            int[] previousHp = null;
            int[] previousStates = null;
            for (int frame = 0; frame < 6; frame++)
            {
                DispatchOneFrame(battleStarted: false);
                int[] pausedHp = new int[TotalAgents];
                buffers.combatBuffers.hpReadBuffer.GetData(pausedHp);
                int[] states = ReadStates();

                if (previousHp != null)
                {
                    CollectionAssert.AreEqual(previousHp, pausedHp, "hp changed across pause frames (swap parity leak)");
                    CollectionAssert.AreEqual(previousStates, states, "states changed across pause frames");
                }
                previousHp = pausedHp;
                previousStates = states;
            }
            yield return null;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void BuildScenario()
        {
            var created = new System.Collections.Generic.List<ScriptableObject>();

            UnitTypeConfig MakeType(int teamId, int count)
            {
                SpawnConfig spawn = ScriptableObject.CreateInstance<SpawnConfig>();
                spawn.unitCount = count;
                spawn.spawnSize = Vector3.zero;

                CombatConfig combat = ScriptableObject.CreateInstance<CombatConfig>();
                combat.attackDamage = AttackDamage;
                combat.attackInterval = AttackInterval;
                combat.attackRange = 3f;
                combat.targetAcquireRadius = 8f;
                combat.maxHp = 100;

                FlockingConfig flocking = ScriptableObject.CreateInstance<FlockingConfig>();
                flocking.separationStrength = 0f;
                flocking.densityAvoidanceStrength = 0f;
                flocking.densitySpeedPenalty = 0f;

                UnitTypeConfig config = ScriptableObject.CreateInstance<UnitTypeConfig>();
                config.teamId = teamId;
                config.spawnConfig = spawn;
                config.combatConfig = combat;
                config.flockingConfig = flocking;

                created.Add(spawn);
                created.Add(combat);
                created.Add(flocking);
                created.Add(config);
                return config;
            }

            scenario = ScriptableObject.CreateInstance<ScenarioConfig>();
            scenario.unitTypes = new[] { MakeType(0, AttackerCount), MakeType(1, DefenderCount) };
            createdConfigs = created.ToArray();

            registry = new UnitTypeRegistry();
            registry.RegisterFromScenario(scenario);
            Assert.AreEqual(2, registry.UnitTypeCount);
        }

        private int dispatchedFrames;

        private void DispatchOneFrame(bool battleStarted)
        {
            registry.FillGpuSettings(settingsCache);
            buffers.UploadUnitTypeSettings(settingsCache);

            PipelineFrameContext context = new PipelineFrameContext
            {
                deltaTime = FrameDt,
                // Deterministic frame counter: Time.frameCount does not advance between
                // dispatches issued inside a single editor frame, which would freeze the
                // staggered target-search phase.
                frameIndex = ++dispatchedFrames,
                totalAgentCount = TotalAgents,
                unitTypeCount = registry.UnitTypeCount,
                agentThreadGroupsX = 1,
                gridThreadGroupsX = 1,
                battleStarted = battleStarted,
                combatEnabled = true,
                attackerTeamId = 0,
                defenderTeamId = 1,
                rebuildDensityMap = true,
                densityMapThreadGroupsX = 2,
                densityMapThreadGroupsY = 2,
                defenderMovementMode = 0,
                defenderGuardRadius = 50f,
                localTargetSearchCellRadius = 4,
                grid = new GridFrameSettings
                {
                    resolutionX = 8,
                    resolutionZ = 8,
                    origin = new Vector2(-8f, -8f),
                    worldSize = new Vector2(16f, 16f),
                    cellSize = 2f,
                    maxAgentsPerCell = 16,
                    boundaryPadding = 0.5f
                },
                attackerFlow = new TeamFlowFrameSettings
                {
                    enabled = attackerFlowEnabled,
                    rebuildThisFrame = attackerFlowRebuild,
                    threadGroupsX = 4,
                    resolutionX = 16,
                    resolutionZ = 16,
                    origin = new Vector2(-8f, -8f),
                    cellSize = 1f,
                    targetMode = attackerFlowTargetMode,
                    targetPoint = attackerFlowTargetPoint,
                    sectorCount = 5,
                    minAgentsPerTarget = 8
                },
                defenderFlow = new TeamFlowFrameSettings { enabled = false, resolutionX = 16, resolutionZ = 16, origin = new Vector2(-8f, -8f), cellSize = 1f },
                lod = new LodFrameSettings
                {
                    lodCenterPosition = Vector3.zero,
                    nearLodRadius = lodNearRadius,
                    midLodRadius = lodMidRadius,
                    maxRenderDistance = maxRenderDistance,
                    nearAnimationInterval = 1,
                    midAnimationInterval = 1,
                    farAnimationInterval = 1,
                    nearSimulationInterval = 1,
                    midSimulationInterval = 1,
                    farSimulationInterval = simFarInterval
                }
            };

            orchestrator.DispatchFrame(context);
        }

        private int[] ReadStates()
        {
            AgentData[] agents = new AgentData[TotalAgents];
            buffers.agentBuffer.GetData(agents);
            int[] states = new int[TotalAgents];
            for (int i = 0; i < TotalAgents; i++)
                states[i] = agents[i].currentState;
            return states;
        }
    }
}
#endif
