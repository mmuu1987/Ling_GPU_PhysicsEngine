using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MassEngine.Game.EditorTools
{
    /// <summary>
    /// 兵种平衡 (2026-10-14): equal-budget duels, numbers only. Enters Play, loads one flat battlefield, then for each
    /// pair in Logs/Chal/duel_request.txt redeploys A (army 0) vs B (army 1), each side buying floor(budget / price)
    /// units, runs at 4x until annihilation or the game-time limit, and appends one CSV row. mirror=1 also runs B vs A
    /// (cancels side bias). Local draft stat overrides are cleared; global overrides (if any) still apply and are logged.
    /// Request keys (key=value lines): entry, budget, gap, limit (game s), speed, mirror, aspect, timeout (real s),
    /// pairs=idA:idB;idA:idB ...  Output: Logs/Chal/duel-*.csv, status in Logs/Chal/duel_status.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class BalanceDuelProbe
    {
        const string Menu = "Tools/挑战模式/等预算对战测试（自动进 Play，自动退出）";
        const string Dir = "Logs/Chal";
        const string ActiveKey = "BalanceDuelProbe.Active";
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static string StatusPath => Path.Combine(Dir, "duel_status.txt");
        static IEnumerator routine;
        static double waitUntil, deadline;
        static Dictionary<string, string> request;

        static BalanceDuelProbe()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (EditorApplication.isPlaying) Begin();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup("domain reload outside Play");
            }
        }

        [MenuItem(Menu, false, 60)]
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
            deadline = EditorApplication.timeSinceStartup + Get("timeout", 3000f);
            EditorApplication.update += Tick;
            Log("play entered, routine started");
        }

        static void Cleanup(string why)
        {
            EditorApplication.update -= Tick;
            routine = null;
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.playModeStartScene = null;
            Time.timeScale = 1f;
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
            EditorApplication.update -= Tick;
            routine = null;
            Time.timeScale = 1f;
            if (EditorApplication.isPlaying) { Log("exiting play"); EditorApplication.ExitPlaymode(); }
            else Cleanup("stopped outside Play");
        }

        static IEnumerator Steps()
        {
            string entry = GetString("entry", "plains-1024");
            int budget = (int)Get("budget", 10000f);
            float gap = Get("gap", 40f), limit = Get("limit", 150f), speed = Get("speed", 4f), aspect = Get("aspect", 2.5f);
            bool mirror = Get("mirror", 1f) != 0f;
            string tag = GetString("tag", "duel");
            var pairs = new List<(string, string)>();
            foreach (var p in GetString("pairs", "").Split(';'))
            {
                var ab = p.Trim().Split(':');
                if (ab.Length == 2 && ab[0].Length > 0 && ab[1].Length > 0) pairs.Add((ab[0].Trim(), ab[1].Trim()));
            }
            if (pairs.Count == 0) { Log("ERROR no pairs"); yield break; }
            string csv = Path.Combine(Dir, tag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            File.WriteAllText(csv, "a,b,priceA,priceB,nA,nB,result,winner,aliveA,aliveB,fracA,fracB,score,gameSeconds,realSeconds,note\n", new UTF8Encoding(false));
            Log("CSV " + csv + " pairs=" + pairs.Count + " mirror=" + mirror + " budget=" + budget);

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
            var controller = session.Controller;
            var deploy = controller != null ? controller.GetComponent<WarSandboxRuntimeDeployment>() : null;
            if (deploy == null || deploy.battlefieldCatalog == null) { Log("ERROR no deployment/catalog"); yield break; }
            var catalog = deploy.battlefieldCatalog;
            Log("entered " + entry + " phase=" + controller.Phase + " globalStatsFile=" + System.IO.File.Exists(deploy.StatStore.FilePath));
            yield return 1f;

            var runs = new List<(string, string)>();
            foreach (var p in pairs) { runs.Add(p); if (mirror && p.Item1 != p.Item2) runs.Add((p.Item2, p.Item1)); }
            int index = 0;
            foreach (var run in runs)
            {
                index++;
                // Side spec: "id" or "id+id*0.4" (first part in front, the rest stacked behind; weights split the budget).
                var sideA = Side(catalog, run.Item1, 0); var sideB = Side(catalog, run.Item2, 1);
                if (sideA == null || sideB == null) { Row(csv, run.Item1, run.Item2, 0, 0, 0, 0, "missing", -1, 0, 0, 0, 0, "template not found"); continue; }
                if (!deploy.TryBeginEdit(true, out error)) { Row(csv, run.Item1, run.Item2, 0, 0, 0, 0, "edit", -1, 0, 0, 0, 0, Clean(error)); continue; }
                var all = new List<Part>(sideA); all.AddRange(sideB);
                // Probe only: launch-preset templates are not on this battlefield's roster; admit them for this Play session (nothing saved).
                var list = typeof(WarSandboxRuntimeDeployment).GetField("templates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(deploy) as List<UnitTypeConfig>;
                if (list != null) foreach (var part in all) if (!list.Contains(part.t)) list.Add(part.t);
                // ...and lift this battlefield's roster policy for the session (Play-mode field change, reverted on exit).
                var policyField = typeof(WarSandboxRuntimeDeployment).GetField("rosterPolicy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (policyField != null && GetString("keepPolicy", "0") != "1") policyField.SetValue(deploy, null);
                yield return 0.3f;
                var rules = deploy.Draft.Rules;
                rules.gameMode = WarSandboxGameMode.Annihilation;
                rules.challengeEnabled = false; rules.budgetEnabled = false;
                // Trial stat overrides (draft-local, never saved): stats=id.Stat=value|id.Stat=value
                var placeholders = new List<WarSandboxDeploymentEntry>();
                foreach (var part in all) placeholders.Add(Make(part.t, part.team, 10, aspect));
                deploy.Draft.Replace(placeholders.ToArray(), rules, new WarSandboxStatOverrides());
                foreach (var kv in StatOverrides())
                {
                    var t = Find(catalog, kv.Item1);
                    if (t != null && all.Exists(x => x.t == t) && !deploy.Draft.SetStat(t, kv.Item2, kv.Item3)) Log("WARN SetStat refused " + kv.Item1 + "." + kv.Item2 + "=" + kv.Item3);
                }
                var trialStats = deploy.Draft.Stats;
                var resolver = deploy.DraftStats;
                int na = 0, nb = 0, pa = 0, pb = 0;
                foreach (var part in all)
                {
                    part.price = Mathf.Max(1, Mathf.RoundToInt(WarSandboxUnitCost.UnitPrice(resolver, part.t) * PriceScale(part.id)));
                    part.count = Mathf.Max(1, Mathf.FloorToInt(budget * part.w / part.price));
                    if (part.team == 0) { na += part.count; pa += part.count * part.price; } else { nb += part.count; pb += part.count * part.price; }
                }
                bool fit = false;
                foreach (float g in new[] { gap, gap * 1.5f, gap * 0.75f })
                {
                    var entries = new List<WarSandboxDeploymentEntry>();
                    foreach (var side in new[] { sideA, sideB })
                    {
                        float sign = side[0].team == 0 ? -1f : 1f, offset = g * .5f;
                        foreach (var part in side)
                        {
                            var e = Rotate(Make(part.t, part.team, part.count, aspect));
                            e.center = new Vector3(sign * (offset + e.Size.x * .5f), 0f, 0f);
                            offset += e.Size.x + 4f;
                            entries.Add(e);
                        }
                    }
                    deploy.Draft.Replace(entries.ToArray(), rules, trialStats.Clone());
                    if (deploy.TryValidate(out error)) { fit = true; break; }
                }
                // pa/pb columns hold the money actually spent per side.
                if (!fit) { Row(csv, run.Item1, run.Item2, pa, pb, na, nb, "layout", -1, 0, 0, 0, 0, Clean(error)); continue; }
                if (!deploy.TryApply(out error)) { Row(csv, run.Item1, run.Item2, pa, pb, na, nb, "apply", -1, 0, 0, 0, 0, Clean(error)); continue; }
                yield return 0.5f;
                if (controller.Phase != WarSandboxBattlePhase.Running) controller.StartOrResumeBattle();
                double wait = EditorApplication.timeSinceStartup + 30;
                while (controller.Phase != WarSandboxBattlePhase.Running && EditorApplication.timeSinceStartup < wait) yield return 0.2f;
                if (controller.Phase != WarSandboxBattlePhase.Running) { Row(csv, run.Item1, run.Item2, pa, pb, na, nb, "start", -1, 0, 0, 0, 0, controller.Phase.ToString()); continue; }
                controller.SetSimulationSpeed(speed);
                var shots0 = Shots(controller.manager);
                float t0 = Time.time; double r0 = EditorApplication.timeSinceStartup; int f0 = Time.frameCount;
                yield return 1f;
                int iaInit = Mathf.Max(1, controller.GetAliveUnitCount(0)), ibInit = Mathf.Max(1, controller.GetAliveUnitCount(1));
                var samples = new List<float>();
                foreach (var tok in GetString("sample", "").Split(',')) if (float.TryParse(tok, NumberStyles.Float, IC, out float sv)) samples.Add(sv);
                int nextSample = 0;
                while (controller.Phase == WarSandboxBattlePhase.Running && Time.time - t0 < limit)
                {
                    if (nextSample < samples.Count && Time.time - t0 >= samples[nextSample])
                    {
                        RangedStates(controller.manager, all, run.Item1 + " vs " + run.Item2, Time.time - t0);
                        nextSample++;
                    }
                    yield return 0.25f;
                }
                float gameSeconds = Time.time - t0; double realSeconds = EditorApplication.timeSinceStartup - r0;
                int aa = controller.GetAliveUnitCount(0), ab2 = controller.GetAliveUnitCount(1);
                string result = controller.Phase == WarSandboxBattlePhase.Running ? "timeout" : controller.Phase.ToString();
                int winner = aa > 0 && ab2 == 0 ? 0 : ab2 > 0 && aa == 0 ? 1 : -1;
                var shots1 = Shots(controller.manager);
                int frames = Mathf.Max(1, Time.frameCount - f0);
                string prices = "frames=" + frames + " dt=" + (gameSeconds / frames).ToString("0.000", IC) + " shots req=" + (shots1.Item1 - shots0.Item1) + " launched=" + (shots1.Item2 - shots0.Item2) + " deferred=" + (shots1.Item3 - shots0.Item3) + " ";
                foreach (var part in all) prices += part.id + "@" + part.price + "x" + part.count + " ";
                Row(csv, run.Item1, run.Item2, pa, pb, na, nb, result, winner, aa, ab2, gameSeconds, (float)realSeconds,
                    "spawned " + iaInit + "/" + ibInit + " " + prices.Trim());
                Log("[" + index + "/" + runs.Count + "] " + run.Item1 + " vs " + run.Item2 + " -> " + result + " alive " + aa + "/" + na + " : " + ab2 + "/" + nb + " t=" + gameSeconds.ToString("0", IC));
                if (controller.Phase == WarSandboxBattlePhase.Running) controller.PauseBattle();
                Time.timeScale = 1f;
                yield return 0.3f;
            }
            Log("ALL DONE " + csv);
        }

        // Ranged diagnostics: cumulative launch requests, launched projectiles, pool-capacity deferrals.
        // Ranged readiness census (diagnostics only, synchronous GPU readback): for every alive agent
        // of each ranged team, why it is or is not firing right now. Appends to Logs/Chal/rstate.txt.
        static void RangedStates(MassEngineManager manager, List<Part> parts, string label, float t)
        {
            var buffers = manager != null ? manager.Buffers : null;
            if (buffers == null || buffers.agentBuffer == null) return;
            int n = buffers.agentBuffer.count;
            var agents = new MassEngine.AgentData[n]; buffers.agentBuffer.GetData(agents);
            var pos = new Vector2[n]; buffers.agentPositionReadBuffer.GetData(pos);
            var team = new int[n]; buffers.combatBuffers.teamIdBuffer.GetData(team);
            var hp = new int[n]; buffers.combatBuffers.hpReadBuffer.GetData(hp);
            var target = new int[n]; buffers.combatBuffers.targetAgentIndexBuffer.GetData(target);
            var slots = new int[buffers.combatBuffers.engagementSlotAssignmentBuffer.count];
            buffers.combatBuffers.engagementSlotAssignmentBuffer.GetData(slots);
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                var combat = part.t != null ? part.t.combatConfig : null;
                if (combat == null || combat.projectileRange <= 0.01f) continue;
                float range = combat.projectileRange;
                int alive = 0, attack = 0, noTarget = 0, blocked = 0, inRangeOther = 0, outMoving = 0, outStuck = 0;
                var outBand = new int[6]; // distance beyond range: <2, <5, <10, <20, <40, >=40 m
                for (int i = 0; i < n; i++)
                {
                    if (team[i] != part.team || hp[i] <= 0) continue;
                    alive++;
                    var a = agents[i];
                    if (a.currentState == (int)MassEngine.AgentState.Attack) { attack++; continue; }
                    int tg = target[i];
                    if (tg < 0 || tg >= n || hp[tg] <= 0) { noTarget++; continue; }
                    float d = Vector2.Distance(pos[i], pos[tg]);
                    long word = (long)n + (long)i * 13 + 7;
                    float status = word < slots.Length ? BitConverter.ToSingle(BitConverter.GetBytes(slots[word]), 0) : 0f;
                    if (d <= range + 0.5f)
                    {
                        if (status < 0f) blocked++; else inRangeOther++;
                        continue;
                    }
                    float over = d - range;
                    outBand[over < 2 ? 0 : over < 5 ? 1 : over < 10 ? 2 : over < 20 ? 3 : over < 40 ? 4 : 5]++;
                    if (new Vector2(a.velocity.x, a.velocity.z).magnitude > 0.5f) outMoving++; else outStuck++;
                }
                sb.AppendLine(string.Format(IC, "{0} | t={1:0} {2} team{3} alive={4} attack={5} blockedInRange={6} inRangeOther={7} outOfRange={8} (moving {9}, stuck {10}; beyond <2:{11} <5:{12} <10:{13} <20:{14} <40:{15} 40+:{16}) noTarget={17}",
                    label, t, part.id, part.team, alive, attack, blocked, inRangeOther, outMoving + outStuck, outMoving, outStuck,
                    outBand[0], outBand[1], outBand[2], outBand[3], outBand[4], outBand[5], noTarget));
            }
            if (sb.Length > 0) File.AppendAllText("Logs/Chal/rstate.txt", sb.ToString(), new UTF8Encoding(false));
        }

        static (long, long, long) Shots(MassEngineManager manager)
        {
            if (manager == null) return (0, 0, 0);
            var pm = typeof(MassEngineManager).GetField("projectileManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(manager) as MassEngine.Projectiles.ProjectileGpuManager;
            long req = pm != null ? (long)pm.TotalRequested : 0, def = pm != null ? (long)pm.CapacityDeferralCount : 0;
            return (req, (long)manager.TotalLaunchedProjectiles, def);
        }

        sealed class Part { public string id; public UnitTypeConfig t; public int team; public float w; public int price, count; }

        static List<Part> Side(WarSandboxBattlefieldCatalog catalog, string spec, int team)
        {
            var parts = new List<Part>();
            float total = 0f;
            foreach (var token in spec.Split('+'))
            {
                var bits = token.Split('*');
                float w = bits.Length > 1 && float.TryParse(bits[1], NumberStyles.Float, IC, out float v) && v > 0f ? v : -1f;
                var t = Find(catalog, bits[0].Trim());
                if (t == null) return null;
                parts.Add(new Part { id = bits[0].Trim(), t = t, team = team, w = w });
            }
            float given = 0f; int free = 0;
            foreach (var p in parts) if (p.w > 0f) given += p.w; else free++;
            foreach (var p in parts) if (p.w <= 0f) p.w = free > 0 ? Mathf.Max(0f, 1f - given) / free : 0f;
            foreach (var p in parts) total += p.w;
            if (total <= 0f) return null;
            foreach (var p in parts) p.w /= total;
            return parts;
        }

        static List<(string, WarSandboxUnitStat, float)> StatOverrides()
        {
            var list = new List<(string, WarSandboxUnitStat, float)>();
            foreach (var item in GetString("stats", "").Split('|'))
            {
                int eq = item.IndexOf('='), dot = eq > 0 ? item.LastIndexOf('.', eq) : -1;
                if (dot <= 0) continue;
                if (Enum.TryParse(item.Substring(dot + 1, eq - dot - 1).Trim(), out WarSandboxUnitStat stat) &&
                    float.TryParse(item.Substring(eq + 1).Trim(), NumberStyles.Float, IC, out float v))
                    list.Add((item.Substring(0, dot).Trim(), stat, v));
            }
            return list;
        }

        // Trial price multipliers: pmul=id:1.3|id:0.8
        static float PriceScale(string id)
        {
            foreach (var item in GetString("pmul", "").Split('|'))
            {
                var kv = item.Split(':');
                if (kv.Length == 2 && kv[0].Trim() == id && float.TryParse(kv[1].Trim(), NumberStyles.Float, IC, out float v) && v > 0f) return v;
            }
            return 1f;
        }

        static WarSandboxDeploymentEntry Make(UnitTypeConfig t, int team, int count, float aspect)
        {
            var e = WarSandboxDeploymentEntry.From(t);
            e.teamId = team; e.count = count; e.manualSize = Vector3.zero; e.aspect = aspect; e.facing = 0;
            e.FitDensityToBody();
            // Huge bodies with few units: grid rounding can leave a gap under 2r; grow the explicit footprint until it fits.
            if (e.manualSize.x > 0f && t.flockingConfig != null)
                for (int i = 0; i < 40 && !WarSandboxDeploymentResize.Fits(e, t.flockingConfig.agentRadius, .08f, out _); i++)
                    e.manualSize = new Vector3(e.manualSize.x * 1.05f, e.manualSize.y, e.manualSize.z * 1.05f);
            return e;
        }

        // Aspect is width/depth along x/z; for armies facing along x the long side must lie along z.
        static WarSandboxDeploymentEntry Rotate(WarSandboxDeploymentEntry e)
        {
            if (e.Size.x > e.Size.z && e.aspect > 0f) e.aspect = 1f / e.aspect;
            return e;
        }

        static UnitTypeConfig Find(WarSandboxBattlefieldCatalog catalog, string id)
        {
            foreach (var t in catalog.templates) if (t != null && t.templateId == id) return t.config;
            return null;
        }

        static string Clean(string s) => (s ?? "").Replace(',', ';').Replace('\n', ' ');

        static void Row(string csv, string a, string b, int pa, int pb, int na, int nb, string result, int winner,
            int aa, int ab, float gameSeconds, float realSeconds, string note)
        {
            float fa = na > 0 ? aa / (float)na : 0f, fb = nb > 0 ? ab / (float)nb : 0f;
            string line = string.Join(",", a, b, pa.ToString(IC), pb.ToString(IC), na.ToString(IC), nb.ToString(IC), result, winner.ToString(IC),
                aa.ToString(IC), ab.ToString(IC), fa.ToString("0.000", IC), fb.ToString("0.000", IC), (fa - fb).ToString("0.000", IC),
                gameSeconds.ToString("0.0", IC), realSeconds.ToString("0.0", IC), Clean(note));
            File.AppendAllText(csv, line + "\n");
            if (result == "missing" || result == "edit" || result == "layout" || result == "apply" || result == "start") Log("SKIP " + a + " vs " + b + " " + result + " " + note);
        }

        static Dictionary<string, string> Request()
        {
            if (request != null) return request;
            request = new Dictionary<string, string>();
            string path = Path.Combine(Dir, "duel_request.txt");
            if (File.Exists(path))
                foreach (var raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq > 0) request[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
                }
            return request;
        }

        static float Get(string key, float fallback) =>
            Request().TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, IC, out float v) ? v : fallback;

        static string GetString(string key, string fallback) => Request().TryGetValue(key, out var s) && s.Length > 0 ? s : fallback;

        static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

        static void Log(string line)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(StatusPath, Stamp() + " " + line + "\n");
            Debug.Log("[BalanceDuelProbe] " + line);
        }
    }
}
