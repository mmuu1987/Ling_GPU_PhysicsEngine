// TEMPORARY Editor-only probe (2026-10-13, GPU nav field N0b baseline). Archive after the N series; never commit.
// Menu (or the remote runner) -> enters Play from the MainMenu scene, enters a battlefield, deploys, starts the battle,
// issues the requested orders and records navigation/frame metrics once per second. Read-only: no simulation,
// settings or asset changes. Request: Logs/NavBaseline/request.txt (key=value). Output: Logs/NavBaseline/<tag>-<time>/.
//   mode=chase  both armies Attack (dynamic enemy fields).  mode=pass  team 0 Move through a ForestHills pass, team 1 Hold.
//   entry, units, duration, tag, passX, passZ, shots (comma list of seconds)
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    [InitializeOnLoad]
    public static class NavBaselineProbe
    {
        const string Menu = "Tools/战场 1024/导航基线采样（自动进 Play，自动退出）";
        const string Dir = "Logs/NavBaseline";
        const string ActiveKey = "NavBaselineProbe.Active";
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static string StatusPath => Path.Combine(Dir, "status.txt");
        static string RequestPath => Path.Combine(Dir, "request.txt");
        static IEnumerator routine;
        static LocalOrderReceipt localReceipt;
        static bool[] localMember;
        static Vector2 orderCentre;
        static double waitUntil, deadline;
        static Dictionary<string, string> request;
        static RuntimeFlowConfig flowAsset; static bool flowOld, flowTouched, steerOld, crowdOld, localNavOld, lineOld, arrivalOld; static float crowdGainOld;
        static void RestoreFlow()
        {
            if (!flowTouched || flowAsset == null) return;
            flowAsset.gpuNavField = flowOld; flowAsset.gpuNavSteering = steerOld;
            flowTouched = false;
            flowAsset.gpuNavCrowd = crowdOld; flowAsset.gpuNavCrowdGain = crowdGainOld; flowAsset.gpuNavLocalOrders = localNavOld; flowAsset.gpuNavFormationLine = lineOld; flowAsset.gpuNavArrivalFollow = arrivalOld;
            Log("restored " + flowAsset.name + ".gpuNavField=" + flowOld + " gpuNavSteering=" + steerOld);
        }

        static NavBaselineProbe()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (EditorApplication.isPlaying) Begin();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup("domain reload outside Play");
            }
        }

        [MenuItem(Menu, false, 45)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("ERROR already playing"); return; }
            Directory.CreateDirectory(Dir);
            File.WriteAllText(StatusPath, Stamp() + " requested\n");
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0) { Log("ERROR no build scenes"); return; }
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenes[0].path);
            if (menu == null) { Log("ERROR menu scene missing " + scenes[0].path); return; }
            EditorSceneManager.playModeStartScene = menu;
            SessionState.SetBool(ActiveKey, true);
            Application.runInBackground = true;
            Log("entering play from " + scenes[0].path);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode) Begin();
            else if (change == PlayModeStateChange.EnteredEditMode) Cleanup("left Play");
        }

        static void Begin()
        {
            if (routine != null) return;
            EditorSceneManager.playModeStartScene = null;
            Application.runInBackground = true;
            request = null;
            routine = Steps();
            waitUntil = 0;
            deadline = EditorApplication.timeSinceStartup + Get("timeout", 600f);
            EditorApplication.update += Tick;
            Log("play entered, routine started");
        }

        static void Cleanup(string why)
        {
            RestoreFlow();
            EditorApplication.update -= Tick;
            routine = null;
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.playModeStartScene = null;
            Log("finished (" + why + ")");
        }

        static void Tick()
        {
            if (routine == null) { EditorApplication.update -= Tick; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            SampleFrame();
            if (EditorApplication.timeSinceStartup > deadline) { Log("ERROR timeout"); Stop(); return; }
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Log("ERROR " + e); more = false; }
            if (!more) Stop();
            else if (routine.Current is float seconds) waitUntil = EditorApplication.timeSinceStartup + seconds;
        }

        static void Stop()
        {
            RestoreFlow();
            EditorApplication.update -= Tick;
            routine = null;
            if (EditorApplication.isPlaying) { Log("exiting play"); EditorApplication.ExitPlaymode(); }
            else Cleanup("stopped outside Play");
        }

        // ---- per-frame accumulation (player frames only)
        static int lastFrame = -1, frames;
        static double dtSum, dtMax;
        static readonly List<float> allDt = new List<float>();
        static bool recordFrames;
        static void SampleFrame()
        {
            if (!recordFrames || !EditorApplication.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            float dt = Time.unscaledDeltaTime;
            frames++; dtSum += dt; dtMax = Math.Max(dtMax, dt); allDt.Add(dt);
        }

        static object runtimeObj;
        static object Rt(string name)
        {
            if (runtimeObj == null) return null;
            var p = runtimeObj.GetType().GetProperty(name);
            return p != null ? p.GetValue(runtimeObj) : null;
        }
        static string RtF(string name) { var v = Rt(name); return v == null ? "" : Convert.ToDouble(v).ToString("0.###", IC); }

        static IEnumerator Steps()
        {
            string mode = GetString("mode", "chase");
            string entry = GetString("entry", "forest-hills-1024");
            int units = (int)Get("units", mode == "pass" ? 20000f : 110000f);
            float duration = Get("duration", mode == "pass" ? 120f : 90f);
            string tag = GetString("tag", mode);
            var shots = new List<float>();
            foreach (var s in GetString("shots", mode == "pass" ? "20,40,70,100" : "30,60").Split(','))
                if (float.TryParse(s, NumberStyles.Float, IC, out float v)) shots.Add(v);
            string folder = Path.GetFullPath(Path.Combine(Dir, tag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(folder);
            Log("OUT " + folder);

            WarSandboxSceneSession session = null;
            double until = EditorApplication.timeSinceStartup + 120;
            while ((session = WarSandboxSceneSession.Instance) == null || session.IsLoading)
            {
                if (EditorApplication.timeSinceStartup > until) { Log("ERROR no menu session"); yield break; }
                yield return 0.25f;
            }
            int gpuNavMode = (int)Get("gpunav", 0f);
            // N5d: LaneApproach retired; the old "lane" key is ignored.
            if (gpuNavMode >= 2 || Get("crowd", 0f) != 0f || Get("gpunavLocal", 0f) != 0f || Get("line", 0f) != 0f || Get("arrival", 0f) != 0f || Get("legacy", 0f) != 0f)
            {
                // Production admission path: config toggles, set before load (the terrain runtime reads them once), restored at the end; asset not saved.
                flowAsset = AssetDatabase.LoadAssetAtPath<RuntimeFlowConfig>(GetString("flowAsset", "Assets/Game/Content/Battlefields/Map1024/Flow1024.asset"));
                if (flowAsset == null) { Log("ERROR flow asset missing"); yield break; }
                flowOld = flowAsset.gpuNavField; steerOld = flowAsset.gpuNavSteering;
                flowTouched = true;
                crowdOld = flowAsset.gpuNavCrowd; crowdGainOld = flowAsset.gpuNavCrowdGain; localNavOld = flowAsset.gpuNavLocalOrders;
                flowAsset.gpuNavLocalOrders = Get("gpunavLocal", 0f) != 0f;
                lineOld = flowAsset.gpuNavFormationLine; flowAsset.gpuNavFormationLine = Get("line", 0f) != 0f;
                arrivalOld = flowAsset.gpuNavArrivalFollow; flowAsset.gpuNavArrivalFollow = Get("arrival", 0f) != 0f;
                if (Get("crowd", 0f) != 0f) { flowAsset.gpuNavCrowd = true; flowAsset.gpuNavCrowdGain = Get("crowdGain", flowAsset.gpuNavCrowdGain); }
                if (gpuNavMode >= 2) flowAsset.gpuNavField = true;
                if (gpuNavMode == 3) flowAsset.gpuNavSteering = true;
                if (Get("legacy", 0f) != 0f)
                {
                    // N5d: GPU nav became the Flow1024 default; legacy=1 runs the old CPU direction-field path (LaneApproach itself was retired in N5d).
                    flowAsset.gpuNavField = flowAsset.gpuNavSteering = flowAsset.gpuNavCrowd = false;
                    flowAsset.gpuNavLocalOrders = flowAsset.gpuNavFormationLine = flowAsset.gpuNavArrivalFollow = false;
                }
                Log("set " + flowAsset.name + ": gpuNavField=" + flowAsset.gpuNavField + " gpuNavSteering=" + flowAsset.gpuNavSteering
                    + " crowd=" + flowAsset.gpuNavCrowd + " crowdGain=" + flowAsset.gpuNavCrowdGain + " localNav=" + flowAsset.gpuNavLocalOrders + " line=" + flowAsset.gpuNavFormationLine + " arrival=" + flowAsset.gpuNavArrivalFollow + " (restored at the end; asset not saved)");
            }
            if (!session.TryEnterBattlefield(entry, false, out string error)) { Log("ERROR TryEnterBattlefield " + error); yield break; }
            until = EditorApplication.timeSinceStartup + 180;
            yield return 0.5f;
            while (session.IsLoading)
            {
                if (EditorApplication.timeSinceStartup > until) { Log("ERROR load timeout"); yield break; }
                yield return 0.25f;
            }
            if (session.State != WarSandboxEntryState.Battle) { Log("ERROR entry state " + session.State); yield break; }
            var controller = session.Controller;
            Log("entered " + entry + " phase=" + controller.Phase);
            yield return 1f;

            var deploy = controller.GetComponent<WarSandboxRuntimeDeployment>();
            if (deploy != null)
            {
                if (!deploy.IsEditing && controller.Phase != WarSandboxBattlePhase.Running)
                {
                    if (!deploy.TryBeginEdit(false, out error)) Log("TryBeginEdit: " + error);
                    yield return 0.5f;
                }
                if (deploy.IsEditing && units > 0 && deploy.Draft != null && deploy.Draft.Count == 2)
                {
                    var base0 = deploy.Draft[0]; var base1 = deploy.Draft[1];
                    int per = units / 2; bool fit = false;
                    foreach (float density in new[] { 0.7f, 0.85f, 1.0f })
                    {
                        foreach (float gapX in new[] { 50f, 40f, 70f })
                        {
                            var a = base0; var b = base1;
                            a.count = per; b.count = per; a.manualSize = Vector3.zero; b.manualSize = Vector3.zero;
                            a.density = density; b.density = density; a.aspect = 2.2f; b.aspect = 2.2f;
                            int t0 = a.teamId == 0 ? -1 : 1, t1 = b.teamId == 0 ? -1 : 1;
                            if (t0 == t1) { t0 = -1; t1 = 1; }
                            a.center = new Vector3(t0 * (gapX * .5f + a.Size.x * .5f), base0.center.y, 0f);
                            b.center = new Vector3(t1 * (gapX * .5f + b.Size.x * .5f), base1.center.y, 0f);
                            deploy.Draft.Set(0, a); deploy.Draft.Set(1, b);
                            if (deploy.TryValidate(out error)) { fit = true; Log("layout density=" + density + " gap=" + gapX + " size=" + a.Size); break; }
                        }
                        if (fit) break;
                    }
                    if (!fit) { Log("ERROR no valid layout for " + units); yield break; }
                    yield return 0.3f;
                }
                if (deploy.IsEditing)
                {
                    if (!deploy.TryApply(out error)) { Log("ERROR TryApply " + error); yield break; }
                    yield return 0.5f;
                }
            }
            if (controller.Phase != WarSandboxBattlePhase.Running) controller.StartOrResumeBattle();
            until = EditorApplication.timeSinceStartup + 30;
            while (controller.Phase != WarSandboxBattlePhase.Running)
            {
                if (EditorApplication.timeSinceStartup > until) { Log("ERROR not running: " + controller.Phase); yield break; }
                yield return 0.25f;
            }
            var manager = controller.manager;
            var buffers = manager.Buffers;
            int n = buffers.AgentCount;
            var field = typeof(MassEngineManager).GetField("terrainRuntime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            runtimeObj = field != null ? field.GetValue(manager) : null;
            var nav = manager.TerrainNavigation;
            var flow = manager.systemConfig != null ? manager.systemConfig.runtimeFlowConfig : null;
            Log("battle running agents=" + n + " runtime=" + (runtimeObj != null) + " nav=" + (nav != null ? nav.ResolutionX + "x" + nav.ResolutionZ + "@" + nav.CellSize : "none")
                + " gpunavDefault=" + (flow != null && flow.gpuNavField && flow.gpuNavSteering));

            GpuNavCheck gpuNav = null;
            if (gpuNavMode > 0)
            {
                gpuNav = new GpuNavCheck(folder);
                if (gpuNavMode >= 2) gpuNav.Attach(manager); else gpuNav.Enable(manager, (int)Get("gpunavPasses", 2f));
            }

            var agents = new AgentData[n];
            var teams = new int[n];
            buffers.combatBuffers.teamIdBuffer.GetData(teams);
            buffers.agentBuffer.GetData(agents);
            Vector3 target = Vector3.zero;
            int fieldsAtOrder = manager.TerrainCompletedFields;
            double orderTime = EditorApplication.timeSinceStartup;
            int orderFrame = Time.frameCount;
            if (mode == "pass")
            {
                Vector2 c0 = Vector2.zero; int k0 = 0;
                for (int i = 0; i < n; i++) if (teams[i] == 0) { c0 += new Vector2(agents[i].position.x, agents[i].position.z); k0++; }
                c0 /= Mathf.Max(1, k0);
                float tx = Get("passX", -230f), tz = Get("passZ", 455f);
                if (Get("moveAway", 0f) > 0f)
                {
                    // Open-ground move: straight away from team 1's centre (no contact), moveAway metres.
                    Vector2 c1 = Vector2.zero; int k1 = 0;
                    for (int i = 0; i < n; i++) if (teams[i] == 1) { c1 += new Vector2(agents[i].position.x, agents[i].position.z); k1++; }
                    c1 /= Mathf.Max(1, k1);
                    Vector2 away = (c0 - c1).sqrMagnitude > 1f ? (c0 - c1).normalized : Vector2.up;
                    tx = c0.x + away.x * Get("moveAway", 0f); tz = c0.y + away.y * Get("moveAway", 0f);
                }
                orderCentre = c0;
                target = FindPassTarget(nav, c0, tx, tz);
                gpuNav?.PrepareExact(target);
                bool ok0;
                if (Get("local", 0f) != 0f)
                {
                    // N5a: team 0 moves as one local group (LocalOrderChannel) instead of the army order.
                    if (!manager.TryEnableLocalOrderPrototype(out string localError)) { Log("ERROR local prototype " + localError); yield break; }
                    // The rest of team 0 holds; pass metrics count the group only.
                    controller.IssueOrder(ArmyOrder.Hold(0));
                    int want = (int)Get("localMembers", 400f);
                    var members = new List<int>();
                    localMember = new bool[n];
                    for (int i = 0; i < n && members.Count < want; i++) if (teams[i] == 0) { members.Add(i); localMember[i] = true; }
                    // A Move needs a fully clear square arrival region (LocalOrderPlan): pick open ground beyond the pass.
                    Vector2? clear = null;
                    foreach (int tryCount in new[] { members.Count, 300, 200, 150, 100, 64 })
                    {
                        if (tryCount > members.Count) continue;
                        clear = FindClearArrival(nav, c0, tryCount, Get("passX", -230f), Get("passZ", 455f) + Get("localAhead", 60f));
                        if (!clear.HasValue) continue;
                        for (int k = tryCount; k < members.Count; k++) localMember[members[k]] = false;
                        members.RemoveRange(tryCount, members.Count - tryCount);
                        break;
                    }
                    if (!clear.HasValue) { Log("ERROR no clear arrival region for >= 64 members beyond the pass"); yield break; }
                    target = new Vector3(clear.Value.x, 0, clear.Value.y);
                    localReceipt = manager.LocalOrders.Submit(manager.LocalOrders.Epoch, 0, 1, members.ToArray(), LocalOrderKind.Move, new Vector2(target.x, target.z));
                    ok0 = localReceipt.Status != LocalOrderStatus.Rejected;
                    Log("local Move team0 members=" + members.Count + " status=" + localReceipt.Status + " error=" + localReceipt.Error);
                }
                else ok0 = controller.IssueOrder(ArmyOrder.Move(0, target));
                bool ok1 = controller.IssueOrder(ArmyOrder.Hold(1));
                Log("pass order team0 Move " + target + " -> " + ok0 + "; team1 Hold -> " + ok1 + " from centre " + c0);
                if (localReceipt != null)
                {
                    double admit = EditorApplication.timeSinceStartup + 5;
                    while (localReceipt.Status == LocalOrderStatus.AwaitingSnapshot && EditorApplication.timeSinceStartup < admit) yield return 0.1f;
                    Log("local receipt after admission: " + localReceipt.Status + " error=" + localReceipt.Error + " planMs=" + localReceipt.PlanMilliseconds.ToString("0.0", IC)
                        + (manager.GpuNavField != null ? " gpuLocal commits=" + manager.GpuNavField.LocalCommits + " rejectedNav=" + manager.GpuNavField.LocalRejectedNavigation : ""));
                    if (localReceipt.Status == LocalOrderStatus.Rejected) { Log("ERROR local order rejected; stopping early"); localReceipt = null; localMember = null; yield break; }
                }
            }
            else
            {
                Log("orders attack -> " + controller.IssueOrder(ArmyOrder.Attack(0)) + "," + controller.IssueOrder(ArmyOrder.Attack(1)));
            }
            orderTime = EditorApplication.timeSinceStartup; orderFrame = Time.frameCount;
            if (gpuNav != null && mode == "pass")
            {
                // N2 acceptance: frame-by-frame after the order (seed valid within 2 frames).
                for (int k = 0; k < 8; k++)
                {
                    int f0 = Time.frameCount;
                    double frameWait = EditorApplication.timeSinceStartup + 5;
                    while (Time.frameCount == f0 && EditorApplication.timeSinceStartup < frameWait) yield return 0f;
                    gpuNav.FrameSample(Time.frameCount - orderFrame, orderFrame, buffers);
                }
            }

            var csv = new StringBuilder();
            csv.AppendLine("t,frames,dtAvgMs,dtMaxMs,completedFields,totalSolveMs,maxSolveMs,lastSolveMs,asyncSolves,asyncDiscarded,asyncForced,maxAsyncTicks,moving0,moving1,attack,alive,beyondRidge0,stuck0,medianDist0,axisAligned0");
            var stripes = new StringBuilder();
            stripes.AppendLine("t,team,cellsWithDir,diagonalFrac,neighbourPairs,angleOver30Frac");
            var camera = UnityEngine.Object.FindFirstObjectByType<MyCameraManager>();
            int firstFieldFrames = -1; double halfMovingAt = -1, contactAt = -1;
            long alignedTotal = 0, alignedSampled = 0; // stripes on agents: heading within 3 deg of a multiple of 45 (random ~13.3%)
            int shotIndex = 0;
            float flickerAt = Get("flicker", 0f); bool flickerDone = flickerAt <= 0f; // 2026-10-13: FrontlineFlickerProbe capture at this battle second
            frames = 0; dtSum = 0; dtMax = 0; allDt.Clear(); lastFrame = Time.frameCount; recordFrames = true;
            double start = EditorApplication.timeSinceStartup;
            float t = 0;
            while (t < duration)
            {
                yield return 1f;
                t = (float)(EditorApplication.timeSinceStartup - start);
                if (firstFieldFrames < 0 && manager.TerrainCompletedFields > fieldsAtOrder) firstFieldFrames = Time.frameCount - orderFrame;
                buffers.agentBuffer.GetData(agents);
                int moving0 = 0, moving1 = 0, attack = 0, alive = 0, beyond = 0, stuck = 0, team0 = 0, aligned0 = 0, headed0 = 0;
                var dists = new List<float>();
                for (int i = 0; i < n; i++)
                {
                    var a = agents[i];
                    if (a.currentState == 4) continue;
                    alive++;
                    if (a.currentState == 3) attack++;
                    float speed = a.locomotionSpeed; // filtered real displacement / dt
                    if (teams[i] == 0) { team0++; if (speed > 0.3f) moving0++; }
                    if (teams[i] == 0 && speed > 0.3f && a.currentState != 3)
                    {
                        float vx = a.velocity.x, vz = a.velocity.z;
                        if (vx * vx + vz * vz > 0.01f)
                        {
                            float m = Mathf.Repeat(Mathf.Atan2(vz, vx) * Mathf.Rad2Deg, 45f);
                            headed0++; if (Mathf.Min(m, 45f - m) < 3f) aligned0++;
                        }
                    }
                    else if (speed > 0.3f) moving1++;
                    if (mode == "pass" && teams[i] == 0 && (localMember == null || localMember[i]))
                    {
                        float d = new Vector2(a.position.x - target.x, a.position.z - target.z).magnitude;
                        dists.Add(d);
                        if (a.position.z > 425f) beyond++;
                        if (speed < 0.1f && d > 30f) stuck++;
                    }
                }
                if (halfMovingAt < 0 && team0 > 0 && moving0 * 2 >= team0) halfMovingAt = EditorApplication.timeSinceStartup - orderTime;
                if (contactAt < 0 && attack >= Mathf.Max(20, n / 200)) contactAt = EditorApplication.timeSinceStartup - orderTime;
                dists.Sort();
                float median = dists.Count > 0 ? dists[dists.Count / 2] : -1;
                csv.AppendLine(string.Join(",", new[] {
                    t.ToString("0.0", IC), frames.ToString(IC), (frames > 0 ? dtSum / frames * 1000 : 0).ToString("0.00", IC), (dtMax * 1000).ToString("0.00", IC),
                    manager.TerrainCompletedFields.ToString(IC), manager.TerrainTotalSolveMilliseconds.ToString("0.###", IC), manager.TerrainMaxSolveMilliseconds.ToString("0.###", IC),
                    RtF("LastSolveMilliseconds"), RtF("AsyncSolveCount"), RtF("AsyncDiscardedCount"), RtF("AsyncForcedCompleteCount"), RtF("MaxAsyncLatencyTicks"),
                    moving0.ToString(IC), moving1.ToString(IC), attack.ToString(IC), alive.ToString(IC), beyond.ToString(IC), stuck.ToString(IC), median.ToString("0.0", IC),
                    (headed0 > 0 ? (float)aligned0 / headed0 : -1f).ToString("0.000", IC) }));
                alignedTotal += aligned0; alignedSampled += headed0;
                frames = 0; dtSum = 0; dtMax = 0;
                gpuNav?.Sample(t, Time.frameCount - orderFrame, buffers);
                if (nav != null && ((int)t % 10 == 0 || t >= duration)) AppendStripes(stripes, t, buffers, nav);
                if (shotIndex < shots.Count && t >= shots[shotIndex])
                {
                    Vector3 focusAt = mode == "pass" ? (Get("moveAway", 0f) > 0f ? target : new Vector3(Get("passX", -230f), 0, 380f)) : FrontCentre(agents, n);
                    if (camera != null) camera.FocusBounds(new Bounds(focusAt, Vector3.one * (mode == "pass" ? 140f : 60f)));
                    yield return 1.5f;
                    string shot = Path.Combine(folder, "shot-" + ((int)shots[shotIndex]).ToString("000") + ".png");
                    ScreenCapture.CaptureScreenshot(shot);
                    Log("shot " + shot);
                    shotIndex++;
                }
                if (!flickerDone && t >= flickerAt)
                {
                    flickerDone = true;
                    if (camera != null) camera.FocusBounds(new Bounds(FrontCentre(agents, n), Vector3.one * 60f));
                    yield return 1.5f;
                    string why = FrontlineFlickerProbe.RunUnattended();
                    Log("flicker " + (why ?? "started"));
                    while (why == null && FrontlineFlickerProbe.Busy) yield return 0.5f;
                    Log("flicker done");
                }
            }
            recordFrames = false;
            string steerLine = "gpunav steering=" + (manager.GpuNavField != null && manager.GpuNavField.SteeringEnabled) + " validMask=" + (manager.GpuNavField != null ? manager.GpuNavField.ValidMask : 0u)
                + " metric=" + (manager.GpuNavField != null ? manager.GpuNavField.Metric.ToString() : "-")
                + " crowd=" + (manager.GpuNavField != null && manager.GpuNavField.CrowdActive) + " crowdUpdates=" + (manager.GpuNavField != null ? manager.GpuNavField.CrowdUpdates : 0)
                + (manager.GpuNavField != null && manager.GpuNavField.CrowdError != null ? " crowdError=" + manager.GpuNavField.CrowdError : "");
            if (localReceipt != null)
            {
                var lf = manager.GpuNavField; var lo = manager.LocalOrders;
                steerLine += "\nlocal receipt status=" + localReceipt.Status + " gpuExecuting=" + localReceipt.GpuExecutingMembers + " blocked=" + localReceipt.GpuBlockedMembers
                    + " alive=" + localReceipt.ObservedAlive + " planMs=" + localReceipt.PlanMilliseconds.ToString("0.0", IC) + " error=" + localReceipt.Error
                    + " usedSlots=0x" + (lo != null ? lo.UsedSlotMask.ToString("X") : "-")
                    + (lf != null ? " gpuLocal enabled=" + lf.LocalEnabled + " commits=" + lf.LocalCommits + " seedUploads=" + lf.LocalSeedUploads + " rejectedNav=" + lf.LocalRejectedNavigation
                        + " maxSeedMs=" + lf.LocalMaxSeedMilliseconds.ToString("0.0", IC) + " localValid=0x" + lf.LocalValidMask.ToString("X") + " gpuBytes=" + lf.GpuBytes : " gpuLocal -");
                localReceipt = null;
            }
            localMember = null;
            string gpuNavSummary = gpuNav != null ? gpuNav.Finish(manager, buffers) : null;
            File.WriteAllText(Path.Combine(folder, "metrics.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(folder, "stripes.csv"), stripes.ToString());
            allDt.Sort();
            var s2 = new StringBuilder();
            s2.AppendLine("NAV BASELINE " + mode + " entry=" + entry + " agents=" + n + " duration=" + duration + " time=" + DateTime.Now.ToString("s"));
            s2.AppendLine("nav=" + (nav != null ? nav.ResolutionX + "x" + nav.ResolutionZ + " cell=" + nav.CellSize : "none") + " gpunavDefault=" + (flow != null && flow.gpuNavField && flow.gpuNavSteering));
            if (allDt.Count > 0)
            {
                double sum = 0; foreach (var d in allDt) sum += d;
                s2.AppendLine("frames=" + allDt.Count + " dtAvgMs=" + (sum / allDt.Count * 1000).ToString("0.00", IC)
                    + " p50=" + (allDt[allDt.Count / 2] * 1000).ToString("0.00", IC) + " p95=" + (allDt[(int)(allDt.Count * .95)] * 1000).ToString("0.00", IC)
                    + " max=" + (allDt[allDt.Count - 1] * 1000).ToString("0.00", IC));
            }
            s2.AppendLine("solve: fields=" + manager.TerrainCompletedFields + " totalMs=" + manager.TerrainTotalSolveMilliseconds.ToString("0.###", IC) + " maxMs=" + manager.TerrainMaxSolveMilliseconds.ToString("0.###", IC)
                + " async=" + RtF("AsyncSolveCount") + " discarded=" + RtF("AsyncDiscardedCount") + " forced=" + RtF("AsyncForcedCompleteCount") + " maxAsyncTicks=" + RtF("MaxAsyncLatencyTicks")
                + " burst=" + RtF("BurstSolveCount") + " managed=" + RtF("ManagedSolveCount") + " maxQueueWaitMs=" + manager.TerrainMaxDynamicQueueWaitMilliseconds.ToString("0.###", IC));
 s2.AppendLine(steerLine);
            if (mode == "pass") s2.AppendLine(Formation(agents, teams, n, target, manager.GpuNavField));
            s2.AppendLine("axis-aligned headings (team0 moving, non-attacking; within 3 deg of k*45; random ~0.133): "
                + (alignedSampled > 0 ? ((double)alignedTotal / alignedSampled).ToString("0.000", IC) : "n/a") + " over " + alignedSampled + " samples");
            s2.AppendLine("order->first field frames=" + firstFieldFrames + " order->50% team0 moving s=" + halfMovingAt.ToString("0.0", IC) + " order->contact s=" + contactAt.ToString("0.0", IC));
            if (mode == "pass") s2.AppendLine("pass target=" + target);
            if (gpuNavSummary != null) s2.Append(gpuNavSummary);
            File.WriteAllText(Path.Combine(folder, "summary.txt"), s2.ToString());
            Log("SUMMARY " + s2.ToString().Replace("\n", " | "));
            Log("all done");
        }

        /// <summary>N1 shadow GPU nav field check on the real map: team 0 layer against the exact Dijkstra of the same graph.</summary>
        sealed class GpuNavCheck
        {
            const string ShaderPath = "Assets/MassEngine/FlowField/Shaders/GpuNavField.compute";
            readonly string folder;
            readonly StringBuilder csv = new StringBuilder("t,framesSinceOrder,ticks,passes,goalMode,goalCell,reachableExact,reachableGpu,mismatched,maxAbsErr\n");
            readonly StringBuilder summary = new StringBuilder();
            GpuNavField field;
            float[] gpu, exact, scratch;
            GpuNavGoal exactGoal;
            bool haveExact, bitChecked;
            int passesAtConverged = -1, framesAtConverged = -1; float tAtConverged = -1;
            public GpuNavCheck(string folder) { this.folder = folder; }

            public void Enable(MassEngineManager manager, int passes)
            {
                var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
                if (!manager.TryEnableGpuNavField(shader, out string error)) { Log("GPUNAV enable failed: " + error); summary.AppendLine("gpunav: enable failed " + error); return; }
                field = manager.GpuNavField;
                field.PassesPerFrame = Mathf.Max(1, passes);
                var g = field.Graph;
                int fineWalk = 0; foreach (var w in g.Walkable) if (w != 0) fineWalk++;
                var coarse = manager.TerrainNavigation; int coarseWalk = 0;
                for (int i = 0; i < coarse.CellCount; i++) if (coarse.IsWalkable(coarse.CellCenter(i))) coarseWalk++;
                gpu = new float[g.CellCount]; scratch = new float[g.CellCount];
                string line = "gpunav: fine " + g.Resolution + "^2 @" + g.CellSize + "m walkable=" + fineWalk + " (coarse walkable x4=" + coarseWalk * 4
                    + ") gridMs=" + field.GridBuildMilliseconds.ToString("0.0", IC) + " graphMs=" + field.GraphBuildMilliseconds.ToString("0.0", IC)
                    + " gpuMB=" + (field.GpuBytes / 1048576.0).ToString("0.0", IC) + " passesPerFrame=" + field.PassesPerFrame;
                Log(line); summary.AppendLine(line);
            }

            /// <summary>gpunav=2: the field was built by the config toggle during load; report it and the battle-start seed.</summary>
            public void Attach(MassEngineManager manager)
            {
                field = manager.GpuNavField;
                if (field == null)
                {
                    string why = "gpunav: config toggle produced NO field: " + manager.GpuNavFieldError + " shaderAssigned=" + (manager.shaderConfig != null && manager.shaderConfig.gpuNavFieldShader != null) + " shaderConfig=" + (manager.shaderConfig != null ? manager.shaderConfig.name : "null");
                    Log(why); summary.AppendLine(why); return;
                }
                var g = field.Graph;
                gpu = new float[g.CellCount]; scratch = new float[g.CellCount];
                string line = "gpunav (config toggle, built during load): fine " + g.Resolution + "^2 @" + g.CellSize + "m gridMs=" + field.GridBuildMilliseconds.ToString("0.0", IC)
                    + " graphMs=" + field.GraphBuildMilliseconds.ToString("0.0", IC) + " gpuMB=" + (field.GpuBytes / 1048576.0).ToString("0.0", IC) + " cpuSeedMB=" + (field.CpuBytes / 1048576.0).ToString("0.0", IC)
                    + " passesPerFrame=" + field.PassesPerFrame + " ticksSoFar=" + field.TickCount + " seedsSoFar=" + field.SeedUploads + " team0Goal=" + field.LastGoal(0).mode
                    + " maxDensitySeedFrames=" + field.MaxDensitySeedFrames;
                Log(line); summary.AppendLine(line);
            }

            /// <summary>Exact distances for the goal the field will resolve for this Move target (computed before the order).</summary>
            public void PrepareExact(Vector3 target)
            {
                if (field == null || field.IsDisposed) return;
                var p = new Vector2(target.x, target.z);
                var goal = new GpuNavGoal { mode = GpuNavGoal.Point, cell = field.SnapWalkable(field.FineCell(p), p), coarseResolution = field.CoarseResolution };
                var w = System.Diagnostics.Stopwatch.StartNew();
                exact = GpuNavFieldReference.Dijkstra(field.Graph, goal);
                exactGoal = goal; haveExact = true;
                Log("GPUNAV exact prepared for cell " + goal.cell + " in " + w.Elapsed.TotalMilliseconds.ToString("0.0", IC) + " ms (before the order)");
            }

            readonly StringBuilder frameCsv = new StringBuilder("frameSinceOrder,goalMode,goalCell,seedUploads,lastSeedFrames,seedForced,seedBurst,reachableGpu,mismatched,maxAbsErr,onePassChangedCells\n");
            int firstExactFrame = -1, seedUploadsAtOrder = -1;
            public void FrameSample(int k, int orderFrame, MassGpuBufferManager buffers)
            {
                if (field == null || field.IsDisposed) return;
                if (seedUploadsAtOrder < 0) seedUploadsAtOrder = field.SeedUploads - (field.LastSeedUploadFrame >= orderFrame ? 1 : 0);
                var goal = field.LastGoal(0);
                if (haveExact && goal.mode == GpuNavGoal.Point && !goal.Equals(exactGoal))
                {
                    Log("GPUNAV goal cell " + goal.cell + " differs from the prepared " + exactGoal.cell + "; recomputing");
                    exact = GpuNavFieldReference.Dijkstra(field.Graph, goal); exactGoal = goal;
                }
                field.ReadDistances(0, gpu);
                int bad = -1, ra = 0, re; float maxErr = 0;
                if (haveExact) bad = Mismatch(gpu, exact, out ra, out re, out maxErr);
                string onePass = "";
                bool seeded = field.SeedUploads > seedUploadsAtOrder;
                if (seeded && firstExactFrame < 0 && bad == 0)
                {
                    firstExactFrame = k;
                    field.RunPasses(buffers, 0, 1);
                    field.ReadDistances(0, scratch);
                    int changed = 0;
                    for (int i = 0; i < gpu.Length; i++) if (System.BitConverter.SingleToInt32Bits(gpu[i]) != System.BitConverter.SingleToInt32Bits(scratch[i])) changed++;
                    onePass = changed.ToString(IC);
                    bitChecked = true; // the generic one-pass check in Sample would be redundant now
                }
                frameCsv.AppendLine(k + "," + goal.mode + "," + goal.cell + "," + field.SeedUploads + "," + field.LastSeedFrames + "," + field.SeedForced + "," + field.SeedBurst + "," + ra + "," + bad + "," + maxErr.ToString("0.####", IC) + "," + onePass);
            }

            int densityChecks;
            void DensityCheck(float t, MassGpuBufferManager buffers)
            {
                int cr = field.CoarseResolution, cc = cr * cr;
                var density = new uint[cc];
                buffers.runtimeFlowTargetDensityBuffer.GetData(density, 0, 0, cc);
                var goal = new GpuNavGoal { mode = GpuNavGoal.Density, density = density, coarseResolution = cr };
                var reference = GpuNavFieldReference.Dijkstra(field.Graph, goal);
                field.ReadDistances(0, gpu);
                int bad = Mismatch(gpu, reference, out int ra, out int re, out float maxErr);
                int goals = 0; foreach (var d in density) if (d > 0) goals++;
                string line = "gpunav density tracking t=" + t.ToString("0.0", IC) + "s: goal coarse cells=" + goals + " reachable gpu/exact=" + ra + "/" + re + " mismatched=" + bad + " maxAbsErr=" + maxErr.ToString("0.##", IC) + " m (the GPU follows a moving goal; exact = this frame's density)";
                Log(line); summary.AppendLine(line);
                densityChecks++;
            }

            static int Mismatch(float[] a, float[] e, out int reachA, out int reachE, out float maxErr)
            {
                int bad = 0; reachA = reachE = 0; maxErr = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    bool ra = a[i] < GpuNavFieldGraph.Unreachable, re = e[i] < GpuNavFieldGraph.Unreachable;
                    if (ra) reachA++; if (re) reachE++;
                    if (ra != re) { bad++; continue; }
                    if (!ra) continue;
                    float err = Mathf.Abs(a[i] - e[i]);
                    if (err > maxErr) maxErr = err;
                    if (err > 1e-3f * Mathf.Max(1f, e[i])) bad++;
                }
                return bad;
            }

            public void Sample(float t, int framesSinceOrder, MassGpuBufferManager buffers)
            {
                if (field == null || field.IsDisposed) return;
                var goal = field.LastGoal(0);
                if (goal.mode == GpuNavGoal.Density && ((densityChecks == 0 && t >= 1f) || (densityChecks == 1 && t >= 10f) || (densityChecks == 2 && t >= 30f)))
                    DensityCheck(t, buffers);
                if (goal.mode == GpuNavGoal.None) { csv.AppendLine(t.ToString("0.0", IC) + "," + framesSinceOrder + "," + field.TickCount + "," + field.PassCount + ",0,-1,,,,"); return; }
                if (goal.mode == GpuNavGoal.Density)
                {
                    csv.AppendLine(t.ToString("0.0", IC) + "," + framesSinceOrder + "," + field.TickCount + "," + field.PassCount + ",1,-1,,,,");
                    return; // compared against this frame's density in DensityCheck
                }
                if (!haveExact || !goal.Equals(exactGoal))
                {
                    var w = System.Diagnostics.Stopwatch.StartNew();
                    exact = goal.mode == GpuNavGoal.Density ? null : GpuNavFieldReference.Dijkstra(field.Graph, goal);
                    exactGoal = goal; haveExact = exact != null; passesAtConverged = -1;
                    Log("GPUNAV goal mode=" + goal.mode + " cell=" + goal.cell + " dijkstraMs=" + w.Elapsed.TotalMilliseconds.ToString("0.0", IC));
                }
                field.ReadDistances(0, gpu);
                if (!bitChecked)
                {
                    // One pass on the real graph: GPU output == CPU reference of the same input, bit for bit.
                    var expected = new float[gpu.Length];
                    GpuNavFieldReference.Relax(field.Graph, goal, gpu, expected);
                    field.RunPasses(buffers, 0, 1);
                    field.ReadDistances(0, scratch);
                    int diff = 0, first = -1;
                    for (int i = 0; i < gpu.Length; i++)
                        if (System.BitConverter.SingleToInt32Bits(expected[i]) != System.BitConverter.SingleToInt32Bits(scratch[i])) { diff++; if (first < 0) first = i; }
                    string line = "gpunav bitwise one pass on real graph (goal mode " + goal.mode + "): differing cells=" + diff + (first >= 0 ? " first=" + first + " ref=" + expected[first].ToString("R", IC) + " gpu=" + scratch[first].ToString("R", IC) : "");
                    Log(line); summary.AppendLine(line); bitChecked = true;
                    System.Array.Copy(scratch, gpu, gpu.Length);
                }
                string tail = ",,,,";
                if (haveExact)
                {
                    int bad = Mismatch(gpu, exact, out int ra, out int re, out float maxErr);
                    tail = "," + re + "," + ra + "," + bad + "," + maxErr.ToString("0.####", IC);
                    if (bad == 0 && passesAtConverged < 0) { passesAtConverged = field.PassCount; framesAtConverged = framesSinceOrder; tAtConverged = t; Heatmap("converged"); }
                }
                csv.AppendLine(t.ToString("0.0", IC) + "," + framesSinceOrder + "," + field.TickCount + "," + field.PassCount + "," + goal.mode + "," + goal.cell + tail);
            }

            void Heatmap(string name)
            {
                var g = field.Graph; int r = g.Resolution;
                float max = 1; foreach (var d in gpu) if (d < GpuNavFieldGraph.Unreachable && d > max) max = d;
                var px = new Color32[g.CellCount];
                for (int i = 0; i < px.Length; i++)
                {
                    float d = gpu[i];
                    if (g.Walkable[i] == 0) px[i] = new Color32(20, 20, 24, 255);
                    else if (d >= GpuNavFieldGraph.Unreachable) px[i] = new Color32(200, 0, 200, 255);
                    else if (d == 0) px[i] = new Color32(255, 255, 255, 255);
                    else
                    {
                        float u = d / max; bool contour = (d % 50f) < 2.5f;
                        var c = Color.Lerp(new Color(.1f, .3f, .9f), new Color(1f, .85f, .1f), u);
                        if (contour) c *= .45f;
                        px[i] = c;
                    }
                }
                var tex = new Texture2D(r, r, TextureFormat.RGBA32, false);
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes(Path.Combine(folder, "gpunav-" + name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }

            public string Finish(MassEngineManager manager, MassGpuBufferManager buffers)
            {
                if (field == null || field.IsDisposed) { manager.DisableGpuNavField(); return summary.ToString(); }
                summary.AppendLine("gpunav convergence (|gpu-exact| <= 1e-3*max(1,exact) everywhere, 1 s sampling): "
                    + (passesAtConverged >= 0 ? "passes<=" + passesAtConverged + " framesSinceOrder<=" + framesAtConverged + " t<=" + tAtConverged.ToString("0.0", IC) + "s" : "NOT converged; see gpunav.csv"));
                summary.AppendLine("gpunav ticks=" + field.TickCount + " passes=" + field.PassCount);
                summary.AppendLine("gpunav seed: uploads=" + field.SeedUploads + " discarded=" + field.SeedDiscarded + " forced=" + field.SeedForced + " burst=" + field.SeedBurst
                    + " maxExplicitSeedFrames=" + field.MaxSeedFrames + " maxDensitySeedFrames=" + field.MaxDensitySeedFrames + " maxSeedMs(schedule->upload)=" + field.MaxSeedMilliseconds.ToString("0.0", IC)
                    + " firstExactFrameAfterOrder=" + firstExactFrame);
                if (frameCsv.Length > 0) File.WriteAllText(Path.Combine(folder, "gpunav-frames.csv"), frameCsv.ToString());
                if (field.LastGoal(0).mode != GpuNavGoal.None)
                {
                    field.ReadDistances(0, gpu);
                    var w = System.Diagnostics.Stopwatch.StartNew();
                    field.ReadDistances(0, scratch);
                    double readMs = w.Elapsed.TotalMilliseconds;
                    const int bench = 200;
                    w.Restart();
                    field.RunPasses(buffers, 0, bench);
                    field.ReadDistances(0, scratch);
                    double ms = w.Elapsed.TotalMilliseconds - readMs;
                    summary.AppendLine("gpunav bench: " + bench + " passes " + ms.ToString("0.0", IC) + " ms (~" + (ms / bench).ToString("0.000", IC) + " ms/pass incl. dispatch, readback " + readMs.ToString("0.0", IC) + " ms excluded)");
                    Heatmap("end");
                    // Seed job cost on its own (synchronous Run, Burst compiled by now): same goal, so the layer is unchanged.
                    var goalNow = field.LastGoal(0);
                    if (goalNow.mode == GpuNavGoal.Point || goalNow.mode == GpuNavGoal.Area)
                    {
                        var ws = System.Diagnostics.Stopwatch.StartNew();
                        bool burst = field.SeedNow(0, goalNow);
                        summary.AppendLine("gpunav seed job alone (sync Run + upload): " + ws.Elapsed.TotalMilliseconds.ToString("0.0", IC) + " ms burst=" + burst);
                    }
                }
                File.WriteAllText(Path.Combine(folder, "gpunav.csv"), csv.ToString());
                manager.DisableGpuNavField();
                Log("GPUNAV " + summary.ToString().Replace("\n", " | "));
                return summary.ToString();
            }
        }

        static Vector3 FrontCentre(AgentData[] agents, int n)
        {
            var xs = new List<float>(); var zs = new List<float>();
            for (int i = 0; i < n; i++) if (agents[i].currentState == 3) { xs.Add(agents[i].position.x); zs.Add(agents[i].position.z); }
            if (xs.Count == 0) return Vector3.zero;
            xs.Sort(); zs.Sort();
            return new Vector3(xs[xs.Count / 2], 0, zs[zs.Count / 2]);
        }

        /// <summary>
        /// N5b: final shape of team 0 (the local group when one is used) relative to the order: u = centre at order ->
        /// target, v = across. along/across = p5..p95 extents, centroid offset from the target, crowding per 2 m cell.
        /// </summary>
        static string Formation(AgentData[] agents, int[] teams, int n, Vector3 target, GpuNavField field)
        {
            var t = new Vector2(target.x, target.z);
            Vector2 u = (t - orderCentre).sqrMagnitude > 1f ? (t - orderCentre).normalized : Vector2.up, v = new Vector2(-u.y, u.x);
            var along = new List<float>(); var across = new List<float>(); var cells = new Dictionary<long, int>();
            int settled = 0;
            for (int i = 0; i < n; i++)
            {
                var a = agents[i];
                if (teams[i] != 0 || a.currentState == 4 || (localMember != null && !localMember[i])) continue;
                var d = new Vector2(a.position.x, a.position.z) - t;
                along.Add(Vector2.Dot(d, u)); across.Add(Vector2.Dot(d, v));
                long key = ((long)Mathf.FloorToInt(a.position.x * .5f) << 32) ^ (uint)Mathf.FloorToInt(a.position.z * .5f);
                cells.TryGetValue(key, out int c); cells[key] = c + 1;
                if (a.locomotionSpeed < 0.1f) settled++;
            }
            if (along.Count == 0) return "formation: no agents";
            float meanA = 0, meanB = 0; foreach (float x in along) meanA += x; foreach (float x in across) meanB += x;
            meanA /= along.Count; meanB /= across.Count;
            along.Sort(); across.Sort();
            var occ = new List<int>(cells.Values); occ.Sort();
            float P(List<float> l, float q) => l[Mathf.Clamp((int)(q * (l.Count - 1)), 0, l.Count - 1)];
            return "formation (team0 vs target, u = order centre -> target): agents=" + along.Count + " settled(<0.1m/s)=" + settled
                + " centroidOffset along=" + meanA.ToString("0.0", IC) + " across=" + meanB.ToString("0.0", IC)
                + " depth p5..p95=" + P(along, .05f).ToString("0.0", IC) + ".." + P(along, .95f).ToString("0.0", IC) + " (" + (P(along, .95f) - P(along, .05f)).ToString("0.0", IC) + " m)"
                + " frontage p5..p95=" + P(across, .05f).ToString("0.0", IC) + ".." + P(across, .95f).ToString("0.0", IC) + " (" + (P(across, .95f) - P(across, .05f)).ToString("0.0", IC) + " m)"
                + " per2mCell p95=" + occ[(int)(.95f * (occ.Count - 1))] + " max=" + occ[occ.Count - 1] + " occupied=" + occ.Count
                + (field != null ? " arrivalZone0=" + field.ArrivalZone(0).ToString("0.0", IC) + " lineGoals=" + field.LineGoals + " lineFallbacks=" + field.LineFallbacks + " lineMask=0x" + field.LineMask.ToString("X")
                    + (field.LastGoal(0).mode == GpuNavGoal.Line ? " line centre=(" + field.LastGoal(0).rect.x.ToString("0.0", IC) + "," + field.LastGoal(0).rect.y.ToString("0.0", IC)
                        + ") t=" + field.LastGoal(0).line.x.ToString("0.0", IC) + ".." + field.LastGoal(0).line.y.ToString("0.0", IC) + " cells=" + field.LastGoal(0).cells.Length : "") : "");
        }

        /// <summary>Same arrival-region rule as LocalOrderPlan (cols x rows slots at ceil((2r + .1) / cell) * cell), r = nav clearance.</summary>
        static Vector2? FindClearArrival(TerrainNavigationGrid nav, Vector2 from, int count, float x, float z)
        {
            if (nav == null) return null;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count)), rows = Mathf.CeilToInt(count / (float)cols);
            float step = Mathf.Ceil((2 * nav.Clearance + .1f) / nav.CellSize) * nav.CellSize;
            var extent = new Vector2((cols - 1) * step, (rows - 1) * step);
            for (int r = 0; r <= 320; r += 8)
                for (int dz = -r; dz <= r; dz += 8)
                    for (int dx = -r; dx <= r; dx += 8)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        var p = new Vector2(x + dx, z + dz);
                        if (p.y < 440f || !nav.TryGetCell(p, out int cell)) continue;
                        var c = nav.CellCenter(cell);
                        if (nav.IsWalkable(c) && nav.IsFootprintWalkable(c, extent) && nav.AreConnected(from, c))
                        { Log("local arrival centre " + c + " extent " + extent + " (" + cols + "x" + rows + ")"); return c; }
                    }
            return null;
        }

        static Vector3 FindPassTarget(TerrainNavigationGrid nav, Vector2 from, float x, float z)
        {
            if (nav == null) return new Vector3(x, 0, z);
            for (int r = 0; r <= 40; r += 2)
                for (int dz = -r; dz <= r; dz += 2)
                    for (int dx = -r; dx <= r; dx += 2)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        var p = new Vector2(x + dx, z + dz);
                        if (nav.IsWalkable(p) && nav.AreConnected(from, p)) return new Vector3(p.x, 0, p.y);
                    }
            Log("WARN no connected walkable pass target near " + x + "," + z);
            return new Vector3(x, 0, z);
        }

        // Stripe metric (FLOWFIELD-GRADIENT-PLAN 4.4): among cells with a direction, the fraction of diagonal
        // directions and the fraction of 4-neighbour pairs (both with a direction) whose angle exceeds 30 degrees.
        static void AppendStripes(StringBuilder sb, float t, MassGpuBufferManager b, TerrainNavigationGrid nav)
        {
            int cells = nav.CellCount, rx = nav.ResolutionX;
            var dirs = new Vector2[b.flowFieldDirectionsBuffer.count];
            b.flowFieldDirectionsBuffer.GetData(dirs);
            int teamsInBuffer = dirs.Length / cells;
            float cos30 = Mathf.Cos(30f * Mathf.Deg2Rad);
            for (int team = 0; team < teamsInBuffer && team < 2; team++)
            {
                int withDir = 0, diag = 0, pairs = 0, over = 0;
                int o = team * cells;
                for (int c = 0; c < cells; c++)
                {
                    var d = dirs[o + c];
                    if (d.sqrMagnitude < 1e-4f) continue;
                    withDir++;
                    if (Mathf.Abs(d.x) > 1e-4f && Mathf.Abs(d.y) > 1e-4f) diag++;
                    int x = c % rx;
                    if (x + 1 < rx) { var e = dirs[o + c + 1]; if (e.sqrMagnitude >= 1e-4f) { pairs++; if (Vector2.Dot(d.normalized, e.normalized) < cos30) over++; } }
                    if (c + rx < cells) { var e = dirs[o + c + rx]; if (e.sqrMagnitude >= 1e-4f) { pairs++; if (Vector2.Dot(d.normalized, e.normalized) < cos30) over++; } }
                }
                sb.AppendLine(string.Join(",", new[] { t.ToString("0", IC), team.ToString(IC), withDir.ToString(IC),
                    (withDir > 0 ? (float)diag / withDir : 0).ToString("0.0000", IC), pairs.ToString(IC), (pairs > 0 ? (float)over / pairs : 0).ToString("0.0000", IC) }));
            }
        }

        static string GetString(string key, string fallback)
        {
            if (request == null)
            {
                request = new Dictionary<string, string>();
                if (File.Exists(RequestPath))
                    foreach (var raw in File.ReadAllLines(RequestPath))
                    {
                        int eq = raw.IndexOf('=');
                        if (eq > 0) request[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
                    }
            }
            return request.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;
        }
        static float Get(string key, float fallback) =>
            float.TryParse(GetString(key, ""), NumberStyles.Float, IC, out float v) ? v : fallback;
        static string Stamp() => DateTime.Now.ToString("HH:mm:ss");
        static void Log(string line)
        {
            try { Directory.CreateDirectory(Dir); File.AppendAllText(StatusPath, Stamp() + " " + line + "\n"); } catch { }
            Debug.Log("[NavBaseline] " + line);
        }
    }
}
