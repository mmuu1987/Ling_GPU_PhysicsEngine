using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// Base for the fully procedural 1024 maps (2026-10-13): procedural props + procedural ground, no third-party
    /// files. Obstacles = forest discs / capsules and rock discs / capsules (+ border wood ring). Both army
    /// deployment rectangles (+6 m) are always kept walkable and the Keep points stay open (MustConnect).
    /// Obstacles between the armies live in the 50 m centre strip ("spine") and in the outer bands.
    /// </summary>
    public abstract class ProcMapBase : MapSpec
    {
        protected enum Ground { Green, Dry, Autumn, Snow }
        protected abstract Ground Theme { get; }
        protected virtual Vector4 HazeColor => new Vector4(.38f, .57f, .62f, 1);
        protected virtual float RidgeBase => 55f;
        protected virtual float FarMountains => 240f;
        protected virtual float BorderStart => 446f;
        /// <summary>方案甲（试验）：林下地面压暗 + 地图外圈远景地面改为林下地色，让树间空隙读作林荫。</summary>
        protected virtual bool ShadeForestFloor => false;
        /// <summary>方案乙（试验）：远景改用林冠团（一模型多树冠），替换稀疏的单棵远景树。</summary>
        protected virtual bool CanopyClumps => false;
        string ClumpKind(Flora f)
        {
            if (f.Clump != null) return f.Clump;
            switch (Theme)
            {
                case Ground.Dry: return "ClumpDry";
                case Ground.Autumn: return "ClumpAutumn";
                case Ground.Snow: return "ClumpPineSnow";
                default: return "ClumpGreen";
            }
        }
        static Color ForestShade(Ground g)
        {
            switch (g)
            {
                case Ground.Dry: return new Color(.6f, .64f, .5f);
                case Ground.Autumn: return new Color(.52f, .66f, .44f);
                case Ground.Snow: return new Color(.74f, .8f, .86f);
                default: return new Color(.62f, .7f, .58f);
            }
        }
        public override Vector2 ControlPoint => Vector2.zero;
        public override Vector3 PreviewCamera => new Vector3(-600f, 380f, -720f);

        protected struct Capsule { public Vector2 a, b; public float r; public Capsule(float ax, float az, float bx, float bz, float r) { a = new Vector2(ax, az); b = new Vector2(bx, bz); this.r = r; } }

        protected readonly List<Vector3> Copses = new List<Vector3>();   // x, z, radius
        protected readonly List<Vector3> Knolls = new List<Vector3>();
        protected readonly List<Vector3> Clearings = new List<Vector3>();
        protected readonly List<Capsule> TreeBands = new List<Capsule>(), RockBands = new List<Capsule>(), Trails = new List<Capsule>();
        protected readonly List<Func<float, float>> RoadsZ = new List<Func<float, float>>();   // z = f(x), painted + carved
        protected readonly List<Func<float, float>> RoadsX = new List<Func<float, float>>();   // x = f(z)
        protected virtual Vector2[] Keep => new[] { new Vector2(0f, 400f), new Vector2(0f, -400f), new Vector2(-400f, 0f), new Vector2(400f, 0f) };
        public override Vector2[] MustConnect => Keep;
        protected virtual float TrailHalfWidth => 10f;

        // ---------------------------------------------------------------- feature generation helpers
        protected bool Free(float x, float z, float r, float gap)
        {
            if (EdgeDistance(x, z) > 432f) return false;
            if (InArmyFootprint(x, z, r + 6f)) return false;
            foreach (var k in Keep) if ((new Vector2(x, z) - k).magnitude < r + 26f) return false;
            if (RoadsZ.Count + RoadsX.Count + Trails.Count > 0 && RoadDistance(x, z) < r + 10f) return false;
            foreach (var c in Copses) if (new Vector2(x - c.x, z - c.y).magnitude < r + c.z + gap) return false;
            foreach (var c in Knolls) if (new Vector2(x - c.x, z - c.y).magnitude < r + c.z + gap) return false;
            return true;
        }

        /// <summary>Jittered grid of discs outside the deployment rectangles.</summary>
        protected void Sprinkle(List<Vector3> into, System.Random rng, float step, float chance, float rMin, float rMax, float gap, Func<float, float, float> chanceAt = null)
        {
            for (float gz = -440f; gz <= 440f; gz += step)
                for (float gx = -440f; gx <= 440f; gx += step)
                {
                    float x = gx + (float)(rng.NextDouble() - .5) * step * .8f, z = gz + (float)(rng.NextDouble() - .5) * step * .8f;
                    float r = rMin + (float)rng.NextDouble() * (rMax - rMin);
                    float p = chanceAt != null ? chanceAt(x, z) : chance;
                    if (rng.NextDouble() > p) continue;
                    if (!Free(x, z, r, gap)) continue;
                    into.Add(new Vector3(x, z, r));
                }
        }

        /// <summary>Clusters along the centre line (x≈0) with gaps; radius ≤ 13 so they stay inside the 50 m strip.</summary>
        protected void Spine(List<Vector3> into, System.Random rng, float[] zs, float rMin, float rMax)
        {
            foreach (var z in zs)
            {
                float r = Mathf.Min(13f, rMin + (float)rng.NextDouble() * (rMax - rMin));
                into.Add(new Vector3((float)(rng.NextDouble() - .5) * 6f, z + (float)(rng.NextDouble() - .5) * 10f, r));
            }
        }

        // ---------------------------------------------------------------- fields
        protected static float CapsuleDistance(float x, float z, Capsule c)
        {
            Vector2 p = new Vector2(x, z), ab = c.b - c.a;
            float t = Mathf.Clamp01(Vector2.Dot(p - c.a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return (p - (c.a + ab * t)).magnitude;
        }

        protected float Discs(float x, float z, List<Vector3> discs, float soft, float wobbleOffset)
        {
            float f = 0f, wob = 1f + .32f * (Noise(x, z, .05f, wobbleOffset) - .5f);
            foreach (var c in discs)
            {
                float dx = x - c.x, dz = z - c.y;
                if (Mathf.Abs(dx) > c.z * 1.5f + soft || Mathf.Abs(dz) > c.z * 1.5f + soft) continue;
                f = Mathf.Max(f, .5f + (c.z * wob - Mathf.Sqrt(dx * dx + dz * dz)) / soft);
            }
            return f;
        }

        protected float Bands(float x, float z, List<Capsule> bands, float soft, float wobbleOffset)
        {
            float f = 0f, wob = 1f + .3f * (Noise(x, z, .03f, wobbleOffset) - .5f);
            foreach (var c in bands) f = Mathf.Max(f, .5f + (c.r * wob - CapsuleDistance(x, z, c)) / soft);
            return f;
        }

        protected float RoadDistance(float x, float z)
        {
            float d = 1e6f;
            foreach (var r in RoadsZ) d = Mathf.Min(d, DistanceToCurveZ(x, z, r));
            foreach (var r in RoadsX) d = Mathf.Min(d, DistanceToCurveX(x, z, r));
            foreach (var t in Trails) d = Mathf.Min(d, CapsuleDistance(x, z, t));
            return d;
        }

        /// <summary>Extra forest everywhere (dense woods maps); default none.</summary>
        protected virtual float ForestBase(float x, float z) => 0f;

        float ForestRaw(float x, float z)
        {
            float f = Mathf.Max(Discs(x, z, Copses, 14f, 6f), Bands(x, z, TreeBands, 12f, 4f));
            f = Mathf.Max(f, ForestBase(x, z));
            f = Mathf.Max(f, .5f + (EdgeDistance(x, z) - BorderStart + 40f * (Noise(x, z, .012f, 11f) - .5f)) / 16f);
            return f;
        }

        float RockRaw(float x, float z) => Mathf.Max(Discs(x, z, Knolls, 6f, 9f), Bands(x, z, RockBands, 7f, 14f));

        float Open(float x, float z)
        {
            // 1 where obstacles must not be: deployment rectangles, keep points, roads / trails, clearings
            if (InArmyFootprint(x, z, 6f)) return 1f;
            float o = 0f;
            foreach (var k in Keep) o = Mathf.Max(o, 1f - Smooth(18f, 26f, (new Vector2(x, z) - k).magnitude));
            if (RoadsZ.Count + RoadsX.Count + Trails.Count > 0) o = Mathf.Max(o, 1f - Smooth(TrailHalfWidth, TrailHalfWidth + 7f, RoadDistance(x, z)));
            foreach (var c in Clearings) o = Mathf.Max(o, 1f - Smooth(c.z, c.z + 10f, new Vector2(x - c.x, z - c.y).magnitude));
            return o;
        }

        protected float Forest(float x, float z)
        {
            float f = ForestRaw(x, z), o = Open(x, z);
            if (EdgeDistance(x, z) < 470f) f = Mathf.Min(f, Mathf.Lerp(f, .3f, o));
            return Mathf.Clamp01(f);
        }

        protected float Rock(float x, float z)
        {
            float f = RockRaw(x, z), o = InArmyFootprint(x, z, 6f) ? 1f : 0f;
            foreach (var k in Keep) o = Mathf.Max(o, 1f - Smooth(18f, 26f, (new Vector2(x, z) - k).magnitude));
            return Mathf.Clamp01(Mathf.Min(f, Mathf.Lerp(f, .3f, o)));
        }

        public override bool Blocked(float x, float z) => Forest(x, z) > .5f || Rock(x, z) > .5f;

        // ---------------------------------------------------------------- height / splat
        protected virtual float Relief(float x, float z) => 0f;

        public override float Height(float x, float z)
        {
            float h = 6f * Fbm(x, z, .003f, 3) + 2f * Noise(x, z, .012f, 5f) + Relief(x, z);
            h += 5f * Smooth(.45f, .95f, RockRaw(x, z));
            h = Mathf.Lerp(h, h * .5f + 1.5f, Core(x, z));
            h += 26f * Smooth(440f, 512f, EdgeDistance(x, z)) * (.6f + .4f * Noise(x, z, .01f, 7f));
            return h;
        }

        public override Color32 Splat(float x, float z, float height, float slope)
        {
            float forest = Smooth(.34f, .62f, Forest(x, z)), rock = Smooth(.3f, .6f, Rock(x, z));
            float road = (1f - Smooth(4.5f, 9f, RoadDistance(x, z))) * (1f - Smooth(460f, 476f, EdgeDistance(x, z)));
            float worn = Smooth(.72f, .86f, Noise(x, z, .021f, 13f)) * .3f;
            float dirt = Mathf.Max(road * .9f, worn) * (1f - rock) * (1f - forest * .85f);
            float scree = Smooth(.8f, .93f, Noise(x, z, .05f, 29f)) * Smooth(14f, 22f, height) * .5f;
            float meadow = Smooth(.42f, .78f, Noise(x, z, .007f, 23f)) * .75f * (1f - forest);
            return new Color32(B(dirt), B(Mathf.Max(rock, scree)), B(forest), B(meadow));
        }

        // ---------------------------------------------------------------- look
        GroundLook look;
        public override GroundLook Look
        {
            get
            {
                if (look != null) return look;
                string t = Theme == Ground.Green ? "" : Theme.ToString();
                look = new GroundLook
                {
                    GrassA = ProcPropLibrary.GroundPath("GrassA", t), GrassB = ProcPropLibrary.GroundPath("GrassB", t),
                    Dirt = ProcPropLibrary.GroundPath("Dirt", t), Rock = ProcPropLibrary.GroundPath("Rock", t), Forest = ProcPropLibrary.GroundPath("Forest", t),
                    Tiling = new Vector4(.065f, .055f, .1f, .05f), ForestTiling = .065f, Stylize = 0f,
                    SlopeRock = new Vector4(.14f, .34f, 0, 0), OutsideMix = new Vector4(0, .05f, .55f, .4f),
                    Haze = HazeColor, RidgeBase = RidgeBase, FarMountains = FarMountains,
                };
                if (ShadeForestFloor)
                {
                    look.ForestTint = ForestShade(Theme);
                    look.OutsideMix = new Vector4(0, .05f, .92f, .3f);
                }
                return look;
            }
        }

        // ---------------------------------------------------------------- decoration
        protected sealed class Flora
        {
            public (string cat, float w)[] Trees, Border, Rocks, Under, Meadow;
            public string Distant = "Pine", Mountain = "Mountain", Pebbles = "Pebbles", Clump = null;
            public float TreeMin = 10f, TreeMax = 17f, TreeSpacing = 9.5f, RockSpacing = 6.5f;
            public float RockMin = 3f, RockMax = 9f, TallMin = 8f, TallMax = 16f;
            public float UnderChance = .45f, MeadowDensity = .6f, DistantDensity = .6f;
            public int MaxTrees = 3500, MaxRocks = 2500, Mountains = 10;
        }
        protected abstract Flora Plants { get; }

        static string[] P(string cat) => ProcPropLibrary.Pool(cat);
        static string W(Decorator d, (string cat, float w)[] set)
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

        public override void Decorate(Decorator d)
        {
            var f = Plants;
            Woods(d, "林地", f.TreeSpacing, (x, z) => EdgeDistance(x, z) < 436f && Forest(x, z) > .5f && Rock(x, z) <= .5f,
                (x, z) => P(W(d, f.Trees)), f.TreeMin, f.TreeMax, f.MaxTrees);
            Woods(d, "边界林带", 13f, (x, z) => EdgeDistance(x, z) >= 436f && EdgeDistance(x, z) < 506f, (x, z) => P(W(d, f.Border)), f.TreeMin + 2f, f.TreeMax + 3f, 1800);
            d.Scatter("岩丘", -470f, -470f, 470f, 470f, f.RockSpacing, f.MaxRocks,
                (x, z) => Rock(x, z) > .5f && d.Blocked(x, z) && d.Chance(.85f),
                (x, z) =>
                {
                    string c = W(d, f.Rocks); bool tall = c == "RockTall";
                    return new Decorator.Choice(d.Pick(P(c), false), tall ? d.Rand(f.TallMin, f.TallMax) : d.Rand(f.RockMin, f.RockMax), false, d.Chance(.5f), .6f);
                });
            d.Scatter("林缘灌木", -470f, -470f, 470f, 470f, 6.5f, 1400,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 6f) && Rock(x, z) <= .5f && d.Chance(f.UnderChance),
                (x, z) => { string c = W(d, f.Under); return new Decorator.Choice(d.Pick(P(c), false), SmallHeight(d, c), false, false, .1f); });
            d.Scatter("草甸花草", -460f, -460f, 460f, 460f, 15f, 900,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > 260f && d.Chance(f.MeadowDensity * Smooth(.45f, .7f, Noise(x, z, .008f, 41f))),
                (x, z) => { string c = W(d, f.Meadow); return new Decorator.Choice(d.Pick(P(c), false), SmallHeight(d, c), false, false, .05f); });
            d.Scatter("碎石", -460f, -460f, 460f, 460f, 14f, 400,
                (x, z) => !d.Blocked(x, z) && RockRaw(x, z) > .15f && d.Chance(.55f),
                (x, z) => new Decorator.Choice(d.Pick(P(f.Pebbles), false), d.Rand(.3f, .6f), false, false, .05f));
            if (CanopyClumps)
            {
                // Ridge back (482-520, steep) gets smaller clumps; the apron beyond gets big ones, thinning to 1150.
                var pool = P(ClumpKind(f));
                d.Scatter("远景林冠", -1150f, -1150f, 1150f, 1150f, 26f, 2400,
                    (x, z) =>
                    {
                        float e = EdgeDistance(x, z);
                        if (e < 482f || e > 1150f) return false;
                        return d.Chance(Mathf.Lerp(.95f, .35f, Smooth(560f, 1150f, e)) * (.7f + .3f * Noise(x, z, .01f, 19f)));
                    },
                    (x, z) =>
                    {
                        float h = EdgeDistance(x, z) < 520f ? d.Rand(14f, 18f) : d.Rand(18f, 28f);
                        return new Decorator.Choice(d.Pick(pool, true), h, true, false, h * .2f);
                    });
            }
            else DistantTrees(d, P(f.Distant), f.DistantDensity, 1000);
            var g = d.Group("远山");
            for (int i = 0; i < f.Mountains; i++)
            {
                float a = i * Mathf.PI * 2f / f.Mountains + d.Rand(-.18f, .18f), r = d.Rand(1700f, 2500f);
                d.Place(d.Pick(P(f.Mountain), false), Mathf.Cos(a) * r, Mathf.Sin(a) * r, d.Rand(160f, 300f), g, false, false, 0f);
            }
        }
    }

    // ==================================================================== 1. 乱石荒原
    public sealed class RockySteppeMap : ProcMapBase
    {
        public override string Id => "rocky-steppe-1024";
        public override string Folder => "ProcRockySteppe";
        public override string DisplayName => "乱石荒原";
        public override string Description => "1024×1024 干旱荒原（程序化）\n遍地岩丘与零星枯树，中线一串石堆把战场切成多条通道。";
        public override string Briefing => "两军之间的中线有一串石堆，只能从缝隙穿过；外围岩丘密布，适合小股迂回。\n岩丘、树丛为禁行区，外圈为地图边界。";
        public override int Seed => 3301;
        protected override Ground Theme => Ground.Dry;
        protected override Vector4 HazeColor => new Vector4(.58f, .56f, .48f, 1);
        protected override float RidgeBase => 45f;

        public RockySteppeMap()
        {
            var rng = new System.Random(Seed);
            RoadsZ.Add(x => 30f * Mathf.Sin(x / 160f) + 8f * Mathf.Sin(x / 45f));
            RockBands.Add(new Capsule(-320f, 352f, 40f, 322f, 15f));
            RockBands.Add(new Capsule(-60f, -332f, 300f, -362f, 15f));
            Spine(Knolls, rng, new[] { -250f, -170f, -90f, 90f, 170f, 250f }, 10f, 13f);   // the road keeps the centre gap
            Sprinkle(Knolls, rng, 62f, .55f, 9f, 20f, 16f);
            Sprinkle(Copses, rng, 110f, .3f, 10f, 17f, 18f);
        }

        protected override float Relief(float x, float z) => 14f * Gauss(x, z, -330f, 330f, 120f, 90f) + 12f * Gauss(x, z, 330f, -330f, 120f, 90f);

        protected override Flora Plants => new Flora
        {
            Trees = new[] { ("Dead", .3f), ("Pine", .35f), ("Autumn", .15f), ("Cypress", .2f) },
            Border = new[] { ("Pine", .6f), ("Dead", .2f), ("Cypress", .2f) },
            Rocks = new[] { ("Rock", .55f), ("RockPile", .3f), ("RockTall", .15f) },
            Under = new[] { ("BushDry", .6f), ("Stump", .2f), ("Logs", .2f) },
            Meadow = new[] { ("GrassDry", .6f), ("Pebbles", .25f), ("Flower", .15f) },
            Distant = "Cypress", TreeMin = 8f, TreeMax = 14f, RockSpacing = 6f, DistantDensity = .45f,
        };
    }

    // ==================================================================== 2. 秋枫林地
    public sealed class AutumnWoodsMap : ProcMapBase
    {
        public override string Id => "autumn-woods-1024";
        public override string Folder => "ProcAutumnWoods";
        public override string DisplayName => "秋枫林地";
        public override string Description => "1024×1024 秋季林地（程序化）\n成片的枫林与白桦林星罗棋布，林间小路纵横。";
        public override string Briefing => "外围是一片片树林，林间空地彼此相通；中线有几片小树林可作掩护。\n树林为禁行区，外圈为地图边界。";
        public override int Seed => 3402;
        protected override Ground Theme => Ground.Autumn;
        protected override Vector4 HazeColor => new Vector4(.5f, .54f, .56f, 1);
        protected override bool CanopyClumps => true;

        public AutumnWoodsMap()
        {
            var rng = new System.Random(Seed);
            RoadsX.Add(z => 22f * Mathf.Sin(z / 120f + .7f));
            RoadsZ.Add(x => -20f + 25f * Mathf.Sin(x / 140f));
            Spine(Copses, rng, new[] { -230f, -120f, 0f, 120f, 230f }, 11f, 13f);
            Sprinkle(Copses, rng, 70f, .75f, 13f, 28f, 14f);
            Sprinkle(Knolls, rng, 150f, .35f, 8f, 12f, 16f);
        }

        protected override float Relief(float x, float z) => 10f * Gauss(x, z, -300f, -320f, 140f, 100f) + 9f * Gauss(x, z, 320f, 300f, 140f, 100f);

        protected override Flora Plants => new Flora
        {
            Trees = new[] { ("Autumn", .45f), ("Broadleaf", .25f), ("Birch", .22f), ("Pine", .08f) },
            Border = new[] { ("Autumn", .35f), ("Broadleaf", .3f), ("Pine", .35f) },
            Rocks = new[] { ("Rock", .5f), ("RockPile", .5f) },
            Under = new[] { ("Bush", .3f), ("FallenLog", .2f), ("Mushroom", .2f), ("Fern", .2f), ("Stump", .1f) },
            Meadow = new[] { ("FlowerPatch", .4f), ("Grass", .4f), ("Flower", .2f) },
            Distant = "Broadleaf", MaxTrees = 4500,
        };
    }

    // ==================================================================== 3. 雪松高地
    public sealed class SnowHighlandsMap : ProcMapBase
    {
        public override string Id => "snow-highlands-1024";
        public override string Folder => "ProcSnowHighlands";
        public override string DisplayName => "雪松高地";
        public override string Description => "1024×1024 雪原高地（程序化）\n四角高丘覆满雪松，丘顶岩石裸露，中央雪原开阔。";
        public override string Briefing => "四角是长满雪松的高丘，可迂回占据高地；中线几处岩丘分隔两军。\n松林、岩丘为禁行区，外圈为地图边界。";
        public override int Seed => 3503;
        protected override Ground Theme => Ground.Snow;
        protected override Vector4 HazeColor => new Vector4(.66f, .73f, .78f, 1);
        protected override float FarMountains => 300f;

        static readonly Vector3[] Hills = { new Vector3(-300f, 300f, 30f), new Vector3(300f, -300f, 26f), new Vector3(330f, 330f, 14f), new Vector3(-330f, -330f, 12f) };
        float Hillness(float x, float z) { float h = 0; foreach (var k in Hills) h = Mathf.Max(h, Gauss(x, z, k.x, k.y, 130f, 110f)); return h; }

        public SnowHighlandsMap()
        {
            var rng = new System.Random(Seed);
            RoadsZ.Add(x => 20f * Mathf.Sin(x / 130f));
            Knolls.Add(new Vector3(-300f, 310f, 20f)); Knolls.Add(new Vector3(305f, -300f, 18f));
            Spine(Knolls, rng, new[] { -200f, -70f, 70f, 200f }, 10f, 12f);
            Sprinkle(Copses, rng, 64f, 0f, 12f, 24f, 12f, (x, z) => .2f + .7f * Hillness(x, z));
            Sprinkle(Knolls, rng, 120f, .35f, 8f, 13f, 14f);
        }

        protected override float Relief(float x, float z)
        {
            float h = 0; foreach (var k in Hills) h += k.z * Gauss(x, z, k.x, k.y, 130f, 110f);
            return h;
        }

        protected override Flora Plants => new Flora
        {
            Trees = new[] { ("PineSnow", .88f), ("Dead", .12f) },
            Border = new[] { ("PineSnow", 1f) },
            Rocks = new[] { ("RockSnow", .85f), ("RockTall", .15f) },
            Under = new[] { ("BushSnow", .5f), ("Stump", .25f), ("Logs", .25f) },
            Meadow = new[] { ("GrassDry", .5f), ("Pebbles", .5f) },
            Distant = "PineSnow", Mountain = "MountainSnow", MeadowDensity = .4f, TreeMin = 11f, TreeMax = 18f,
        };
    }

    // ==================================================================== 4. 石林峡谷
    public sealed class StoneForestMap : ProcMapBase
    {
        public override string Id => "stone-forest-1024";
        public override string Folder => "ProcStoneForest";
        public override string DisplayName => "石林峡谷";
        public override string Description => "1024×1024 石林峡谷（程序化）\n南北两侧石柱林立如迷宫，中线一列石柱分隔两军。";
        public override string Briefing => "南北石林里道路曲折，适合埋伏与分割；中线石柱之间留有通道。\n石柱、树丛为禁行区，外圈为地图边界。";
        public override int Seed => 3604;
        protected override Ground Theme => Ground.Green;

        public StoneForestMap()
        {
            var rng = new System.Random(Seed);
            RoadsZ.Add(x => 12f * Mathf.Sin(x / 90f));
            RockBands.Add(new Capsule(-450f, 440f, 450f, 426f, 14f));
            RockBands.Add(new Capsule(-450f, -428f, 450f, -442f, 14f));
            Spine(Knolls, rng, new[] { -250f, -180f, -110f, -40f, 40f, 110f, 180f, 250f }, 8f, 11f);
            Sprinkle(Knolls, rng, 46f, .62f, 7f, 14f, 12f);
            Sprinkle(Copses, rng, 90f, .35f, 10f, 16f, 12f);
        }

        protected override float Relief(float x, float z) => 8f * Gauss(x, z, 0f, 380f, 260f, 70f) + 8f * Gauss(x, z, 0f, -380f, 260f, 70f);

        protected override Flora Plants => new Flora
        {
            Trees = new[] { ("Pine", .45f), ("Cypress", .35f), ("Broadleaf", .2f) },
            Border = new[] { ("Pine", .5f), ("Cypress", .3f), ("Broadleaf", .2f) },
            Rocks = new[] { ("RockTall", .65f), ("Rock", .25f), ("RockPile", .1f) },
            Under = new[] { ("Fern", .4f), ("Bush", .4f), ("Stump", .2f) },
            Meadow = new[] { ("FlowerPatch", .3f), ("Grass", .5f), ("Pebbles", .2f) },
            Distant = "Cypress", RockSpacing = 7.5f, TallMin = 10f, TallMax = 22f, RockMin = 3f, RockMax = 8f,
        };
    }

    // ==================================================================== 5. 密林小径
    public sealed class DeepWoodsMap : ProcMapBase
    {
        public override string Id => "deep-woods-1024";
        public override string Folder => "ProcDeepWoods";
        public override string DisplayName => "密林小径";
        public override string Description => "1024×1024 密林（程序化）\n部署区以外全是密林，只有几条林间小路和零星空地相连。";
        public override string Briefing => "两军部署区之外是密林，靠环形与放射状小路相通，林中空地可藏兵。\n林地为禁行区，外圈为地图边界。";
        public override int Seed => 3705;
        protected override Ground Theme => Ground.Green;
        protected override float TrailHalfWidth => 11f;
        protected override Vector2[] Keep => new[] { new Vector2(0f, 390f), new Vector2(0f, -390f), new Vector2(-390f, 0f), new Vector2(390f, 0f) };

        public DeepWoodsMap()
        {
            var rng = new System.Random(Seed);
            // radial trails from the battlefield to the ring
            RoadsX.Add(z => 16f * Mathf.Sin(z / 70f));
            RoadsZ.Add(x => 16f * Mathf.Sin(x / 80f));
            // ring trail (square-ish loop through the four keep points)
            Trails.Add(new Capsule(-390f, 390f, 390f, 390f, 0f)); Trails.Add(new Capsule(-390f, -390f, 390f, -390f, 0f));
            Trails.Add(new Capsule(-390f, -390f, -390f, 390f, 0f)); Trails.Add(new Capsule(390f, -390f, 390f, 390f, 0f));
            // diagonals from the deployment corners
            Trails.Add(new Capsule(-270f, 270f, -390f, 390f, 0f)); Trails.Add(new Capsule(270f, 270f, 390f, 390f, 0f));
            Trails.Add(new Capsule(-270f, -270f, -390f, -390f, 0f)); Trails.Add(new Capsule(270f, -270f, 390f, -390f, 0f));
            // clearings inside the woods, some with a rock knoll
            for (int i = 0; i < 14; i++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * 400f, z = (float)(rng.NextDouble() * 2 - 1) * 400f;
                if (InArmyFootprint(x, z, 20f) || EdgeDistance(x, z) > 420f) { i--; continue; }
                float r = 18f + (float)rng.NextDouble() * 16f;
                Clearings.Add(new Vector3(x, z, r));
                if (rng.NextDouble() < .5) Knolls.Add(new Vector3(x + (float)(rng.NextDouble() - .5) * r * .6f, z + (float)(rng.NextDouble() - .5) * r * .6f, 7f + (float)rng.NextDouble() * 4f));
            }
            Spine(Copses, rng, new[] { -200f, -60f, 80f, 220f }, 11f, 13f);
        }

        protected override float ForestBase(float x, float z) => .5f + (EdgeDistance(x, z) - 296f) / 10f + 3f * (Noise(x, z, .02f, 33f) - .5f) * .6f;

        protected override Flora Plants => new Flora
        {
            Trees = new[] { ("Broadleaf", .4f), ("Pine", .35f), ("Birch", .15f), ("Autumn", .1f) },
            Border = new[] { ("Pine", .5f), ("Broadleaf", .5f) },
            Rocks = new[] { ("Rock", .5f), ("RockPile", .4f), ("RockTall", .1f) },
            Under = new[] { ("Fern", .35f), ("Bush", .3f), ("Mushroom", .15f), ("FallenLog", .1f), ("Stump", .1f) },
            Meadow = new[] { ("FlowerPatch", .4f), ("Grass", .4f), ("Flower", .2f) },
            Distant = "Pine", MaxTrees = 6000, TreeSpacing = 10f,
        };
    }

    // ==================================================================== menus
    public static class ProcMapMenus
    {
        const string Menu = Battlefield1024Generator.MenuRoot + "程序化地图/";
        public static MapSpec[] All => new MapSpec[] { new RockySteppeMap(), new AutumnWoodsMap(), new SnowHighlandsMap(), new StoneForestMap(), new DeepWoodsMap() };

        [MenuItem(Menu + "生成 乱石荒原", false, 20)] static void M1() => Run(new RockySteppeMap());
        [MenuItem(Menu + "生成 秋枫林地", false, 21)] static void M2() => Run(new AutumnWoodsMap());
        [MenuItem(Menu + "生成 雪松高地", false, 22)] static void M3() => Run(new SnowHighlandsMap());
        [MenuItem(Menu + "生成 石林峡谷", false, 23)] static void M4() => Run(new StoneForestMap());
        [MenuItem(Menu + "生成 密林小径", false, 24)] static void M5() => Run(new DeepWoodsMap());
        [MenuItem(Menu + "生成全部（5 张，较慢）", false, 40)] static void MAll() => Run(All);
        [MenuItem(Menu + "只重建道具库与地面贴图", false, 60)] static void MLib() { if (!EditorApplication.isPlayingOrWillChangePlaymode) ProcPropLibrary.Build(); }

        /// <summary>Generates without dialogs (remote-safe). Refuses when a loaded scene has unsaved changes.</summary>
        public static void Run(params MapSpec[] maps)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[ProcMaps] 请先退出 Play 模式。"); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) { Debug.LogError("[ProcMaps] 场景 " + SceneManager.GetSceneAt(i).name + " 有未保存的修改，已取消生成。请先保存。"); return; }
            string restore = SceneManager.GetActiveScene().path;
            try
            {
                ProcPropLibrary.Build();
                foreach (var m in maps)
                {
                    try
                    {
                        var report = Battlefield1024Generator.Generate(m);
                        Debug.Log("[ProcMaps] 生成完成：" + report.Summary());
                    }
                    catch (Exception e) { Debug.LogError("[ProcMaps] 生成失败：" + m.DisplayName + " — " + e.Message); Debug.LogException(e); }
                    EditorUtility.UnloadUnusedAssetsImmediate();
                    GC.Collect();
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Battlefield1024Generator.RepairCatalogAndBuildSettings();
                if (!string.IsNullOrEmpty(restore) && File.Exists(restore) && SceneManager.GetActiveScene().path != restore)
                    EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
            }
        }
    }
}
