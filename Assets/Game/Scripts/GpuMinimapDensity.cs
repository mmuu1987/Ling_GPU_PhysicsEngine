using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassEngine.Game
{
    /// <summary>
    /// Minimap army shapes (2026-10-12). Read-only GPU observer: counts living agents per army on a Resolution² grid over
    /// the simulation world and reads it back asynchronously (one request in flight, ~4 Hz, no GetData/Wait). The CPU side
    /// turns the counts into a small texture — dominant army per cell, softened with a 1-2-1 blur, opacity by density,
    /// the selected army outlined — and an anchor per army (the occupied cell nearest its head-count centroid) for badges.
    /// Same buffer-lifetime rule as GpuMemberSelection: invalid as soon as the engine reallocates its buffers.
    /// </summary>
    public sealed class GpuMinimapDensity : IDisposable
    {
        public const int Resolution = 128;
        public const int MaxTeams = 16;
        private const double Interval = .25, Timeout = 10;
        private const float VisibleThreshold = .12f, MaxAlpha = .9f, UnselectedAlpha = .8f;

        private readonly MassEngineManager manager;
        private readonly MassGpuBufferManager buffers;
        private readonly ComputeBuffer identity;
        private readonly ComputeShader shader;
        private readonly int clearKernel, splatKernel;
        private ComputeBuffer cells;
        private int teamCount, flightTeams;
        private Vector2 flightWorld;
        private AsyncGPUReadbackRequest request;
        private bool inFlight, faulted, disposed;
        private double nextDispatch, started;
        private uint[] counts = Array.Empty<uint>();
        // 2026-10-17 composition badges: one group per registered unit type (= one 编成), agents contiguous per type.
        // Per group: [alive, sum u*4096, sum v*4096, min u, min v, max u, max v] (extent quantised to 0..65535).
        private const int GroupStride = 7;
        private ComputeBuffer groupBuffer;
        private AsyncGPUReadbackRequest groupRequest;
        private bool groupDone;
        private uint[] groupData = Array.Empty<uint>(), flightGroupData = Array.Empty<uint>();
        private readonly int[] groupTeam, groupOrdinal, groupStart, groupCount;
        private Vector2[] groupAnchors = Array.Empty<Vector2>();
        private int[] groupAlive = Array.Empty<int>();
        private sbyte[] owner = Array.Empty<sbyte>();
        public int GroupCount => groupTeam.Length;
        public int GroupTeam(int g) => groupTeam[g];
        /// <summary>1-based number of the composition inside its army (registration order).</summary>
        public int GroupOrdinal(int g) => groupOrdinal[g];
        public int GroupStart(int g) => groupStart[g];
        public int GroupSize(int g) => groupCount[g];
        private float[] smooth = Array.Empty<float>(), scratch = Array.Empty<float>();
        private Color32[] pixels;
        private Texture2D texture;
        private int builtVersion = -1, builtSelected = int.MinValue;

        /// <summary>Increments every time a new grid arrives.</summary>
        public int Version { get; private set; }
        public int Teams { get; private set; }
        public Vector2 WorldSize { get; private set; }
        public bool HasData => Version > 0 && !faulted;
        public string Fault { get; private set; }
        public bool IsCurrent => !disposed && manager != null && manager.Buffers == buffers && buffers.IsAllocated && buffers.agentBuffer == identity;
        public Texture2D Texture => texture;

        private readonly Vector2[] anchors = new Vector2[MaxTeams];
        private readonly bool[] anchorValid = new bool[MaxTeams];

        public static bool Supported => SystemInfo.supportsComputeShaders && SystemInfo.supportsAsyncGPUReadback;

        public GpuMinimapDensity(MassEngineManager manager)
        {
            if (manager == null || manager.Buffers == null || !manager.Buffers.IsAllocated) throw new InvalidOperationException("Initialize the battle before the minimap");
            if (!Supported) throw new NotSupportedException("Compute and async readback required");
            this.manager = manager; buffers = manager.Buffers; identity = buffers.agentBuffer;
            var source = Resources.Load<ComputeShader>("MinimapDensity");
            if (source == null) throw new InvalidOperationException("MinimapDensity compute shader unavailable");
            shader = UnityEngine.Object.Instantiate(source); shader.hideFlags = HideFlags.HideAndDontSave;
            clearKernel = shader.FindKernel("ClearCells"); splatKernel = shader.FindKernel("SplatAgents");
            texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, false)
            { name = "MinimapArmies", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            pixels = new Color32[Resolution * Resolution];
            texture.SetPixels32(pixels); texture.Apply(false, false);
            var types = manager.UnitTypes != null ? manager.UnitTypes.RegisteredTypes : null;
            int n = types != null ? types.Count : 0;
            groupTeam = new int[n]; groupOrdinal = new int[n]; groupStart = new int[n]; groupCount = new int[n];
            var perTeam = new System.Collections.Generic.Dictionary<int, int>();
            for (int g = 0; g < n; g++)
            {
                var t = types[g];
                groupTeam[g] = t.TeamId; groupStart[g] = t.BufferOffset; groupCount[g] = t.UnitCount;
                perTeam.TryGetValue(t.TeamId, out int k); perTeam[t.TeamId] = ++k; groupOrdinal[g] = k;
            }
            groupBuffer = new ComputeBuffer(Mathf.Max(1, n) * GroupStride, 4);
            groupAnchors = new Vector2[n]; groupAlive = new int[n];
        }

        /// <summary>Call once per frame while the minimap is visible.</summary>
        public void Tick(Vector2 worldSize, int armyCount)
        {
            if (disposed || faulted || !IsCurrent) return;
            try
            {
                if (inFlight)
                {
                    if (!groupDone && groupRequest.done)
                    {
                        if (groupRequest.hasError) throw new InvalidOperationException("Minimap group readback failed");
                        if (flightGroupData.Length != GroupCount * GroupStride) flightGroupData = new uint[GroupCount * GroupStride];
                        if (GroupCount > 0) groupRequest.GetData<uint>().GetSubArray(0, GroupCount * GroupStride).CopyTo(flightGroupData);
                        groupDone = true;
                    }
                    if (request.done && groupDone)
                    {
                        inFlight = false;
                        if (request.hasError) throw new InvalidOperationException("Minimap readback failed");
                        var swap = groupData; groupData = flightGroupData; flightGroupData = swap;
                        int n = flightTeams * Resolution * Resolution;
                        if (counts.Length != n) counts = new uint[n];
                        request.GetData<uint>().CopyTo(counts);
                        Teams = flightTeams; WorldSize = flightWorld; Version++;
                    }
                    else if (Time.realtimeSinceStartupAsDouble - started > Timeout) throw new TimeoutException("Minimap readback timed out");
                }
                if (!inFlight && Time.realtimeSinceStartupAsDouble >= nextDispatch && worldSize.x > 0 && worldSize.y > 0)
                {
                    Dispatch(worldSize, Mathf.Clamp(armyCount, 1, MaxTeams));
                    nextDispatch = Time.realtimeSinceStartupAsDouble + Interval;
                }
            }
            catch (Exception ex) { faulted = true; Fault = ex.Message; inFlight = false; }
        }

        private void Dispatch(Vector2 worldSize, int teams)
        {
            if (cells == null || teamCount != teams)
            {
                cells?.Release();
                teamCount = teams;
                cells = new ComputeBuffer(teams * Resolution * Resolution, 4);
            }
            shader.SetInt("_Count", buffers.AgentCount); shader.SetInt("_Res", Resolution); shader.SetInt("_TeamCount", teams);
            shader.SetInt("_GroupCount", GroupCount);
            shader.SetBuffer(clearKernel, "_Groups", groupBuffer);
            shader.SetBuffer(splatKernel, "_Groups", groupBuffer);
            shader.SetBuffer(splatKernel, "_Types", buffers.unitTypeIndexBuffer);
            shader.SetVector("_World", new Vector4(-worldSize.x * .5f, -worldSize.y * .5f, 1f / worldSize.x, 1f / worldSize.y));
            shader.SetBuffer(clearKernel, "_Cells", cells);
            shader.Dispatch(clearKernel, (teams * Resolution * Resolution + 63) / 64, 1, 1);
            shader.SetBuffer(splatKernel, "_Cells", cells);
            shader.SetBuffer(splatKernel, "_Agents", identity);
            shader.SetBuffer(splatKernel, "_TeamIds", buffers.combatBuffers.teamIdBuffer);
            shader.SetBuffer(splatKernel, "_Hp", buffers.combatBuffers.hpReadBuffer);
            if (buffers.AgentCount > 0) shader.Dispatch(splatKernel, (buffers.AgentCount + 63) / 64, 1, 1);
            request = AsyncGPUReadback.Request(cells);
            groupRequest = AsyncGPUReadback.Request(groupBuffer); groupDone = false;
            flightTeams = teams; flightWorld = worldSize; inFlight = true; started = Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>Rebuilds the texture and anchors if a new grid arrived or the selection changed. Returns true if rebuilt.</summary>
        public bool Build(int selectedTeam, Func<int, Color> palette)
        {
            if (!HasData || texture == null || (builtVersion == Version && builtSelected == selectedTeam)) return false;
            builtVersion = Version; builtSelected = selectedTeam;
            const int R = Resolution, N = R * R;
            int teams = Teams;
            if (smooth.Length != teams * N) { smooth = new float[teams * N]; scratch = new float[N]; }
            // Separable 1-2-1 blur per army: bridges the gaps between widely spaced large units and rounds the edges.
            for (int t = 0; t < teams; t++)
            {
                int o = t * N;
                for (int z = 0; z < R; z++)
                    for (int x = 0; x < R; x++)
                    {
                        int i = z * R + x;
                        float c = counts[o + i] * 2f;
                        if (x > 0) c += counts[o + i - 1];
                        if (x < R - 1) c += counts[o + i + 1];
                        scratch[i] = c * .25f;
                    }
                for (int z = 0; z < R; z++)
                    for (int x = 0; x < R; x++)
                    {
                        int i = z * R + x;
                        float c = scratch[i] * 2f;
                        if (z > 0) c += scratch[i - R];
                        if (z < R - 1) c += scratch[i + R];
                        smooth[o + i] = c * .25f;
                    }
            }
            // Dominant army per cell; opacity grows with density (a lone unit still shows, a dense block is near-solid).
            float cellArea = Mathf.Max(1f, WorldSize.x / R * (WorldSize.y / R));
            float dense = Mathf.Max(1f, cellArea * .25f);
            if (owner.Length != N) owner = new sbyte[N];
            for (int i = 0; i < N; i++)
            {
                int best = -1; float bestValue = VisibleThreshold;
                for (int t = 0; t < teams; t++) { float v = smooth[t * N + i]; if (v > bestValue) { bestValue = v; best = t; } }
                owner[i] = (sbyte)best;
                if (best < 0) { pixels[i] = default; continue; }
                // 2026-10-17: one flat colour per army (density no longer shades it into several tones).
                float a = MaxAlpha;
                if (best != selectedTeam && selectedTeam >= 0) a *= UnselectedAlpha;
                Color c = palette(best); c.a = a;
                pixels[i] = c;
            }
            // Selected army outline: its cells that touch a cell it does not own.
            if (selectedTeam >= 0 && selectedTeam < teams)
            {
                Color edge = palette(selectedTeam) * .55f; edge.a = 1f;
                Color32 edge32 = edge;
                for (int z = 0; z < R; z++)
                    for (int x = 0; x < R; x++)
                    {
                        int i = z * R + x;
                        if (owner[i] != selectedTeam) continue;
                        bool border = x == 0 || x == R - 1 || z == 0 || z == R - 1 ||
                                      owner[i - 1] != selectedTeam || owner[i + 1] != selectedTeam || owner[i - R] != selectedTeam || owner[i + R] != selectedTeam;
                        if (border) pixels[i] = edge32;
                    }
            }
            texture.SetPixels32(pixels); texture.Apply(false, false);
            // Badge anchors: the owned cell nearest each army's raw head-count centroid (so a badge always sits on its army).
            for (int t = 0; t < MaxTeams; t++) anchorValid[t] = false;
            for (int t = 0; t < teams; t++)
            {
                int o = t * N; double sx = 0, sz = 0, total = 0;
                for (int i = 0; i < N; i++) { uint c = counts[o + i]; if (c == 0) continue; sx += c * (i % R + .5); sz += c * (i / R + .5); total += c; }
                if (total <= 0) continue;
                float cx = (float)(sx / total), cz = (float)(sz / total); int bestCell = -1; float bestDistance = float.MaxValue;
                for (int i = 0; i < N; i++)
                {
                    if (owner[i] != t) continue;
                    float dx = i % R + .5f - cx, dz = i / R + .5f - cz, d = dx * dx + dz * dz;
                    if (d < bestDistance) { bestDistance = d; bestCell = i; }
                }
                if (bestCell < 0) { anchors[t] = new Vector2(cx / R, cz / R); anchorValid[t] = true; continue; }
                anchors[t] = new Vector2((bestCell % R + .5f) / R, (bestCell / R + .5f) / R); anchorValid[t] = true;
            }
            BuildGroupAnchors();
            return true;
        }

        /// <summary>Composition anchors: the cell owned by its army nearest the composition's live centroid.</summary>
        private void BuildGroupAnchors()
        {
            const int R = Resolution;
            for (int g = 0; g < GroupCount; g++)
            {
                groupAlive[g] = 0;
                if (groupData.Length < (g + 1) * GroupStride) continue;
                int o = g * GroupStride;
                uint alive = groupData[o]; if (alive == 0) continue;
                float cx = groupData[o + 1] / 4096f / alive * R, cz = groupData[o + 2] / 4096f / alive * R;
                int team = groupTeam[g], best = -1; float bestDistance = float.MaxValue;
                int x0 = Mathf.Clamp((int)cx, 0, R - 1), z0 = Mathf.Clamp((int)cz, 0, R - 1);
                // Search outward in a window first (cheap); the whole grid only if the window has no owned cell.
                for (int pass = 0; pass < 2 && best < 0; pass++)
                {
                    int w = pass == 0 ? 12 : R;
                    for (int z = Mathf.Max(0, z0 - w); z <= Mathf.Min(R - 1, z0 + w); z++)
                        for (int x = Mathf.Max(0, x0 - w); x <= Mathf.Min(R - 1, x0 + w); x++)
                        {
                            int i = z * R + x; if (owner[i] != team) continue;
                            float dx = x + .5f - cx, dz = z + .5f - cz, d = dx * dx + dz * dz;
                            if (d < bestDistance) { bestDistance = d; best = i; }
                        }
                }
                groupAnchors[g] = best >= 0 ? new Vector2((best % R + .5f) / R, (best / R + .5f) / R) : new Vector2(cx / R, cz / R);
                groupAlive[g] = (int)alive;
            }
        }

        /// <summary>Composition anchor in world space (x, z) and its living head count; false if none alive / no data yet.</summary>
        public bool TryGetGroupAnchor(int g, out Vector3 world, out int alive)
        {
            world = default; alive = 0;
            if (g < 0 || g >= GroupCount || groupAlive[g] <= 0 || WorldSize.x <= 0) return false;
            alive = groupAlive[g];
            world = new Vector3((groupAnchors[g].x - .5f) * WorldSize.x, 0, (groupAnchors[g].y - .5f) * WorldSize.y);
            return true;
        }

        /// <summary>
        /// 2026-10-17: world-space extent (x, z) of a composition's living members from the last readback (camera framing on
        /// badge click). False if none alive or no data yet. y is left at 0.
        /// </summary>
        public bool TryGetGroupExtent(int g, out Vector3 min, out Vector3 max)
        {
            min = max = default;
            if (g < 0 || g >= GroupCount || WorldSize.x <= 0 || groupData.Length < (g + 1) * GroupStride) return false;
            int o = g * GroupStride;
            if (groupData[o] == 0 || groupData[o + 3] > groupData[o + 5] || groupData[o + 4] > groupData[o + 6]) return false;
            const float Q = 1f / 65535f;
            min = new Vector3((groupData[o + 3] * Q - .5f) * WorldSize.x, 0f, (groupData[o + 4] * Q - .5f) * WorldSize.y);
            max = new Vector3((groupData[o + 5] * Q - .5f) * WorldSize.x, 0f, (groupData[o + 6] * Q - .5f) * WorldSize.y);
            return true;
        }

        /// <summary>Army anchor in world space (x, z), or false if the army has no living members on the grid.</summary>
        public bool TryGetAnchor(int team, out Vector3 world)
        {
            world = default;
            if (team < 0 || team >= MaxTeams || !anchorValid[team] || WorldSize.x <= 0) return false;
            world = new Vector3((anchors[team].x - .5f) * WorldSize.x, 0, (anchors[team].y - .5f) * WorldSize.y);
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cells?.Release(); cells = null;
            groupBuffer?.Release(); groupBuffer = null;
            Destroy(shader); Destroy(texture); texture = null;
        }

        private static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}


