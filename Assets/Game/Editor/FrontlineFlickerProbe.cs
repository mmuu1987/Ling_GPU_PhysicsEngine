// Editor-only, read-only recorder for front-line flicker analysis. Click while two armies are in contact.
// Picks up to MaxAgents living agents in view (nearest the camera first) and reads the whole agent buffer
// back EVERY rendered frame for Frames frames (AsyncGPUReadback, no stalls). Nothing in the simulation is modified.
// Output: Logs/Flicker1024/<time>/frames.bin (float32 [frame][agent][Fields]) + meta.json; analysed offline.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Game.Editor
{
    public static class FrontlineFlickerProbe
    {
        const string Menu = "Tools/战场 1024/前线闪动采样（战斗接触时点击，约 4 秒）";
        const int MaxAgents = 8000, Frames = 240;
        // px py pz yaw vx vz state anim presentation locomotion target targetDist targetState waitRemaining
        public const int Fields = 14;
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;
        static Session session;

        sealed class Session
        {
            public MassGpuBufferManager buffers;
            public int[] ids; public float[] data;
            public int[] frameCount = new int[Frames]; public float[] dt = new float[Frames], unscaledDt = new float[Frames], timeScale = new float[Frames];
            public Vector3[] cam = new Vector3[Frames];
            public bool[] agentsDone = new bool[Frames], targetsAtAgents = new bool[Frames];
            public int[][] targets = new int[Frames][];
            public float[][] waits = new float[Frames][];
            public int requested, failed, lastFrame = -1;
            public double started; public string folder;
            public StringBuilder meta = new StringBuilder();
        }

        [MenuItem(Menu, true)] static bool Validate() => EditorApplication.isPlaying && session == null;

        [MenuItem(Menu, false, 41)]
        public static void Run()
        {
            try { Begin(); }
            catch (Exception e) { session = null; Debug.LogError("FLICKER 采样失败：" + e); EditorUtility.DisplayDialog("前线闪动采样", "失败：" + e.Message, "OK"); }
        }

        public static bool Busy => session != null;

        // Unattended entry (FrontlineAutoSample): no dialog; returns null on success, else the reason.
        public static string RunUnattended()
        {
            if (session != null) return "busy";
            try { Begin(); return null; }
            catch (Exception e) { session = null; return e.Message; }
        }

        static void Begin()
        {
            var ws = WarSandboxSceneSession.Instance;
            if (ws == null || ws.Controller == null) throw new Exception("没有正在运行的战斗");
            var m = ws.Controller.manager;
            if (m == null || m.Buffers == null || !m.Buffers.IsAllocated) throw new Exception("GPU 缓冲未分配，请在战斗开始后点击");
            var b = m.Buffers;
            int n = b.AgentCount;
            var agents = new AgentData[n]; b.agentBuffer.GetData(agents);
            var teams = new int[n]; b.combatBuffers.teamIdBuffer.GetData(teams);
            var types = new int[n]; b.unitTypeIndexBuffer.GetData(types);
            var settings = new UnitTypeGpuSettings[b.unitTypeSettingsBuffer.count]; b.unitTypeSettingsBuffer.GetData(settings);
            var camera = Camera.main;
            if (camera == null) throw new Exception("没有主相机");
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Vector3 cp = camera.transform.position;
            var picked = Enumerable.Range(0, n)
                .Where(i => agents[i].currentState != 4 &&
                            GeometryUtility.TestPlanesAABB(planes, new Bounds(agents[i].position, Vector3.one * 2f)))
                .OrderBy(i => (agents[i].position - cp).sqrMagnitude).Take(MaxAgents).ToArray();
            if (picked.Length == 0) throw new Exception("视野里没有接敌/攻击中的单位：请把镜头对准交战前线再点");

            var s = new Session { buffers = b, ids = picked, data = new float[(long)Frames * picked.Length * Fields], started = EditorApplication.timeSinceStartup };
            s.folder = Path.GetFullPath("Logs/Flicker1024/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(s.folder);

            var lod = m.systemConfig != null ? m.systemConfig.lodConfig : null;
            var meta = s.meta;
            meta.Append("{\n\"scene\":\"").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path).Append("\",\n");
            meta.Append("\"agentCount\":").Append(n).Append(",\"picked\":").Append(picked.Length).Append(",\"frames\":").Append(Frames).Append(",\"fields\":").Append(Fields).Append(",\n");
            meta.Append("\"fieldNames\":[\"px\",\"py\",\"pz\",\"yaw\",\"vx\",\"vz\",\"state\",\"anim\",\"presentation\",\"locomotion\",\"target\",\"targetDist\",\"targetState\",\"waitRemaining\"],\n");
            if (lod != null)
                meta.Append("\"lod\":{\"near\":").Append(F(lod.nearLodRadius)).Append(",\"mid\":").Append(F(lod.midLodRadius))
                    .Append(",\"animInterval\":[").Append(lod.nearAnimationInterval).Append(',').Append(lod.midAnimationInterval).Append(',').Append(lod.farAnimationInterval)
                    .Append("],\"simInterval\":[").Append(lod.nearSimulationInterval).Append(',').Append(lod.midSimulationInterval).Append(',').Append(lod.farSimulationInterval)
                    .Append("],\"maxRender\":").Append(F(lod.maxRenderDistance)).Append("},\n");
            meta.Append("\"unitTypes\":[");
            for (int t = 0; t < settings.Length; t++)
            {
                var u = settings[t];
                if (t > 0) meta.Append(',');
                meta.Append("\n {\"index\":").Append(t).Append(",\"team\":").Append(u.teamId).Append(",\"radius\":").Append(F(u.agentRadius)).Append(",\"maxSpeed\":").Append(F(u.maxSpeed))
                    .Append(",\"attackRange\":").Append(F(u.attackRange)).Append(",\"projectileRange\":").Append(F(u.projectileRange)).Append(",\"acquire\":").Append(F(u.targetAcquireRadius))
                    .Append(",\"attackInterval\":").Append(F(u.attackInterval)).Append(",\"idleClip\":").Append(F(u.idleClipDuration)).Append(",\"moveClip\":").Append(F(u.moveClipDuration))
                    .Append(",\"attackClip\":").Append(F(u.attackClipDuration)).Append(",\"moveStop\":").Append(F(u.moveStopSpeed)).Append(",\"moveStart\":").Append(F(u.moveStartSpeed))
                    .Append(",\"moveRef\":").Append(F(u.moveReferenceSpeed)).Append(",\"separation\":").Append(F(u.separationStrength)).Append(",\"damping\":").Append(F(u.velocityDamping)).Append('}');
            }
            meta.Append("],\n\"ids\":[").Append(string.Join(",", picked)).Append("],\n");
            meta.Append("\"teams\":[").Append(string.Join(",", picked.Select(i => teams[i]))).Append("],\n");
            meta.Append("\"types\":[").Append(string.Join(",", picked.Select(i => types[i]))).Append("],\n");
            meta.Append("\"scales\":[").Append(string.Join(",", picked.Select(i => F(Mathf.Abs(agents[i].scale.y))))).Append("],\n");

            session = s;
            EditorApplication.update += Tick;
            Debug.Log("FLICKER 采样开始：" + picked.Length + " 个前线单位，" + Frames + " 帧 → " + s.folder);
        }

        static void Tick()
        {
            var s = session;
            if (s == null) { EditorApplication.update -= Tick; return; }
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - s.started > 120) { Abort("退出 Play 或超时"); return; }
            if (s.requested < Frames && Time.frameCount != s.lastFrame && !EditorApplication.isPaused)
            {
                s.lastFrame = Time.frameCount;
                int f = s.requested++;
                s.frameCount[f] = Time.frameCount; s.dt[f] = Time.deltaTime; s.unscaledDt[f] = Time.unscaledDeltaTime; s.timeScale[f] = Time.timeScale;
                var camera = Camera.main; s.cam[f] = camera != null ? camera.transform.position : Vector3.zero;
                // Same command-stream point for both buffers: targets first (small), then agents.
                AsyncGPUReadback.Request(s.buffers.combatBuffers.targetAgentIndexBuffer, r => OnTargets(s, f, r));
                AsyncGPUReadback.Request(s.buffers.combatBuffers.engagementSlotAssignmentBuffer, r => OnWaits(s, f, r));
                AsyncGPUReadback.Request(s.buffers.agentBuffer, r => OnAgents(s, f, r));
            }
            int done = 0;
            for (int f = 0; f < s.requested; f++) if (s.agentsDone[f]) done++;
            if (s.requested == Frames && done + s.failed >= Frames) Finish();
        }

        static void OnTargets(Session s, int f, AsyncGPUReadbackRequest r)
        {
            if (session != s) return;
            if (r.hasError) { s.targets[f] = null; return; }
            var all = r.GetData<int>();
            var t = new int[s.ids.Length];
            for (int k = 0; k < t.Length; k++) t[k] = all[s.ids[k]];
            s.targets[f] = t;
        }

        // Congestion record word 5 = remainingSeconds of the congestion wait (CongestionWaiting.hlsl:
        // record base = agentCount + index * 13).
        static void OnWaits(Session s, int f, AsyncGPUReadbackRequest r)
        {
            if (session != s || r.hasError) return;
            var all = r.GetData<int>();
            int n = s.buffers.AgentCount;
            var w = new float[s.ids.Length];
            for (int k = 0; k < w.Length; k++)
            {
                long i = n + (long)s.ids[k] * 13 + 5;
                w[k] = i < all.Length ? BitConverter.Int32BitsToSingle(all[(int)i]) : -1f;
            }
            s.waits[f] = w;
        }

        static void OnAgents(Session s, int f, AsyncGPUReadbackRequest r)
        {
            if (session != s) return;
            if (r.hasError) { s.failed++; return; }
            var all = r.GetData<AgentData>();
            int[] t = s.targets[f];
            s.targetsAtAgents[f] = t != null;
            long o = (long)f * s.ids.Length * Fields;
            for (int k = 0; k < s.ids.Length; k++, o += Fields)
            {
                var a = all[s.ids[k]];
                int target = t != null ? t[k] : -2;
                float dist = -1f, tstate = -1f;
                if (target >= 0 && target < all.Length)
                {
                    var b = all[target];
                    dist = new Vector2(b.position.x - a.position.x, b.position.z - a.position.z).magnitude;
                    tstate = b.currentState;
                }
                var d = s.data;
                d[o] = a.position.x; d[o + 1] = a.position.y; d[o + 2] = a.position.z; d[o + 3] = a.rotation.y;
                d[o + 4] = a.velocity.x; d[o + 5] = a.velocity.z; d[o + 6] = a.currentState; d[o + 7] = a.currentAnimationTime;
                d[o + 8] = a.presentationState; d[o + 9] = a.locomotionSpeed; d[o + 10] = target; d[o + 11] = dist; d[o + 12] = tstate;
                d[o + 13] = s.waits[f] != null ? s.waits[f][k] : -1f;
            }
            s.agentsDone[f] = true;
        }

        static void Finish()
        {
            var s = session; session = null; EditorApplication.update -= Tick;
            try
            {
                using (var w = new BinaryWriter(File.Create(Path.Combine(s.folder, "frames.bin"))))
                    foreach (float v in s.data) w.Write(v);
                var m = s.meta;
                m.Append("\"frameCount\":[").Append(string.Join(",", s.frameCount)).Append("],\n");
                m.Append("\"dt\":[").Append(string.Join(",", s.dt.Select(F))).Append("],\n");
                m.Append("\"unscaledDt\":[").Append(string.Join(",", s.unscaledDt.Select(F))).Append("],\n");
                m.Append("\"timeScale\":[").Append(string.Join(",", s.timeScale.Select(F))).Append("],\n");
                m.Append("\"cam\":[").Append(string.Join(",", s.cam.Select(c => "[" + F(c.x) + "," + F(c.y) + "," + F(c.z) + "]"))).Append("],\n");
                m.Append("\"framesOk\":[").Append(string.Join(",", s.agentsDone.Select(x => x ? 1 : 0))).Append("],\n");
                m.Append("\"targetsOk\":[").Append(string.Join(",", s.targetsAtAgents.Select(x => x ? 1 : 0))).Append("]\n}\n");
                File.WriteAllText(Path.Combine(s.folder, "meta.json"), m.ToString());
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.folder), "latest.txt"), s.folder);
                Debug.Log("FLICKER 采样完成：" + s.folder + "（失败帧 " + s.failed + "）");
            }
            catch (Exception e) { Debug.LogError("FLICKER 写文件失败：" + e); }
        }

        static void Abort(string why)
        {
            session = null; EditorApplication.update -= Tick;
            Debug.LogWarning("FLICKER 采样中止：" + why);
        }

        static string F(float v) => v.ToString("0.#####", IC);
    }
}

