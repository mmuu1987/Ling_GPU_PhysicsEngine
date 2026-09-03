#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
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
        private bool attackerFlowDynamic;
        private int attackerFlowTargetMode;
        private Vector3 attackerFlowTargetPoint;
        private int attackerFlowMinPerTarget = 8;
        private int gridMaxAgentsPerCell = 16;
        private int staticObstacleCount;
        private float staticObstaclePadding;
        private readonly Vector4[] staticObstacleRects = new Vector4[StaticObstacleMath.MaxObstacleCount];

        // Fixture population; BuildScenario overwrites these so a test can rebuild
        // the whole rig at a larger scale (cross-thread-group coverage).
        private int fixtureAttackerCount = AttackerCount;
        private int fixtureTotalAgents = TotalAgents;
        private MassGpuShaderSet shaderSet;
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

            gridMaxAgentsPerCell = 16;
            staticObstacleCount = 0;
            staticObstaclePadding = 0f;
            for (int i = 0; i < staticObstacleRects.Length; i++)
                staticObstacleRects[i] = Vector4.zero;

            ComputeShader spatialHash = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Spatial/Shaders/AgentSpatialHash.compute");
            ComputeShader runtimeFlow = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "FlowField/Shaders/AgentRuntimeFlow.compute");
            ComputeShader combat = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Simulation/Shaders/AgentCombatSimulation.compute");
            ComputeShader lod = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "VatRender/Shaders/AgentLodClassification.compute");
            Assert.NotNull(spatialHash, "spatial hash compute shader asset missing");
            Assert.NotNull(combat, "combat compute shader asset missing");

            MassGpuShaderSet shaders = MassGpuShaderSet.Find(spatialHash, runtimeFlow, combat, lod);
            Assert.IsTrue(shaders.IsValid);
            shaderSet = shaders;

            BuildScenario(AttackerCount, DefenderCount);
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
        public IEnumerator ConfiguredFlowDetoursAroundStaticObstacle()
        {
            attackerFlowEnabled = true;
            attackerFlowRebuild = true;
            attackerFlowTargetMode = 1;
            attackerFlowTargetPoint = new Vector3(7f, 0f, 0.5f);
            staticObstacleCount = 1;
            staticObstaclePadding = 0.25f;
            staticObstacleRects[0] = new Vector4(-1f, -3f, 1f, 3f);

            DispatchOneFrame(battleStarted: false);
            yield return null;

            Vector2[] directions = new Vector2[16 * 16];
            buffers.flowFieldDirectionsBuffer.GetData(directions);
            Vector2 westCell = directions[8 * 16 + 2]; // world (-5.5, 0.5)
            Assert.Greater(westCell.x, 0.2f, "detour must still make eastward progress: " + westCell);
            Assert.Greater(Mathf.Abs(westCell.y), 0.2f, "blocked direct ray must bend around a wall corner: " + westCell);

            attackerFlowEnabled = false;
            attackerFlowRebuild = false;
            attackerFlowTargetMode = 0;
            staticObstacleCount = 0;
        }

        [UnityTest]
        public IEnumerator SimulationPushesAgentsOutOfStaticObstacles()
        {
            staticObstacleCount = 1;
            staticObstacleRects[0] = new Vector4(-2f, -2f, 2f, 5f);

            DispatchOneFrame(battleStarted: true);
            yield return null;

            AgentData[] result = new AgentData[TotalAgents];
            buffers.agentBuffer.GetData(result);
            for (int i = 0; i < result.Length; i++)
            {
                Vector3 position = result[i].position;
                bool insideRawObstacle = position.x >= -2f && position.x <= 2f &&
                                         position.z >= -2f && position.z <= 5f;
                Assert.IsFalse(insideRawObstacle, "agent " + i + " remained inside the obstacle at " + position);
            }

            staticObstacleCount = 0;
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

        [UnityTest]
        public IEnumerator DynamicSectorSelectionSteersFlowAtEnemyCluster()
        {
            // Golden test for the parallel per-sector Select kernel AND the endgame
            // fallback that moved into Generate: both paths must aim the field at the
            // enemy cluster near (6, 6).
            AgentData[] agents = (AgentData[])initialAgents.Clone();
            for (int i = 0; i < TotalAgents; i++)
            {
                bool attacker = initialTeamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                agents[i].position = attacker
                    ? new Vector3(-6f, 0f, lane * 1.5f - 2f)
                    : new Vector3(6f, 0f, 5.5f + lane * 0.4f);
                agents[i].velocity = Vector3.zero;
            }
            buffers.UploadInitialData(agents, initialTeamIds, initialHp, initialUnitTypeIndices);

            attackerFlowEnabled = true;
            attackerFlowRebuild = true;
            attackerFlowDynamic = true;
            attackerFlowTargetMode = 0;
            attackerFlowMinPerTarget = 2; // 4 clustered defenders clear this bar
            DispatchOneFrame(battleStarted: true);
            yield return null;

            int[] stats = new int[4];
            int attackerTeamStatsOffset = 0; // team 0 is attacker
            buffers.runtimeFlowStatsBuffer.GetData(stats, 0, attackerTeamStatsOffset, 4);
            Assert.AreEqual(4, stats[0], "density build must count the 4 living defenders");
            Assert.AreEqual(1, stats[3], "exactly one sector meets the min-agents bar");

            Vector2[] directions = new Vector2[16 * 16];
            buffers.flowFieldDirectionsBuffer.GetData(directions);
            Vector2 westCell = directions[14 * 16 + 2]; // world (-5.5, 6.5), same sector as the cluster
            Assert.Greater(westCell.x, 0.7f, "sector path: west cells must point east at the cluster, got " + westCell);

            // Endgame fallback: raise the bar so no sector qualifies; Generate must
            // steer at the global centroid instead of zeroing the field.
            attackerFlowMinPerTarget = 50;
            attackerFlowRebuild = true;
            DispatchOneFrame(battleStarted: true);
            yield return null;

            buffers.runtimeFlowStatsBuffer.GetData(stats, 0, attackerTeamStatsOffset, 4);
            Assert.AreEqual(0, stats[3], "no sector may meet a bar of 50");
            buffers.flowFieldDirectionsBuffer.GetData(directions);
            westCell = directions[14 * 16 + 2];
            Assert.Greater(westCell.x, 0.7f, "fallback path: west cells must point east at the centroid, got " + westCell);

            attackerFlowEnabled = false;
            attackerFlowRebuild = false;
            attackerFlowDynamic = false;
            attackerFlowMinPerTarget = 8;
        }

        [UnityTest]
        public IEnumerator DensityMapCountsAliveAgentsPerCell()
        {
            // TG-01: the density map is the sole input of the per-square-meter crowd
            // pressure; its cell counts must equal the number of LIVING agents inside.
            DispatchOneFrame(battleStarted: false);
            yield return null;

            int[] map = ReadDensityMap();
            int total = 0;
            for (int i = 0; i < map.Length; i++)
                total += map[i];
            Assert.AreEqual(TotalAgents, total, "density map must count every living agent exactly once");
            // Default layout: attackers at x=-0.5 (cell 7), defenders at x=0.5 (cell 8),
            // z lanes 0/1.5/3/4.5 -> cells 8, 9, 11, 12.
            int[] laneCells = { 8, 9, 11, 12 };
            foreach (int zCell in laneCells)
            {
                Assert.AreEqual(1, map[zCell * 16 + 7], "attacker cell z=" + zCell);
                Assert.AreEqual(1, map[zCell * 16 + 8], "defender cell z=" + zCell);
            }

            // Dead agents must vanish from the map.
            int[] hp = (int[])initialHp.Clone();
            for (int i = 0; i < TotalAgents; i++)
            {
                if (initialTeamIds[i] != 0)
                    hp[i] = 0;
            }
            buffers.UploadInitialData(initialAgents, initialTeamIds, hp, initialUnitTypeIndices);
            DispatchOneFrame(battleStarted: false);
            yield return null;

            map = ReadDensityMap();
            total = 0;
            for (int i = 0; i < map.Length; i++)
                total += map[i];
            Assert.AreEqual(AttackerCount, total, "dead defenders must not appear in the density map");
            foreach (int zCell in laneCells)
                Assert.AreEqual(0, map[zCell * 16 + 8], "dead defender cell z=" + zCell);
        }

        private int[] ReadDensityMap()
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(buffers.densityMapTexture);
            request.WaitForCompletion();
            Assert.IsFalse(request.hasError, "density map readback failed");
            var data = request.GetData<int>();
            int[] map = new int[data.Length];
            data.CopyTo(map);
            return map;
        }

        [UnityTest]
        public IEnumerator InterleavedTeamsAcrossThreadGroupsFightAndClassifyCorrectly()
        {
            // TG-05 + TG-06: team ids interleaved per index (any surviving index-range
            // team inference misfires) across 256 agents = four 64-thread groups.
            buffers.ReleaseAll();
            registry.ReleaseAll();
            foreach (ScriptableObject asset in createdConfigs)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }
            Object.DestroyImmediate(scenario);

            BuildScenario(128, 128);
            buffers = new MassGpuBufferManager();
            orchestrator = new ComputePipelineOrchestrator(shaderSet, buffers);
            buffers.Allocate(fixtureTotalAgents, 64, 64, 16, 16, registry.UnitTypeCount);
            registry.InitializeAll(buffers, orchestrator);
            gridMaxAgentsPerCell = 64;

            AgentData[] agents = new AgentData[fixtureTotalAgents];
            int[] teamIds = new int[fixtureTotalAgents];
            int[] hp = new int[fixtureTotalAgents];
            int[] unitTypeIndices = new int[fixtureTotalAgents];
            registry.GenerateAgents(agents);
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                teamIds[i] = i % 2;
                unitTypeIndices[i] = i % 2;
                hp[i] = 100;
                int lane = i / 2;
                agents[i].position = new Vector3(teamIds[i] == 0 ? -0.5f : 0.5f, 0f, -7.5f + lane * 0.117f);
                agents[i].velocity = Vector3.zero;
            }
            buffers.UploadInitialData(agents, teamIds, hp, unitTypeIndices);
            settingsCache = new UnitTypeGpuSettings[registry.UnitTypeCount];
            registry.FillGpuSettings(settingsCache);
            buffers.UploadUnitTypeSettings(settingsCache);
            dispatchedFrames = 0;

            for (int frame = 0; frame < 40; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                if ((frame & 15) == 0)
                    yield return null;
            }

            int[] resultHp = new int[fixtureTotalAgents];
            buffers.combatBuffers.hpReadBuffer.GetData(resultHp);
            int damagedEven = 0;
            int damagedOdd = 0;
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                if (resultHp[i] >= 100)
                    continue;
                if (i % 2 == 0)
                    damagedEven++;
                else
                    damagedOdd++;
            }
            Assert.Greater(damagedEven, 32, "interleaved team 0 must take widespread damage across all thread groups");
            Assert.Greater(damagedOdd, 32, "interleaved team 1 must take widespread damage across all thread groups");

            int type0Instances = ReadInstanceCount(0, 0) + ReadInstanceCount(0, 1) + ReadInstanceCount(0, 2);
            int type1Instances = ReadInstanceCount(1, 0) + ReadInstanceCount(1, 1) + ReadInstanceCount(1, 2);
            Assert.AreEqual(128, type0Instances, "unit type 0 classify count (interleaved unitTypeIndexBuffer)");
            Assert.AreEqual(128, type1Instances, "unit type 1 classify count (interleaved unitTypeIndexBuffer)");

            gridMaxAgentsPerCell = 16;
        }

        [UnityTest]
        public IEnumerator TeamCombatGridKeepsOutnumberedEnemyTargetableDuringMixedGridOverflow()
        {
            buffers.ReleaseAll();
            registry.ReleaseAll();
            foreach (ScriptableObject asset in createdConfigs)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }
            Object.DestroyImmediate(scenario);

            const int denseAttackerCount = 64;
            BuildScenario(denseAttackerCount, 1);
            buffers = new MassGpuBufferManager();
            orchestrator = new ComputePipelineOrchestrator(shaderSet, buffers);
            gridMaxAgentsPerCell = 1;
            buffers.Allocate(fixtureTotalAgents, 64, gridMaxAgentsPerCell, 16, 16, registry.UnitTypeCount);
            registry.InitializeAll(buffers, orchestrator);

            AgentData[] agents = new AgentData[fixtureTotalAgents];
            int[] teamIds = new int[fixtureTotalAgents];
            int[] hp = new int[fixtureTotalAgents];
            int[] unitTypeIndices = new int[fixtureTotalAgents];
            registry.GenerateAgents(agents);
            registry.FillCombatArrays(teamIds, hp, unitTypeIndices);
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                agents[i].position = Vector3.zero;
                agents[i].velocity = Vector3.zero;
            }

            buffers.UploadInitialData(agents, teamIds, hp, unitTypeIndices);
            settingsCache = new UnitTypeGpuSettings[registry.UnitTypeCount];
            registry.FillGpuSettings(settingsCache);
            buffers.UploadUnitTypeSettings(settingsCache);
            dispatchedFrames = 0;

            // Build the hashes without combat first. The mixed grid can hold only one
            // of 65 occupants, while the defender-specific cell must still retain the
            // lone defender for attacker target queries.
            DispatchOneFrame(battleStarted: false);
            int[] teamCounts = new int[buffers.teamGridCountsBuffer.count];
            int[] teamIndices = new int[buffers.teamGridAgentIndicesBuffer.count];
            buffers.teamGridCountsBuffer.GetData(teamCounts);
            buffers.teamGridAgentIndicesBuffer.GetData(teamIndices);
            const int occupiedCell = 4 + 4 * 8;
            int defenderCell = 64 + occupiedCell;
            Assert.That(teamCounts[defenderCell], Is.EqualTo(1));
            Assert.That(teamIds[teamIndices[defenderCell]], Is.EqualTo(1));

            for (int frame = 0; frame < 24; frame++)
                DispatchOneFrame(battleStarted: true);
            yield return null;

            int[] resultHp = new int[fixtureTotalAgents];
            buffers.combatBuffers.hpReadBuffer.GetData(resultHp);
            Assert.That(resultHp[denseAttackerCount], Is.LessThanOrEqualTo(0),
                "the lone defender became untargetable when attackers overflowed the mixed spatial cell");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void BuildScenario(int attackerCount, int defenderCount)
        {
            fixtureAttackerCount = attackerCount;
            fixtureTotalAgents = attackerCount + defenderCount;

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
            scenario.unitTypes = new[] { MakeType(0, attackerCount), MakeType(1, defenderCount) };
            createdConfigs = created.ToArray();

            registry = new UnitTypeRegistry();
            registry.RegisterFromScenario(scenario);
            Assert.AreEqual(2, registry.UnitTypeCount);
        }

        private int dispatchedFrames;

        [UnityTest]
        public IEnumerator TeamSpatialTelemetryReducesLiveGpuPopulation()
        {
            AgentData[] agents = (AgentData[])initialAgents.Clone();
            for (int i = 0; i < agents.Length; i++)
            {
                bool attacker = initialTeamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                agents[i].position = new Vector3(attacker ? -10f : 20f, 0f, lane * 2f);
            }
            buffers.UploadInitialData(agents, initialTeamIds, initialHp, initialUnitTypeIndices);

            BattleTelemetry telemetry = new BattleTelemetry(shaderSet.SpatialHashShader, 0.1f);
            telemetry.ConfigureObservationZone(new Vector3(-10f, 0f, 3f), 5f, true);
            telemetry.Tick(buffers, 1f);
            for (int frame = 0; frame < 120 && !telemetry.Snapshot.valid; frame++)
                yield return null;

            BattleTelemetrySnapshot snapshot = telemetry.Snapshot;
            Assert.That(snapshot.valid, Is.True, "team spatial telemetry readback timed out");
            Assert.That(snapshot.aliveAttackers, Is.EqualTo(AttackerCount));
            Assert.That(snapshot.aliveDefenders, Is.EqualTo(DefenderCount));
            Assert.That(snapshot.attackers.centroid, Is.EqualTo(new Vector3(-10f, 0f, 3f)));
            Assert.That(snapshot.defenders.centroid, Is.EqualTo(new Vector3(20f, 0f, 3f)));
            Assert.That(snapshot.attackers.observationZoneCount, Is.EqualTo(AttackerCount));
            Assert.That(snapshot.defenders.observationZoneCount, Is.Zero);
        }

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
                totalAgentCount = fixtureTotalAgents,
                unitTypeCount = registry.UnitTypeCount,
                teamCount = 2,
                agentThreadGroupsX = Mathf.Max(1, (fixtureTotalAgents + 63) / 64),
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
                staticObstacleCount = staticObstacleCount,
                staticObstaclePadding = staticObstaclePadding,
                staticObstacleRects = staticObstacleRects,
                grid = new GridFrameSettings
                {
                    resolutionX = 8,
                    resolutionZ = 8,
                    origin = new Vector2(-8f, -8f),
                    worldSize = new Vector2(16f, 16f),
                    cellSize = 2f,
                    maxAgentsPerCell = gridMaxAgentsPerCell,
                    boundaryPadding = 0.5f
                },
                attackerFlow = new TeamFlowFrameSettings
                {
                    enabled = attackerFlowEnabled,
                    rebuildThisFrame = attackerFlowRebuild,
                    dynamicFlowEnabled = attackerFlowDynamic,
                    threadGroupsX = 4,
                    resolutionX = 16,
                    resolutionZ = 16,
                    origin = new Vector2(-8f, -8f),
                    cellSize = 1f,
                    targetMode = attackerFlowTargetMode,
                    targetPoint = attackerFlowTargetPoint,
                    sectorCount = 5,
                    minAgentsPerTarget = attackerFlowMinPerTarget
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
            AgentData[] agents = new AgentData[fixtureTotalAgents];
            buffers.agentBuffer.GetData(agents);
            int[] states = new int[fixtureTotalAgents];
            for (int i = 0; i < fixtureTotalAgents; i++)
                states[i] = agents[i].currentState;
            return states;
        }
    }
}
#endif
