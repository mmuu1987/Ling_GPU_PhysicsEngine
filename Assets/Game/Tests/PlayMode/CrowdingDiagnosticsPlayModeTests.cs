#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MassEngine.Game.Tests
{
    // Explicit, instrumented observation. Passing means capture validity, NOT that crowding is fixed.
    public class CrowdingDiagnosticsPlayModeTests
    {
        [Serializable] public class Sample
        {
            public string phase, variant, battlePhase, csv, image;
            public int repeat, tick, alive, team0Alive, attacking, waiting, nominalPairs, softPairs, deepPairs, sameTeamSoftPairs, enemySoftPairs, nominalAgents, deepAgents, maxCellPopulation, overflowCells;
            public float seconds, fixedDt, minDistance, nearestRatioP05, nearestRatioP50, nearestRatioP95, meanSpeed, maxSpeed, team0GoalDistance;
        }
        [Serializable] public class Evidence
        {
            public string scene, graphics, unity, notes;
            public bool captureComplete, productionChanged, performanceQualified;
            public int population;
            public List<Sample> samples = new List<Sample>();
        }
        private Scene scene;
        private MassEngineManager manager;
        private WarSandboxBattleController controller;
        private Camera capture;
        private GameObject cameraOwner;
        private RenderTexture target;
        private AgentData[] agents;
        private int[] teams, types, hp, congestion, grid;
        private UnitTypeGpuSettings[] settings;
        private float previousCaptureDelta, previousTimeScale;
        private Evidence evidence;
        private string variant = "baseline";
        private ScenarioConfig officialScenario;
        private MassEngineSystemConfig officialSystem;
        private MassEngineShaderConfig officialShaders;
        private string officialLodJson;
        private Vector3 fixedLodPosition;
        private UnitTypeGpuSettings[] officialSettings;
        private readonly List<Object> runtimeOwned = new List<Object>();
        private string officialFlockingJson;
        private void ReleaseExperiment()
        {
            if (manager == null) return;
            manager.PauseBattle(); manager.Release(); manager.scenarioConfig = officialScenario; manager.systemConfig = officialSystem; manager.shaderConfig = officialShaders;
            foreach (var o in runtimeOwned) { Assert.That(AssetDatabase.GetAssetPath(o), Is.Empty); Object.DestroyImmediate(o); }
            runtimeOwned.Clear();
        }
        private void UseVariant(string name)
        {
            ReleaseExperiment(); variant = name;
            // Render-only qualification repair; both conditions use identical LOD cadence/center.
            var system = Object.Instantiate(officialSystem); runtimeOwned.Add(system);
            var lod = Object.Instantiate(officialSystem.lodConfig); runtimeOwned.Add(lod);
            lod.enableFrustumCulling = false; lod.maxRenderDistance = 0;
            system.lodConfig = lod; manager.systemConfig = system;
            var expectedLod = Object.Instantiate(lod); expectedLod.enableFrustumCulling = officialSystem.lodConfig.enableFrustumCulling;
            expectedLod.maxRenderDistance = officialSystem.lodConfig.maxRenderDistance;
            Assert.That(EditorJsonUtility.ToJson(expectedLod).Replace(expectedLod.name, officialSystem.lodConfig.name), Is.EqualTo(officialLodJson));
            Object.DestroyImmediate(expectedLod);
            if (name == "reference-24")
            {
                var scenario = Object.Instantiate(officialScenario); runtimeOwned.Add(scenario);
                scenario.unitTypes = new UnitTypeConfig[officialScenario.unitTypes.Length];
                for (int i = 0; i < scenario.unitTypes.Length; i++)
                {
                    var unit = Object.Instantiate(officialScenario.unitTypes[i]); runtimeOwned.Add(unit);
                    var flock = Object.Instantiate(unit.flockingConfig); runtimeOwned.Add(flock); flock.separationStrength = 24;
                    unit.flockingConfig = flock; scenario.unitTypes[i] = unit;
                }
                manager.scenarioConfig = scenario;
            }
            else Assert.That(name, Is.EqualTo("official-48"));
            Assert.That(manager.shaderConfig, Is.SameAs(officialShaders), "No wait-cap shader probe may enter this adopted configuration run.");
            controller.ResetBattle(); manager.Buffers.unitTypeSettingsBuffer.GetData(settings);
            for (int i = 0; i < settings.Length; i++)
            {
                var expected = officialSettings[i]; if (name == "reference-24") expected.separationStrength = 24;
                Assert.That(JsonUtility.ToJson(settings[i]), Is.EqualTo(JsonUtility.ToJson(expected)));
            }
            Assert.That(manager.systemConfig.simulationConfig.cellSize, Is.EqualTo(officialSystem.simulationConfig.cellSize));
            Assert.That(manager.Buffers.MaxAgentsPerCell, Is.EqualTo(officialSystem.simulationConfig.maxAgentsPerCell));
            File.AppendAllText(Path.Combine(output, "variants.txt"), name + " capacity=" + manager.Buffers.MaxAgentsPerCell + " settings=" + string.Join(";", settings.Select(x => JsonUtility.ToJson(x))) + "\n");
        }
        private string output;
        private static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
        private static float Bits(int x) => BitConverter.ToSingle(BitConverter.GetBytes(x), 0);
        private static float Quantile(float[] a, float q) => a.Length == 0 ? 0 : a[Mathf.Clamp(Mathf.RoundToInt((a.Length - 1) * q), 0, a.Length - 1)];
        [UnitySetUp] public IEnumerator Setup()
        {
            const string prefix = "--interaction-p3-output=";
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            previousCaptureDelta = Time.captureDeltaTime; previousTimeScale = Time.timeScale;
            if (arg == null) Assert.Ignore("Diagnostic is opt-in; requires P3 isolation. Normal test runs do not run this experiment.");
            output = Path.Combine(arg.Substring(prefix.Length), "crowding-evidence"); Directory.CreateDirectory(output);
            Assert.That(WarSandboxSceneSession.Instance, Is.Null);
            previousCaptureDelta = Time.captureDeltaTime; previousTimeScale = Time.timeScale;
            Time.captureDeltaTime = 1f / 60f; Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("Assets/Game/Scenes/Green.unity", LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath("Assets/Game/Scenes/Green.unity"); yield return null; yield return null;
            manager = WarSandboxSceneSession.FindManager(scene, out _); Assert.That(manager, Is.Not.Null);
            controller = WarSandboxRuntimeBootstrap.EnsureControls(manager); controller.RebuildArmyStates(); manager.PauseBattle();
            Assert.That(manager.Buffers.AgentCount, Is.EqualTo(2048)); Assert.That(new WarSandboxUnitStatStore().Current.IsEmpty, Is.True);
            officialScenario = manager.scenarioConfig; officialSystem = manager.systemConfig; officialShaders = manager.shaderConfig;
            officialLodJson = EditorJsonUtility.ToJson(officialSystem.lodConfig);
            Assert.That(manager.lodCenter, Is.Not.Null); fixedLodPosition = manager.lodCenter.position;
            officialFlockingJson = string.Join(";", officialScenario.unitTypes.Select(x => EditorJsonUtility.ToJson(x.flockingConfig)));
            var b = manager.Buffers; agents = new AgentData[b.AgentCount]; teams = new int[b.AgentCount]; types = new int[b.AgentCount]; hp = new int[b.AgentCount];
            settings = new UnitTypeGpuSettings[b.UnitTypeCount]; b.unitTypeSettingsBuffer.GetData(settings); officialSettings = (UnitTypeGpuSettings[])settings.Clone();
            Assert.That(officialSettings.All(x => Mathf.Approximately(x.separationStrength, 48)), Is.True, "Verify saved official configuration reaches actual GPU, not only a runtime stronger clone.");
            b.combatBuffers.teamIdBuffer.GetData(teams); b.unitTypeIndexBuffer.GetData(types);
            Assert.That(settings.All(x => Mathf.Approximately(x.agentRadius, .55f)), Is.True);
            evidence = new Evidence { scene = scene.path, graphics = SystemInfo.graphicsDeviceName, unity = Application.unityVersion, population = b.AgentCount,
                notes = "User-adopted official separationStrength 48 versus runtime-only old-value reference 24. Both conditions retain radius, scale, count, capacity, damage and original combat shader. Both disable ONLY render frustum/distance rejection in cloned LOD config; simulation cadence and center are unchanged. Fixed 60Hz synchronous capture is NOT performance. Battle-duration/survivor tradeoff explicitly accepted, not a claim of zero side effects." };
            cameraOwner = new GameObject("P3 diagnostic capture only"); capture = cameraOwner.AddComponent<Camera>();
            if (manager.cullingCamera != null) capture.CopyFrom(manager.cullingCamera);
            capture.enabled = false; capture.orthographic = true; capture.orthographicSize = 34; capture.nearClipPlane = .1f; capture.farClipPlane = 1200;
            if (!Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Any(x => x.enabled)) cameraOwner.AddComponent<AudioListener>();
            target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); target.Create(); capture.targetTexture = target; capture.aspect = 1920f / 1080;
            var metadata = new StringBuilder();
            metadata.AppendLine("simulation=" + JsonUtility.ToJson(manager.systemConfig.simulationConfig));
            metadata.AppendLine("runtimeFlow=" + JsonUtility.ToJson(manager.systemConfig.runtimeFlowConfig));
            metadata.AppendLine("manager=" + JsonUtility.ToJson(manager));
            for (int i = 0; i < settings.Length; i++)
            {
                var u = manager.scenarioConfig.unitTypes[i]; metadata.AppendLine("unit=" + AssetDatabase.GetAssetPath(u));
                metadata.AppendLine("gpu=" + JsonUtility.ToJson(settings[i])); metadata.AppendLine("spawn=" + JsonUtility.ToJson(u.spawnConfig));
                metadata.AppendLine("flocking=" + JsonUtility.ToJson(u.flockingConfig)); metadata.AppendLine("render=" + AssetDatabase.GetAssetPath(u.renderConfig));
            }
            File.WriteAllText(Path.Combine(output, "configuration.txt"), metadata.ToString());
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            Time.captureDeltaTime = previousCaptureDelta; Time.timeScale = previousTimeScale;
            if (capture != null) capture.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (cameraOwner != null) Object.DestroyImmediate(cameraOwner);
            if (officialScenario != null)
            {
                Assert.That(string.Join(";", officialScenario.unitTypes.Select(x => EditorJsonUtility.ToJson(x.flockingConfig))), Is.EqualTo(officialFlockingJson));
                Assert.That(EditorJsonUtility.ToJson(officialSystem.lodConfig), Is.EqualTo(officialLodJson));
                ReleaseExperiment();
            }
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }
        private void Focus(Vector3 focus)
        {
            if (controller.TryResolveGroundPoint(focus, out var ground, out _)) focus = ground;
            capture.transform.position = focus + new Vector3(0, 70, -50); capture.transform.LookAt(focus);
            File.AppendAllText(Path.Combine(output, "cameras.txt"), "focus=" + focus.ToString("R") + " position=" + capture.transform.position.ToString("R") + " rotation=" + capture.transform.eulerAngles.ToString("R") + " orthographicSize=34\n");
        }
        private void Photograph(string name)
        {
            capture.Render(); var previous = RenderTexture.active; var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target; tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); tex.Apply();
                var pixels = tex.GetPixels32(); Assert.That(pixels.Count(p => p.r + p.g + p.b > 20), Is.GreaterThan(pixels.Length / 2));
                File.WriteAllBytes(Path.Combine(output, name), tex.EncodeToPNG());
                int orange = pixels.Count(p => p.r > 150 && p.g > 50 && p.r > p.g * 1.3f && p.b < p.g * .4f);
                File.AppendAllText(Path.Combine(output, "subject-pixels.csv"), name + "," + orange + "\n");
                Assert.That(orange, Is.GreaterThan(500), "Green-scene diagnostic subject absent: terrain-only screenshots are NOT valid.");
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(tex); }
        }
        private void Read(string phase, int repeat, int tick, Vector3 goal, bool photograph)
        {
            Assert.That(manager.lodCenter.position, Is.EqualTo(fixedLodPosition));
            var b = manager.Buffers; b.agentBuffer.GetData(agents); b.combatBuffers.hpReadBuffer.GetData(hp);
            congestion = new int[b.combatBuffers.engagementSlotAssignmentBuffer.count]; b.combatBuffers.engagementSlotAssignmentBuffer.GetData(congestion);
            grid = new int[b.gridCountsBuffer.count]; b.gridCountsBuffer.GetData(grid);
            var current = new UnitTypeGpuSettings[settings.Length]; b.unitTypeSettingsBuffer.GetData(current);
            for (int i = 0; i < settings.Length; i++) Assert.That(JsonUtility.ToJson(current[i]), Is.EqualTo(JsonUtility.ToJson(settings[i])));
            Assert.That(agents.All(a => a.scale == Vector3.one), Is.True); Assert.That(b.AgentCount, Is.EqualTo(2048));
            var alive = Enumerable.Range(0, agents.Length).Where(i => hp[i] > 0 && agents[i].currentState != (int)AgentState.Dead).ToArray();
            var nearest = new float[agents.Length]; for (int i = 0; i < nearest.Length; i++) nearest[i] = float.MaxValue;
            var touched = new bool[agents.Length]; var deep = new bool[agents.Length];
            var row = new Sample { phase = phase, variant = variant, repeat = repeat, tick = tick, seconds = tick / 60f, fixedDt = Time.deltaTime, battlePhase = controller.Phase.ToString(), alive = alive.Length, minDistance = float.MaxValue,
                csv = variant + "-" + phase + "-r" + repeat + "-f" + tick + ".csv" };
            foreach (int i in alive)
            {
                var a = agents[i]; Assert.That(float.IsNaN(a.position.x) || float.IsNaN(a.position.z) || float.IsInfinity(a.position.x) || float.IsInfinity(a.position.z), Is.False);
                float speed = new Vector2(a.velocity.x, a.velocity.z).magnitude; row.meanSpeed += speed; row.maxSpeed = Mathf.Max(row.maxSpeed, speed);
                if (a.currentState == (int)AgentState.Attack) row.attacking++;
                if (Bits(congestion[agents.Length + i * 12 + 5]) > 0) row.waiting++;
                if (teams[i] == 0) { row.team0Alive++; row.team0GoalDistance += Vector2.Distance(new Vector2(a.position.x, a.position.z), new Vector2(goal.x, goal.z)); }
            }
            for (int a = 0; a < alive.Length; a++) for (int c = a + 1; c < alive.Length; c++)
            {
                int i = alive[a], j = alive[c]; float dx = agents[i].position.x - agents[j].position.x, dz = agents[i].position.z - agents[j].position.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz); float ratio = distance / (settings[types[i]].agentRadius + settings[types[j]].agentRadius);
                row.minDistance = Mathf.Min(row.minDistance, distance); nearest[i] = Mathf.Min(nearest[i], ratio); nearest[j] = Mathf.Min(nearest[j], ratio);
                if (ratio < 1) { row.nominalPairs++; touched[i] = true; touched[j] = true; }
                if (ratio < .9f) { row.softPairs++; if (teams[i] == teams[j]) row.sameTeamSoftPairs++; else row.enemySoftPairs++; }
                if (ratio < .5f) { row.deepPairs++; deep[i] = true; deep[j] = true; }
            }
            var ratios = alive.Select(i => nearest[i]).Where(x => x < float.MaxValue).OrderBy(x => x).ToArray();
            row.nearestRatioP05 = Quantile(ratios, .05f); row.nearestRatioP50 = Quantile(ratios, .5f); row.nearestRatioP95 = Quantile(ratios, .95f);
            row.nominalAgents = touched.Count(x => x); row.deepAgents = deep.Count(x => x); row.meanSpeed /= Mathf.Max(1, row.alive); row.team0GoalDistance /= Mathf.Max(1, row.team0Alive);
            row.maxCellPopulation = grid.Max(); row.overflowCells = grid.Count(x => x > b.MaxAgentsPerCell);
            using (var csv = new StreamWriter(Path.Combine(output, row.csv)))
            {
                csv.WriteLine("id,team,type,hp,state,x,y,z,vx,vz,radius,scaleX,scaleY,scaleZ,waitSeconds,observedSeconds");
                for (int i = 0; i < agents.Length; i++)
                {
                    var a = agents[i]; csv.WriteLine(string.Join(",", i, teams[i], types[i], hp[i], a.currentState, F(a.position.x), F(a.position.y), F(a.position.z), F(a.velocity.x), F(a.velocity.z), F(settings[types[i]].agentRadius), F(a.scale.x), F(a.scale.y), F(a.scale.z), F(Bits(congestion[agents.Length + i * 12 + 5])), F(Bits(congestion[agents.Length + i * 12 + 4]))));
                }
            }
            if (photograph) { row.image = Path.ChangeExtension(row.csv, ".png"); Photograph(row.image); }
            evidence.samples.Add(row); File.WriteAllText(Path.Combine(output, "evidence.json"), JsonUtility.ToJson(evidence, true)); Debug.Log("P3_CROWD_SAMPLE " + JsonUtility.ToJson(row));
        }
        private IEnumerator Observe(string phase, int repeat, int[] ticks, Vector3 goal)
        {
            int last = 0;
            foreach (int tick in ticks)
            {
                for (int frame = last; frame < tick; frame++)
                {
                    yield return null;
                    if (!manager.IsBattleRunning) { Read(phase + "-ended", repeat, frame + 1, goal, false); yield break; }
                }
                Read(phase, repeat, tick, goal, repeat == 0 && (phase == "move" && tick == 1800 || phase == "contact" && tick == 2400)); last = tick;
            }
        }
        [Serializable] public class P3HoldMotion
        {
            public string variant, binarySchema="1200 records: int32 frame then 64*[float32 x,z,vx,vz,int32 presentationState]; little endian";
            public int repeat, frames, population=2048, movingTeamPopulation=1024, reverseEvents, movingPresentationAgentFrames;
            public int[] trackedIds;
            public float seconds=20, fixedDt=1f/60f, meanSpeed, maxStep, maxSpeed;
            public bool passed;
        }
        [UnityTest, Timeout(600000)] public IEnumerator P3PostMoveHoldMotion24And48()
        {
            foreach(string condition in new[]{"reference-24","official-48"})
            {
                UseVariant(condition);
                for(int repeat=0;repeat<2;repeat++)
                {
                    controller.ResetBattle();yield return null;
                    Assert.IsTrue(controller.IssueOrder(ArmyOrder.Hold(1)));Assert.IsTrue(controller.IssueMoveOrder(0,new Vector3(-20,0,0),false));controller.StartOrResumeBattle();
                    for(int f=0;f<1800;f++)yield return null;
                    Read("motion-before-hold",repeat,1800,new Vector3(-20,0,0),false);
                    Assert.IsTrue(controller.IssueOrder(ArmyOrder.Hold(0)));
                    for(int f=0;f<120;f++)yield return null;
                    var ids=Enumerable.Range(0,agents.Length).Where(i=>teams[i]==0).ToArray();Assert.AreEqual(1024,ids.Length);
                    manager.Buffers.agentBuffer.GetData(agents);
                    var previous=agents.Select(x=>new Vector2(x.position.x,x.position.z)).ToArray();var previousVelocity=agents.Select(x=>new Vector2(x.velocity.x,x.velocity.z)).ToArray();
                    var r=new P3HoldMotion{variant=condition,repeat=repeat,trackedIds=ids.OrderBy(i=>Vector2.Distance(previous[i],new Vector2(-20,0))).Take(64).ToArray()};
                    string stem=Path.Combine(output,condition+"-hold-motion-r"+repeat);
                    using(var binary=new BinaryWriter(new System.IO.Compression.GZipStream(File.Create(stem+".bin.gz"),System.IO.Compression.CompressionLevel.Optimal)))
                    using(var csv=new StreamWriter(stem+".csv"))
                    {
                        csv.WriteLine("frame,meanSpeed,p95Speed,maxStep,reverseEvents,movingPresentation");
                        for(int frame=0;frame<1200;frame++)
                        {
                            yield return null;manager.Buffers.agentBuffer.GetData(agents);Assert.IsTrue(manager.IsBattleRunning);
                            var speeds=new float[ids.Length];float maxStep=0,sum=0;int reverse=0,moving=0;
                            for(int k=0;k<ids.Length;k++)
                            {
                                int i=ids[k];var agent=agents[i];var point=new Vector2(agent.position.x,agent.position.z);var velocity=new Vector2(agent.velocity.x,agent.velocity.z);
                                Assert.IsFalse(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsInfinity(point.x)||float.IsInfinity(point.y));Assert.AreNotEqual((int)AgentState.Dead,agent.currentState);
                                float step=Vector2.Distance(point,previous[i]);maxStep=Mathf.Max(maxStep,step);speeds[k]=velocity.magnitude;sum+=speeds[k];
                                if(velocity.magnitude>.05f && previousVelocity[i].magnitude>.05f && Vector2.Dot(velocity.normalized,previousVelocity[i].normalized)<-.5f)reverse++;
                                if(agent.presentationState==(int)AgentState.Move)moving++;
                                previous[i]=point;previousVelocity[i]=velocity;
                            }
                            Array.Sort(speeds);r.meanSpeed+=sum/ids.Length;r.maxStep=Mathf.Max(r.maxStep,maxStep);r.maxSpeed=Mathf.Max(r.maxSpeed,speeds[speeds.Length-1]);r.reverseEvents+=reverse;r.movingPresentationAgentFrames+=moving;r.frames++;
                            Assert.Less(maxStep,.25f,"Hold-motion window contains a jump larger than 0.25m per 60Hz step.");
                            csv.WriteLine(frame+","+F(sum/ids.Length)+","+F(speeds[(int)(speeds.Length*.95f)])+","+F(maxStep)+","+reverse+","+moving);
                            binary.Write(frame);
                            foreach(int i in r.trackedIds){var x=agents[i];binary.Write(x.position.x);binary.Write(x.position.z);binary.Write(x.velocity.x);binary.Write(x.velocity.z);binary.Write(x.presentationState);}
                        }
                    }
                    r.meanSpeed/=r.frames;r.passed=true;File.WriteAllText(stem+".json",JsonUtility.ToJson(r,true));
                    Read("motion-after-hold",repeat,3120,new Vector3(-20,0,0),false);manager.PauseBattle();
                }
            }
            evidence.productionChanged=true;evidence.captureComplete=true;File.WriteAllText(Path.Combine(output,"evidence.json"),JsonUtility.ToJson(evidence,true));
        }

        [UnityTest, Timeout(600000)] public IEnumerator AdoptedOfficialSeparationAndOldReferenceAreMeasuredOnRealGpu()
        {
            foreach (string condition in new[] { "reference-24", "official-48" })
            {
            UseVariant(condition);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                controller.ResetBattle(); yield return null; Focus(new Vector3(-128, 0, 0)); Read("initial", repeat, 0, new Vector3(-128, 0, 0), repeat == 0);
                Assert.That(controller.IssueOrder(ArmyOrder.Hold(0)), Is.True); Assert.That(controller.IssueOrder(ArmyOrder.Hold(1)), Is.True);
                controller.StartOrResumeBattle(); Assert.That(manager.IsBattleRunning, Is.True);
                yield return Observe("hold", repeat, new[] { 60, 180 }, new Vector3(-128, 0, 0));
                controller.ResetBattle(); yield return null; Focus(new Vector3(-20, 0, 0));
                Assert.That(controller.IssueOrder(ArmyOrder.Hold(1)), Is.True); Assert.That(controller.IssueMoveOrder(0, new Vector3(-20, 0, 0), false), Is.True);
                controller.StartOrResumeBattle(); Assert.That(manager.IsBattleRunning, Is.True);
                yield return Observe("move", repeat, new[] { 300, 600, 1200, 1800 }, new Vector3(-20, 0, 0));
                controller.ResetBattle(); yield return null; Focus(Vector3.zero); Assert.That(controller.StartDefaultBattle(), Is.True);
                yield return Observe("contact", repeat, new[] { 600, 1200, 1800, 2400, 3600, 4800 }, Vector3.zero);
                manager.PauseBattle();
            }
            }
            Assert.That(evidence.samples.Any(x => x.phase.StartsWith("contact") && x.attacking > 0), Is.True, "No melee contact captured; do not infer contact behavior.");
            Assert.That(evidence.samples.Any(x => x.phase == "move" && x.team0GoalDistance < 70), Is.True, "Move case did not approach the goal.");
            Assert.That(evidence.samples.Count(x => x.phase == "contact-ended"), Is.EqualTo(4), "Both repetitions in both conditions must finish; do not conceal stalemate.");
            evidence.productionChanged = true;
            evidence.captureComplete = true; File.WriteAllText(Path.Combine(output, "evidence.json"), JsonUtility.ToJson(evidence, true));
        }
    }
}
#endif

