// Editor-only, read-only observation of a RUNNING battle. Click the menu while the stripes are visible.
// Nothing is modified: no settings, navigation, shaders or scene state. Output: Logs/Stripe1024/<time>/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    public static class StripeDiagnose1024
    {
        const string Menu = "Tools/战场 1024/条纹诊断（战斗中点击）";
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("0.###", IC);

        [MenuItem(Menu, true)]
        static bool Validate() => EditorApplication.isPlaying;

        [MenuItem(Menu, false, 40)]
        public static void Run()
        {
            try
            {
                string folder = Capture();
                EditorUtility.RevealInFinder(Path.Combine(folder, "summary.txt"));
                Debug.Log("STRIPE1024 诊断已写入 " + folder);
            }
            catch (Exception e)
            {
                Debug.LogError("STRIPE1024 诊断失败：" + e);
                EditorUtility.DisplayDialog("条纹诊断", "失败：" + e.Message, "OK");
            }
        }

        static string Capture()
        {
            var session = WarSandboxSceneSession.Instance;
            if (session == null || session.Controller == null) throw new Exception("没有正在运行的战斗（WarSandboxSceneSession/Controller 为空）");
            var c = session.Controller;
            var m = c.manager;
            if (m == null || m.Buffers == null || !m.Buffers.IsAllocated) throw new Exception("GPU 缓冲未分配，请在战斗开始后点击");
            var b = m.Buffers;
            string folder = Path.GetFullPath("Logs/Stripe1024/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(folder);

            int n = b.AgentCount;
            var agents = new AgentData[n]; b.agentBuffer.GetData(agents);
            var teams = new int[n]; b.combatBuffers.teamIdBuffer.GetData(teams);
            var dirs = new Vector2[b.flowFieldDirectionsBuffer.count]; b.flowFieldDirectionsBuffer.GetData(dirs);
            var flowParams = new TeamFlowParams[b.teamFlowParamsBuffer.count]; b.teamFlowParamsBuffer.GetData(flowParams);
            var flow = m.systemConfig != null ? m.systemConfig.runtimeFlowConfig : null;
            var nav = m.TerrainNavigation;
            uint[] walkable = nav != null ? nav.CopyWalkable() : null;

            var s = new StringBuilder();
            s.AppendLine("STRIPE1024 diagnose  " + DateTime.Now.ToString("s") + "  scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            s.AppendLine("phase=" + c.Phase + " gameMode=" + c.gameMode + " agents=" + n + " frame=" + Time.frameCount);
            if (flow != null)
                s.AppendLine("flowConfig=" + flow.name + " res=" + flow.flowFieldResolution + " cell=" + F(flow.flowFieldCellSize) + " origin=" + flow.flowFieldOrigin);
            if (nav != null)
                s.AppendLine("nav res=" + nav.ResolutionX + "x" + nav.ResolutionZ + " cell=" + F(nav.CellSize) + " origin=" + nav.Origin + " clearance=" + F(nav.Clearance) + " fields=" + m.TerrainCompletedFields);
            for (int t = 0; t < flowParams.Length; t++)
            {
                var p = flowParams[t];
                var army = SafeArmy(c, t);
                s.AppendLine("team" + t + " targetMode=" + p.targetMode + " (0=NONE/动态寻敌 1=POINT 2=AREA) flowEnabled=" + p.flowEnabled + " dynamic=" + p.dynamicEnabled +
                             " target=(" + F(p.targetPointX) + "," + F(p.targetPointZ) + ") stop=" + F(p.targetStopRadius) + " order=" + army);
            }

            int cellsPerTeam = nav != null ? nav.CellCount : (b.FlowCellCount > 0 ? b.FlowCellCount : dirs.Length);
            float cell = nav != null ? nav.CellSize : (flow != null ? flow.flowFieldCellSize : 4f);
            Vector2 origin = nav != null ? nav.Origin : (flow != null ? flow.flowFieldOrigin : Vector2.zero);
            int rx = nav != null ? nav.ResolutionX : (flow != null ? flow.flowFieldResolution : 256);
            int rz = nav != null ? nav.ResolutionZ : rx;

            for (int team = 0; team < Math.Min(2, flowParams.Length); team++)
                AnalyzeTeam(s, team, agents, teams, dirs, walkable, cellsPerTeam, cell, origin, rx, rz);

            File.WriteAllText(Path.Combine(folder, "summary.txt"), s.ToString(), new UTF8Encoding(false));

            var csv = new StringBuilder("team,x,z,vx,vz,state\n");
            for (int i = 0; i < n; i++)
            {
                var a = agents[i];
                if (a.currentState == 4) continue;
                csv.Append(teams[i]).Append(',').Append(F(a.position.x)).Append(',').Append(F(a.position.z)).Append(',')
                   .Append(F(a.velocity.x)).Append(',').Append(F(a.velocity.z)).Append(',').Append(a.currentState).Append('\n');
            }
            File.WriteAllText(Path.Combine(folder, "positions.csv"), csv.ToString());

            for (int team = 0; team < Math.Min(2, flowParams.Length); team++)
            {
                var fc = new StringBuilder("x,z,dx,dz,walkable\n");
                for (int z = 0; z < rz; z++)
                    for (int x = 0; x < rx; x++)
                    {
                        int ci = z * rx + x, di = team * cellsPerTeam + ci;
                        Vector2 d = di < dirs.Length ? dirs[di] : Vector2.zero;
                        uint w = walkable != null && ci < walkable.Length ? walkable[ci] : 1u;
                        if (d.sqrMagnitude < 1e-8f && w != 0) continue;
                        fc.Append(x).Append(',').Append(z).Append(',').Append(F(d.x)).Append(',').Append(F(d.y)).Append(',').Append(w).Append('\n');
                    }
                File.WriteAllText(Path.Combine(folder, "flow-team" + team + ".csv"), fc.ToString());
            }
            return folder;
        }

        static string SafeArmy(WarSandboxBattleController c, int team)
        {
            try
            {
                var a = c.GetArmy(team);
                if (a == null) return "-";
                return a.hasOrder ? a.currentOrder.type.ToString() : "none";
            }
            catch (Exception) { return "?"; }
        }

        static float Percentile(List<float> v, float q)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            int i = Mathf.Clamp(Mathf.RoundToInt(q * (v.Count - 1)), 0, v.Count - 1);
            return v[i];
        }

        static void AnalyzeTeam(StringBuilder s, int team, AgentData[] agents, int[] teams, Vector2[] dirs, uint[] walkable,
                                int cellsPerTeam, float cell, Vector2 origin, int rx, int rz)
        {
            Vector2 vsum = Vector2.zero; int alive = 0, moving = 0;
            var states = new int[5];
            for (int i = 0; i < agents.Length; i++)
            {
                if (teams[i] != team) continue;
                var a = agents[i];
                int st = Mathf.Clamp(a.currentState, 0, 4); states[st]++;
                if (st == 4) continue;
                alive++;
                var v = new Vector2(a.velocity.x, a.velocity.z);
                if (v.magnitude > 0.3f) { vsum += v.normalized; moving++; }
            }
            s.AppendLine();
            s.AppendLine("=== team" + team + " alive=" + alive + " moving=" + moving + " states idle/move/engage/attack/dead=" + string.Join("/", states));
            if (moving < 50) { s.AppendLine("  (too few moving agents)"); return; }
            Vector2 h = vsum.normalized;
            bool alongX = Mathf.Abs(h.x) >= Mathf.Abs(h.y);
            Vector2 fwdAxis = alongX ? new Vector2(Mathf.Sign(h.x), 0) : new Vector2(0, Mathf.Sign(h.y));
            s.AppendLine("  mean heading=(" + F(h.x) + "," + F(h.y) + ") grid forward axis=" + fwdAxis + " (lateral axis=" + (alongX ? "z" : "x") + ")");

            var lon = new List<float>();
            for (int i = 0; i < agents.Length; i++)
                if (teams[i] == team && agents[i].currentState != 4)
                    lon.Add(Vector2.Dot(new Vector2(agents[i].position.x, agents[i].position.z), fwdAxis));
            float front = Percentile(lon, 0.98f);
            s.AppendLine("  front (98th pct along axis)=" + F(front));

            float[][] bands = { new[] { 0f, 20f }, new[] { 20f, 50f }, new[] { 50f, 100f }, new[] { 100f, 200f }, new[] { 200f, 400f } };
            float lateralOrigin = alongX ? origin.y : origin.x;
            foreach (var band in bands)
            {
                var lat = new List<float>(); float latVel = 0, spd = 0; int cnt = 0;
                var phase = new int[8];
                for (int i = 0; i < agents.Length; i++)
                {
                    if (teams[i] != team || agents[i].currentState == 4) continue;
                    var p = new Vector2(agents[i].position.x, agents[i].position.z);
                    float behind = front - Vector2.Dot(p, fwdAxis);
                    if (behind < band[0] || behind >= band[1]) continue;
                    float l = alongX ? p.y : p.x;
                    lat.Add(l); cnt++;
                    float ph = Mathf.Repeat((l - lateralOrigin) / cell, 1f);
                    phase[Mathf.Min(7, (int)(ph * 8))]++;
                    var v = new Vector2(agents[i].velocity.x, agents[i].velocity.z);
                    if (v.magnitude > 0.3f) { latVel += Mathf.Abs(alongX ? v.y : v.x) / v.magnitude; spd += v.magnitude; }
                }
                s.Append("  band ").Append(band[0]).Append('-').Append(band[1]).Append(" m behind front: agents=").Append(cnt);
                if (cnt < 200) { s.AppendLine(" (few)"); continue; }
                var sorted = new List<float>(lat);
                float lo = Percentile(sorted, 0.1f), hi = Percentile(sorted, 0.9f);
                int nb = Mathf.Max(4, Mathf.FloorToInt((hi - lo) / 0.5f));
                var hist = new float[nb];
                foreach (float l in lat) { int k = Mathf.FloorToInt((l - lo) / 0.5f); if (k >= 0 && k < nb) hist[k]++; }
                float mean = hist.Average(), sd = Mathf.Sqrt(hist.Select(x => (x - mean) * (x - mean)).Average());
                int empty1m = 0, n1 = nb / 2;
                for (int k = 0; k + 1 < nb; k += 2) if (hist[k] + hist[k + 1] < 0.3f * 2 * mean) empty1m++;
                // dominant lateral period (1.5..24 m) from a direct DFT of the 0.5 m histogram
                float bestP = 0, bestPow = 0, totalPow = 0;
                for (int f = 1; f < nb / 2; f++)
                {
                    float period = nb * 0.5f / f; double re = 0, im = 0;
                    for (int k = 0; k < nb; k++) { double a = 2 * Math.PI * f * k / nb; re += (hist[k] - mean) * Math.Cos(a); im -= (hist[k] - mean) * Math.Sin(a); }
                    float pow = (float)(re * re + im * im); totalPow += pow;
                    if (period >= 1.5f && period <= 24f && pow > bestPow) { bestPow = pow; bestP = period; }
                }
                s.Append(" lateralCV=").Append(F(sd / Mathf.Max(1e-3f, mean)))
                 .Append(" empty1mBins=").Append(empty1m).Append('/').Append(n1)
                 .Append(" dominantPeriod=").Append(F(bestP)).Append("m (").Append(F(100f * bestPow / Mathf.Max(1e-3f, totalPow))).Append("% power)")
                 .Append(" meanLateralVel=").Append(F(latVel / Mathf.Max(1, cnt))).Append(" meanSpeed=").Append(F(spd / Mathf.Max(1, cnt)));
                s.Append(" cellPhase8=[").Append(string.Join(",", phase.Select(x => F(100f * x / cnt)))).AppendLine("]%");

                // flow directions of this team inside the band footprint
                int zero = 0, fwd = 0, diag = 0, other = 0, blocked = 0, total = 0;
                for (int z = 0; z < rz; z++)
                    for (int x = 0; x < rx; x++)
                    {
                        Vector2 cc = origin + new Vector2((x + 0.5f) * cell, (z + 0.5f) * cell);
                        float behind = front - Vector2.Dot(cc, fwdAxis), l = alongX ? cc.y : cc.x;
                        if (behind < band[0] || behind >= band[1] || l < lo || l > hi) continue;
                        int ci = z * rx + x; total++;
                        if (walkable != null && ci < walkable.Length && walkable[ci] == 0) { blocked++; continue; }
                        int di = team * cellsPerTeam + ci; Vector2 d = di < dirs.Length ? dirs[di] : Vector2.zero;
                        if (d.sqrMagnitude < 1e-4f) zero++;
                        else if (Vector2.Dot(d.normalized, fwdAxis) > 0.999f) fwd++;
                        else if (Mathf.Abs(Mathf.Abs(d.x) - Mathf.Abs(d.y)) < 0.02f) diag++;
                        else other++;
                    }
                s.AppendLine("      flow cells=" + total + " forwardCardinal=" + fwd + " diagonal=" + diag + " other=" + other + " zero=" + zero + " blocked=" + blocked);
            }
        }
    }
}
