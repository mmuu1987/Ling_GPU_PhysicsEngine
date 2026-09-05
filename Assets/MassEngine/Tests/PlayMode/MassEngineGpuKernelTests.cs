#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using MassEngine.Projectiles;

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
        private ProjectileGpuManager projectileManager;
        private bool projectileSimulationEnabled;
        private bool projectileProcessingEnabled;
        private float projectileSimulationTime;
        // TotalLaunched as of the previous active-list check, so the helper can tell an
        // upload frame (list legally one frame behind) from a quiet one (must match exactly).
        private int activeListLaunchCursor = -1;

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
        // Per-team flow records handed to DispatchOneFrame. Null means the historical layout the
        // attackerFlow* fields above express: team 0 navigates, every other team's field is off.
        private TeamFlowFrameSettings[] fixtureTeamFlows;
        private int gridMaxAgentsPerCell = 16;
        // Per-team stance uploaded before every dispatch. Null means the historical two-team
        // default: attacker advances, defender holds - which is what defenderMovementMode = 0
        // expressed back when stance was a single uniform owned by "the defender".
        private int[] fixtureTeamStances;
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
            fixtureTeamStances = null;
            fixtureTeamFlows = null;
            staticObstacleCount = 0;
            staticObstaclePadding = 0f;
            for (int i = 0; i < staticObstacleRects.Length; i++)
                staticObstacleRects[i] = Vector4.zero;

            ComputeShader spatialHash = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Spatial/Shaders/AgentSpatialHash.compute");
            ComputeShader runtimeFlow = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "FlowField/Shaders/AgentRuntimeFlow.compute");
            ComputeShader combat = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Simulation/Shaders/AgentCombatSimulation.compute");
            ComputeShader lod = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "VatRender/Shaders/AgentLodClassification.compute");
            ComputeShader projectile = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderRoot + "Projectiles/Shaders/ProjectileSimulation.compute");
            Assert.NotNull(spatialHash, "spatial hash compute shader asset missing");
            Assert.NotNull(combat, "combat compute shader asset missing");

            MassGpuShaderSet shaders = MassGpuShaderSet.Find(spatialHashShader: spatialHash, runtimeFlowShader: runtimeFlow, combatSimulationShader: combat, lodClassificationShader: lod, projectileShader: projectile);
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
            projectileSimulationEnabled = false;
            projectileProcessingEnabled = false;
            projectileSimulationTime = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            if (projectileManager != null)
                projectileManager.Dispose();
            projectileManager = null;

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

            // Army-wide cadence bound, sampled every frame. Every agent - both teams trade
            // here - may land at most one hit per attackInterval, so total damage after N
            // frames can never exceed agents * damage * (elapsed / interval + 1). That is a
            // statement about the cadence alone, independent of how the attackers
            // distributed themselves over victims.
            int worstFrame = -1;
            int worstLost = 0;
            int worstBound = 0;
            int victimFocus = 0;
            int victimIndex = -1;

            for (int frame = 0; frame < framesForFirstBlood && !sawDeath; frame++)
            {
                DispatchOneFrame(battleStarted: true);

                buffers.combatBuffers.hpReadBuffer.GetData(hp);
                int armyLost = 0;
                for (int i = 0; i < TotalAgents; i++)
                {
                    if (hp[i] < 100)
                        sawDamage = true;
                    armyLost += 100 - Mathf.Max(0, hp[i]);
                    if (hp[i] <= 0 && !sawDeath)
                    {
                        sawDeath = true;
                        firstDeathFrame = frame;
                        victimIndex = i;
                    }
                }

                int elapsedIntervals = Mathf.FloorToInt(((frame + 1) * FrameDt) / AttackInterval);
                int bound = TotalAgents * AttackDamage * (elapsedIntervals + 1);
                // Tightest frame, not just violations, so the report always carries the
                // real numbers: a difference of zero means the cadence budget was met
                // exactly, anything positive means attackInterval was outrun.
                if (worstFrame < 0 || armyLost - bound > worstLost - worstBound)
                {
                    worstFrame = frame;
                    worstLost = armyLost;
                    worstBound = bound;
                }

                if (sawDeath && victimIndex >= 0)
                {
                    int[] deathTargets = new int[TotalAgents];
                    buffers.combatBuffers.targetAgentIndexBuffer.GetData(deathTargets);
                    for (int i = 0; i < AttackerCount; i++)
                    {
                        if (deathTargets[i] == victimIndex)
                            victimFocus++;
                    }
                }

                if ((frame & 31) == 0)
                    yield return null;
            }

            string cadence = "first death on frame " + firstDeathFrame + "; victim " + victimIndex +
                " was targeted by " + victimFocus + " of " + AttackerCount + " attackers at death; " +
                "army damage peaked at " + worstLost + " against a cadence bound of " + worstBound +
                " on frame " + worstFrame;

            Assert.IsTrue(sawDamage, "no damage was ever applied on the GPU");
            Assert.IsTrue(sawDeath, "no agent died although damage should be lethal within the frame budget");

            // Attack CADENCE guard, measured army-wide rather than per victim. Every agent
            // - both teams trade in this fixture - may land at most one hit per
            // attackInterval, so total damage after N frames can never exceed
            // agents * damage * (elapsed / interval + 1). A regression that ignores
            // attackInterval and hits every frame outruns that within a few frames.
            //
            // This replaces a lower bound on the FIRST DEATH frame, which silently assumed
            // 4v4 hands every attacker its own victim. It does not: the engagement slot
            // capacity here is 8, so four attackers on one defender run a load ratio of
            // 0.5 and never trigger redistribution. Two attackers per victim is legal and
            // kills in five rounds, which failed an assertion about cadence for a reason
            // that had nothing to do with cadence. The bound below is a statement about
            // the cadence alone, independent of how attackers distribute over victims.
            Assert.LessOrEqual(worstLost, worstBound,
                "army damage outran the attack cadence, so attackInterval is being ignored. " + cadence);

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

        /// <summary>
        /// Sizes the stance table to whatever team count the buffers were allocated with, so a
        /// widened layout is filled to the end instead of leaving trailing teams on the
        /// zero-initialized Hold that a short array would keep.
        /// </summary>
        private void UploadFixtureTeamStances()
        {
            int teamCount = buffers.TeamCount;
            int[] stances = new int[teamCount];
            for (int teamId = 0; teamId < teamCount; teamId++)
            {
                stances[teamId] = fixtureTeamStances != null && teamId < fixtureTeamStances.Length
                    ? fixtureTeamStances[teamId]
                    : (int)(teamId == 1 ? TeamStance.Hold : TeamStance.Advance);
            }

            buffers.UploadTeamStances(stances);
        }

        /// <summary>
        /// One flow record per allocated team. Team 0 is driven by the attackerFlow* fields, which
        /// is what the single attacker record used to carry; the rest stay off unless a test fills
        /// fixtureTeamFlows. The grid values are stamped over every record on purpose: the flow
        /// buffers are partitioned as teamId * cellCount + cell, which only holds while every team
        /// shares one grid, so a test cannot accidentally give one team a grid of its own.
        /// </summary>
        private TeamFlowFrameSettings[] BuildFixtureTeamFlows()
        {
            int teamCount = Mathf.Max(1, buffers.TeamCount);
            TeamFlowFrameSettings[] flows = new TeamFlowFrameSettings[teamCount];
            for (int teamId = 0; teamId < teamCount; teamId++)
            {
                if (fixtureTeamFlows != null && teamId < fixtureTeamFlows.Length)
                    flows[teamId] = fixtureTeamFlows[teamId];
                else if (teamId == 0)
                    flows[teamId] = new TeamFlowFrameSettings
                    {
                        enabled = attackerFlowEnabled,
                        rebuildThisFrame = attackerFlowRebuild,
                        dynamicFlowEnabled = attackerFlowDynamic,
                        targetMode = attackerFlowTargetMode,
                        targetPoint = attackerFlowTargetPoint,
                        sectorCount = 5,
                        minAgentsPerTarget = attackerFlowMinPerTarget
                    };

                flows[teamId].threadGroupsX = 4;
                flows[teamId].resolutionX = 16;
                flows[teamId].resolutionZ = 16;
                flows[teamId].origin = new Vector2(-8f, -8f);
                flows[teamId].cellSize = 1f;
            }

            return flows;
        }

        /// <summary>
        /// Reads one team's slice of the direction field. The slice offset is the whole point of
        /// the readback: one buffer now holds every team's cells back to back.
        /// </summary>
        private Vector2[] ReadFlowDirections(int teamId)
        {
            Vector2[] directions = new Vector2[buffers.FlowCellCount];
            buffers.flowFieldDirectionsBuffer.GetData(directions, 0, teamId * buffers.FlowCellCount, directions.Length);
            return directions;
        }

        /// <summary>
        /// A record that rebuilds this frame and steers at one configured point. Grid fields are
        /// left out on purpose - BuildFixtureTeamFlows stamps the shared grid over every record.
        /// </summary>
        private static TeamFlowFrameSettings NavigatingTeamFlow(Vector3 targetPoint)
        {
            return new TeamFlowFrameSettings
            {
                enabled = true,
                rebuildThisFrame = true,
                targetMode = 1, // FLOW_TARGET_POINT
                targetPoint = targetPoint,
                sectorCount = 5,
                minAgentsPerTarget = 8
            };
        }

        /// <summary>Reads one team's slice of the runtime flow stats.</summary>
        private int[] ReadFlowStats(int teamId)
        {
            int[] stats = new int[MassGpuBufferManager.FlowStatsSlotsPerTeam];
            buffers.runtimeFlowStatsBuffer.GetData(stats, 0, teamId * MassGpuBufferManager.FlowStatsSlotsPerTeam, stats.Length);
            return stats;
        }

        /// <summary>
        /// Asserts every agent of one team either left its spawn or did not move at all,
        /// comparing against initialAgents because ResetBattlefield respawns from that array.
        /// </summary>
        private void AssertTeamDisplacement(Vector2[] positions, int teamId, bool expectMoved)
        {
            int inspected = 0;
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                if (initialTeamIds[i] != teamId)
                    continue;

                inspected++;
                Vector2 spawn = new Vector2(initialAgents[i].position.x, initialAgents[i].position.z);
                float moved = Vector2.Distance(positions[i], spawn);
                if (expectMoved)
                    Assert.That(moved, Is.GreaterThan(0.1f), "advancing agent " + i + " (team " + teamId + ") never left its spawn");
                else
                    Assert.That(moved, Is.LessThan(0.001f), "holding agent " + i + " (team " + teamId + ") drifted " + moved + "m");
            }

            Assert.That(inspected, Is.GreaterThan(0), "no agent belongs to team " + teamId);
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

            Vector2[] directions = ReadFlowDirections(0);
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

            directions = ReadFlowDirections(0);
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

            Vector2[] directions = ReadFlowDirections(0);
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

            int[] stats = ReadFlowStats(0);
            Assert.AreEqual(4, stats[0], "density build must count the 4 living defenders");
            Assert.AreEqual(1, stats[3], "exactly one sector meets the min-agents bar");

            Vector2[] directions = ReadFlowDirections(0);
            Vector2 westCell = directions[14 * 16 + 2]; // world (-5.5, 6.5), same sector as the cluster
            Assert.Greater(westCell.x, 0.7f, "sector path: west cells must point east at the cluster, got " + westCell);

            // Endgame fallback: raise the bar so no sector qualifies; Generate must
            // steer at the global centroid instead of zeroing the field.
            attackerFlowMinPerTarget = 50;
            attackerFlowRebuild = true;
            DispatchOneFrame(battleStarted: true);
            yield return null;

            stats = ReadFlowStats(0);
            Assert.AreEqual(0, stats[3], "no sector may meet a bar of 50");
            directions = ReadFlowDirections(0);
            westCell = directions[14 * 16 + 2];
            Assert.Greater(westCell.x, 0.7f, "fallback path: west cells must point east at the centroid, got " + westCell);

            attackerFlowEnabled = false;
            attackerFlowRebuild = false;
            attackerFlowDynamic = false;
            attackerFlowMinPerTarget = 8;
        }

        [UnityTest]
        public IEnumerator ThreeTeamsEachFollowOwnFlowField()
        {
            // Step 2 of multi-group navigation: one team-partitioned direction buffer replaced the
            // attacker/defender pair, so a third army owns a field instead of borrowing another
            // team's. Three point targets pulling three different ways is what tells per-team
            // slices apart from a shared one: a kernel writing the wrong slice, or a dispatch loop
            // that stops after two teams, leaves two of these three readbacks identical or blank.
            const int centerCell = 8 * 16 + 8; // cell (8, 8) = world (0.5, 0.5), inside every field

            AllocateFixtureBuffers(3);
            ResetBattlefield();
            fixtureTeamFlows = new[]
            {
                NavigatingTeamFlow(new Vector3(7f, 0f, 0.5f)),
                NavigatingTeamFlow(new Vector3(-7f, 0f, 0.5f)),
                NavigatingTeamFlow(new Vector3(0.5f, 0f, 7f))
            };

            DispatchOneFrame(battleStarted: false);
            yield return null;

            Vector2 east = ReadFlowDirections(0)[centerCell];
            Vector2 west = ReadFlowDirections(1)[centerCell];
            Vector2 north = ReadFlowDirections(2)[centerCell];
            // Normalized directions with no obstacles in the way, so each axis is within
            // rounding of +/-1: a slice that picked up a neighbour's target fails by sign.
            Assert.Greater(east.x, 0.9f, "team 0 must steer east at its own target, got " + east);
            Assert.Less(west.x, -0.9f, "team 1 must steer west at its own target, got " + west);
            Assert.Greater(north.y, 0.9f, "team 2 must steer north at its own target, got " + north);

            fixtureTeamFlows = null;
        }

        [UnityTest]
        public IEnumerator DisabledTeamFlowLeavesOtherTeamsFields()
        {
            // The acceptance gate for the merge: the shipped scenario runs with the defender's
            // field switched off, and folding both fields into one buffer must not quietly turn
            // it on. Team 1 asks for nothing while team 0 rebuilds; team 1's slice has to stay
            // zeroed rather than inherit whatever the dispatched team wrote.
            const int centerCell = 8 * 16 + 8;

            AllocateFixtureBuffers(2);
            ResetBattlefield();
            fixtureTeamFlows = new[]
            {
                NavigatingTeamFlow(new Vector3(7f, 0f, 0.5f)),
                new TeamFlowFrameSettings { enabled = false }
            };

            DispatchOneFrame(battleStarted: false);
            yield return null;

            Vector2[] navigating = ReadFlowDirections(0);
            Assert.Greater(navigating[centerCell].x, 0.9f, "the navigating team lost its field, got " + navigating[centerCell]);

            Vector2[] idle = ReadFlowDirections(1);
            for (int i = 0; i < idle.Length; i++)
                Assert.AreEqual(0f, idle[i].sqrMagnitude, 0.000001f, "disabled team 1 cell " + i + " picked up a direction");

            fixtureTeamFlows = null;
        }

        [UnityTest]
        public IEnumerator DensityMapCountsAliveAgentsPerCell()
        {
            // TG-01: the density map is the sole input of the per-square-meter crowd
            // pressure; its cell counts must equal the number of LIVING agents inside.
            DispatchOneFrame(battleStarted: false);
            yield return null;

            int[] map = ReadDensityMap();
            int[] attackerMap = ReadDensityMap(buffers.attackerDensityMapTexture);
            int[] defenderMap = ReadDensityMap(buffers.defenderDensityMapTexture);
            int total = 0;
            int attackerTotal = 0;
            int defenderTotal = 0;
            for (int i = 0; i < map.Length; i++)
            {
                total += map[i];
                attackerTotal += attackerMap[i];
                defenderTotal += defenderMap[i];
            }
            Assert.AreEqual(TotalAgents, total, "density map must count every living agent exactly once");
            Assert.AreEqual(AttackerCount, attackerTotal, "attacker density must contain only team 0");
            Assert.AreEqual(DefenderCount, defenderTotal, "defender density must contain only team 1");
            // Default layout: attackers at x=-0.5 (cell 7), defenders at x=0.5 (cell 8),
            // z lanes 0/1.5/3/4.5 -> cells 8, 9, 11, 12.
            int[] laneCells = { 8, 9, 11, 12 };
            foreach (int zCell in laneCells)
            {
                Assert.AreEqual(1, map[zCell * 16 + 7], "attacker cell z=" + zCell);
                Assert.AreEqual(1, map[zCell * 16 + 8], "defender cell z=" + zCell);
                Assert.AreEqual(1, attackerMap[zCell * 16 + 7], "attacker team cell z=" + zCell);
                Assert.AreEqual(0, attackerMap[zCell * 16 + 8], "attacker map leaked defender z=" + zCell);
                Assert.AreEqual(0, defenderMap[zCell * 16 + 7], "defender map leaked attacker z=" + zCell);
                Assert.AreEqual(1, defenderMap[zCell * 16 + 8], "defender team cell z=" + zCell);
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
            attackerMap = ReadDensityMap(buffers.attackerDensityMapTexture);
            defenderMap = ReadDensityMap(buffers.defenderDensityMapTexture);
            total = 0;
            attackerTotal = 0;
            defenderTotal = 0;
            for (int i = 0; i < map.Length; i++)
            {
                total += map[i];
                attackerTotal += attackerMap[i];
                defenderTotal += defenderMap[i];
            }
            Assert.AreEqual(AttackerCount, total, "dead defenders must not appear in the density map");
            Assert.AreEqual(AttackerCount, attackerTotal);
            Assert.AreEqual(0, defenderTotal, "dead defenders must vanish from their team density map");
            foreach (int zCell in laneCells)
                Assert.AreEqual(0, map[zCell * 16 + 8], "dead defender cell z=" + zCell);
        }

        [UnityTest]
        public IEnumerator EngagementSlotOccupancyRedirectsAnOverloadedApproach()
        {
            // Four attackers claim the same slot around defender 4. The occupancy pass
            // must record all four claims before combat chooses lower-load sectors.
            int[] targets = new int[TotalAgents];
            int[] assignments = new int[TotalAgents];
            for (int i = 0; i < TotalAgents; i++)
            {
                targets[i] = -1;
                assignments[i] = -1;
            }

            int targetIndex = AttackerCount;
            for (int i = 0; i < AttackerCount; i++)
            {
                targets[i] = targetIndex;
                assignments[i] = targetIndex * MassGpuBufferManager.EngagementSlotsPerTarget;
            }
            buffers.combatBuffers.targetAgentIndexBuffer.SetData(targets);
            buffers.combatBuffers.engagementSlotAssignmentBuffer.SetData(assignments);

            DispatchOneFrame(battleStarted: true);
            yield return null;

            uint[] occupancy = new uint[fixtureTotalAgents * MassGpuBufferManager.EngagementSlotsPerTarget];
            buffers.combatBuffers.engagementSlotOccupancyBuffer.GetData(occupancy);
            uint packed = occupancy[targetIndex * MassGpuBufferManager.EngagementSlotsPerTarget];
            Assert.AreEqual((uint)dispatchedFrames & 0x00FFFFFFu, packed >> 8, "slot counter must carry the current frame stamp");
            Assert.AreEqual(AttackerCount, (int)(packed & 0xFFu), "all prior assignments must be counted");

            buffers.combatBuffers.engagementSlotAssignmentBuffer.GetData(assignments);
            bool redirected = false;
            for (int i = 0; i < AttackerCount; i++)
            {
                Assert.AreEqual(targetIndex, assignments[i] / MassGpuBufferManager.EngagementSlotsPerTarget);
                redirected |= assignments[i] % MassGpuBufferManager.EngagementSlotsPerTarget != 0;
            }
            Assert.IsTrue(redirected, "an overloaded slot must redirect at least one attacker");
        }

        [UnityTest]
        public IEnumerator TargetLoadBalancingDistributesAttackersWithoutDroppingTheLastEnemy()
        {
            buffers.ReleaseAll();
            registry.ReleaseAll();
            foreach (ScriptableObject asset in createdConfigs)
            {
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }
            Object.DestroyImmediate(scenario);

            const int attackerCount = 16;
            const int defenderCount = 2;
            BuildScenario(attackerCount, defenderCount);
            buffers = new MassGpuBufferManager();
            orchestrator = new ComputePipelineOrchestrator(shaderSet, buffers);
            gridMaxAgentsPerCell = 32;
            buffers.Allocate(fixtureTotalAgents, 64, gridMaxAgentsPerCell, 16, 16, registry.UnitTypeCount);
            registry.InitializeAll(buffers, orchestrator);

            AgentData[] agents = new AgentData[fixtureTotalAgents];
            int[] teamIds = new int[fixtureTotalAgents];
            int[] hp = new int[fixtureTotalAgents];
            int[] unitTypeIndices = new int[fixtureTotalAgents];
            registry.GenerateAgents(agents);
            registry.FillCombatArrays(teamIds, hp, unitTypeIndices);
            for (int i = 0; i < attackerCount; i++)
            {
                agents[i].position = new Vector3(-4f, 0f, -1.5f + i * 0.2f);
                agents[i].velocity = Vector3.zero;
            }
            agents[attackerCount].position = new Vector3(0f, 0f, -1f);
            agents[attackerCount + 1].position = new Vector3(0f, 0f, 1f);
            hp[attackerCount] = 10000;
            hp[attackerCount + 1] = 10000;
            buffers.UploadInitialData(agents, teamIds, hp, unitTypeIndices);

            settingsCache = new UnitTypeGpuSettings[registry.UnitTypeCount];
            registry.FillGpuSettings(settingsCache);
            buffers.UploadUnitTypeSettings(settingsCache);
            dispatchedFrames = 0;

            int[] targets = new int[fixtureTotalAgents];
            int[] assignments = new int[fixtureTotalAgents];
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                targets[i] = -1;
                assignments[i] = -1;
            }
            for (int i = 0; i < attackerCount; i++)
            {
                targets[i] = attackerCount;
                assignments[i] = attackerCount * MassGpuBufferManager.EngagementSlotsPerTarget +
                    i % MassGpuBufferManager.EngagementSlotsPerTarget;
            }
            buffers.combatBuffers.targetAgentIndexBuffer.SetData(targets);
            buffers.combatBuffers.engagementSlotAssignmentBuffer.SetData(assignments);

            // Per-frame history, because a single end-of-run sample cannot tell "never
            // redirected" apart from "redirected and swung back": the retarget cadence is
            // LOCAL_TARGET_SEARCH_INTERVAL frames, so frame 8 lands exactly on a beat.
            System.Text.StringBuilder history = new System.Text.StringBuilder("targets per frame");
            for (int frame = 0; frame < 8; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);
                int onFirst = 0;
                int onSecond = 0;
                for (int i = 0; i < attackerCount; i++)
                {
                    if (targets[i] == attackerCount)
                        onFirst++;
                    else if (targets[i] == attackerCount + 1)
                        onSecond++;
                }

                history.Append(" [").Append(dispatchedFrames).Append(": ")
                    .Append(onFirst).Append('/').Append(onSecond).Append(']');
            }
            yield return null;

            buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);
            int firstTargetCount = 0;
            int secondTargetCount = 0;
            for (int i = 0; i < attackerCount; i++)
            {
                if (targets[i] == attackerCount)
                    firstTargetCount++;
                else if (targets[i] == attackerCount + 1)
                    secondTargetCount++;
            }
            // The redirect decision reads engagement occupancy, so report what the GPU
            // actually saw. Without it a failure cannot be told apart from a stale
            // occupancy stamp, an unmatched search cadence or a scoring problem.
            string load = DescribeEngagementLoad(new[] { attackerCount, attackerCount + 1 }) +
                ", " + history;
            Assert.Greater(firstTargetCount, 0,
                "hysteresis must keep part of the force on the original target; " + load);
            Assert.Greater(secondTargetCount, 0,
                "overloaded targeting must redirect part of the force to the second defender; " + load);

            hp[attackerCount] = 0;
            hp[attackerCount + 1] = 10000;
            buffers.combatBuffers.hpReadBuffer.SetData(hp);
            buffers.combatBuffers.hpWriteBuffer.SetData(hp);
            for (int frame = 0; frame < 12; frame++)
                DispatchOneFrame(battleStarted: true);
            yield return null;

            buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);
            for (int i = 0; i < attackerCount; i++)
                Assert.AreEqual(attackerCount + 1, targets[i], "the sole surviving defender must remain targetable regardless of load");

            gridMaxAgentsPerCell = 16;
        }

        /// <summary>
        /// Engagement occupancy as the combat kernel reads it: only slots stamped with the
        /// current frame count, which is what CurrentEngagementOccupancy requires.
        /// </summary>
        private string DescribeEngagementLoad(int[] targetIndices)
        {
            int slots = MassGpuBufferManager.EngagementSlotsPerTarget;
            uint[] occupancy = new uint[fixtureTotalAgents * slots];
            buffers.combatBuffers.engagementSlotOccupancyBuffer.GetData(occupancy);
            uint stamp = (uint)dispatchedFrames & 0x00FFFFFFu;

            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("frame ").Append(dispatchedFrames).Append(", engagement load");
            foreach (int targetIndex in targetIndices)
            {
                int fresh = 0;
                int stale = 0;
                for (int slot = 0; slot < slots; slot++)
                {
                    uint packed = occupancy[targetIndex * slots + slot];
                    if ((packed >> 8) == stamp)
                        fresh += (int)(packed & 0xFFu);
                    else
                        stale += (int)(packed & 0xFFu);
                }

                text.Append(" [target ").Append(targetIndex).Append(": fresh ").Append(fresh);
                if (stale > 0)
                    text.Append(", stale ").Append(stale);
                text.Append(']');
            }

            return text.ToString();
        }

        private int[] ReadDensityMap()
        {
            return ReadDensityMap(buffers.densityMapTexture);
        }

        private int[] ReadDensityMap(RenderTexture texture)
        {
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(texture);
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

        [UnityTest]
        public IEnumerator RaisingTeamCountDoesNotChangeTwoTeamOutcome()
        {
            // Regression gate for step 1 of multi-group navigation: widening the team
            // dimension of the partitioned buffers must not move a two-team battle. A kernel
            // that still hard-codes bucket 0/1 instead of indexing by the agent's own teamId
            // looks for the enemy in the wrong segment once the layout is wider, and this
            // diverges on the first frame anyone acquires a target.
            // Long enough for two attack intervals (12.5 frames each), so the cooldown
            // path is exercised too and the comparison is not decided by a single strike.
            const int frames = 40;
            const int widenedTeamCount = 5;

            int[] baselineHp = new int[fixtureTotalAgents];
            Vector2[] baselinePositions = new Vector2[fixtureTotalAgents];
            int[] baselineTargets = new int[fixtureTotalAgents];
            AllocateFixtureBuffers(MassGpuBufferManager.DefaultTeamCount);
            yield return RunFixtureBattle(frames, baselineHp, baselinePositions, baselineTargets);

            // Without damage an identical rerun would prove nothing: reading the team
            // buckets is what the enemy sweep does, and it only shows up in the readback
            // once somebody has been hit. Damage, not death - fixture hp takes ten strikes.
            int baselineDamaged = 0;
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                if (baselineHp[i] < initialHp[i])
                    baselineDamaged++;
            }
            Assert.That(baselineDamaged, Is.GreaterThan(0), "baseline battle dealt no damage to compare against");

            int[] widenedHp = new int[fixtureTotalAgents];
            Vector2[] widenedPositions = new Vector2[fixtureTotalAgents];
            int[] widenedTargets = new int[fixtureTotalAgents];
            AllocateFixtureBuffers(widenedTeamCount);
            Assert.That(buffers.TeamCount, Is.EqualTo(widenedTeamCount), "fixture rebuild did not widen the team dimension");
            yield return RunFixtureBattle(frames, widenedHp, widenedPositions, widenedTargets);

            CollectionAssert.AreEqual(baselineHp, widenedHp, "hp diverged after widening teamCount");
            CollectionAssert.AreEqual(baselineTargets, widenedTargets, "target selection diverged after widening teamCount");
            for (int i = 0; i < fixtureTotalAgents; i++)
                Assert.That(widenedPositions[i], Is.EqualTo(baselinePositions[i]), "agent " + i + " position diverged after widening teamCount");
        }

        [UnityTest]
        public IEnumerator EveryNonSelfTeamIsHostile()
        {
            // Step 3 of multi-group navigation: the enemy sweep walks every bucket except the
            // agent's own, instead of the single "opposite" bucket it used to pick. Nobody ever
            // scanned team 2's bucket before, so a third army was invisible - it took zero
            // damage while shooting the two original teams freely.
            const int frames = 40;
            const int thirdTeamId = 2;
            const int thirdArmyStart = AttackerCount + DefenderCount / 2;

            // The back half of the defenders defects to a third army. Every team holds, so this
            // measures hostility alone: SetUp parks the lines 1m apart, inside the attack range
            // a holding stance acquires on, and nobody moves to muddy the comparison.
            for (int i = thirdArmyStart; i < TotalAgents; i++)
                initialTeamIds[i] = thirdTeamId;
            fixtureTeamStances = new[] { (int)TeamStance.Hold, (int)TeamStance.Hold, (int)TeamStance.Hold };

            AllocateFixtureBuffers(thirdTeamId + 1);
            int[] hp = new int[fixtureTotalAgents];
            Vector2[] positions = new Vector2[fixtureTotalAgents];
            int[] targets = new int[fixtureTotalAgents];
            yield return RunFixtureBattle(frames, hp, positions, targets);

            // Outbound: the third army sees the other two. Per agent, because every one of them
            // has an enemy within attack range.
            for (int i = thirdArmyStart; i < TotalAgents; i++)
            {
                Assert.That(targets[i], Is.GreaterThanOrEqualTo(0), "third-army agent " + i + " found no enemy at all");
                Assert.That(initialTeamIds[targets[i]], Is.Not.EqualTo(thirdTeamId),
                    "third-army agent " + i + " targeted its own team " + targets[i]);
            }

            // Inbound: somebody sweeps the third army's bucket. Counted over the team rather
            // than asserted per agent - which enemy an attacker settles on is decided by the
            // selection score and engagement slots, and this test is not about that split.
            int targetingThirdArmy = 0;
            int thirdArmyDamaged = 0;
            for (int i = 0; i < AttackerCount; i++)
            {
                if (targets[i] >= 0 && initialTeamIds[targets[i]] == thirdTeamId)
                    targetingThirdArmy++;
            }
            for (int i = thirdArmyStart; i < TotalAgents; i++)
            {
                if (hp[i] < initialHp[i])
                    thirdArmyDamaged++;
            }
            Assert.That(targetingThirdArmy, Is.GreaterThan(0), "no team-0 agent ever targeted the third army: its bucket was never swept");
            Assert.That(thirdArmyDamaged, Is.GreaterThan(0), "the third army took no damage: it was hostile to others but invisible to them");

            int originalTeamsDamaged = 0;
            for (int i = 0; i < thirdArmyStart; i++)
            {
                if (hp[i] < initialHp[i])
                    originalTeamsDamaged++;
            }
            Assert.That(originalTeamsDamaged, Is.GreaterThan(0), "the two original teams stopped fighting once a third one existed");
        }

        [UnityTest]
        public IEnumerator SwappingTeamStancesSwapsWhichArmyHolds()
        {
            // Stance used to be one uniform meaning "the defender holds"; it is now one entry
            // per teamId. Swapping the two entries has to swap which army stays put - a shader
            // that still keys the hold branch off defenderTeamId pins team 1 in both runs.
            const int frames = 30;

            // 5m apart: outside attack range (3m), inside acquire radius (8m). An advancing team
            // acquires at that distance and closes in; a holding team acquires on attack range
            // alone, so it neither targets nor moves. Separation and density steering are off in
            // this fixture, so "holding" means no displacement at all, not merely a slow drift.
            for (int i = 0; i < TotalAgents; i++)
            {
                bool attacker = initialTeamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                initialAgents[i].position = new Vector3(attacker ? -2.5f : 2.5f, 0f, lane * 1.5f);
                initialAgents[i].velocity = Vector3.zero;
            }

            Vector2[] attackerAdvances = new Vector2[fixtureTotalAgents];
            fixtureTeamStances = new[] { (int)TeamStance.Advance, (int)TeamStance.Hold };
            yield return RunFixtureBattle(frames, new int[fixtureTotalAgents], attackerAdvances, new int[fixtureTotalAgents]);
            AssertTeamDisplacement(attackerAdvances, teamId: 0, expectMoved: true);
            AssertTeamDisplacement(attackerAdvances, teamId: 1, expectMoved: false);

            Vector2[] defenderAdvances = new Vector2[fixtureTotalAgents];
            fixtureTeamStances = new[] { (int)TeamStance.Hold, (int)TeamStance.Advance };
            yield return RunFixtureBattle(frames, new int[fixtureTotalAgents], defenderAdvances, new int[fixtureTotalAgents]);
            AssertTeamDisplacement(defenderAdvances, teamId: 0, expectMoved: false);
            AssertTeamDisplacement(defenderAdvances, teamId: 1, expectMoved: true);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the GPU buffers for the current scenario with an explicit team count.
        /// The rig (scenario, registry, shader set) is reused; only the buffer layout and
        /// the orchestrator bound to it are recreated.
        /// </summary>
        private void AllocateFixtureBuffers(int teamCount)
        {
            if (buffers != null)
                buffers.ReleaseAll();

            buffers = new MassGpuBufferManager();
            orchestrator = new ComputePipelineOrchestrator(shaderSet, buffers);
            buffers.Allocate(fixtureTotalAgents, 64, gridMaxAgentsPerCell, 16, 16, registry.UnitTypeCount, teamCount);
            registry.InitializeAll(buffers, orchestrator);
        }

        /// <summary>Runs a fixed number of combat frames from the pristine fixture state, then reads the result back.</summary>
        private IEnumerator RunFixtureBattle(int frames, int[] hp, Vector2[] positions, int[] targets)
        {
            ResetBattlefield();
            projectileSimulationTime = 0f;

            for (int frame = 0; frame < frames; frame++)
                DispatchOneFrame(battleStarted: true);
            yield return null;

            buffers.combatBuffers.hpReadBuffer.GetData(hp);
            buffers.agentPositionReadBuffer.GetData(positions);
            buffers.combatBuffers.targetAgentIndexBuffer.GetData(targets);
        }

        private void BuildScenario(
            int attackerCount,
            int defenderCount,
            float attackerProjectileRange = 0f,
            float attackerProjectileSpeed = 0f,
            float attackerProjectileGravity = 0f,
            float attackerProjectileHitRadius = 0f,
            float attackerProjectileMaxLifetime = 0f,
            float defenderProjectileRange = 0f,
            float defenderProjectileSpeed = 0f,
            float defenderProjectileGravity = 0f,
            float defenderProjectileHitRadius = 0f,
            float defenderProjectileMaxLifetime = 0f)
        {
            fixtureAttackerCount = attackerCount;
            fixtureTotalAgents = attackerCount + defenderCount;

            var created = new System.Collections.Generic.List<ScriptableObject>();

            UnitTypeConfig MakeType(int teamId, int count, float projectileRange, float projectileSpeed, float projectileGravity, float projectileHitRadius, float projectileMaxLifetime)
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
                combat.projectileRange = projectileRange;
                combat.projectileSpeed = projectileSpeed;
                combat.projectileGravity = projectileGravity;
                combat.projectileHitRadius = projectileHitRadius;
                combat.projectileMaxLifetime = projectileMaxLifetime;

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
            scenario.unitTypes = new[] {
                MakeType(0, attackerCount, attackerProjectileRange, attackerProjectileSpeed, attackerProjectileGravity, attackerProjectileHitRadius, attackerProjectileMaxLifetime),
                MakeType(1, defenderCount, defenderProjectileRange, defenderProjectileSpeed, defenderProjectileGravity, defenderProjectileHitRadius, defenderProjectileMaxLifetime)
            };
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
            UploadFixtureTeamStances();

            if (battleStarted)
                projectileSimulationTime += FrameDt;

            PipelineFrameContext context = new PipelineFrameContext
            {
                deltaTime = FrameDt,
                // Deterministic frame counter: Time.frameCount does not advance between
                // dispatches issued inside a single editor frame, which would freeze the
                // staggered target-search phase.
                frameIndex = ++dispatchedFrames,
                totalAgentCount = fixtureTotalAgents,
                unitTypeCount = registry.UnitTypeCount,
                agentThreadGroupsX = Mathf.Max(1, (fixtureTotalAgents + 63) / 64),
                gridThreadGroupsX = 1,
                projectileThreadGroupsX = projectileSimulationEnabled && buffers.MaxProjectiles > 0
                    ? Mathf.Max(1, (buffers.MaxProjectiles + 63) / 64)
                    : 0,
                simulationTime = projectileSimulationTime,
                battleStarted = battleStarted,
                combatEnabled = true,
                attackerTeamId = 0,
                defenderTeamId = 1,
                rebuildDensityMap = true,
                densityMapThreadGroupsX = 2,
                densityMapThreadGroupsY = 2,
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
                teamFlows = BuildFixtureTeamFlows(),
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

            if (projectileProcessingEnabled && projectileManager != null)
            {
                projectileManager.ProcessLaunchRequests(
                    buffers.combatBuffers.launchRequestBuffer,
                    buffers.agentPositionReadBuffer,
                    buffers.combatBuffers.targetAgentIndexBuffer,
                    initialUnitTypeIndices,
                    settingsCache,
                    fixtureTotalAgents,
                    projectileSimulationTime);
            }
        }

        private void InitializeProjectileManager()
        {
            if (projectileManager != null)
                projectileManager.Dispose();

            projectileManager = new ProjectileGpuManager();
            projectileManager.Initialize(
                shaderSet.ProjectileShader,
                shaderSet.CombatSimulationShader,
                buffers.projectileBuffer,
                buffers.MaxProjectiles,
                buffers.combatBuffers.launchRequestBuffer,
                fixtureTotalAgents);
            projectileManager.ClearAllProjectiles();
            projectileSimulationEnabled = true;
            projectileProcessingEnabled = true;
            projectileSimulationTime = 0f;
            activeListLaunchCursor = -1;
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

        // ------------------------------------------------------------------
        // Projectile System Tests (Ranged Weapon System - Stage 7)
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator RangedUnitLaunchesProjectileOnCooldown()
        {
            // Requirement 4.3, 5.1: the complete GPU-request -> async readback -> pool
            // upload path creates a projectile after the ranged cooldown elapses.
            BuildRangedScenario(attackerRanged: true, defenderRanged: false);

            for (int frame = 0; frame < 120 && projectileManager.TotalLaunched == 0; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
            }

            Assert.Greater(projectileManager.TotalLaunched, 0,
                "the manager never consumed a ranged launch request and uploaded a projectile");
        }

        [UnityTest]
        public IEnumerator ProjectileHitsTargetAndDealsDamage()
        {
            // Requirement 3.3, 3.4, 3.5, 10.3: projectile travels, detects collision,
            // accumulates damage, and is destroyed on hit.
            // 6f, not 10f: BuildScenario arms every type with targetAcquireRadius = 8f, so a
            // wider gap acquires no target, nothing is ever launched, and the test used to
            // fail on "no damage" while the launch path itself was fine.
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 6f);

            int[] initialHpSnapshot = (int[])initialHp.Clone();
            bool anyDamage = false;
            int[] currentHp = new int[fixtureTotalAgents];
            for (int frame = 0; frame < 180 && !anyDamage; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;

                buffers.combatBuffers.hpReadBuffer.GetData(currentHp);
                for (int i = AttackerCount; i < fixtureTotalAgents; i++)
                {
                    if (currentHp[i] < initialHpSnapshot[i])
                    {
                        anyDamage = true;
                        int damageDealt = initialHpSnapshot[i] - currentHp[i];
                        Assert.GreaterOrEqual(damageDealt, AttackDamage, "damage should be at least AttackDamage");
                        break;
                    }
                }
            }

            Assert.IsTrue(anyDamage, "projectile should have hit at least one defender and dealt damage");
        }

        [UnityTest]
        public IEnumerator ProjectileExpiresByLifetime()
        {
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 100f);
            projectileProcessingEnabled = false;

            ProjectileGpuData projectile = ProjectileGpuData.CreateEmpty();
            projectile.position = new Vector3(-50f, 0f, 0f);
            projectile.velocity = Vector3.zero;
            projectile.targetAgentIndex = AttackerCount;
            projectile.sourceTeamId = 0;
            projectile.launchTime = projectileSimulationTime;
            projectile.maxLifetime = FrameDt * 2f;
            projectile.hitRadius = 0.1f;
            buffers.projectileBuffer.SetData(new[] { projectile }, 0, 0, 1);

            for (int frame = 0; frame < 4; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
            }

            ProjectileGpuData[] result = new ProjectileGpuData[buffers.MaxProjectiles];
            buffers.projectileBuffer.GetData(result);
            Assert.AreEqual(-1, result[0].targetAgentIndex,
                "expired projectile slot was not released by the GPU kernel");
        }

        [UnityTest]
        public IEnumerator PausingBattleFreezesActiveProjectile()
        {
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 100f);
            projectileProcessingEnabled = false;

            ProjectileGpuData projectile = ProjectileGpuData.CreateEmpty();
            projectile.position = new Vector3(-50f, 0f, 0f);
            projectile.velocity = new Vector3(10f, 0f, 0f);
            projectile.targetAgentIndex = AttackerCount;
            projectile.sourceTeamId = 0;
            projectile.launchTime = projectileSimulationTime;
            projectile.maxLifetime = 5f;
            buffers.projectileBuffer.SetData(new[] { projectile }, 0, 0, 1);

            for (int frame = 0; frame < 5; frame++)
            {
                DispatchOneFrame(battleStarted: false);
                yield return null;
            }

            ProjectileGpuData[] result = new ProjectileGpuData[buffers.MaxProjectiles];
            buffers.projectileBuffer.GetData(result);
            Assert.That(result[0].position, Is.EqualTo(projectile.position));
            Assert.AreEqual(projectile.targetAgentIndex, result[0].targetAgentIndex);
        }

        [UnityTest]
        public IEnumerator MeleeUnitsUnaffectedByProjectileSystem()
        {
            // Requirement 9.4: units with projectileRange = 0 use melee logic,
            // and existing melee tests still pass (backward compatibility).
            BuildRangedScenario(attackerRanged: false, defenderRanged: false);

            // Run the same damage test as the original melee test
            int framesToFirstStrike = Mathf.CeilToInt(AttackInterval / FrameDt) + 2;
            for (int frame = 0; frame < framesToFirstStrike; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
            }

            int[] hp = new int[fixtureTotalAgents];
            buffers.combatBuffers.hpReadBuffer.GetData(hp);

            bool anyMeleeDamage = false;
            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                if (hp[i] < initialHp[i])
                {
                    anyMeleeDamage = true;
                    Assert.GreaterOrEqual(initialHp[i] - hp[i], AttackDamage, "melee damage should be at least AttackDamage");
                }
            }

            Assert.IsTrue(anyMeleeDamage, "melee units should deal damage via InterlockedAdd, not projectiles");

            // Verify launchRequestBuffer stays at 0 for melee units (计数器模式)
            int[] launchRequests = new int[fixtureTotalAgents];
            buffers.combatBuffers.launchRequestBuffer.GetData(launchRequests);
            for (int i = 0; i < fixtureTotalAgents; i++)
                Assert.AreEqual(0, launchRequests[i], "melee unit " + i + " should never write launch request");
        }

        // ------------------------------------------------------------------
        // Projectile render contract: the active-index list and its indirect draw args.
        // Pixels are not asserted - the contract that matters is that the instance count
        // equals the GPU's own live-slot count, every frame, including hits and resets.
        // ------------------------------------------------------------------

        private uint ReadProjectileInstanceCount()
        {
            uint[] args = new uint[5];
            buffers.projectileDrawArgsBuffer.GetData(args);
            return args[1];
        }

        /// <summary>
        /// Asserts the rendered instance count and index list agree with the pool itself:
        /// no duplicates, every listed slot actually alive, and the same cardinality.
        /// Called after frames rather than at the end, so the frame a slot is released is
        /// covered - a release must leave the list immediately, which is what the strict
        /// branch below pins down.
        ///
        /// A launch is the one legal skew. ProcessLaunchRequests runs after the compute
        /// dispatch (the fixture mirrors MassEngineManager.Update here), so a slot filled
        /// this frame is only collected on the next one. TotalLaunched tells us exactly
        /// which frames uploaded, and only those are allowed to run a frame behind.
        /// </summary>
        private void AssertActiveListMatchesPool(string context)
        {
            ProjectileGpuData[] pool = new ProjectileGpuData[buffers.MaxProjectiles];
            buffers.projectileBuffer.GetData(pool);

            int expected = 0;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].targetAgentIndex >= 0)
                    expected++;
            }

            uint count = ReadProjectileInstanceCount();
            int launched = projectileManager != null ? projectileManager.TotalLaunched : 0;
            bool uploadedThisFrame = activeListLaunchCursor < 0 || launched != activeListLaunchCursor;
            activeListLaunchCursor = launched;

            if (uploadedThisFrame)
            {
                // Never more than the pool holds - a stale or duplicated slot would show up
                // as an excess instance - but the freshly uploaded ones may still be pending.
                Assert.LessOrEqual((int)count, expected,
                    context + ": indirect instance count exceeds the number of live pool slots");
            }
            else
            {
                Assert.AreEqual(expected, (int)count,
                    context + ": indirect instance count disagrees with the number of live pool slots");
            }

            uint[] indices = new uint[buffers.MaxProjectiles];
            buffers.activeProjectileIndexBuffer.GetData(indices);

            HashSet<uint> seen = new HashSet<uint>();
            for (int i = 0; i < (int)count; i++)
            {
                uint slot = indices[i];
                Assert.Less(slot, (uint)buffers.MaxProjectiles, context + ": active index out of range");
                Assert.IsTrue(seen.Add(slot), context + ": slot " + slot + " appears twice in the active list");
                Assert.GreaterOrEqual(pool[slot].targetAgentIndex, 0,
                    context + ": active list points at idle slot " + slot + ", which would render a stale trail");
            }
        }

        [UnityTest]
        public IEnumerator ActiveProjectileListDrivesNonZeroInstanceCount()
        {
            // 6f, not more: BuildScenario arms every type with targetAcquireRadius = 8f,
            // so a wider gap acquires no target at all and nothing is ever launched.
            // At speed 20 this still leaves ~15 frames of flight to observe.
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 6f);

            uint peak = 0;
            for (int frame = 0; frame < 240 && peak == 0; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;

                AssertActiveListMatchesPool("frame " + frame);
                uint count = ReadProjectileInstanceCount();
                if (count > peak)
                    peak = count;
            }

            Assert.Greater(peak, 0u,
                "projectiles were launched but the indirect draw args never reported a single instance, so nothing would render");
        }

        [UnityTest]
        public IEnumerator ActiveProjectileListDropsSlotOnHit()
        {
            // 6f, not more: BuildScenario arms every type with targetAcquireRadius = 8f,
            // so a wider gap acquires no target at all and nothing is ever launched.
            // At speed 20 this still leaves ~15 frames of flight to observe.
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 6f);

            int[] hpBefore = (int[])initialHp.Clone();
            int[] hp = new int[fixtureTotalAgents];
            bool sawFlight = false;
            bool sawDamage = false;

            for (int frame = 0; frame < 240 && !sawDamage; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;

                // Checked every frame: the frame a hit releases a slot is exactly the frame
                // where a CPU-side count would still be advertising it as renderable.
                AssertActiveListMatchesPool("frame " + frame);
                if (ReadProjectileInstanceCount() > 0)
                    sawFlight = true;

                buffers.combatBuffers.hpReadBuffer.GetData(hp);
                for (int i = AttackerCount; i < fixtureTotalAgents; i++)
                {
                    if (hp[i] < hpBefore[i])
                    {
                        sawDamage = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(sawFlight, "no projectile was ever reported as renderable");
            Assert.IsTrue(sawDamage, "no projectile hit landed, so slot release on hit was never exercised");

            // Keep firing for a while: AssertActiveListMatchesPool is the real assertion
            // here, and it must hold across every launch/hit cycle, not just the first.
            for (int frame = 0; frame < 60; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
                AssertActiveListMatchesPool("sustained frame " + frame);
            }
        }

        [UnityTest]
        public IEnumerator ActiveProjectileListDropsExpiredSlot()
        {
            // Deterministic counterpart to the hit test: one hand-placed projectile with a
            // two-frame lifetime, so the drop from 1 to 0 instances is unambiguous.
            BuildRangedScenario(attackerRanged: false, defenderRanged: false, distance: 100f);
            projectileProcessingEnabled = false;

            ProjectileGpuData projectile = ProjectileGpuData.CreateEmpty();
            projectile.position = new Vector3(-50f, 0f, 0f);
            projectile.velocity = Vector3.zero;
            projectile.targetAgentIndex = AttackerCount;
            projectile.sourceTeamId = 0;
            projectile.launchTime = projectileSimulationTime;
            projectile.maxLifetime = FrameDt * 2f;
            projectile.hitRadius = 0.1f;
            buffers.projectileBuffer.SetData(new[] { projectile }, 0, 0, 1);

            DispatchOneFrame(battleStarted: true);
            yield return null;
            Assert.AreEqual(1u, ReadProjectileInstanceCount(),
                "a freshly placed live projectile was not picked up by the active list");
            AssertActiveListMatchesPool("before expiry");

            for (int frame = 0; frame < 4; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
            }

            Assert.AreEqual(0u, ReadProjectileInstanceCount(),
                "the expired slot is still being drawn");
            AssertActiveListMatchesPool("after expiry");
        }

        [UnityTest]
        public IEnumerator PausedProjectileRenderListStaysStable()
        {
            // Pausing must freeze the visuals, not blank them: the list is rebuilt every
            // frame regardless of battleStarted, so it has to come out identical each time.
            BuildRangedScenario(attackerRanged: false, defenderRanged: false, distance: 100f);
            projectileProcessingEnabled = false;

            int poolSize = buffers.MaxProjectiles;
            Assert.GreaterOrEqual(poolSize, 2, "fixture pool is too small to cover a multi-instance list");

            ProjectileGpuData[] placed = new ProjectileGpuData[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                placed[i] = ProjectileGpuData.CreateEmpty();
                placed[i].position = new Vector3(-50f + i, 1f + i, 0f);
                placed[i].velocity = new Vector3(10f, 0f, 0f);
                placed[i].targetAgentIndex = AttackerCount;
                placed[i].sourceTeamId = i % 2;
                placed[i].launchTime = projectileSimulationTime;
                placed[i].maxLifetime = 60f;
                placed[i].trailLength = 1f;
            }
            buffers.projectileBuffer.SetData(placed);

            uint firstCount = 0;
            for (int frame = 0; frame < 6; frame++)
            {
                DispatchOneFrame(battleStarted: false);
                yield return null;

                uint count = ReadProjectileInstanceCount();
                if (frame == 0)
                    firstCount = count;

                Assert.AreEqual((uint)poolSize, count,
                    "paused frame " + frame + ": every placed projectile must stay renderable while paused");
                Assert.AreEqual(firstCount, count, "paused frame " + frame + ": instance count drifted while paused");
                AssertActiveListMatchesPool("paused frame " + frame);

                ProjectileGpuData[] now = new ProjectileGpuData[poolSize];
                buffers.projectileBuffer.GetData(now);
                for (int i = 0; i < poolSize; i++)
                {
                    Assert.That(now[i].position, Is.EqualTo(placed[i].position),
                        "paused frame " + frame + ": projectile " + i + " moved while paused");
                    Assert.AreEqual(placed[i].targetAgentIndex, now[i].targetAgentIndex,
                        "paused frame " + frame + ": projectile " + i + " changed lifecycle state while paused");
                }
            }
        }

        [UnityTest]
        public IEnumerator ClearingProjectilesEmptiesActiveListAndAllowsRefight()
        {
            // 6f, not more: BuildScenario arms every type with targetAcquireRadius = 8f,
            // so a wider gap acquires no target at all and nothing is ever launched.
            // At speed 20 this still leaves ~15 frames of flight to observe.
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 6f);

            uint before = 0;
            for (int frame = 0; frame < 240 && before == 0; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
                before = ReadProjectileInstanceCount();
            }
            Assert.Greater(before, 0u, "no projectile was renderable before the reset");

            // Stands in for ResetScenario, which clears the pool the same way.
            projectileManager.ClearAllProjectiles();
            DispatchOneFrame(battleStarted: true);
            yield return null;

            Assert.AreEqual(0u, ReadProjectileInstanceCount(),
                "projectiles were cleared but the indirect draw still reports instances, so trails would linger after a reset");
            AssertActiveListMatchesPool("after clear");

            uint after = 0;
            for (int frame = 0; frame < 240 && after == 0; frame++)
            {
                DispatchOneFrame(battleStarted: true);
                yield return null;
                after = ReadProjectileInstanceCount();
                AssertActiveListMatchesPool("refight frame " + frame);
            }
            Assert.Greater(after, 0u, "the battle could not produce renderable projectiles again after a reset");
        }

        [UnityTest]
        public IEnumerator MissingRenderResourcesWarnOnceAndKeepSimulating()
        {
            // 6f, not more: BuildScenario arms every type with targetAcquireRadius = 8f,
            // so a wider gap acquires no target at all and nothing is ever launched.
            // At speed 20 this still leaves ~15 frames of flight to observe.
            BuildRangedScenario(attackerRanged: true, defenderRanged: false, distance: 6f);

            ProjectileRenderConfig config = ScriptableObject.CreateInstance<ProjectileRenderConfig>();
            config.hideFlags = HideFlags.DontSave;
            config.material = null;
            ProjectileGpuRenderDispatcher dispatcher = new ProjectileGpuRenderDispatcher();
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(200f, 120f, 200f));

            // One warning total, however many frames run: a repeated log would drown the
            // console at 60 fps and read as an error loop. Expect proves at least one
            // arrives (an unfulfilled expectation fails at teardown); the counter below
            // proves no second one does. Counting beats LogAssert.NoUnexpectedReceived
            // here, which would also trip over unrelated fixture warnings such as the
            // MovementConfig default notice.
            LogAssert.Expect(LogType.Warning, new Regex("projectile trails skipped"));

            int skipWarnings = 0;
            Application.LogCallback countSkips = (condition, stackTrace, type) =>
            {
                if (type == LogType.Warning && condition.Contains("projectile trails skipped"))
                    skipWarnings++;
            };
            Application.logMessageReceived += countSkips;

            try
            {
                uint peak = 0;
                for (int frame = 0; frame < 240; frame++)
                {
                    DispatchOneFrame(battleStarted: true);
                    dispatcher.Draw(config, buffers, bounds, attackerTeamId: 0);
                    yield return null;

                    AssertActiveListMatchesPool("unconfigured frame " + frame);
                    uint count = ReadProjectileInstanceCount();
                    if (count > peak)
                        peak = count;
                }

                Assert.Greater(peak, 0u,
                    "the simulation stalled when render resources were missing; visuals are optional, physics is not");
            }
            finally
            {
                Application.logMessageReceived -= countSkips;
                dispatcher.Release();
                Object.DestroyImmediate(config);
            }

            Assert.AreEqual(1, skipWarnings,
                "the missing-material warning must be logged exactly once, not once per frame");
        }

        // PLACEHOLDER_RENDER_TESTS

        private void BuildRangedScenario(bool attackerRanged, bool defenderRanged, float distance = 1f)
        {
            // Rebuild scenario with ranged weapon config
            float attackerProjectileRange = attackerRanged ? 50f : 0f;
            float defenderProjectileRange = defenderRanged ? 50f : 0f;

            BuildScenario(AttackerCount, DefenderCount,
                attackerProjectileRange: attackerProjectileRange,
                attackerProjectileSpeed: 20f,
                attackerProjectileGravity: 0f,
                attackerProjectileHitRadius: 0.5f,
                attackerProjectileMaxLifetime: 5f,
                defenderProjectileRange: defenderProjectileRange,
                defenderProjectileSpeed: 20f,
                defenderProjectileGravity: 0f,
                defenderProjectileHitRadius: 0.5f,
                defenderProjectileMaxLifetime: 5f);

            buffers.ReleaseAll();
            buffers.Allocate(fixtureTotalAgents, 64, 16, 16, 16, registry.UnitTypeCount);
            registry.InitializeAll(buffers, orchestrator);

            AgentData[] agents = new AgentData[fixtureTotalAgents];
            int[] teamIds = new int[fixtureTotalAgents];
            int[] hp = new int[fixtureTotalAgents];
            int[] unitTypeIndices = new int[fixtureTotalAgents];
            registry.GenerateAgents(agents);
            registry.FillCombatArrays(teamIds, hp, unitTypeIndices);

            for (int i = 0; i < fixtureTotalAgents; i++)
            {
                bool attacker = teamIds[i] == 0;
                int lane = attacker ? i : i - AttackerCount;
                agents[i].position = new Vector3(attacker ? -distance * 0.5f : distance * 0.5f, 0f, lane * 1.5f);
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
            InitializeProjectileManager();
        }
    }
}
#endif
