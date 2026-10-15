// Editor-only, TEMPORARY measurement probe for the P4 command-lag analysis (2026-10-12). Archive after use.
// Menu -> enters Play from MainMenu, enters a battlefield, starts the battle, box-selects part of team 0 through the
// real HUD path (TestSelectionPointerP7 + TestPlayerOrderP8) and issues Move/Hold/Attack/Retreat in rounds.
// Per command it records: Submit call cost, rejections/retries, receipt timeline (readback/plan/upload ms, Committed,
// GpuExecuting), every frame time, movement of the selected members (async position readback every 0.25 s),
// GC counts and Library/ShaderCache files written during the window. Nothing in the simulation is modified.
// Output: Logs/CommandLatency/status.txt and Logs/CommandLatency/result.txt. Request: Logs/CommandLatency/request.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace MassEngine.Game.Editor
{
    [InitializeOnLoad]
    public static class CommandLatencyProbe
    {
        const string Menu = "Tools/战场 1024/指令卡顿测量（自动进 Play，自动退出）";
        const string Dir = "Logs/CommandLatency";
        const string ActiveKey = "CommandLatencyProbe.Active";
        static string StatusPath => Path.Combine(Dir, "status.txt");
        static string ResultPath => Path.Combine(Dir, "result.txt");
        static string RequestPath => Path.Combine(Dir, "request.txt");
        static IEnumerator routine;
        static double waitUntil, deadline;

        static CommandLatencyProbe()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (EditorApplication.isPlaying) Begin();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup("domain reload outside Play");
            }
        }

        [MenuItem(Menu, false, 43)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("ERROR already playing"); return; }
            Directory.CreateDirectory(Dir);
            File.WriteAllText(StatusPath, Stamp() + " requested\n");
            File.WriteAllText(ResultPath, "");
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0) { Log("ERROR no build scenes"); return; }
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenes[0].path);
            if (menu == null) { Log("ERROR menu scene missing"); return; }
            EditorSceneManager.playModeStartScene = menu;
            SessionState.SetBool(ActiveKey, true);
            Application.runInBackground = true;
            Log("entering play");
            EditorApplication.EnterPlaymode();
        }

        // Key-equivalence test for LocalOrderShaderPrewarm: if a variant the runtime already compiled is a cache hit
        // here (milliseconds, no new cache file), ShaderUtil and the runtime dispatch share the same cache key.
        [MenuItem("Tools/战场 1024/指令卡顿测量 - 预热键测试", false, 45)]
        public static void WarmKeyTest()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("ERROR key test needs Edit mode"); return; }
            request = null;
            int mask = (int)Get("warm.mask", 1f); bool charge = Get("warm.charge", 1f) > 0.5f;
            string root = Path.GetFullPath(Path.Combine("Library", "ShaderCache"));
            var start = DateTime.Now;
            string result = LocalOrderShaderPrewarm.Warm(mask, charge, "key test");
            int fresh = 0;
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    if (File.GetLastWriteTime(file) >= start) fresh++;
            Log("KEYTEST " + result + " | newCacheFiles=" + fresh);
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode) Begin();
            else if (change == PlayModeStateChange.ExitingPlayMode) Unhook();
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
            Hook();
            EditorApplication.update += Tick;
            Log("play entered, routine started");
        }

        static void Cleanup(string why)
        {
            Unhook();
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
            Unhook();
            EditorApplication.update -= Tick;
            routine = null;
            if (EditorApplication.isPlaying) { Log("exiting play"); EditorApplication.ExitPlaymode(); }
            else Cleanup("stopped outside Play");
        }

        // ---------------- per-frame recorder (player loop, so every rendered frame is seen) ----------------
        struct ProbeFrame { }
        static PlayerLoopSystem originalLoop; static bool hooked;
        static void Hook()
        {
            if (hooked) return;
            originalLoop = PlayerLoop.GetCurrentPlayerLoop();
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var subs = root.subSystemList;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i].type != typeof(UnityEngine.PlayerLoop.Update)) continue;
                var list = new List<PlayerLoopSystem>(subs[i].subSystemList ?? new PlayerLoopSystem[0]);
                list.Add(new PlayerLoopSystem { type = typeof(ProbeFrame), updateDelegate = Frame });
                subs[i].subSystemList = list.ToArray();
            }
            root.subSystemList = subs;
            PlayerLoop.SetPlayerLoop(root);
            hooked = true;
        }
        static void Unhook()
        {
            if (!hooked) return;
            PlayerLoop.SetPlayerLoop(originalLoop);
            hooked = false; recording = false;
        }

        static bool recording; static double windowStart, lastFrame;
        static readonly List<double> frames = new List<double>();
        static readonly List<string> longFrames = new List<string>();
        static readonly List<string> events = new List<string>();
        static readonly List<string> motion = new List<string>();
        static LocalOrderReceipt receipt; static LocalOrderStatus lastStatus; static double commitT, gpuT;
        static MassEngineManager mgr; static int[] watchMembers; static Vector2[] prevPos; static Vector2[] startPos;
        static bool posPending; static AsyncGPUReadbackRequest posReq; static double nextPos, posReqT;
        static double move50T, still80T; static float lastMovingFrac;

        static void Frame()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!recording) { lastFrame = now; return; }
            double dt = now - lastFrame; lastFrame = now;
            frames.Add(dt);
            if (dt > 0.05) longFrames.Add((now - windowStart - dt).ToString("F3") + "+" + (dt * 1000).ToString("F0") + "ms");
            if (receipt != null && receipt.Status != lastStatus)
            {
                lastStatus = receipt.Status;
                events.Add((now - windowStart).ToString("F3") + " " + lastStatus);
                if ((lastStatus == LocalOrderStatus.Committed || lastStatus == LocalOrderStatus.GpuExecuting) && commitT < 0) commitT = now - windowStart;
                if (lastStatus == LocalOrderStatus.GpuExecuting && gpuT < 0) gpuT = now - windowStart;
            }
            if (posPending && posReq.done)
            {
                posPending = false;
                if (!posReq.hasError && watchMembers != null)
                {
                    var data = posReq.GetData<Vector2>();
                    if (data.Length > 0)
                    {
                        int moving = 0; double far = 0;
                        for (int k = 0; k < watchMembers.Length; k++)
                        {
                            int i = watchMembers[k]; if (i >= data.Length) continue;
                            Vector2 p = data[i];
                            if (prevPos[k].x != float.MaxValue && (p - prevPos[k]).magnitude > 0.15f) moving++;
                            if (startPos[k].x == float.MaxValue) startPos[k] = p;
                            far += (p - startPos[k]).magnitude;
                            prevPos[k] = p;
                        }
                        float frac = watchMembers.Length > 0 ? moving / (float)watchMembers.Length : 0;
                        double t = posReqT - windowStart;
                        motion.Add(t.ToString("F2") + ":" + (frac * 100).ToString("F0") + "%/" + (far / Math.Max(1, watchMembers.Length)).ToString("F1") + "m");
                        if (move50T < 0 && frac >= 0.5f && lastMovingFrac < 0.5f) move50T = t;
                        if (still80T < 0 && frac <= 0.2f && t > 0.3) still80T = t;
                        lastMovingFrac = frac;
                    }
                }
            }
            if (!posPending && now >= nextPos && mgr != null && mgr.Buffers != null && mgr.Buffers.agentPositionReadBuffer != null)
            {
                posReq = AsyncGPUReadback.Request(mgr.Buffers.agentPositionReadBuffer);
                posPending = true; posReqT = now; nextPos = now + 0.25;
            }
        }

        static void StartWindow(int[] members, float lastFracGuess)
        {
            frames.Clear(); longFrames.Clear(); events.Clear(); motion.Clear();
            receipt = null; lastStatus = LocalOrderStatus.Rejected; commitT = gpuT = move50T = still80T = -1;
            watchMembers = members; prevPos = new Vector2[members.Length]; startPos = new Vector2[members.Length];
            for (int k = 0; k < members.Length; k++) { prevPos[k] = new Vector2(float.MaxValue, 0); startPos[k] = prevPos[k]; }
            lastMovingFrac = lastFracGuess; posPending = false; nextPos = 0;
            windowStart = Time.realtimeSinceStartupAsDouble; lastFrame = windowStart;
            recording = true;
        }

        // ---------------- scenario ----------------
        static IEnumerator Steps()
        {
            string entry = GetString("entry", "forest-hills-1024");
            int units = (int)Get("units", 110000f);
            int rounds = (int)Get("rounds", 2f);
            float window = Get("window", 7f);
            int minSel = (int)Get("minsel", 600f), maxSel = (int)Get("maxsel", 4000f);
            string[] kinds = GetString("kinds", "move,hold,attack,retreat").Split(',');

            WarSandboxSceneSession session = null;
            double until = EditorApplication.timeSinceStartup + 120;
            while ((session = WarSandboxSceneSession.Instance) == null || session.IsLoading)
            {
                if (EditorApplication.timeSinceStartup > until) { Log("ERROR no menu session"); yield break; }
                yield return 0.25f;
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
            if (GetString("mode", "") == "deploy") { foreach (var step in DeploySteps(controller)) yield return step; yield break; }

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
                            if (deploy.TryValidate(out error)) { fit = true; Log("layout density=" + density + " gap=" + gapX); break; }
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
            var manager = controller.manager; mgr = manager;
            int n = manager.Buffers.AgentCount;
            bool enemyAttack = controller.IssueOrder(ArmyOrder.Attack(1));
            controller.SelectArmy(0);
            Log("battle running agents=" + n + " enemyAttack=" + enemyAttack + " selectedTeam=" + controller.selectedTeam);
            yield return Get("warm", 6f);

            var hud = UnityEngine.Object.FindFirstObjectByType<WarSandboxCommandHUD>();
            var camera = UnityEngine.Object.FindFirstObjectByType<MyCameraManager>();
            if (hud == null) { Log("ERROR no HUD"); yield break; }
            var feedbackField = typeof(WarSandboxCommandHUD).GetField("commandFeedback", BindingFlags.Instance | BindingFlags.NonPublic);

            // Frame team 0 and box-select a part of it through the real HUD selection path.
            var teams = new int[n]; manager.Buffers.combatBuffers.teamIdBuffer.GetData(teams);
            var agents = new AgentData[n]; manager.Buffers.agentBuffer.GetData(agents);
            Vector3 c0 = Vector3.zero, c1 = Vector3.zero; int n0 = 0, n1 = 0;
            for (int i = 0; i < n; i++) { if (teams[i] == 0) { c0 += agents[i].position; n0++; } else { c1 += agents[i].position; n1++; } }
            c0 /= Mathf.Max(1, n0); c1 /= Mathf.Max(1, n1);
            if (camera != null) camera.FocusBounds(new Bounds(c0, Vector3.one * Get("focus", 60f)));
            yield return 3f;
            int[] members = null;
            foreach (float f in new[] { 0.25f, 0.15f, 0.35f, 0.1f, 0.5f, 0.06f, 0.7f })
            {
                float W = Screen.width, H = Screen.height;
                var a = new Vector2(W * (0.5f - f / 2), H * (0.5f - f / 2)); var b = new Vector2(W * (0.5f + f / 2), H * (0.5f + f / 2));
                hud.TestSelectionPointerP7(a, true, true, false);
                hud.TestSelectionPointerP7(b, false, true, false);
                hud.TestSelectionPointerP7(b, false, false, true);
                double t0 = EditorApplication.timeSinceStartup; int[] ids = null;
                while (EditorApplication.timeSinceStartup - t0 < 4)
                {
                    yield return 0.1f;
                    var sel = hud.SelectionPreview;
                    if (sel != null && sel.State.Scope == MemberSelectionScope.Local && sel.TrySnapshot(out ids, out _)) break;
                    if (sel != null && sel.State.Scope == MemberSelectionScope.Empty) { ids = Array.Empty<int>(); break; }
                    ids = null;
                }
                Log("select f=" + f + " screen=" + Screen.width + "x" + Screen.height + " count=" + (ids == null ? -1 : ids.Length) + " status=" + hud.TestSelectionStatusP7().Replace("\n", " "));
                if (ids != null && ids.Length >= minSel && ids.Length <= maxSel) { members = ids; break; }
            }
            if (members == null) { Log("ERROR could not make a selection in range"); yield break; }
            var unitTypes = new int[n]; manager.Buffers.unitTypeIndexBuffer.GetData(unitTypes);
            var typeHist = members.GroupBy(i => unitTypes[i]).Select(g => "type" + g.Key + "=" + g.Count());
            var maskProp = typeof(MassGpuBufferManager).GetProperty("LocalPartitionTypeMask", BindingFlags.Instance | BindingFlags.NonPublic);
            Result("# P4 command latency " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " entry=" + entry + " agents=" + n + " selected=" + members.Length +
                   " types[" + string.Join(",", typeHist) + "] partitionTypeMask=" + (maskProp != null ? maskProp.GetValue(manager.Buffers) : "?") +
                   " localOrdersBefore=" + (manager.LocalOrders != null) + " unity=" + Application.unityVersion + " gfx=" + SystemInfo.graphicsDeviceType);

            string cacheRoot = Path.GetFullPath(Path.Combine("Library", "ShaderCache"));
            for (int round = 0; round < rounds; round++)
            {
                foreach (string rawKind in kinds)
                {
                    string k = rawKind.Trim();
                    ArmyOrderType kind = k == "attack" ? ArmyOrderType.Attack : k == "hold" ? ArmyOrderType.Hold : k == "retreat" ? ArmyOrderType.Retreat : ArmyOrderType.Move;
                    // Move target: 35 m from the selection centroid toward the enemy (outside the window).
                    var pos = new Vector2[n]; manager.Buffers.agentPositionReadBuffer.GetData(pos);
                    Vector2 cs = Vector2.zero; foreach (int i in members) cs += pos[i]; cs /= members.Length;
                    Vector2 dir = (new Vector2(c1.x, c1.z) - cs).normalized; if (dir.sqrMagnitude < 0.5f) dir = Vector2.right;
                    Vector2 tgt = cs + dir * (round % 2 == 0 ? 35f : -25f);
                    int gc0 = GC.CollectionCount(0), gc2 = GC.CollectionCount(2);
                    var cacheStart = DateTime.Now;
                    yield return 0.5f;
                    StartWindow(members, kind == ArmyOrderType.Hold ? 1f : 0f);
                    var sw = Stopwatch.StartNew(); bool ok = false; int attempts = 0; double acceptT = -1, firstCallMs = -1; string reject = null;
                    var previous = hud.LastPlayerReceipt;
                    while (true)
                    {
                        attempts++;
                        var call = Stopwatch.StartNew();
                        ok = hud.TestPlayerOrderP8(kind, new Vector3(tgt.x, c0.y, tgt.y));
                        call.Stop(); if (firstCallMs < 0) firstCallMs = call.Elapsed.TotalMilliseconds;
                        if (ok && hud.LastPlayerReceipt != null && hud.LastPlayerReceipt != previous) { receipt = hud.LastPlayerReceipt; acceptT = sw.Elapsed.TotalSeconds; break; }
                        reject = feedbackField != null ? (feedbackField.GetValue(hud) as string ?? "").Replace("\n", " ") : "?";
                        if (sw.Elapsed.TotalSeconds > 10) break;
                        yield return 0.1f;
                    }
                    yield return window;
                    recording = false;
                    var r = receipt;
                    var sorted = frames.OrderBy(x => x).ToList();
                    double max = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0, med = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0;
                    double over100 = frames.Where(x => x > 0.1).Sum();
                    // Shader cache files written during this window (scan outside the window).
                    var newCache = new List<string>();
                    try
                    {
                        if (Directory.Exists(cacheRoot))
                            foreach (var file in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories))
                            {
                                var w = File.GetLastWriteTime(file);
                                if (w >= cacheStart) newCache.Add(((w - cacheStart).TotalSeconds - 0.5).ToString("F2") + "s:" + file.Substring(cacheRoot.Length).Replace('\\', '/'));
                            }
                    }
                    catch (Exception e) { newCache.Add("scan error " + e.Message); }
                    var sb = new StringBuilder();
                    sb.Append("CMD r").Append(round + 1).Append(' ').Append(k).Append(" members=").Append(members.Length)
                      .Append(" accepted=").Append(ok).Append(" attempts=").Append(attempts).Append(" acceptT=").Append(acceptT.ToString("F2"))
                      .Append(" submitCallMs=").Append(firstCallMs.ToString("F1"))
                      .Append(" | readbackMs=").Append(r != null ? r.ReadbackMilliseconds.ToString("F0") : "-")
                      .Append(" planMs=").Append(r != null ? r.PlanMilliseconds.ToString("F0") : "-")
                      .Append(" uploadMs=").Append(r != null ? r.UploadMilliseconds.ToString("F1") : "-")
                      .Append(" commitT=").Append(commitT.ToString("F2")).Append(" gpuExecT=").Append(gpuT.ToString("F2"))
                      .Append(" submitted=").Append(r != null ? r.SubmittedMembers : 0).Append(" gpuExec=").Append(r != null ? r.GpuExecutingMembers : 0)
                      .Append(" status=").Append(r != null ? r.Status.ToString() : "none").Append(r != null && r.Error != null ? " err=" + r.Error : "")
                      .Append(r != null && r.NavigationError != null ? " navErr=" + r.NavigationError : "")
                      .Append(" | frames=").Append(frames.Count).Append(" medMs=").Append((med * 1000).ToString("F1")).Append(" maxMs=").Append((max * 1000).ToString("F0"))
                      .Append(" sumOver100ms=").Append((over100 * 1000).ToString("F0"))
                      .Append(" | move50T=").Append(move50T.ToString("F2")).Append(" still80T=").Append(still80T.ToString("F2"))
                      .Append(" | gc0+=").Append(GC.CollectionCount(0) - gc0).Append(" gc2+=").Append(GC.CollectionCount(2) - gc2)
                      .Append(" | shaderCacheNew=").Append(newCache.Count)
                      .Append(" groups=").Append(manager.LocalOrders != null ? manager.LocalOrders.ActiveGroups : -1);
                    if (reject != null && !ok) sb.Append("\n   lastReject: ").Append(reject);
                    sb.Append("\n   events: ").Append(string.Join(" ; ", events));
                    sb.Append("\n   longFrames(>50ms): ").Append(string.Join(" ", longFrames.Take(40)));
                    sb.Append("\n   motion: ").Append(string.Join(" ", motion.Take(32)));
                    if (newCache.Count > 0) sb.Append("\n   cache: ").Append(string.Join(" ", newCache.OrderBy(x => x).Take(12)));
                    Result(sb.ToString());
                    Log("done r" + (round + 1) + " " + k + " max=" + (max * 1000).ToString("F0") + "ms commitT=" + commitT.ToString("F2"));
                    yield return 1f;
                }
            }
            Log("all commands done");
        }

        // Deployment B check (2026-10-12): spatial problems are written to the draft; hard problems are refused;
        // apply/save stay blocked until the draft is valid; Undo restores.
        static IEnumerable<object> DeploySteps(WarSandboxBattleController controller)
        {
            var deploy = controller.GetComponent<WarSandboxRuntimeDeployment>();
            if (deploy == null) { Log("ERROR no deployment"); yield break; }
            if (!deploy.IsEditing && !deploy.TryBeginEdit(false, out string be)) { Log("ERROR TryBeginEdit " + be); yield break; }
            yield return 0.5f;
            var d = deploy.Draft;
            bool v0 = deploy.TryValidate(out string e0);
            Log("DEPLOY start count=" + d.Count + " valid=" + v0 + " " + e0 + " e0=" + d[0].count + "@" + d[0].center + " size=" + d[0].Size);
            // 1) +20000 on formation 1 (auto footprint grows) -> accepted, draft invalid for a spatial reason
            var o = d[0]; var n = o; n.count = o.count + (int)Get("add", 150000f);
            bool ok1 = deploy.TryEditFormation(d, d.Revision, 0, o, n, true, out string r1);
            bool v1 = deploy.TryValidate(out string e1);
            Log("DEPLOY add accepted=" + ok1 + " err=" + r1 + " | draftValid=" + v1 + " problem=" + e1 + " size=" + d[0].Size);
            // 2) drag while invalid -> accepted
            var o2 = d[0]; var c2 = o2.center + new Vector3(0, 0, 30f);
            bool ok2 = deploy.TryTranslateDraft(d, d.Revision, 0, o2, c2, out string r2);
            bool v2 = deploy.TryValidate(out string e2);
            Log("DEPLOY drag-while-invalid accepted=" + ok2 + " err=" + r2 + " | draftValid=" + v2 + " problem=" + e2);
            // 3) save and apply refused while invalid
            string r3 = "skipped (draft valid)"; bool s3 = !v2 && deploy.TrySavePlan("zz-deploy-b-probe", "probe", false, out r3);
            Log("DEPLOY save-while-invalid ok=" + s3 + " err=" + r3);
            string ra = "skipped (draft valid)"; bool a3 = !v2 && deploy.TryApply(out ra);
            Log("DEPLOY apply-while-invalid ok=" + a3 + " err=" + ra + " editing=" + deploy.IsEditing);
            // 4) hard limit: exceed the policy/total cap -> refused, draft unchanged
            var o4 = d[0]; var n4 = o4; n4.count = (int)Get("cap", 199500f);
            int rev = d.Revision;
            bool ok4 = deploy.TryEditFormation(d, d.Revision, 0, o4, n4, true, out string r4);
            Log("DEPLOY hard-cap accepted=" + ok4 + " err=" + r4 + " revisionUnchanged=" + (d.Revision == rev) + " count=" + d[0].count);
            // 5) undo twice -> back to the valid start
            d.Undo(); d.Undo();
            bool v5 = deploy.TryValidate(out string e5);
            Log("DEPLOY undo x2 valid=" + v5 + " " + e5 + " e0=" + d[0].count + "@" + d[0].center);
            deploy.CancelEditing();
            Log("DEPLOY done");
        }

        static Dictionary<string, string> request;
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
            float.TryParse(GetString(key, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        static string Stamp() => DateTime.Now.ToString("HH:mm:ss");
        static void Log(string line)
        {
            try { Directory.CreateDirectory(Dir); File.AppendAllText(StatusPath, Stamp() + " " + line + "\n"); } catch { }
            Debug.Log("[CmdLatency] " + line);
        }
        static void Result(string line)
        {
            try { Directory.CreateDirectory(Dir); File.AppendAllText(ResultPath, line + "\n"); } catch { }
        }
    }
}
