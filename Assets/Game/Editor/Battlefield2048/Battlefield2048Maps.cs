using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-14 程序化地图 2048 (approved: size 2048, generated in the Editor and saved as fixed maps, richer terrain,
    /// terrain only affects walking/routing, spawn front gap ≈550 m, 4 templates). Each map builds a seeded height field
    /// (fBm + domain warp, ridged crests, terraces, canyons, passes, thermal + hydraulic erosion) and then enforces the
    /// constraints: flat walkable deployment zones (±400 m), a gentle open core, ≥ MinRoutes wide routes between the
    /// zones (flood fill), optional 180° symmetry. A failed check retries with a sub-seed (up to 8 attempts).
    /// Slope limit stays 38° (cells steeper than that are unwalkable — cliffs need no extra obstacle).
    /// Props reuse the procedural library; the 2,000,000-triangle budget is unchanged, so density per area is ≈1/4
    /// of the 1024 maps and props go to forests, ridges, cliffs and the map edge.
    /// </summary>
    public abstract class Proc2048MapBase : MapSpec
    {
        public override BattlefieldMapSize Size => BattlefieldMapSize.S2048;
        public override float ControlPointRadius => 50f;
        public override Vector3 PreviewCamera => new Vector3(-1250f, 820f, -1500f);
        public override Vector3 PreviewTarget => new Vector3(0f, 0f, 80f);
        public override Vector2[] MustConnect { get { Ensure(); return keeps.ToArray(); } }

        protected abstract string Theme { get; }              // "", "Dry", "Autumn", "Snow"
        protected virtual Vector4 HazeColor => new Vector4(.38f, .57f, .62f, 1);
        protected virtual float RidgeBase => 60f;
        protected virtual float FarMountains => 260f;
        protected virtual bool Symmetric => true;
        protected virtual int MinRoutes => 3;
        /// <summary>x of the column where separate routes are counted (canyon map: off the N–S link at x = 0).</summary>
        protected virtual float RouteColumnX => 0f;
        /// <summary>Core = between the two fronts (|x| &lt; 272) and |z| &lt; CoreHalfDepth; kept below CoreMaxDegrees.</summary>
        protected virtual float CoreHalfDepth => 300f;
        protected virtual float CoreMaxDegrees => 16f;
        protected virtual float EdgeRise => 30f;
        protected abstract void Shape(Field2048 f, Noise2048 n, System.Random rng);
        protected abstract Flora2048 Plants { get; }

        // ------------------------------------------------------------------ build state
        Field2048 field; string buildLog, failure; int attemptUsed;
        /// <summary>Last built field even when the checks failed (preview only).</summary>
        public Field2048 LastField => field;
        public string LastLog => buildLog;
        protected readonly List<Vector2> keeps = new List<Vector2>();
        readonly List<Corridor> corridors = new List<Corridor>();
        public string BuildLog { get { Ensure(); return buildLog; } }
        public Field2048 Field { get { Ensure(); return field; } }

        protected enum Grade { Carve, Ramp }
        struct Corridor { public Vector2 a, b; public float half, falloff; public Grade mode; public float target; public bool fixedTarget, trail, deferred; }

        protected float ZoneHalfX => Size.ArmySize.x * .5f;
        protected float ZoneHalfZ => Size.ArmySize.y * .5f;
        protected float FrontX => Size.ArmyCenter.x - ZoneHalfX;      // 272.6

        /// <summary>Registers a carved/graded strip. Deferred strips are applied after erosion and zone flattening, with end
        /// heights sampled then (ramps, passes). With Symmetric the mirrored strip is added too.</summary>
        protected void Strip(Vector2 a, Vector2 b, float half, float falloff, Grade mode, bool trail = true, bool deferred = true, float? target = null)
        {
            var c = new Corridor { a = a, b = b, half = half, falloff = falloff, mode = mode, trail = trail, deferred = deferred, fixedTarget = target.HasValue, target = target ?? 0f };
            AddStrip(c);
            if (!Symmetric) return;
            var m = c; m.a = -a; m.b = -b;
            bool same = ((m.a - c.a).sqrMagnitude < 1f && (m.b - c.b).sqrMagnitude < 1f) || ((m.a - c.b).sqrMagnitude < 1f && (m.b - c.a).sqrMagnitude < 1f);
            if (!same) AddStrip(m);
        }

        void AddStrip(Corridor c) { corridors.Add(c); if (!c.deferred) ApplyStrip(field, c); }

        /// <summary>Polyline helper: z = f(x) sampled every <paramref name="dx"/> metres between x0 and x1.</summary>
        protected void StripCurve(float x0, float x1, float dx, Func<float, float> zOfX, float half, float falloff, Grade mode, float? target, bool deferred, bool trail = true)
        {
            for (float x = x0; x < x1 - .01f; x += dx)
            {
                float xb = Mathf.Min(x1, x + dx);
                Strip(new Vector2(x, zOfX(x)), new Vector2(xb, zOfX(xb)), half, falloff, mode, trail, deferred, target);
            }
        }

        protected void Keep(float x, float z) { keeps.Add(new Vector2(x, z)); if (Symmetric) keeps.Add(new Vector2(-x, -z)); }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a; t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        void ApplyStrip(Field2048 f, Corridor c)
        {
            float hA = c.fixedTarget ? c.target : Field2048.Sample(f.H, c.a.x, c.a.y);
            float hB = c.fixedTarget ? c.target : Field2048.Sample(f.H, c.b.x, c.b.y);
            float reach = c.half + c.falloff;
            float minX = Mathf.Min(c.a.x, c.b.x) - reach, maxX = Mathf.Max(c.a.x, c.b.x) + reach;
            float minZ = Mathf.Min(c.a.y, c.b.y) - reach, maxZ = Mathf.Max(c.a.y, c.b.y) + reach;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((minX + Field2048.Half) / Field2048.Step)), x1 = Mathf.Min(Field2048.N - 1, Mathf.CeilToInt((maxX + Field2048.Half) / Field2048.Step));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ + Field2048.Half) / Field2048.Step)), z1 = Mathf.Min(Field2048.N - 1, Mathf.CeilToInt((maxZ + Field2048.Half) / Field2048.Step));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(Field2048.X(x), Field2048.X(z));
                    float d = SegmentDistance(p, c.a, c.b, out float t);
                    if (d >= reach) continue;
                    float w = 1f - Field2048.Smooth(c.half, reach, d);
                    float target = Mathf.Lerp(hA, hB, t);
                    int i = z * Field2048.N + x;
                    f.H[i] = Mathf.Lerp(f.H[i], c.mode == Grade.Carve ? Mathf.Min(f.H[i], target) : target, w);
                }
        }

        protected bool InZone(float x, float z, float margin) =>
            Mathf.Abs(Mathf.Abs(x) - Size.ArmyCenter.x) < ZoneHalfX + margin && Mathf.Abs(z) < ZoneHalfZ + margin;

        protected float ZoneDistance(float x, float z)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(Mathf.Abs(x) - Size.ArmyCenter.x) - ZoneHalfX), dz = Mathf.Max(0f, Mathf.Abs(z) - ZoneHalfZ);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        protected float CoreWeight(float x, float z) =>
            (1f - Field2048.Smooth(FrontX - 40f, FrontX + 20f, Mathf.Abs(x))) * (1f - Field2048.Smooth(CoreHalfDepth - 40f, CoreHalfDepth + 60f, Mathf.Abs(z)));

        protected static float EdgeDistance2(float x, float z) => Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));

        /// <summary>Random discs into a mask (copses, rock knolls), skipping zones / keeps / core when asked.</summary>
        protected void Discs(float[] mask, System.Random rng, int count, float rMin, float rMax, Func<float, float, bool> allowed, float soft = 10f, float lift = 0f)
        {
            for (int k = 0; k < count; k++)
            {
                float cx = (float)(rng.NextDouble() * 2 - 1) * (Size.EdgeBlock - 40f), cz = (float)(rng.NextDouble() * 2 - 1) * (Size.EdgeBlock - 40f);
                float r = rMin + (float)rng.NextDouble() * (rMax - rMin);
                if (!allowed(cx, cz) || ZoneDistance(cx, cz) < r + 40f) continue;
                int x0 = Mathf.Max(0, (int)((cx - r - soft + Field2048.Half) / Field2048.Step)), x1 = Mathf.Min(Field2048.N - 1, (int)((cx + r + soft + Field2048.Half) / Field2048.Step) + 1);
                int z0 = Mathf.Max(0, (int)((cz - r - soft + Field2048.Half) / Field2048.Step)), z1 = Mathf.Min(Field2048.N - 1, (int)((cz + r + soft + Field2048.Half) / Field2048.Step) + 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = new Vector2(Field2048.X(x) - cx, Field2048.X(z) - cz).magnitude;
                        float v = Mathf.Clamp01(.5f + (r - d) / soft);
                        int i = z * Field2048.N + x;
                        mask[i] = Mathf.Max(mask[i], v);
                        if (lift > 0f) field.H[i] += lift * Field2048.Smooth(0f, 1f, v);
                    }
            }
        }

        // ------------------------------------------------------------------ build + constraints
        protected void Ensure()
        {
            if (failure != null) throw new Exception(failure);
            if (field != null) return;
            var log = new StringBuilder();
            const int Attempts = 8;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                int sub = Seed + attempt * 7919;
                field = new Field2048(); keeps.Clear(); corridors.Clear();
                var t0 = DateTime.Now;
                BuildOnce(sub);
                bool ok = Check(out string report);
                log.AppendLine("attempt " + attempt + " seed " + sub + " " + (ok ? "OK" : "FAIL") + " (" + (DateTime.Now - t0).TotalSeconds.ToString("F1") + " s)\n" + report);
                if (ok) { attemptUsed = attempt; buildLog = log.ToString(); return; }
            }
            buildLog = log.ToString();
            failure = DisplayName + "：" + Attempts + " 次子种子都没满足约束（详见日志）";
            throw new Exception(DisplayName + "：" + Attempts + " 次子种子都没满足约束（详见日志）\n" + buildLog);
        }

        void BuildOnce(int sub)
        {
            var f = field; var n = new Noise2048(sub); var rng = new System.Random(sub);
            // zones (+ margin) are locked against erosion
            f.Apply(f.Lock, (x, z, v) => InZone(x, z, 30f) ? 1f : 0f);
            Shape(f, n, rng);
            // map edge rise (like the 1024 maps' rim, scaled)
            f.Apply(f.H, (x, z, h) => h + EdgeRise * Field2048.Smooth(Size.EdgeBlock - 60f, Size.Half, EdgeDistance2(x, z)) * (.6f + .4f * n.P(x, z, .01f, 7f)));
            Erode(f, n, sub);
            // deployment zones: flat plateau at the zone's mean height, wide falloff
            float sum = 0; int count = 0;
            for (int z = 0; z < Field2048.N; z++)
                for (int x = 0; x < Field2048.N; x++)
                    if (InZone(Field2048.X(x), Field2048.X(z), 0f)) { sum += f.H[z * Field2048.N + x]; count++; }
            float zoneLevel = sum / Mathf.Max(1, count);
            f.Apply(f.H, (x, z, h) =>
            {
                float w = 1f - Field2048.Smooth(24f, 24f + ZoneFalloff, ZoneDistance(x, z));
                return Mathf.Lerp(h, zoneLevel + 1.2f * (n.P(x, z, .004f, 77f) - .5f), w);
            });
            foreach (var c in corridors.ToArray()) if (c.deferred) ApplyStrip(f, c);
            // gentle open core: blend towards a blurred copy, then clamp the slope
            var blurred = Field2048.Blur(f.H, 8, 3);
            var core = new float[f.H.Length];
            f.Apply(core, (x, z, v) => CoreWeight(x, z));
            for (int i = 0; i < f.H.Length; i++) f.H[i] = Mathf.Lerp(f.H[i], blurred[i], core[i] * .8f);
            f.SlopeClamp(core, CoreMaxDegrees - 2f, 40);
            if (Symmetric) Field2048.Symmetrize(f.H, 120f);
            // masks: open where obstacles must not be (zones, keeps, trails), then cut forest / rock there
            f.Apply(f.Open, (x, z, v) =>
            {
                float o = 1f - Field2048.Smooth(10f, 18f, ZoneDistance(x, z));
                var p = new Vector2(x, z);
                foreach (var k in keeps) o = Mathf.Max(o, 1f - Field2048.Smooth(26f, 36f, (p - k).magnitude));
                return o;
            });
            foreach (var c in corridors)
            {
                if (!c.trail) continue;
                float reach = c.half + 12f;
                f.Apply(f.Trail, (x, z, v) =>
                {
                    if (v >= 1f) return v;
                    float d = SegmentDistance(new Vector2(x, z), c.a, c.b, out _);
                    return d >= reach ? v : Mathf.Max(v, 1f - Field2048.Smooth(c.half * .5f, reach, d));
                });
            }
            for (int i = 0; i < f.H.Length; i++) f.Open[i] = Mathf.Max(f.Open[i], f.Trail[i] > .6f ? 1f : 0f);
            if (Symmetric) { Field2048.Symmetrize(f.Forest, 40f); Field2048.Symmetrize(f.Rock, 40f); Field2048.Symmetrize(f.Open, 40f); Field2048.Symmetrize(f.Trail, 40f); }
            for (int i = 0; i < f.H.Length; i++) { float keep = 1f - f.Open[i]; f.Forest[i] = Mathf.Min(f.Forest[i], Mathf.Lerp(.3f, f.Forest[i], keep)); f.Rock[i] = Mathf.Min(f.Rock[i], Mathf.Lerp(.3f, f.Rock[i], keep)); }
        }

        protected virtual float ZoneFalloff => 150f;

        /// <summary>Default erosion: some thermal smoothing plus droplets. Cliff maps override with a steep talus.</summary>
        protected virtual void Erode(Field2048 f, Noise2048 n, int seed)
        {
            f.Thermal(12, 34f, .5f);
            f.Hydraulic(seed, 60000);
        }

        bool CellBlocked(int cx, int cz)
        {
            float x = -Field2048.Half + (cx + .5f) * Field2048.Step, z = -Field2048.Half + (cz + .5f) * Field2048.Step;
            if (EdgeDistance2(x, z) > Size.EdgeBlock) return true;
            return Field2048.Sample(field.Forest, x, z) > .5f || Field2048.Sample(field.Rock, x, z) > .5f;
        }

        static int CellOf(float v) => Mathf.Clamp(Mathf.FloorToInt((v + Field2048.Half) / Field2048.Step), 0, Field2048.Cells - 1);

        bool Check(out string report)
        {
            const int M = Field2048.Cells;
            var walk = new bool[M * M];
            int walkable = 0, steep = 0, blocked = 0; float coreMax = 0f;
            for (int z = 0; z < M; z++)
                for (int x = 0; x < M; x++)
                {
                    bool b = CellBlocked(x, z), s = field.SlopeWalkable(x, z, Battlefield1024Generator.SlopeLimit);
                    walk[z * M + x] = !b && s; if (walk[z * M + x]) walkable++; else if (!b) steep++; else blocked++;
                    float cx = -Field2048.Half + (x + .5f) * Field2048.Step, cz = -Field2048.Half + (z + .5f) * Field2048.Step;
                    if (CoreWeight(cx, cz) > .99f) coreMax = Mathf.Max(coreMax, field.SlopeDegrees(x, z));
                }
            // zones fully walkable
            int zoneBad = 0;
            for (int z = 0; z < M; z++)
                for (int x = 0; x < M; x++)
                {
                    float cx = -Field2048.Half + (x + .5f) * Field2048.Step, cz = -Field2048.Half + (z + .5f) * Field2048.Step;
                    if (InZone(cx, cz, 0f) && !walk[z * M + x]) zoneBad++;
                }
            // wide = 8 m clearance (agents need room; the nav grid applies its own clearance too)
            var wide = new bool[M * M];
            for (int z = 2; z < M - 2; z++)
                for (int x = 2; x < M - 2; x++)
                {
                    bool ok = true;
                    for (int dz = -2; dz <= 2 && ok; dz++) for (int dx = -2; dx <= 2 && ok; dx++) ok = walk[(z + dz) * M + x + dx];
                    wide[z * M + x] = ok;
                }
            var reach = new bool[M * M]; var queue = new Queue<int>();
            int start = CellOf(-Size.ArmyCenter.x) + CellOf(0f) * M;
            if (wide[start]) { reach[start] = true; queue.Enqueue(start); }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue(), cx = c % M, cz = c / M;
                if (cx > 0) Visit(c - 1); if (cx < M - 1) Visit(c + 1); if (cz > 0) Visit(c - M); if (cz < M - 1) Visit(c + M);
            }
            void Visit(int c) { if (!reach[c] && wide[c]) { reach[c] = true; queue.Enqueue(c); } }
            bool east = reach[CellOf(Size.ArmyCenter.x) + CellOf(0f) * M];
            var missing = keeps.Where(k => !reach[CellOf(k.x) + CellOf(k.y) * M]).ToList();
            // routes: runs of reachable cells crossing the centre column (gaps ≤ 2 cells merged, width ≥ 24 m)
            int col = CellOf(RouteColumnX); var runs = new List<Vector2Int>(); int runStart = -1, gap = 0;
            for (int z = 0; z <= M; z++)
            {
                bool r = z < M && reach[z * M + col];
                if (r) { if (runStart < 0) runStart = z; gap = 0; }
                else if (runStart >= 0)
                {
                    gap++;
                    if (gap > 2 || z == M) { int end = z - gap; if (end - runStart + 1 >= 6) runs.Add(new Vector2Int(runStart, end)); runStart = -1; gap = 0; }
                }
            }
            float min = field.H.Min(), max = field.H.Max();
            var sb = new StringBuilder();
            sb.AppendFormat("  heights {0:F0}..{1:F0} m; cells walkable {2:P0}, steep {3:P0}, obstacles {4:P0}; core max slope {5:F1}°\n",
                min, max, walkable / (float)(M * M), steep / (float)(M * M), blocked / (float)(M * M), coreMax);
            sb.AppendFormat("  zones bad cells {0}; east reachable {1}; routes at x={5:F0}: {2} [{3}]; missing keeps: {4}\n", zoneBad, east, runs.Count,
                string.Join(", ", runs.Select(r => ((-Field2048.Half + r.x * Field2048.Step)).ToString("F0") + ".." + ((-Field2048.Half + (r.y + 1) * Field2048.Step)).ToString("F0"))),
                missing.Count == 0 ? "none" : string.Join(" ", missing), RouteColumnX);
            report = sb.ToString();
            return zoneBad == 0 && east && missing.Count == 0 && runs.Count >= MinRoutes && coreMax <= CoreMaxDegrees + 4f;
        }

        // ------------------------------------------------------------------ MapSpec fields
        public override float Height(float x, float z) { Ensure(); return Field2048.Sample(field.H, x, z); }
        public override bool Blocked(float x, float z) { Ensure(); return Field2048.Sample(field.Forest, x, z) > .5f || Field2048.Sample(field.Rock, x, z) > .5f; }

        public override Color32 Splat(float x, float z, float height, float slope)
        {
            Ensure();
            float forest = Smooth(.34f, .62f, Field2048.Sample(field.Forest, x, z)), rock = Smooth(.3f, .6f, Field2048.Sample(field.Rock, x, z));
            float trail = Field2048.Sample(field.Trail, x, z) * (1f - Smooth(Size.EdgeBlock - 20f, Size.EdgeBlock, EdgeDistance2(x, z)));
            float steep = Smooth(.13f, .3f, slope);
            float worn = Smooth(.72f, .86f, Noise(x, z, .011f, 13f)) * .3f;
            float dirt = Mathf.Max(Mathf.Max(trail * .8f, worn), ExtraDirt(x, z, height, slope)) * (1f - rock) * (1f - forest * .85f);
            float scree = Smooth(.8f, .93f, Noise(x, z, .03f, 29f)) * Smooth(.05f, .12f, slope) * .6f;
            float meadow = Smooth(.42f, .78f, Noise(x, z, .004f, 23f)) * .75f * (1f - forest) * (1f - steep);
            return new Color32(B(dirt), B(Mathf.Max(Mathf.Max(rock, steep), scree)), B(forest), B(meadow));
        }

        protected virtual float ExtraDirt(float x, float z, float height, float slope) => 0f;

        GroundLook look;
        public override GroundLook Look
        {
            get
            {
                if (look != null) return look;
                look = new GroundLook
                {
                    GrassA = ProcPropLibrary.GroundPath("GrassA", Theme), GrassB = ProcPropLibrary.GroundPath("GrassB", Theme),
                    Dirt = ProcPropLibrary.GroundPath("Dirt", Theme), Rock = ProcPropLibrary.GroundPath("Rock", Theme), Forest = ProcPropLibrary.GroundPath("Forest", Theme),
                    Tiling = new Vector4(.065f, .055f, .1f, .05f), ForestTiling = .065f, Stylize = 0f,
                    SlopeRock = new Vector4(.14f, .34f, 0, 0), OutsideMix = new Vector4(0, .05f, .55f, .4f),
                    Haze = HazeColor, HazeStart = 1100f, HazeEnd = 3600f, RidgeBase = RidgeBase, FarMountains = FarMountains,
                };
                return look;
            }
        }

        // ------------------------------------------------------------------ decoration (same library as the 1024 procedural maps)
        protected sealed class Flora2048
        {
            public (string cat, float w)[] Trees, Border, Rocks, Under, Meadow;
            public string Distant = "Pine", Mountain = "Mountain", Pebbles = "Pebbles";
            public float TreeMin = 10f, TreeMax = 17f, TreeSpacing = 11f, RockSpacing = 7f, RockMin = 3f, RockMax = 9f, TallMin = 8f, TallMax = 16f;
            public float UnderChance = .35f, MeadowDensity = .5f, DistantDensity = .55f;
            public int MaxTrees = 3800, MaxBorder = 1400, MaxRocks = 1500, MaxCliff = 700, MaxUnder = 900, MaxMeadow = 900, MaxDistant = 1100, Mountains = 14;
        }

        static string[] Pool(string cat) => ProcPropLibrary.Pool(cat);
        static string Weighted(Decorator d, (string cat, float w)[] set)
        {
            float u = d.Rand(0f, set.Sum(s => s.w));
            foreach (var s in set) { if (u < s.w) return s.cat; u -= s.w; }
            return set[set.Length - 1].cat;
        }
        static readonly Dictionary<string, Vector2> SmallHeights = new Dictionary<string, Vector2>
        {
            { "Bush", new Vector2(1.6f, 3.2f) }, { "BushDry", new Vector2(1.4f, 2.8f) }, { "BushSnow", new Vector2(1.6f, 3f) },
            { "Fern", new Vector2(.8f, 1.4f) }, { "Mushroom", new Vector2(.6f, 1.1f) }, { "Stump", new Vector2(1.2f, 2.2f) },
            { "FallenLog", new Vector2(.8f, 1.2f) }, { "Logs", new Vector2(1.2f, 2f) }, { "FlowerPatch", new Vector2(.5f, .9f) },
            { "Flower", new Vector2(.7f, 1.3f) }, { "Grass", new Vector2(.7f, 1.3f) }, { "GrassDry", new Vector2(.7f, 1.3f) }, { "Pebbles", new Vector2(.3f, .6f) },
        };
        static float SmallHeight(Decorator d, string cat) => SmallHeights.TryGetValue(cat, out var h) ? d.Rand(h.x, h.y) : d.Rand(.8f, 1.5f);

        float SlopeAt(Decorator d, float x, float z) => d.Surface.TrySample(new Vector2(x, z), out var q) ? q.Gradient.magnitude : 0f;

        public override void Decorate(Decorator d)
        {
            Ensure();
            var f = Plants; float lim = Size.EdgeBlock, H = Size.Half;
            Func<float, float, float> forest = (x, z) => Field2048.Sample(field.Forest, x, z), rock = (x, z) => Field2048.Sample(field.Rock, x, z);
            d.Scatter("林地", -lim, -lim, lim, lim, f.TreeSpacing, f.MaxTrees,
                (x, z) => EdgeDistance2(x, z) < lim - 8f && forest(x, z) > .5f && rock(x, z) <= .5f && d.Blocked(x, z),
                (x, z) =>
                {
                    bool interior = d.DeepBlocked(x, z, 14f);
                    return new Decorator.Choice(d.Pick(Pool(Weighted(d, f.Trees)), interior), d.Rand(f.TreeMin, f.TreeMax), interior, !interior && d.Chance(.45f), .35f);
                });
            d.Scatter("岩丘", -lim, -lim, lim, lim, f.RockSpacing, f.MaxRocks,
                (x, z) => rock(x, z) > .5f && d.Blocked(x, z) && d.Chance(.8f),
                (x, z) =>
                {
                    string c = Weighted(d, f.Rocks); bool tall = c == "RockTall";
                    return new Decorator.Choice(d.Pick(Pool(c), false), tall ? d.Rand(f.TallMin, f.TallMax) : d.Rand(f.RockMin, f.RockMax), false, d.Chance(.5f), .6f);
                });
            // cliffs (unwalkable by slope, no forest): embedded boulders so the escarpments read as rock
            d.Scatter("崖壁岩石", -lim, -lim, lim, lim, 15f, f.MaxCliff,
                (x, z) => EdgeDistance2(x, z) < lim - 8f && forest(x, z) <= .5f && SlopeAt(d, x, z) > .95f && d.Chance(.75f),
                (x, z) =>
                {
                    string c = Weighted(d, f.Rocks);
                    return new Decorator.Choice(d.Pick(Pool(c), false), d.Rand(5f, 11f), false, d.Chance(.4f), 1.2f);
                });
            d.Scatter("边界林带", -H + 4f, -H + 4f, H - 4f, H - 4f, 15f, f.MaxBorder,
                (x, z) => EdgeDistance2(x, z) >= lim - 8f && EdgeDistance2(x, z) < H - 4f,
                (x, z) => new Decorator.Choice(d.Pick(Pool(Weighted(d, f.Border)), true), d.Rand(f.TreeMin + 2f, f.TreeMax + 3f), true, false, .4f));
            d.Scatter("林缘灌木", -lim, -lim, lim, lim, 8f, f.MaxUnder,
                (x, z) => d.Blocked(x, z) && forest(x, z) > .3f && !d.DeepBlocked(x, z, 6f) && rock(x, z) <= .5f && d.Chance(f.UnderChance),
                (x, z) => { string c = Weighted(d, f.Under); return new Decorator.Choice(d.Pick(Pool(c), false), SmallHeight(d, c), false, false, .1f); });
            d.Scatter("草甸花草", -lim, -lim, lim, lim, 22f, f.MaxMeadow,
                (x, z) => !d.Blocked(x, z) && !InZone(x, z, 10f) && d.Chance(f.MeadowDensity * Smooth(.45f, .7f, Noise(x, z, .006f, 41f))),
                (x, z) => { string c = Weighted(d, f.Meadow); return new Decorator.Choice(d.Pick(Pool(c), false), SmallHeight(d, c), false, false, .05f); });
            d.Scatter("碎石", -lim, -lim, lim, lim, 18f, 450,
                (x, z) => !d.Blocked(x, z) && (rock(x, z) > .15f || SlopeAt(d, x, z) > .45f) && d.Chance(.5f),
                (x, z) => new Decorator.Choice(d.Pick(Pool(f.Pebbles), false), d.Rand(.3f, .6f), false, false, .05f));
            var far = Pool(f.Distant);
            d.Scatter("远景树林", -H - 1100f, -H - 1100f, H + 1100f, H + 1100f, 32f, f.MaxDistant,
                (x, z) =>
                {
                    float e = EdgeDistance2(x, z);
                    if (e < H + 6f || e > H + 1100f) return false;
                    return d.Chance(f.DistantDensity * (1f - Smooth(H + 200f, H + 1100f, e)) * Smooth(.35f, .6f, Noise(x, z, .004f, 9f)));
                },
                (x, z) => new Decorator.Choice(d.Pick(far, true), d.Rand(15f, 26f), true, false, .4f));
            var g = d.Group("远山");
            for (int i = 0; i < f.Mountains; i++)
            {
                float a = i * Mathf.PI * 2f / f.Mountains + d.Rand(-.15f, .15f), r = d.Rand(2900f, 4300f);
                d.Place(d.Pick(Pool(f.Mountain), false), Mathf.Cos(a) * r, Mathf.Sin(a) * r, d.Rand(200f, 360f), g, false, false, 0f);
            }
        }
    }

    // ==================================================================== 0. 测试平原（性能预检，隐藏）
    public sealed class TestPlain2048Map : Proc2048MapBase
    {
        public override string Id => "test-plain-2048";
        public override string Folder => "TestPlain2048";
        public override string DisplayName => "测试平原 2048";
        public override string Description => "2048×2048 平地（性能预检用，不在地图列表显示）";
        public override string Briefing => "性能预检用平地。";
        public override int Seed => 4001;
        protected override string Theme => "";
        protected override int MinRoutes => 1;
        protected override void Shape(Field2048 f, Noise2048 n, System.Random rng) => f.Apply(f.H, (x, z, h) => 3f * n.Fbm(x, z, .002f, 3));
        protected override void Erode(Field2048 f, Noise2048 n, int seed) { }
        protected override Flora2048 Plants => new Flora2048
        {
            Trees = new[] { ("Pine", 1f) }, Border = new[] { ("Pine", .5f), ("Broadleaf", .5f) }, Rocks = new[] { ("Rock", 1f) },
            Under = new[] { ("Bush", 1f) }, Meadow = new[] { ("Grass", 1f) }, MaxMeadow = 0, MaxDistant = 600,
        };
    }

    // ==================================================================== 1. 丘陵会战
    public sealed class RollingHills2048Map : Proc2048MapBase
    {
        public override string Id => "rolling-hills-2048";
        public override string Folder => "Hills2048";
        public override string DisplayName => "丘陵会战";
        public override string Description => "2048×2048 起伏丘陵（程序化）\n连绵缓丘与林地，中央开阔，适合大军团正面会战与侧翼迂回。";
        public override string Briefing => "两军之间是开阔缓坡，两翼丘陵起伏、林地成片，可借丘陵与树林迂回。\n树林、岩丘、陡坡为禁行区，外圈为地图边界。";
        public override int Seed => 4101;
        protected override string Theme => "";
        protected override int MinRoutes => 1;
        protected override float CoreHalfDepth => 420f;
        protected override float CoreMaxDegrees => 14f;

        protected override void Shape(Field2048 f, Noise2048 n, System.Random rng)
        {
            f.Apply(f.H, (x, z, h) => 36f * n.FbmWarp(x, z, .0014f, 5, .0009f, 170f) + 10f * n.Ridged(x, z, .0035f, 3) * n.Fbm(x, z, .001f, 2, 5f));
            // a few big hills on the flanks / behind the lines
            for (int k = 0; k < 12; k++)
            {
                float hx = (float)(rng.NextDouble() * 2 - 1) * 880f, hz = (float)(rng.NextDouble() * 2 - 1) * 880f;
                if (Mathf.Abs(hz) < 380f && Mathf.Abs(hx) < 650f) continue;
                float amp = 14f + (float)rng.NextDouble() * 16f, rx = 120f + (float)rng.NextDouble() * 110f, rz = 110f + (float)rng.NextDouble() * 120f;
                f.Apply(f.H, (x, z, h) => h + amp * Gauss(x, z, hx, hz, rx, rz));
            }
            // copses on hill tops and in the flanks, a few rock knolls
            f.Apply(f.Forest, (x, z, v) => Mathf.Max(v, (1f - CoreWeight(x, z)) * Smooth(.6f, .68f, n.Fbm(x, z, .004f, 3, 33f))));
            Discs(f.Forest, rng, 70, 22f, 55f, (x, z) => CoreWeight(x, z) < .2f, 12f);
            Discs(f.Rock, rng, 40, 9f, 18f, (x, z) => CoreWeight(x, z) < .1f, 6f, 4f);
            Keep(0f, 700f);
        }

        // softer rills than the default (no unwalkable gully streaks on the hill flanks)
        protected override void Erode(Field2048 f, Noise2048 n, int seed)
        {
            f.Thermal(12, 30f, .5f);
            f.Hydraulic(seed, 30000, .5f);
        }

        protected override Flora2048 Plants => new Flora2048
        {
            Trees = new[] { ("Broadleaf", .45f), ("Pine", .3f), ("Birch", .25f) },
            Border = new[] { ("Pine", .5f), ("Broadleaf", .5f) },
            Rocks = new[] { ("Rock", .5f), ("RockPile", .4f), ("RockTall", .1f) },
            Under = new[] { ("Bush", .35f), ("Fern", .35f), ("Stump", .15f), ("FallenLog", .15f) },
            Meadow = new[] { ("FlowerPatch", .4f), ("Grass", .4f), ("Flower", .2f) },
            Distant = "Pine",
        };
    }

    // ==================================================================== 2. 山脊隘口
    public sealed class RidgePass2048Map : Proc2048MapBase
    {
        public override string Id => "ridge-pass-2048";
        public override string Folder => "RidgePass2048";
        public override string DisplayName => "山脊隘口";
        public override string Description => "2048×2048 山脊与隘口（程序化）\n一道林木山脊纵贯战场，只能从中央大隘口和南北两处小隘口通过。";
        public override string Briefing => "两军之间横着一道陡峭山脊，中央隘口宽阔，南北各有一条窄隘口可以迂回。\n山脊陡坡、树林、岩丘为禁行区，外圈为地图边界。";
        public override int Seed => 4202;
        protected override string Theme => "Autumn";
        protected override Vector4 HazeColor => new Vector4(.5f, .54f, .56f, 1);
        protected override int MinRoutes => 3;
        protected override float CoreHalfDepth => 110f;
        protected override float CoreMaxDegrees => 16f;

        static float RidgeX(float z) => 70f * Mathf.Sin(z / 260f);   // odd in z → point symmetric

        protected override void Shape(Field2048 f, Noise2048 n, System.Random rng)
        {
            f.Apply(f.H, (x, z, h) =>
            {
                float baseH = 10f * n.FbmWarp(x, z, .0016f, 4, .001f, 120f);
                float r = Mathf.Abs(x - RidgeX(z));
                float crest = 62f + 26f * n.Ridged(0f, z, .004f, 3, 11f);
                float ridge = crest * Field2048.Smooth(170f, 45f, r) + 10f * n.Ridged(x, z, .012f, 2, 3f) * Field2048.Smooth(170f, 60f, r);
                float flank = 26f * Gauss(x, z, -760f, 420f, 150f, 220f) + 22f * Gauss(x, z, -720f, -520f, 140f, 160f);
                return baseH + ridge + flank;
            });
            // forest on the ridge slopes (with gaps), copses behind the lines
            f.Apply(f.Forest, (x, z, v) =>
            {
                float r = Mathf.Abs(x - RidgeX(z));
                float onRidge = Field2048.Smooth(185f, 120f, r) * Smooth(.38f, .5f, n.Fbm(x, z, .006f, 3, 21f));
                return Mathf.Max(v, onRidge);
            });
            Discs(f.Forest, rng, 45, 24f, 60f, (x, z) => Mathf.Abs(x - RidgeX(z)) > 260f && Mathf.Abs(x) > 300f, 12f);
            Discs(f.Rock, rng, 30, 9f, 16f, (x, z) => Mathf.Abs(x - RidgeX(z)) > 200f, 6f, 4f);
            // passes through the ridge: centre (wide) and north / south (narrow); mirrored copies are added automatically
            Strip(new Vector2(-300f, 0f), new Vector2(300f, 0f), 95f, 70f, Grade.Carve, false);
            Strip(new Vector2(-300f, 560f), new Vector2(300f, 560f), 42f, 55f, Grade.Carve);
            Keep(0f, 560f); Keep(0f, 0f);
        }

        // keep the ridge flanks steeper than the 38° limit: no low-angle thermal pass
        protected override void Erode(Field2048 f, Noise2048 n, int seed)
        {
            f.Hydraulic(seed, 50000, .8f);
            f.Thermal(4, 50f, .4f);
        }

        protected override Flora2048 Plants => new Flora2048
        {
            Trees = new[] { ("Autumn", .45f), ("Broadleaf", .25f), ("Birch", .2f), ("Pine", .1f) },
            Border = new[] { ("Autumn", .35f), ("Broadleaf", .3f), ("Pine", .35f) },
            Rocks = new[] { ("Rock", .5f), ("RockPile", .35f), ("RockTall", .15f) },
            Under = new[] { ("Bush", .3f), ("FallenLog", .2f), ("Mushroom", .2f), ("Fern", .2f), ("Stump", .1f) },
            Meadow = new[] { ("FlowerPatch", .4f), ("Grass", .4f), ("Flower", .2f) },
            Distant = "Broadleaf", MaxTrees = 4200,
        };
    }

    // ==================================================================== 3. 峡谷台地
    public sealed class CanyonMesa2048Map : Proc2048MapBase
    {
        public override string Id => "canyon-mesa-2048";
        public override string Folder => "CanyonMesa2048";
        public override string DisplayName => "峡谷台地";
        public override string Description => "2048×2048 峡谷与台地（程序化）\n层层台地被断崖分隔，三条干河谷与中央盆地相连。";
        public override string Briefing => "中央是开阔盆地；南北两条峡谷贯穿东西，台地之间以断崖相隔，只有坡道可上下。\n断崖、岩柱、枯树林为禁行区，外圈为地图边界。";
        public override int Seed => 4303;
        protected override string Theme => "Dry";
        protected override Vector4 HazeColor => new Vector4(.6f, .56f, .47f, 1);
        protected override float RidgeBase => 50f;
        protected override int MinRoutes => 3;
        protected override float CoreHalfDepth => 220f;
        protected override float CoreMaxDegrees => 14f;
        protected override float RouteColumnX => 150f;
        const float Floor = 2f;

        static float CanyonZ(float x) => 430f + 55f * Mathf.Sin(x / 170f) + 25f * Mathf.Sin(x / 63f + 1.3f);

        protected override void Shape(Field2048 f, Noise2048 n, System.Random rng)
        {
            f.Apply(f.H, (x, z, h) => Field2048.Terrace(10f + 95f * n.FbmWarp(x, z, .0017f, 4, .0009f, 220f), 14f, .08f));
            // central basin
            f.Apply(f.H, (x, z, h) => Mathf.Lerp(h, Floor + 3f * n.P(x, z, .01f, 3f), 1f - Field2048.Smooth(170f, 470f, new Vector2(x, z * 1.15f).magnitude)));
            // two meandering dry canyons across the map (north one authored, south one is its mirror) + N–S link to the basin
            StripCurve(-990f, 990f, 30f, CanyonZ, 34f, 22f, Grade.Carve, Floor, false);
            Strip(new Vector2(0f, 120f), new Vector2(0f, 430f), 32f, 22f, Grade.Carve, true, false, Floor);
            // hoodoo fields and boulders (rock masks raise the ground a little)
            Discs(f.Rock, rng, 70, 7f, 15f, (x, z) => new Vector2(x, z).magnitude > 360f && Mathf.Abs(z - CanyonZ(x)) > 70f, 5f, 6f);
            f.Apply(f.Forest, (x, z, v) => Mathf.Max(v, Smooth(.66f, .72f, n.Fbm(x, z, .005f, 3, 17f)) * Field2048.Smooth(300f, 420f, new Vector2(x, z).magnitude)));
            // ramps (applied after the zones are flattened): zone ↔ basin, zone ↔ north canyon (mirrors added automatically)
            Strip(new Vector2(-340f, 0f), new Vector2(-170f, 0f), 70f, 30f, Grade.Ramp);
            Strip(new Vector2(-400f, 250f), new Vector2(-400f, CanyonZ(-400f)), 36f, 20f, Grade.Ramp);
            Keep(0f, 430f);
        }

        protected override void Erode(Field2048 f, Noise2048 n, int seed)
        {
            f.Hydraulic(seed, 50000, .8f);
            f.Thermal(4, 58f, .4f);
        }

        protected override float ZoneFalloff => 120f;

        protected override float ExtraDirt(float x, float z, float height, float slope) => Smooth(Floor + 4f, Floor + 1f, height) * .7f;

        protected override Flora2048 Plants => new Flora2048
        {
            Trees = new[] { ("Dead", .4f), ("Cypress", .35f), ("Pine", .25f) },
            Border = new[] { ("Pine", .5f), ("Dead", .2f), ("Cypress", .3f) },
            Rocks = new[] { ("RockTall", .45f), ("Rock", .4f), ("RockPile", .15f) },
            Under = new[] { ("BushDry", .6f), ("Stump", .2f), ("Logs", .2f) },
            Meadow = new[] { ("GrassDry", .6f), ("Pebbles", .25f), ("Flower", .15f) },
            Distant = "Cypress", TreeMin = 8f, TreeMax = 14f, TallMin = 10f, TallMax = 22f, MaxCliff = 900, DistantDensity = .4f,
        };
    }

    // ==================================================================== 4. 断崖高原
    public sealed class CliffPlateau2048Map : Proc2048MapBase
    {
        public override string Id => "cliff-plateau-2048";
        public override string Folder => "CliffPlateau2048";
        public override string DisplayName => "断崖高原";
        public override string Description => "2048×2048 雪原与断崖高原（程序化）\n南北两块高原以断崖俯瞰中央雪原，各有三条坡道可上。";
        public override string Briefing => "中央雪原开阔；南北两块高原边缘是断崖，只能从坡道登上，高原上松林与岩丘交错。\n断崖、松林、岩丘为禁行区，外圈为地图边界。";
        public override int Seed => 4404;
        protected override string Theme => "Snow";
        protected override Vector4 HazeColor => new Vector4(.66f, .73f, .78f, 1);
        protected override float FarMountains => 320f;
        protected override int MinRoutes => 3;
        protected override float CoreHalfDepth => 300f;
        protected override float CoreMaxDegrees => 14f;
        protected override float ZoneFalloff => 110f;

        protected override void Shape(Field2048 f, Noise2048 n, System.Random rng)
        {
            f.Apply(f.H, (x, z, h) =>
            {
                float low = 8f * n.FbmWarp(x, z, .0018f, 4, .001f, 140f);
                float edge = 440f + 520f * (n.Fbm(x, z, .0016f, 3, 61f) - .5f);
                float plateau = Field2048.Smooth(edge - 8f, edge + 8f, Mathf.Abs(z));
                float top = 50f + 10f * n.Fbm(x, z, .003f, 3, 13f);
                float butte = 42f * Field2048.Smooth(85f, 70f, new Vector2(x + 770f, z - 140f).magnitude);
                return low + plateau * top + butte;
            });
            f.Apply(f.Forest, (x, z, v) =>
            {
                float high = Field2048.Smooth(380f, 520f, Mathf.Abs(z));
                return Mathf.Max(v, high * Smooth(.5f, .58f, n.Fbm(x, z, .004f, 3, 29f)));
            });
            Discs(f.Forest, rng, 30, 22f, 45f, (x, z) => Mathf.Abs(z) < 380f && Mathf.Abs(x) > 600f, 12f);
            Discs(f.Rock, rng, 40, 9f, 17f, (x, z) => Mathf.Abs(z) > 520f || Mathf.Abs(x) > 620f, 6f, 5f);
            // ramps up the north escarpment (the south ones are the mirror images)
            Strip(new Vector2(-650f, 250f), new Vector2(-650f, 640f), 34f, 18f, Grade.Ramp);
            Strip(new Vector2(-150f, 260f), new Vector2(-90f, 660f), 34f, 18f, Grade.Ramp);
            Strip(new Vector2(380f, 260f), new Vector2(380f, 650f), 34f, 18f, Grade.Ramp);
            Keep(0f, 720f); Keep(-650f, 700f);
        }

        protected override void Erode(Field2048 f, Noise2048 n, int seed)
        {
            f.Hydraulic(seed, 40000, .7f);
            f.Thermal(6, 52f, .4f);
        }

        protected override Flora2048 Plants => new Flora2048
        {
            Trees = new[] { ("PineSnow", .88f), ("Dead", .12f) },
            Border = new[] { ("PineSnow", 1f) },
            Rocks = new[] { ("RockSnow", .85f), ("RockTall", .15f) },
            Under = new[] { ("BushSnow", .5f), ("Stump", .25f), ("Logs", .25f) },
            Meadow = new[] { ("GrassDry", .5f), ("Pebbles", .5f) },
            Distant = "PineSnow", Mountain = "MountainSnow", MeadowDensity = .35f, TreeMin = 11f, TreeMax = 18f,
        };
    }

    // ==================================================================== menus (no dialogs; remote-safe)
    public static class Battlefield2048Menus
    {
        const string Menu = Battlefield2048Setup.MenuRoot;
        public static MapSpec[] All => new MapSpec[] { new RollingHills2048Map(), new RidgePass2048Map(), new CanyonMesa2048Map(), new CliffPlateau2048Map() };

        [MenuItem(Menu + "生成 测试平原（性能预检，隐藏）", false, 10)] static void M0() => Run(new TestPlain2048Map());
        [MenuItem(Menu + "生成 丘陵会战", false, 20)] static void M1() => Run(new RollingHills2048Map());
        [MenuItem(Menu + "生成 山脊隘口", false, 21)] static void M2() => Run(new RidgePass2048Map());
        [MenuItem(Menu + "生成 峡谷台地", false, 22)] static void M3() => Run(new CanyonMesa2048Map());
        [MenuItem(Menu + "生成 断崖高原", false, 23)] static void M4() => Run(new CliffPlateau2048Map());
        [MenuItem(Menu + "生成全部（4 张，较慢）", false, 30)] static void MAll() => Run(All);
        [MenuItem(Menu + "只检查地形（不生成，写日志和预览图）", false, 40)] static void MCheck() => CheckOnly(All.Concat(new MapSpec[] { new TestPlain2048Map() }).ToArray());

        /// <summary>Same as ProcMapMenus.Run but does not rebuild the prop library (prefabs must already exist).</summary>
        public static void Run(params MapSpec[] maps)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Battlefield2048] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[Battlefield2048] 有未保存的场景，已取消。"); return; }
            if (!File.Exists(Battlefield2048Setup.TemplateScene)) { Debug.LogError("[Battlefield2048] 缺少模板，请先运行「准备模板」。"); return; }
            string restore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var lines = new List<string>();
            try
            {
                foreach (var m in maps)
                {
                    var t0 = DateTime.Now;
                    try
                    {
                        if (m is Proc2048MapBase p) WriteTerrainLog(p);
                        var report = Battlefield1024Generator.Generate(m);
                        lines.Add("OK " + m.Id + " " + (DateTime.Now - t0).TotalSeconds.ToString("F0") + " s\n" + report.Summary() + "\n  prefabs: " + string.Join(", ", report.prefabs.Take(12)) + "\n  notes: " + string.Join("；", report.notes));
                        Debug.Log("[Battlefield2048] 生成完成：" + report.Summary());
                    }
                    catch (Exception e) { lines.Add("FAIL " + m.Id + "：" + e.Message); Debug.LogError("[Battlefield2048] 生成失败：" + m.DisplayName + " — " + e.Message); Debug.LogException(e); }
                    EditorUtility.UnloadUnusedAssetsImmediate(); GC.Collect();
                }
                if (maps.Any(m => m is TestPlain2048Map)) Hide("test-plain-2048");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Battlefield1024Generator.RepairCatalogAndBuildSettings();
                WriteText(BattlefieldMapSize.S2048.LogDir + "/generate.txt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + string.Join("\n", lines) + "\n");
                if (!string.IsNullOrEmpty(restore) && File.Exists(restore) && UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != restore)
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(restore, UnityEditor.SceneManagement.OpenSceneMode.Single);
            }
        }

        static void Hide(string id)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WarSandboxBattlefieldCatalog>(Battlefield1024Generator.CatalogPath);
            var e = catalog != null ? catalog.entries.FirstOrDefault(x => x.id == id) : null;
            if (e == null) return;
            e.hiddenFromSelection = true; e.selectionNote = "性能预检用";
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
        }

        public static void CheckOnly(MapSpec[] maps)
        {
            foreach (var m in maps)
            {
                if (!(m is Proc2048MapBase p)) continue;
                try { WriteTerrainLog(p); Debug.Log("[Battlefield2048] 地形检查 " + p.DisplayName + "\n" + p.BuildLog); }
                catch (Exception e) { Debug.LogError("[Battlefield2048] 地形检查失败 " + p.DisplayName + "：" + e.Message.Split('\n')[0]); }
            }
        }

        static void WriteText(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }

        /// <summary>Terrain log + a 512² preview: hillshade, blue = obstacle mask, red = too steep, yellow = zones.</summary>
        public static void WriteTerrainLog(Proc2048MapBase p)
        {
            Field2048 f; string text;
            try { f = p.Field; text = p.BuildLog; }
            catch (Exception) { f = p.LastField; text = "FAIL\n" + p.LastLog; if (f != null) Preview(p, f, text); throw; }
            Preview(p, f, text);
        }

        static void Preview(Proc2048MapBase p, Field2048 f, string text)
        {
            string dir = p.Size.LogDir;
            WriteText(dir + "/" + p.Id + "-terrain.txt", text);
            const int M = Field2048.Cells;
            var tex = new Texture2D(M, M, TextureFormat.RGB24, false);
            var px = new Color32[M * M];
            float min = f.H.Min(), max = Mathf.Max(min + 1f, f.H.Max());
            for (int z = 0; z < M; z++)
                for (int x = 0; x < M; x++)
                {
                    int i = z * Field2048.N + x;
                    float h = f.H[i], gx = (f.H[i + 1] - h) / Field2048.Step, gz = (f.H[i + Field2048.N] - h) / Field2048.Step;
                    float shade = Mathf.Clamp01(.55f + (-gx * .7f + gz * .7f) * .9f);
                    float t = (h - min) / (max - min);
                    var c = Color.Lerp(new Color(.25f, .45f, .2f), new Color(.85f, .82f, .7f), t) * (.4f + .7f * shade);
                    float wx = -Field2048.Half + (x + .5f) * Field2048.Step, wz = -Field2048.Half + (z + .5f) * Field2048.Step;
                    bool obstacle = Field2048.Sample(f.Forest, wx, wz) > .5f || Field2048.Sample(f.Rock, wx, wz) > .5f;
                    bool steep = !f.SlopeWalkable(x, z, Battlefield1024Generator.SlopeLimit);
                    if (obstacle) c = Color.Lerp(c, new Color(.1f, .25f, .8f), .55f);
                    else if (steep) c = Color.Lerp(c, new Color(.85f, .15f, .1f), .6f);
                    if (Mathf.Abs(Mathf.Abs(wx) - p.Size.ArmyCenter.x) < p.Size.ArmySize.x * .5f && Mathf.Abs(wz) < p.Size.ArmySize.y * .5f &&
                        ((x + z) % 8 == 0)) c = new Color(1f, .9f, .1f);
                    c.a = 1f; px[z * M + x] = c;
                }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(dir + "/" + p.Id + "-terrain.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
