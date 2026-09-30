using UnityEngine;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
#endif

namespace MassEngine.Game
{
    /// <summary>Opt-in player verification of the real terrain scene, commands and GPU movement.</summary>
    [DefaultExecutionOrder(-150)]
    public sealed class WarSandboxTerrainPlayableSmoke : MonoBehaviour
    {
        public WarSandboxSceneSession session;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Serializable]
        private sealed class Report
        {
            public bool passed, completed, gpuReleased, rejectedCliffCommand;
            public string error, unity, gpu, buildGuid;
            public int agents, groundedSamples, launchedProjectiles, readbackFailures;
            public bool combatDamageObserved;
            public float maxGroundError, westHeightGain, eastHeightGain, elapsedSeconds;
        }
        private readonly Report report = new Report();
        private static WarSandboxTerrainPlayableSmoke instance;
        private string output;
        private bool running;
        private float started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Awake()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--terrain-smoke") < 0) { enabled = false; return; }
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            output = Path.GetFullPath("TerrainSmoke");
            foreach (string arg in args)
                if (arg.StartsWith("--terrain-smoke-output=", StringComparison.Ordinal)) output = Path.GetFullPath(arg.Substring(23));
            Directory.CreateDirectory(output);
            session.enterDefaultOnStart = false;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
        }

        private void Start()
        {
            if (instance != this) return;
            started = Time.realtimeSinceStartup; running = true;
            report.unity = Application.unityVersion; report.gpu = SystemInfo.graphicsDeviceName; report.buildGuid = Application.buildGUID;
            Application.logMessageReceived += TrackError;
            StartCoroutine(Guard(Run()));
        }

        private void Update()
        {
            if (running && Time.realtimeSinceStartup - started > 240) Finish("Terrain smoke timed out.");
        }

        private IEnumerator Guard(IEnumerator task)
        {
            while (running)
            {
                object next;
                try { if (!task.MoveNext()) { Finish(null); yield break; } next = task.Current; }
                catch (Exception error) { Finish(error.ToString()); yield break; }
                yield return next;
            }
        }

        private IEnumerator Run()
        {
            Require(session.TryEnterBattlefield("mountain-battle", false, out string error), error);
            while (session.IsLoading) yield return null;
            Require(session.State == WarSandboxEntryState.Battle, session.Error);
            var controller = session.Controller;
            var manager = controller.manager;
            Require(manager.TerrainGpuAllocated, manager.TerrainError ?? "Terrain GPU storage absent.");
            var buffers = manager.Buffers;
            report.agents = manager.UnitTypes.TotalAgentCount;
            Require(report.agents == 384, "Unexpected terrain recipe population.");
            var agents = new AgentData[report.agents];
            var hp = new int[report.agents];
            var teams = new int[report.agents];
            buffers.combatBuffers.teamIdBuffer.GetData(teams);
            buffers.agentBuffer.GetData(agents);
            buffers.combatBuffers.hpReadBuffer.GetData(hp);
            var initialHp = (int[])hp.Clone();
            Vector3[] before = Centroids(agents, teams);
            string source = JsonUtility.ToJson(manager.scenarioConfig);
            Require(controller.IssueOrder(ArmyOrder.Hold(1)), controller.CommandError);
            Require(controller.IssueMoveOrder(0, new Vector3(-165, 0, 8), false), controller.CommandError);
            Require(controller.IssueMoveOrder(2, new Vector3(45, 0, 0), false), controller.CommandError);
            var oldOrder = controller.GetArmy(0).currentOrder;
            report.rejectedCliffCommand = !controller.IssueMoveOrder(0, new Vector3(-80, 0, 73), false);
            Require(report.rejectedCliffCommand && !string.IsNullOrEmpty(controller.CommandError), "Cliff command was accepted or had no feedback.");
            Require(controller.GetArmy(0).currentOrder.target == oldOrder.target, "Rejected command changed the previous order.");
            float deadline = Time.realtimeSinceStartup + 150;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(.5f);
                buffers.agentBuffer.GetData(agents); buffers.combatBuffers.hpReadBuffer.GetData(hp);
                for (int i = 0; i < agents.Length; i++)
                {
                    Vector3 p = agents[i].position;
                    Require(manager.TerrainSurface.TrySample(new Vector2(p.x, p.z), out var sample), "Agent left the terrain.");
                    report.maxGroundError = Mathf.Max(report.maxGroundError, Mathf.Abs(p.y - sample.Position.y));
                    if (hp[i] > 0) Require(manager.TerrainNavigation.IsWalkable(new Vector2(p.x, p.z)), "Live agent entered a forbidden cell.");
                    report.groundedSamples++;
                }
                Vector3[] after = Centroids(agents, teams);
                report.westHeightGain = after[0].y - before[0].y;
                report.eastHeightGain = after[2].y - before[2].y;
                if (report.westHeightGain > 38 && report.eastHeightGain > 30) break;
            }
            Require(report.westHeightGain > 38 && report.eastHeightGain > 30, "Both armies must climb the real west/east ramps.");
            Require(report.maxGroundError < .01f, "GPU units do not match the visible heightfield.");
            Require(source == JsonUtility.ToJson(manager.scenarioConfig), "Movement changed the source scenario.");
            Require(controller.StartDefaultBattle(), controller.CommandError);
            deadline = Time.realtimeSinceStartup + 100;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(.5f);
                report.launchedProjectiles = manager.TotalLaunchedProjectiles;
                buffers.combatBuffers.hpReadBuffer.GetData(hp);
                for (int i = 0; i < hp.Length; i++)
                    if (hp[i] < initialHp[i]) report.combatDamageObserved = true;
                if (report.launchedProjectiles > 0 && report.combatDamageObserved) break;
            }
            Require(report.launchedProjectiles > 0 && report.combatDamageObserved, "Default terrain orders must produce real shots and combat damage.");
            controller.PauseBattle();
            Require(session.TryReturnToMenu(true, out error), error);
            while (session.IsLoading) yield return null;
            report.gpuReleased = !buffers.IsAllocated && (manager == null || !manager.TerrainGpuAllocated);
            Require(report.gpuReleased, "Terrain/world buffers survived scene exit.");
        }

        private static Vector3[] Centroids(AgentData[] agents, int[] teams)
        {
            var result = new Vector3[3]; var count = new int[3];
            for (int i = 0; i < agents.Length; i++) { result[teams[i]] += agents[i].position; count[teams[i]]++; }
            for (int team = 0; team < 3; team++) result[team] /= Mathf.Max(1, count[team]);
            return result;
        }

        private static void Require(bool valid, string error) { if (!valid) throw new InvalidOperationException(error ?? "Terrain validation failed."); }
        private void TrackError(string condition, string trace, LogType type)
        {
            if (condition.Contains("AsyncGPUReadback failed for launch requests"))
            { report.readbackFailures++; report.error = condition; }
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) report.error = condition + "\n" + trace;
        }
        private void Finish(string error)
        {
            if (!running) return;
            running = false; report.completed = true;
            report.error = error ?? report.error; report.passed = string.IsNullOrEmpty(report.error);
            report.elapsedSeconds = Time.realtimeSinceStartup - started;
            Application.logMessageReceived -= TrackError;
            File.WriteAllText(Path.Combine(output, "terrain-smoke.json"), JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 1);
        }
#endif
    }
}
