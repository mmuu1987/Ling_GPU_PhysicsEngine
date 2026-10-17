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
    /// 测试方案库 (2026-10-17): builds the showcase / test plans through the real game path (enter battlefield, begin
    /// edit, replace draft, validate, TrySavePlan), so battlefield/terrain versions and template revisions always match
    /// the live catalog. Each plan gets a map drawn with a fixed seed from the 2048 battlefields (plan 9 is pinned to
    /// 秋枫林地). Saved files land in persistentDataPath/WarSandboxPlans and are copied to TestPlans/ in the repo.
    /// 「安装到方案库」 copies TestPlans/*.json back into persistentDataPath/WarSandboxPlans (no Play needed).
    /// Status: Logs/TestPlans/status.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class TestPlanLibraryBuilder
    {
        const string MenuBuild = "Tools/测试方案/生成测试方案（自动进 Play，写入方案库）";
        const string MenuInstall = "Tools/测试方案/安装到方案库";
        const string Dir = "Logs/TestPlans";
        const string RepoDir = "TestPlans";
        const string ActiveKey = "TestPlanLibraryBuilder.Active";
        const int Seed = 20261017;
        static readonly string[] Maps2048 = { "test-plain-2048", "rolling-hills-2048", "ridge-pass-2048", "canyon-mesa-2048", "cliff-plateau-2048" };
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static IEnumerator routine;
        static double waitUntil, deadline;

        const string Male = "roster-male", Ranger = "roster-ranger", Rider = "roster-mounted-knight", Gunner = "roster-cc0-gunner",
            Artillery = "roster-cc0-artillery", Dragon = "roster-dragon", Kaiju = "roster-kaiju-dino";

        sealed class Part { public string id; public int count; public float weight; }
        sealed class Spec
        {
            public string slot, name, map;
            public Part[] a, b;            // listed front to back
            public int equalTotal;         // > 0: one part per side, counts set so both sides cost the same
            public int capA;               // equalTotal mode: cap on side A (large units: 2000 per battlefield)
            public float aspect = 2.5f;    // formation width / depth
            public bool pinned => map != null && Array.IndexOf(Maps2048, map) < 0;
        }

        static Part P(string id, int count) => new Part { id = id, count = count };
        // Riders are large units (battlefield cap 2000 for both sides together): 900 per side.
        static Part[] Mix(int total) => new[] { new Part { id = Male, weight = .6f }, new Part { id = Rider, weight = .045f }, new Part { id = Ranger, weight = .355f } }
            .Also(parts => { foreach (var p in parts) p.count = Mathf.RoundToInt(total * p.weight); });

        static List<Spec> Specs()
        {
            var list = new List<Spec>
            {
                new Spec { slot = "T01-artillery-show", name = "测试01 炮车炸飞展示：炮车 vs 男战士", a = new[] { P(Artillery, 1000) }, b = new[] { P(Male, 40000) } },
                new Spec { slot = "T02-artillery-gunner", name = "测试02 炮车 vs 机枪手（等价，前排各带男战士）", a = new[] { P(Male, 13000), P(Artillery, 1000) }, b = new[] { P(Male, 13000), P(Gunner, 0) } },
                new Spec { slot = "T03-gunner-male", name = "测试03 机枪手 vs 男战士（等价）", a = new[] { P(Gunner, 0) }, b = new[] { P(Male, 0) }, equalTotal = 35000 },
                new Spec { slot = "T04-rider-charge", name = "测试04 骑兵冲阵 vs 男战士（等价）", a = new[] { P(Rider, 1900) }, b = new[] { P(Male, 0) } },
                new Spec { slot = "T05-dragon-ranger", name = "测试05 龙 vs 弓箭手", a = new[] { P(Dragon, 60) }, b = new[] { P(Ranger, 30000) } },
                new Spec { slot = "T06-kaiju-male", name = "测试06 巨兽 vs 男战士", a = new[] { P(Kaiju, 20) }, b = new[] { P(Male, 40000) } },
                new Spec { slot = "T07-ranger-male", name = "测试07 弓箭手 vs 男战士（等价）", a = new[] { P(Ranger, 0) }, b = new[] { P(Male, 0) }, equalTotal = 40000 },
                new Spec { slot = "T08-mixed", name = "测试08 混编对战 2万 vs 2万", a = Mix(20000), b = Mix(20000) },
                // 秋枫林地 (1024, wooded) holds only ≈1.7万 in rectangular formations, below the 3万 floor: plan 9 is a
                // second mixed matchup on a seeded 2048 map instead (equal value: 322000 per side).
                new Spec { slot = "T09-mixed-gunner", name = "测试09 混编B：男战士+机枪手 vs 男战士+骑兵+弓箭手（等价）",
                    a = new[] { P(Male, 16000), P(Gunner, 1500) }, b = new[] { P(Male, 15000), P(Rider, 600), P(Ranger, 1225) } },
                new Spec { slot = "T10-male-extreme", name = "测试10 极限 男战士 2.5万 vs 2.5万", a = new[] { P(Male, 25000) }, b = new[] { P(Male, 25000) } },
            };
            var random = new System.Random(Seed);
            foreach (var s in list) { int pick = random.Next(Maps2048.Length); if (s.map == null) s.map = Maps2048[pick]; }
            return list;
        }

        // The seeded map first; a random (non-pinned) plan falls back to the other 2048 maps if its layout does not fit.
        static List<string> Candidates(Spec spec)
        {
            var list = new List<string> { spec.map };
            if (!spec.pinned) foreach (var m in Maps2048) if (!list.Contains(m)) list.Add(m);
            return list;
        }

        static T Also<T>(this T value, Action<T> action) { action(value); return value; }

        static TestPlanLibraryBuilder()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (EditorApplication.isPlaying) Begin();
                else if (!EditorApplication.isPlayingOrWillChangePlaymode) Cleanup("domain reload outside Play");
            }
        }

        [MenuItem(MenuInstall, false, 81)]
        public static void Install()
        {
            string target = Path.Combine(Application.persistentDataPath, "WarSandboxPlans");
            if (!Directory.Exists(RepoDir)) { Log("ERROR no " + RepoDir + " folder"); EditorUtility.DisplayDialog("测试方案", "找不到 " + RepoDir + " 文件夹。", "好"); return; }
            Directory.CreateDirectory(target);
            int n = 0;
            foreach (var file in Directory.GetFiles(RepoDir, "*.json")) { File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true); n++; }
            Log("installed " + n + " plans -> " + target);
            EditorUtility.DisplayDialog("测试方案", "已安装 " + n + " 个测试方案到方案库：\n" + target, "好");
        }

        [MenuItem(MenuBuild, false, 80)]
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
            EditorApplication.EnterPlaymode();
        }

        static string StatusPath => Path.Combine(Dir, "status.txt");

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
            routine = Steps();
            waitUntil = 0;
            deadline = EditorApplication.timeSinceStartup + 1500;
            EditorApplication.update += Tick;
            Log("play entered");
        }

        static void Cleanup(string why)
        {
            EditorApplication.update -= Tick;
            routine = null;
            SessionState.SetBool(ActiveKey, false);
            EditorSceneManager.playModeStartScene = null;
            Log("finished (" + why + ")");
        }

        static void Stop()
        {
            EditorApplication.update -= Tick;
            routine = null;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            else Cleanup("stopped outside Play");
        }

        static void Tick()
        {
            if (routine == null) { EditorApplication.update -= Tick; return; }
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > deadline) { Log("ERROR timeout"); Stop(); return; }
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            bool more;
            try { more = routine.MoveNext(); if (more && routine.Current is float s) waitUntil = EditorApplication.timeSinceStartup + s; }
            catch (Exception e) { Log("ERROR " + e); more = false; }
            if (!more) Stop();
        }

        static IEnumerator Steps()
        {
            WarSandboxSceneSession session = null;
            double until = EditorApplication.timeSinceStartup + 120;
            while ((session = WarSandboxSceneSession.Instance) == null || session.IsLoading)
            {
                if (EditorApplication.timeSinceStartup > until) { Log("ERROR no menu session"); yield break; }
                yield return 0.25f;
            }
            Directory.CreateDirectory(RepoDir);
            int ok = 0;
            var specs = Specs();
            // Optional Logs/TestPlans/only.txt: comma-separated slots to (re)build; others are left untouched.
            string onlyPath = Path.Combine(Dir, "only.txt");
            if (File.Exists(onlyPath))
            {
                var only = new List<string>(File.ReadAllText(onlyPath).Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries));
                specs.RemoveAll(x => !only.Contains(x.slot));
                Log("only: " + string.Join(",", only));
            }
            foreach (var spec in specs)
            foreach (var map in Candidates(spec))
            {
                spec.map = map;
                if (session.CurrentEntryId != spec.map)
                {
                    if (!session.TryEnterBattlefield(spec.map, true, out string enterError)) { Log("SKIP " + spec.slot + " enter " + spec.map + ": " + enterError); continue; }
                    yield return 0.5f;
                    until = EditorApplication.timeSinceStartup + 180;
                    while (session.IsLoading || session.State != WarSandboxEntryState.Battle)
                    {
                        if (EditorApplication.timeSinceStartup > until || session.State == WarSandboxEntryState.Failed) break;
                        yield return 0.25f;
                    }
                    if (session.State != WarSandboxEntryState.Battle) { Log("SKIP " + spec.slot + " load failed " + spec.map + " " + session.Error); continue; }
                    yield return 1f;
                }
                var controller = session.Controller;
                var deploy = controller != null ? controller.GetComponent<WarSandboxRuntimeDeployment>() : null;
                if (deploy == null || deploy.battlefieldCatalog == null) { Log("SKIP " + spec.slot + " no deployment"); continue; }
                string result = null;
                try { result = Build(spec, deploy); }
                catch (Exception e) { result = "exception " + e.Message; }
                if (result == null)
                {
                    string src = Path.Combine(deploy.PlanStore.DirectoryPath, spec.slot + ".json");
                    File.Copy(src, Path.Combine(RepoDir, spec.slot + ".json"), true);
                    ok++;
                    yield return 0.3f;
                    break;
                }
                Log("FAIL " + spec.slot + " on " + spec.map + ": " + result);
                yield return 0.3f;
            }
            Log("ALL DONE " + ok + "/" + specs.Count + " saved; repo copies in " + Path.GetFullPath(RepoDir));
        }

        static string Build(Spec spec, WarSandboxRuntimeDeployment deploy)
        {
            if (!deploy.TryBeginEdit(true, out string error)) return "edit: " + error;
            var catalog = deploy.battlefieldCatalog;
            var authored = deploy.Draft.Snapshot();
            var resolver = deploy.DraftStats;
            int Price(string id) => Mathf.Max(1, Mathf.RoundToInt(WarSandboxUnitCost.UnitPrice(resolver, Find(catalog, id))));
            foreach (var p in spec.a) if (Find(catalog, p.id) == null) return "template missing " + p.id;
            foreach (var p in spec.b) if (Find(catalog, p.id) == null) return "template missing " + p.id;
            if (spec.equalTotal > 0)
            {
                int pa = Price(spec.a[0].id), pb = Price(spec.b[0].id);
                spec.a[0].count = Mathf.Max(1, Mathf.RoundToInt(spec.equalTotal * pb / (float)(pa + pb)));
                if (spec.capA > 0 && spec.a[0].count > spec.capA) spec.a[0].count = spec.capA;
                spec.b[0].count = Mathf.Max(1, Mathf.RoundToInt(spec.a[0].count * pa / (float)pb));
            }
            // A side-B part with count 0 gets the same value as the side-A part at the same index.
            for (int i = 0; i < spec.b.Length && i < spec.a.Length; i++)
                if (spec.b[i].count <= 0) spec.b[i].count = Mathf.Max(1, Mathf.RoundToInt(spec.a[i].count * Price(spec.a[i].id) / (float)Price(spec.b[i].id)));
            var rules = deploy.Draft.Rules;
            rules.gameMode = WarSandboxGameMode.Annihilation;
            rules.challengeEnabled = false; rules.budgetEnabled = false;

            // Anchors: the battlefield's authored spawn centres per team (spawn gap ≈550 m on 2048 maps); then
            // centred fallbacks with shrinking gaps if a formation does not fit the terrain / bounds.
            var anchors = new List<(Vector3, Vector3)>();
            Vector3 a0 = Vector3.zero, a1 = Vector3.zero; int n0 = 0, n1 = 0;
            foreach (var e in authored) { if (e.teamId == 0) { a0 += e.center; n0++; } else if (e.teamId == 1) { a1 += e.center; n1++; } }
            if (n0 > 0 && n1 > 0) anchors.Add((a0 / n0, a1 / n1));
            foreach (float gap in new[] { 550f, 400f, 250f, 150f, 80f })
                foreach (float z in new[] { 0f, 250f, -250f, 450f, -450f })
                {
                    float k = Mathf.Max(.25f, deploy.WorldSize.x / 2048f);   // 1024 maps: half the offsets
                    anchors.Add((new Vector3(-gap * .5f * k, 0f, z * k), new Vector3(gap * .5f * k, 0f, z * k)));
                }
            string last = null; var errors = new List<string>();
            var aspects = new List<float> { spec.aspect };
            foreach (float extra in new[] { 4f, 3.5f, 5f, 3f }) if (!aspects.Contains(extra)) aspects.Add(extra);
            foreach (float aspect in aspects)
            foreach (var (left, right) in anchors)
            {
                bool flip = left.x > right.x;   // team 0 must be the side the stacking direction points away from
                var entries = new List<WarSandboxDeploymentEntry>();
                Stack(entries, catalog, spec.a, 0, left, flip ? 1f : -1f, aspect);
                Stack(entries, catalog, spec.b, 1, right, flip ? -1f : 1f, aspect);
                deploy.Draft.Replace(entries.ToArray(), rules, new WarSandboxStatOverrides());
                if (!deploy.TryValidate(out last)) { if (errors.Count < 6 && !errors.Contains(last)) errors.Add(last); continue; }
                if (!deploy.TrySavePlan(spec.slot, spec.name, true, out error)) return "save: " + error;
                int na = 0, nb = 0; string parts = "";
                foreach (var p in spec.a) { na += p.count; parts += p.id + "x" + p.count + "@" + Price(p.id) + " "; }
                parts += "| ";
                foreach (var p in spec.b) { nb += p.count; parts += p.id + "x" + p.count + "@" + Price(p.id) + " "; }
                Log("OK " + spec.slot + " map=" + spec.map + " total=" + (na + nb) + " anchors=" + left.ToString("0") + "/" + right.ToString("0") + " aspect=" + aspect.ToString("0.0", IC) + " " + parts.Trim());
                return null;
            }
            // Last resort (wooded maps): split each part into chunks of at most 1500 and place them one by one on a
            // grid around the team anchor, nearest free legal spot first, alternating teams.
            foreach (float scale in new[] { 1f, .875f, .75f })
            {
                Vector3 left = n0 > 0 ? a0 / n0 : new Vector3(-150f, 0f, 0f), right = n1 > 0 ? a1 / n1 : new Vector3(150f, 0f, 0f);
                float half = deploy.WorldSize.x * .5f, step = Mathf.Max(12f, deploy.WorldSize.x / 64f);
                var queues = new[] { Chunks(catalog, spec.a, 0, scale), Chunks(catalog, spec.b, 1, scale) };
                var placed = new List<WarSandboxDeploymentEntry>();
                double t0 = EditorApplication.timeSinceStartup; int validations = 0;
                for (int i = 0; i < Mathf.Max(queues[0].Count, queues[1].Count) * 2; i++)
                {
                    int team = i % 2, index = i / 2;
                    if (index >= queues[team].Count) continue;
                    var e = queues[team][index];
                    var anchor = team == 0 ? left : right;
                    float away = (team == 0) == (left.x <= right.x) ? -1f : 1f;
                    var cands = new List<Vector3>();
                    for (float dx = -2f * step; dx < half; dx += step)
                        for (float z = -half; z <= half; z += step)
                        {
                            var c = new Vector3(anchor.x + away * dx, 0f, z);
                            if (Mathf.Abs(c.x) + e.Size.x * .5f < half && Mathf.Abs(c.z) + e.Size.z * .5f < half && Mathf.Sign(c.x) == Mathf.Sign(anchor.x)) cands.Add(c);
                        }
                    cands.Sort((p, q) => Score(p, anchor).CompareTo(Score(q, anchor)));
                    bool ok = false; int tries = 0;
                    foreach (var c in cands)
                    {
                        e.center = c;
                        bool overlap = false;
                        foreach (var o in placed)
                            if (Mathf.Abs(o.center.x - c.x) * 2f < o.Size.x + e.Size.x + 8f && Mathf.Abs(o.center.z - c.z) * 2f < o.Size.z + e.Size.z + 8f) { overlap = true; break; }
                        if (overlap) continue;
                        var trial = new List<WarSandboxDeploymentEntry>(placed) { e };
                        deploy.Draft.Replace(trial.ToArray(), rules, new WarSandboxStatOverrides());
                        validations++;
                        if (deploy.TryValidate(out last) || last == null || !last.Contains("编成 " + trial.Count + "：")) { placed.Add(e); ok = true; break; }
                        if (++tries > 2000) break;
                    }
                    if (!ok) { errors.Add("scatter: no spot for chunk " + (i + 1) + " (" + last + ")"); break; }
                }
                Log("scatter " + spec.slot + " chunks=" + placed.Count + " scale=" + scale.ToString("0.000", IC) + " validations=" + validations + " seconds=" + (EditorApplication.timeSinceStartup - t0).ToString("0", IC));
                if (placed.Count == queues[0].Count + queues[1].Count)
                {
                    deploy.Draft.Replace(placed.ToArray(), rules, new WarSandboxStatOverrides());
                    if (deploy.TryValidate(out last))
                    {
                        if (!deploy.TrySavePlan(spec.slot, spec.name, true, out error)) return "save: " + error;
                        int total = 0; foreach (var e in placed) total += e.count;
                        Log("OK " + spec.slot + " map=" + spec.map + " total=" + total + " scattered " + placed.Count + " formations");
                        return null;
                    }
                    errors.Add("scatter final: " + last);
                }
            }
            return "layout: " + string.Join(" | ", errors) + " (world " + deploy.WorldSize + ", authored " + a0 / Mathf.Max(1, n0) + "/" + a1 / Mathf.Max(1, n1) + ")";
        }

        // Front part sits at the anchor; later parts stack behind it (away from the enemy), 6 m apart.
        static float Score(Vector3 p, Vector3 anchor) => Mathf.Abs(p.x - anchor.x) + .7f * Mathf.Abs(p.z - anchor.z);

        static List<WarSandboxDeploymentEntry> Chunks(WarSandboxBattlefieldCatalog catalog, Part[] parts, int team, float scale)
        {
            var list = new List<WarSandboxDeploymentEntry>();
            foreach (var part in parts)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(part.count * scale));
                int pieces = Mathf.Max(1, Mathf.CeilToInt(count / 1500f));
                for (int k = 0; k < pieces; k++)
                {
                    var e = WarSandboxDeploymentEntry.From(Find(catalog, part.id));
                    e.teamId = team; e.count = count / pieces + (k < count % pieces ? 1 : 0);
                    e.manualSize = Vector3.zero; e.aspect = 1.5f; e.facing = 0;
                    e.FitDensityToBody();
                    if (e.Size.x > e.Size.z && e.aspect > 0f) e.aspect = 1f / e.aspect;
                    list.Add(e);
                }
            }
            return list;
        }

        static void Stack(List<WarSandboxDeploymentEntry> entries, WarSandboxBattlefieldCatalog catalog, Part[] parts, int team, Vector3 anchor, float back, float aspect)
        {
            float x = anchor.x;
            foreach (var part in parts)
            {
                var e = WarSandboxDeploymentEntry.From(Find(catalog, part.id));
                e.teamId = team; e.count = part.count; e.manualSize = Vector3.zero; e.aspect = aspect; e.facing = 0;
                e.FitDensityToBody();
                if (e.Size.x > e.Size.z && e.aspect > 0f) e.aspect = 1f / e.aspect;
                e.center = new Vector3(x + back * e.Size.x * .5f, 0f, anchor.z);
                x += back * (e.Size.x + 6f);
                entries.Add(e);
            }
        }

        static UnitTypeConfig Find(WarSandboxBattlefieldCatalog catalog, string id)
        {
            foreach (var t in catalog.templates) if (t != null && t.templateId == id) return t.config;
            return null;
        }

        static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

        static void Log(string line)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(StatusPath, Stamp() + " " + line + "\n", new UTF8Encoding(false));
            Debug.Log("[TestPlanLibrary] " + line);
        }
    }
}
