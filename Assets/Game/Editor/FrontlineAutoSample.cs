// Editor-only, unattended front-line sampling for remote tuning sessions (2026-10-12 round 6).
// Menu (or the remote runner) -> enters Play from the MainMenu scene, enters a battlefield with its DEFAULT deployment,
// starts the battle, waits for contact, frames the contact centre with the camera, runs FrontlineFlickerProbe
// N times and leaves Play. Nothing in the simulation is modified. Progress: Logs/AutoSample/status.txt.
// Optional Logs/AutoSample/request.txt (key=value lines): entry, samples, gap, settle, focus, contact, timeout.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    [InitializeOnLoad]
    public static class FrontlineAutoSample
    {
        const string Menu = "Tools/战场 1024/无人值守前线采样（自动进 Play，自动退出）";
        const string Dir = "Logs/AutoSample";
        const string ActiveKey = "FrontlineAutoSample.Active";
        static string StatusPath => Path.Combine(Dir, "status.txt");
        static string RequestPath => Path.Combine(Dir, "request.txt");
        static IEnumerator routine;
        static double waitUntil;
        static double deadline;

        static FrontlineAutoSample()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (EditorApplication.isPlaying) Begin();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup("domain reload outside Play");
            }
        }

        [MenuItem(Menu, false, 42)]
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
            RestoreLod();
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

        // request.txt "lod.<field>=<value>" lines override LodConfig fields for this session only; the
        // originals are restored before leaving Play (the asset itself is never marked dirty or saved).
        static readonly List<KeyValuePair<System.Reflection.FieldInfo, object>> lodOriginals = new List<KeyValuePair<System.Reflection.FieldInfo, object>>();
        static UnityEngine.Object lodTarget;
        static void ApplyLodOverrides(UnityEngine.Object lod)
        {
            lodOriginals.Clear(); lodTarget = lod;
            if (lod == null) { Log("no LodConfig"); return; }
            GetString("entry", "");
            foreach (var kv in request)
            {
                if (!kv.Key.StartsWith("lod.")) continue;
                var field = lod.GetType().GetField(kv.Key.Substring(4));
                if (field == null) { Log("unknown lod field " + kv.Key); continue; }
                object value;
                if (field.FieldType == typeof(float)) value = float.Parse(kv.Value, System.Globalization.CultureInfo.InvariantCulture);
                else if (field.FieldType == typeof(int)) value = int.Parse(kv.Value);
                else if (field.FieldType == typeof(bool)) value = bool.Parse(kv.Value);
                else { Log("unsupported lod field type " + kv.Key); continue; }
                lodOriginals.Add(new KeyValuePair<System.Reflection.FieldInfo, object>(field, field.GetValue(lod)));
                field.SetValue(lod, value);
                Log("lod override " + field.Name + " = " + value);
            }
        }
        static void RestoreLod()
        {
            if (lodTarget != null)
                foreach (var kv in lodOriginals) kv.Key.SetValue(lodTarget, kv.Value);
            if (lodOriginals.Count > 0) Log("lod overrides restored");
            lodOriginals.Clear(); lodTarget = null;
        }

        static void Stop()
        {
            RestoreLod();
            EditorApplication.update -= Tick;
            routine = null;
            if (EditorApplication.isPlaying) { Log("exiting play"); EditorApplication.ExitPlaymode(); }
            else Cleanup("stopped outside Play");
        }

        static IEnumerator Steps()
        {
            string entry = GetString("entry", "forest-hills-1024");
            int samples = (int)Get("samples", 2f);
            float gap = Get("gap", 12f), settle = Get("settle", 12f), focus = Get("focus", 24f);
            int contact = (int)Get("contact", 400f);
            int units = (int)Get("units", 110000f);

            WarSandboxSceneSession session = null;
            double until = EditorApplication.timeSinceStartup + 120;
            while ((session = WarSandboxSceneSession.Instance) == null || session.IsLoading)
            {
                if (EditorApplication.timeSinceStartup > until) { Log("ERROR no menu session"); yield break; }
                yield return 0.25f;
            }
            Log("menu state=" + session.State);
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
            ApplyLodOverrides(controller.manager != null && controller.manager.systemConfig != null ? controller.manager.systemConfig.lodConfig : null);
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
                            Log("layout rejected density=" + density + " gap=" + gapX + ": " + error);
                        }
                        if (fit) break;
                    }
                    if (!fit) { Log("ERROR no valid layout for " + units); yield break; }
                    yield return 0.3f;
                }
                if (deploy.IsEditing)
                {
                    if (!deploy.TryApply(out error)) { Log("ERROR TryApply " + error); yield break; }
                    Log("deployment applied");
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
            int n = manager.Buffers.AgentCount;
            Log("battle running agents=" + n);
            string orders = GetString("orders", "attack,attack");
            var parts = orders.Split(',');
            for (int team = 0; team < parts.Length && team < 2; team++)
            {
                string o = parts[team].Trim();
                bool ok = o == "attack" ? controller.IssueOrder(ArmyOrder.Attack(team)) :
                          o == "hold" ? controller.IssueOrder(ArmyOrder.Hold(team)) : true;
                Log("order team " + team + " " + o + " -> " + ok);
            }

            var agents = new AgentData[n];
            double battleStart = EditorApplication.timeSinceStartup;
            int attack = 0;
            while (true)
            {
                yield return 1f;
                manager.Buffers.agentBuffer.GetData(agents);
                attack = 0;
                for (int i = 0; i < n; i++) if (agents[i].currentState == 3) attack++;
                if (attack >= Mathf.Min(contact, Mathf.Max(20, n / 200))) break;
                if (EditorApplication.timeSinceStartup - battleStart > 150) { Log("ERROR no contact after 150 s (attack=" + attack + ")"); yield break; }
            }
            Log("contact attack=" + attack + " after " + (EditorApplication.timeSinceStartup - battleStart).ToString("F1") + " s; settling " + settle + " s");
            yield return settle;

            var camera = UnityEngine.Object.FindFirstObjectByType<MyCameraManager>();
            for (int k = 0; k < samples; k++)
            {
                manager.Buffers.agentBuffer.GetData(agents);
                var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
                int alive = 0;
                for (int i = 0; i < n; i++)
                {
                    if (agents[i].currentState != 4) alive++;
                    if (agents[i].currentState != 3) continue;
                    xs.Add(agents[i].position.x); ys.Add(agents[i].position.y); zs.Add(agents[i].position.z);
                }
                if (xs.Count == 0) { Log("ERROR no attackers left"); yield break; }
                xs.Sort(); ys.Sort(); zs.Sort();
                var centre = new Vector3(xs[xs.Count / 2], ys[ys.Count / 2], zs[zs.Count / 2]);
                if (camera != null) camera.FocusBounds(new Bounds(centre, Vector3.one * focus));
                Log("sample " + (k + 1) + "/" + samples + " alive=" + alive + " attack=" + xs.Count + " centre=" + centre + " camera=" + (camera != null));
                yield return 1.5f;
                string fail = FrontlineFlickerProbe.RunUnattended();
                if (fail != null) { Log("ERROR probe " + fail); yield break; }
                until = EditorApplication.timeSinceStartup + 150;
                while (FrontlineFlickerProbe.Busy)
                {
                    if (EditorApplication.timeSinceStartup > until) { Log("ERROR probe timeout"); yield break; }
                    yield return 0.5f;
                }
                string latest = Path.Combine("Logs/Flicker1024", "latest.txt");
                Log("SAMPLE " + (File.Exists(latest) ? File.ReadAllText(latest).Trim() : "?"));
                if (k + 1 < samples) yield return gap;
            }
            Log("all samples done");
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
            Debug.Log("[AutoSample] " + line);
        }
            // Compile status of the combat compute shader (kernel validity + compiler messages), so a
        // broken kernel is caught in the Editor instead of as "Kernel at index (n) is invalid" in Play.
        [MenuItem("Tools/战场 1024/战斗着色器编译检查", false, 43)]
        static void CheckCombatShader()
        {
            const string path = "Assets/MassEngine/Simulation/Shaders/AgentCombatSimulation.compute";
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (cs == null) { Debug.Log("[ShaderCheck] not found"); return; }
            string[] kernels = { "ClearPendingDamage", "ClearDensityMap", "BuildDensityMap", "BuildEngagementSlotOccupancy",
                "SimulateCombatAndAccumulateDamage", "SimulateLocalAttackMelee", "SimulateLocalMoveMelee", "SimulateLocalHoldRanged" };
            foreach (var k in kernels)
            {
                bool has = cs.HasKernel(k);
                Debug.Log("[ShaderCheck] " + k + " has=" + has + (has ? " supported=" + cs.IsSupported(cs.FindKernel(k)) : ""));
            }
            int k4 = cs.FindKernel("SimulateCombatAndAccumulateDamage");
            bool terrainWas = Shader.IsKeywordEnabled("MASS_TERRAIN_ENABLED");
            var charge = new UnityEngine.Rendering.LocalKeyword(cs, "MASS_MELEE_CHARGE");
            var orders = new UnityEngine.Rendering.LocalKeyword(cs, "MASS_LOCAL_ORDERS");
            bool chargeWas = cs.IsKeywordEnabled(charge), ordersWas = cs.IsKeywordEnabled(orders);
            for (int v = 0; v < 8; v++)
            {
                if ((v & 1) != 0) Shader.EnableKeyword("MASS_TERRAIN_ENABLED"); else Shader.DisableKeyword("MASS_TERRAIN_ENABLED");
                cs.SetKeyword(charge, (v & 2) != 0);
                cs.SetKeyword(orders, (v & 4) != 0);
                Debug.Log("[ShaderCheck] variant terrain=" + ((v & 1) != 0) + " charge=" + ((v & 2) != 0) + " orders=" + ((v & 4) != 0) + " kernel4 supported=" + cs.IsSupported(k4));
            }
            if (terrainWas) Shader.EnableKeyword("MASS_TERRAIN_ENABLED"); else Shader.DisableKeyword("MASS_TERRAIN_ENABLED");
            cs.SetKeyword(charge, chargeWas); cs.SetKeyword(orders, ordersWas);
            int n = ShaderUtil.GetComputeShaderMessageCount(cs);
            var msgs = ShaderUtil.GetComputeShaderMessages(cs);
            int errors = 0;
            foreach (var m in msgs)
            {
                if (m.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) continue;
                errors++;
                if (errors <= 10) Debug.Log("[ShaderCheck] ERROR " + m.message + " | " + m.messageDetails + " | " + m.file + ":" + m.line + " " + m.platform);
            }
            Debug.Log("[ShaderCheck] messages=" + n + " errors=" + errors);
        }
            // C1 (2026-10-12): the spatial hash compute now stamps large bodies; same kernel/message check.
        [MenuItem("Tools/战场 1024/空间哈希着色器编译检查", false, 43)]
        static void CheckSpatialHashShader()
        {
            const string path = "Assets/MassEngine/Spatial/Shaders/AgentSpatialHash.compute";
            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            if (cs == null) { Debug.Log("[ShaderCheck] spatial hash not found"); return; }
            foreach (var k in new[] { "ClearGrid", "BuildSpatialHash", "ClearTeamSpatialStats", "BuildTeamSpatialStats" })
            {
                bool has = cs.HasKernel(k);
                Debug.Log("[ShaderCheck] " + k + " has=" + has + (has ? " supported=" + cs.IsSupported(cs.FindKernel(k)) : ""));
            }
            int errors = 0;
            foreach (var m in ShaderUtil.GetComputeShaderMessages(cs))
            {
                if (m.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) continue;
                errors++;
                if (errors <= 10) Debug.Log("[ShaderCheck] ERROR " + m.message + " | " + m.messageDetails + " | " + m.file + ":" + m.line + " " + m.platform);
            }
            Debug.Log("[ShaderCheck] spatial hash messages=" + ShaderUtil.GetComputeShaderMessageCount(cs) + " errors=" + errors);
        }
            // Console dump: why Play refuses to start ("All compiler errors have to be fixed").
        [MenuItem("Tools/战场 1024/控制台错误转储", false, 44)]
        static void DumpConsoleErrors()
        {
            Debug.Log("[ConsoleDump] scriptCompilationFailed=" + EditorUtility.scriptCompilationFailed);
            var asm = typeof(EditorWindow).Assembly;
            var logEntries = asm.GetType("UnityEditor.LogEntries");
            var logEntryType = asm.GetType("UnityEditor.LogEntry");
            if (logEntries == null || logEntryType == null) { Debug.Log("[ConsoleDump] no LogEntries"); return; }
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            int count = (int)logEntries.GetMethod("StartGettingEntries", flags).Invoke(null, null);
            var entry = Activator.CreateInstance(logEntryType);
            var get = logEntries.GetMethod("GetEntryInternal", flags);
            var msgField = logEntryType.GetField("message") ?? logEntryType.GetField("condition");
            var modeField = logEntryType.GetField("mode");
            int shown = 0;
            try
            {
                for (int i = 0; i < count && shown < 15; i++)
                {
                    get.Invoke(null, new object[] { i, entry });
                    string msg = msgField != null ? (string)msgField.GetValue(entry) : "";
                    int mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                    if (msg == null) continue;
                    bool err = msg.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 && msg.IndexOf("Shader warning", StringComparison.Ordinal) < 0;
                    if (!err) continue;
                    shown++;
                    string first = msg.Split('\n')[0];
                    Debug.Log("[ConsoleDump] mode=" + mode + " " + (first.Length > 400 ? first.Substring(0, 400) : first));
                }
            }
            finally { logEntries.GetMethod("EndGettingEntries", flags).Invoke(null, null); }
            Debug.Log("[ConsoleDump] entries=" + count + " errorsShown=" + shown);
        }
    }
}
