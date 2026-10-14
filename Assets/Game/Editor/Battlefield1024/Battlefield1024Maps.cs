using System.Collections.Generic;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 翠绿平原: open rolling grassland for full-size battles. Two broad low hills (north-west,
    /// south-east) give high ground, scattered copses and rock knolls sit outside the open core,
    /// a dirt road crosses the field, an irregular wood rings the border.
    /// </summary>
    public sealed class PlainsMap : MapSpec
    {
        public override string Id => "plains-1024";
        public override string Folder => "Plains";
        public override string DisplayName => "翠绿平原";
        public override string Description => "1024×1024 开阔草原\n缓坡与零星树丛，适合大规模正面会战。";
        public override string Briefing => "中央 600×600 米开阔平地，两翼各有一座缓丘可占高地。\n树丛与岩丘为禁行区，外圈林带为地图边界。";
        public override int Seed => 1101;
        public override Vector2 ControlPoint => new Vector2(0f, 0f);

        readonly List<Vector3> copses = new List<Vector3>();   // x, z, radius
        readonly Vector3[] knolls =                               // rock outcrops: x, z, radius
        {
            new Vector3(-150f, 382f, 13f), new Vector3(196f, -392f, 12f), new Vector3(-408f, -158f, 10f), new Vector3(414f, 172f, 9f),
            new Vector3(-360f, 300f, 7f), new Vector3(352f, -318f, 7f),
        };
        GroundLook look;

        public PlainsMap()
        {
            var rng = new System.Random(Seed);
            for (float gz = -450f; gz <= 450f; gz += 90f)
                for (float gx = -450f; gx <= 450f; gx += 90f)
                {
                    float x = gx + (float)(rng.NextDouble() - .5) * 70f, z = gz + (float)(rng.NextDouble() - .5) * 70f;
                    float r = 16f + (float)rng.NextDouble() * 24f;
                    if (rng.NextDouble() < .2) continue;
                    if (Mathf.Abs(x) < OpenLimit + r && Mathf.Abs(z) < OpenLimit + r) continue;
                    if (EdgeDistance(x, z) > 430f) continue;
                    if (RoadDistance(x, z) < r + 10f) continue;
                    if (Avoid(x, z, r + 22f)) continue;
                    copses.Add(new Vector3(x, z, r));
                }
        }

        const float OpenLimit = Battlefield1024Generator.OpenCore + 20f;
        static readonly Vector2[] Keep = { new Vector2(-140f, 330f), new Vector2(192f, -340f), new Vector2(0f, -400f), new Vector2(0f, 400f), new Vector2(-400f, 0f), new Vector2(400f, 0f) };
        bool Avoid(float x, float z, float r)
        {
            foreach (var k in Keep) if ((new Vector2(x, z) - k).magnitude < r) return true;
            foreach (var k in knolls) if ((new Vector2(x - k.x, z - k.y)).magnitude < r + k.z) return true;
            return false;
        }

        public override GroundLook Look => look ??= new GroundLook
        {
            GrassA = TF + "Terrain/T_fwOF_GrassTerrain_01_BC.png",
            GrassB = "Assets/ThirdParty/Polytope Studio/Lowpoly_Environments/Sources/Textures/PT_Ground_Grass_Green_01.png",
            Dirt = TF + "Terrain/T_fwOF_Soil_01_BC.png",
            Rock = TF + "Terrain/T_fwOF_RockSurf_02_BC.png",
            Forest = TF + "Terrain/T_fwOF_Moss_02_BC.png",
            Tiling = new Vector4(.07f, .05f, .11f, .045f), ForestTiling = .06f,
            GrassATint = new Color(.93f, 1f, .86f), GrassBTint = new Color(1f, 1f, .9f), DirtTint = new Color(1f, .95f, .84f),
            ForestTint = new Color(.78f, .86f, .74f), Stylize = .5f,
            PaletteDark = new Vector4(.15f, .22f, .075f, 1), PaletteLight = new Vector4(.33f, .39f, .16f, 1),
            OutsideMix = new Vector4(0, 0, .35f, .7f), RidgeBase = 45f, FarMountains = 200f,
        };

        static float Road(float x) => 46f * Mathf.Sin(x / 190f + .4f) + 14f * Mathf.Sin(x / 61f);
        static float Track(float z) => -40f + 30f * Mathf.Sin(z / 150f);
        static float RoadDistance(float x, float z) => Mathf.Min(DistanceToCurveZ(x, z, Road), DistanceToCurveX(x, z, Track) + 2f);

        public override float Height(float x, float z)
        {
            float h = 5f * Fbm(x, z, .0028f, 3) + 2.2f * Noise(x, z, .011f, 5f);
            h += 17f * Gauss(x, z, -140f, 372f, 170f, 85f) + 13f * Gauss(x, z, 192f, -386f, 150f, 80f);
            h += 9f * Gauss(x, z, -405f, -152f, 70f, 95f) + 8f * Gauss(x, z, 412f, 170f, 75f, 90f);
            h = Mathf.Lerp(h, h * .45f + 1.5f, Core(x, z));
            float e = EdgeDistance(x, z);
            h += 26f * Smooth(440f, 512f, e) * (.6f + .4f * Noise(x, z, .01f, 7f));
            return h;
        }

        float Forest(float x, float z)
        {
            float f = 0f, wobble = 1f + .36f * (Noise(x, z, .05f, 3f) - .5f);
            foreach (var c in copses)
            {
                float d = new Vector2(x - c.x, z - c.y).magnitude * wobble;
                f = Mathf.Max(f, Mathf.Clamp01(.5f + (c.z - d) / 16f));
            }
            float ring = EdgeDistance(x, z) - 446f + 40f * (Noise(x, z, .012f, 11f) - .5f);
            return Mathf.Max(f, Mathf.Clamp01(.5f + ring / 16f));
        }

        float Rock(float x, float z)
        {
            float f = 0f;
            foreach (var k in knolls) f = Mathf.Max(f, Mathf.Clamp01(.5f + (k.z - new Vector2(x - k.x, z - k.y).magnitude) / 6f));
            return f;
        }

        public override bool Blocked(float x, float z) => Forest(x, z) > .5f || Rock(x, z) > .5f;

        public override Color32 Splat(float x, float z, float height, float slope)
        {
            float forest = Smooth(.36f, .62f, Forest(x, z)), rock = Smooth(.3f, .6f, Rock(x, z));
            float w = 6.5f + 1.5f * Noise(x, z, .02f, 2f);
            float road = (1f - Smooth(w - 2.5f, w + 3f, DistanceToCurveZ(x, z, Road))) * (1f - Smooth(440f, 470f, EdgeDistance(x, z)));
            float track = (1f - Smooth(2.5f, 6f, DistanceToCurveX(x, z, Track))) * .75f * (1f - Smooth(400f, 440f, EdgeDistance(x, z)));
            float worn = Smooth(.7f, .84f, Noise(x, z, .021f, 13f)) * .35f;
            float dirt = Mathf.Max(Mathf.Max(road, track), worn) * (1f - forest) * (1f - rock);
            float hillRock = Smooth(.78f, .92f, Noise(x, z, .04f, 17f)) * Smooth(11f, 16f, height) * .55f;
            float meadow = Smooth(.38f, .74f, Noise(x, z, .006f, 19f)) * .8f + Smooth(8f, 15f, height) * .25f;
            return new Color32(B(dirt), B(Mathf.Max(rock, hillRock)), B(forest), B(meadow));
        }

        public override Vector2[] MustConnect => Keep;

        public override void Decorate(Decorator d)
        {
            // Copses: broadleaf with a few birches; the border wood mixes in conifers.
            Woods(d, "树丛", 9f, (x, z) => EdgeDistance(x, z) < 436f && Rock(x, z) <= .5f, (x, z) => d.Chance(.2f) ? Birch : Broadleaf, 10f, 16f, 2600);
            Woods(d, "边界林带", 13f, (x, z) => EdgeDistance(x, z) >= 436f && EdgeDistance(x, z) < 506f, (x, z) => d.Chance(.35f) ? Conifer : Broadleaf, 12f, 19f, 1800);
            // Rock knolls.
            var rocks = d.Group("岩丘");
            foreach (var k in knolls)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(k.z * k.z / 18f), 3, 12);
                for (int i = 0; i < n; i++)
                {
                    float a = d.Rand(0, Mathf.PI * 2), r = d.Rand(0, k.z * .8f);
                    float px = k.x + Mathf.Cos(a) * r, pz = k.y + Mathf.Sin(a) * r;
                    if (!d.Blocked(px, pz)) continue;
                    d.Place(d.Pick(i % 3 == 0 ? RockPiles : Rocks, false), px, pz, d.Rand(3f, 8f), rocks, false, true, .6f);
                }
            }
            // Shrubs and deadwood on blocked cells along wood edges.
            d.Scatter("林缘灌木", -470f, -470f, 470f, 470f, 7f, 900,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 6f) && d.Chance(.45f),
                (x, z) => d.Chance(.15f) ? new Decorator.Choice(d.Pick(Deadwood, false), d.Rand(1.2f, 2.2f), false, false)
                                         : new Decorator.Choice(d.Pick(Shrubs, false), d.Rand(1.6f, 3.2f), false, false));
            // Meadow flowers and grass tufts outside the open core (visual only, units walk through).
            d.Scatter("草甸花草", -460f, -460f, 460f, 460f, 18f, 900,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > OpenLimit - 40f && d.Chance(Smooth(.45f, .7f, Noise(x, z, .006f, 19f))),
                (x, z) => new Decorator.Choice(d.Pick(Meadow, false), d.Rand(.7f, 1.4f), false, false, .05f));
            DistantTrees(d, Broadleaf, .55f, 900);
            DistantMountains(d, 9);
        }
    }

    /// <summary>
    /// 森林丘陵: a valley between two forested ridges. Each ridge belt is cut by two passes that
    /// lead to a narrow back valley, so flanking means squeezing through forest chokepoints.
    /// Side woods and small copses frame the open core; rock outcrops crown the hills.
    /// </summary>
    public sealed class ForestHillsMap : MapSpec
    {
        public override string Id => "forest-hills-1024";
        public override string Folder => "ForestHills";
        public override string DisplayName => "森林丘陵";
        public override string Description => "1024×1024 林间谷地\n两道林木山脊夹着中央谷地，山脊上的隘口通往背后的小谷。";
        public override string Briefing => "中央谷地开阔，南北两道林带只有四个隘口可穿越，适合迂回与伏击。\n林地、岩丘为禁行区，外圈为地图边界。";
        public override int Seed => 2207;
        public override Vector2 ControlPoint => new Vector2(0f, 0f);
        public override Vector3 PreviewCamera => new Vector3(-600f, 380f, -720f);

        static readonly float[] NorthPasses = { -230f, 210f }, SouthPasses = { -120f, 300f };
        const float PassWidth = 56f;
        readonly Vector3[] copses = { new Vector3(-336f, 210f, 28f), new Vector3(340f, 196f, 26f), new Vector3(-330f, -214f, 26f), new Vector3(336f, -200f, 28f), new Vector3(0f, 336f, 30f) };
        readonly Vector3[] outcrops = { new Vector3(336f, 40f, 12f), new Vector3(-342f, -58f, 12f), new Vector3(-438f, 58f, 14f), new Vector3(440f, -84f, 14f) };
        GroundLook look;

        public override GroundLook Look => look ??= new GroundLook
        {
            GrassA = SC.Replace("Prefabs/Mobile/", "Textures/Ground/") + "forestpack_moss_light_tile_diffuse.png",
            GrassB = TF + "Terrain/T_fwOF_GrassTerrain_01_BC.png",
            Dirt = SC.Replace("Prefabs/Mobile/", "Textures/Ground/") + "forestpack_road_tile_diffuse.png",
            Rock = SC.Replace("Prefabs/Mobile/", "Textures/Ground/") + "forestpack_texture_rock_tile_diffuse.png",
            Forest = TF + "Terrain/T_fwOF_Moss_02_BC.png",
            Tiling = new Vector4(.06f, .07f, .1f, .05f), ForestTiling = .065f,
            GrassATint = new Color(.9f, .98f, .84f), GrassBTint = new Color(.92f, .98f, .86f), DirtTint = new Color(.92f, .86f, .76f),
            RockTint = new Color(.82f, .86f, .84f), ForestTint = new Color(.62f, .72f, .6f), Stylize = .42f,
            PaletteDark = new Vector4(.11f, .18f, .07f, 1), PaletteLight = new Vector4(.27f, .33f, .14f, 1),
            SlopeRock = new Vector4(.14f, .34f, 0, 0), OutsideMix = new Vector4(0, 0, .6f, .4f),
            Haze = new Vector4(.36f, .53f, .58f, 1), RidgeBase = 70f, FarMountains = 280f,
        };

        static float NorthRidge(float x) => 380f + 15f * Mathf.Sin(x / 170f + 1.1f);
        static float SouthRidge(float x) => -378f + 15f * Mathf.Sin(x / 150f + .3f);
        static float Road(float x) => 20f * Mathf.Sin(x / 140f) + 10f * Mathf.Sin(x / 47f);

        public override float Height(float x, float z)
        {
            float h = 7f * Fbm(x, z, .0032f, 3);
            h += 34f * Gauss1(z - NorthRidge(x), 72f) * (.65f + .35f * Noise(x, 0f, .006f, 3f));
            h += 30f * Gauss1(z - SouthRidge(x), 68f) * (.6f + .4f * Noise(x, 0f, .006f, 8f));
            h += 24f * Gauss(x, z, -436f, 58f, 80f, 120f) + 22f * Gauss(x, z, 438f, -84f, 85f, 110f);
            h += 7f * Gauss(x, z, -210f, 170f, 55f, 55f) + 6f * Gauss(x, z, 230f, -190f, 50f, 50f);
            h = Mathf.Lerp(h, h * .6f + 1f, Core(x, z));
            h += 30f * Smooth(440f, 512f, EdgeDistance(x, z)) * (.6f + .4f * Noise(x, z, .01f, 7f));
            return h;
        }

        static float Gap(float x, float[] passes)
        {
            float g = 0f;
            foreach (var p in passes) g = Mathf.Max(g, 1f - Smooth(PassWidth * .5f, PassWidth * .5f + 14f, Mathf.Abs(x - p)));
            return g;
        }

        float Forest(float x, float z)
        {
            float wobble = 10f * (Noise(x, z, .03f, 4f) - .5f) * 2f;
            float north = (42f + wobble - Mathf.Abs(z - NorthRidge(x))) / 12f + .5f - Gap(x, NorthPasses) * 6f;
            float south = (42f + wobble - Mathf.Abs(z - SouthRidge(x))) / 12f + .5f - Gap(x, SouthPasses) * 6f;
            float side = (Mathf.Abs(x) - 372f) / 30f + (Noise(x, z, .012f, 21f) - .5f) * 1.6f + .5f;
            side -= (1f - Smooth(18f, 30f, Mathf.Abs(z - Road(x)))) * 2f;   // the valley road stays open to the edge band
            float f = Mathf.Max(Mathf.Max(north, south), side);
            foreach (var c in copses) f = Mathf.Max(f, .5f + (c.z * (1f + .3f * (Noise(x, z, .05f, 6f) - .5f)) - new Vector2(x - c.x, z - c.y).magnitude) / 14f);
            if (InArmyFootprint(x, z, 8f)) f = Mathf.Min(f, .3f);
            return Mathf.Clamp01(f);
        }

        float Rock(float x, float z)
        {
            float f = 0f;
            foreach (var k in outcrops) f = Mathf.Max(f, Mathf.Clamp01(.5f + (k.z - new Vector2(x - k.x, z - k.y).magnitude) / 6f));
            return f;
        }

        public override bool Blocked(float x, float z) => Forest(x, z) > .5f || Rock(x, z) > .5f;

        static float PassRoad(float x, float z, float[] passes, bool north)
        {
            float best = 1e6f;
            foreach (var p in passes)
            {
                bool span = north ? z > 60f : z < -60f;
                if (!span) continue;
                best = Mathf.Min(best, Mathf.Abs(x - (p + 8f * Mathf.Sin(z / 40f))));
            }
            return best;
        }

        public override Color32 Splat(float x, float z, float height, float slope)
        {
            float forest = Smooth(.34f, .62f, Forest(x, z)), rock = Smooth(.3f, .6f, Rock(x, z));
            float e = EdgeDistance(x, z);
            float main = 1f - Smooth(5.5f, 10f, DistanceToCurveZ(x, z, Road));
            float passes = (1f - Smooth(3f, 7f, Mathf.Min(PassRoad(x, z, NorthPasses, true), PassRoad(x, z, SouthPasses, false)))) * .85f;
            float back = (1f - Smooth(3f, 7f, Mathf.Min(Mathf.Abs(z - 452f), Mathf.Abs(z + 452f)))) * .7f;
            float dirt = Mathf.Max(Mathf.Max(main, passes), back) * (1f - Smooth(460f, 476f, e)) * (1f - rock) * (1f - forest * .85f);
            float meadow = Smooth(.42f, .78f, Noise(x, z, .007f, 23f)) * .75f * (1f - forest);
            float scree = Smooth(.8f, .93f, Noise(x, z, .05f, 29f)) * Smooth(18f, 26f, height) * .6f;
            return new Color32(B(dirt), B(Mathf.Max(rock, scree)), B(forest), B(meadow));
        }

        public override Vector2[] MustConnect => new[]
        {
            new Vector2(-230f, 452f), new Vector2(210f, 452f), new Vector2(-120f, -452f), new Vector2(300f, -452f), new Vector2(0f, 270f), new Vector2(0f, -270f), new Vector2(-400f, 0f), new Vector2(400f, 0f),
        };

        public override void Decorate(Decorator d)
        {
            Woods(d, "山脊林带", 10.5f, (x, z) => Mathf.Abs(z) > 300f && EdgeDistance(x, z) < 470f,
                (x, z) => d.Chance(.68f) ? Conifer : d.Chance(.5f) ? Birch : Broadleaf, 13f, 21f, 1900);
            Woods(d, "侧翼林地", 11f, (x, z) => Mathf.Abs(z) <= 300f && Mathf.Abs(x) > 300f && EdgeDistance(x, z) < 470f && Rock(x, z) <= .5f,
                (x, z) => d.Chance(.45f) ? Conifer : Broadleaf, 12f, 19f, 700);
            Woods(d, "林间树丛", 8.5f, (x, z) => Mathf.Abs(z) <= 300f && Mathf.Abs(x) <= 300f && Rock(x, z) <= .5f,
                (x, z) => d.Chance(.3f) ? Birch : Broadleaf, 10f, 16f, 400);
            Woods(d, "边界林带", 14f, (x, z) => EdgeDistance(x, z) >= 470f && EdgeDistance(x, z) < 506f, (x, z) => d.Chance(.75f) ? Conifer : Broadleaf, 14f, 22f, 1000);
            var rocks = d.Group("岩丘");
            foreach (var k in outcrops)
                for (int i = 0; i < 10; i++)
                {
                    float a = d.Rand(0, Mathf.PI * 2), r = d.Rand(0, k.z * .8f);
                    float px = k.x + Mathf.Cos(a) * r, pz = k.y + Mathf.Sin(a) * r;
                    if (d.Blocked(px, pz)) d.Place(d.Pick(i % 3 == 0 ? RockPiles : Rocks, false), px, pz, d.Rand(3.5f, 9f), rocks, false, true, .6f);
                }
            // Mossy boulders inside the ridge woods.
            d.Scatter("林中巨石", -470f, -470f, 470f, 470f, 34f, 220,
                (x, z) => Mathf.Abs(z) > 300f && d.DeepBlocked(x, z, 6f) && d.Chance(.35f),
                (x, z) => new Decorator.Choice(d.Pick(d.Chance(.5f) ? Rocks : RockPiles, false), d.Rand(2.5f, 6f), false, false, .5f));
            d.Scatter("林缘灌木", -470f, -470f, 470f, 470f, 6.5f, 1100,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 6f) && d.Chance(.5f),
                (x, z) => d.Chance(.22f) ? new Decorator.Choice(d.Pick(Deadwood, false), d.Rand(1.2f, 2.4f), false, false)
                                         : new Decorator.Choice(d.Pick(Shrubs, false), d.Rand(1.6f, 3.4f), false, false));
            d.Scatter("草甸花草", -460f, -460f, 460f, 460f, 20f, 600,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > 260f && d.Chance(Smooth(.5f, .75f, Noise(x, z, .007f, 23f))),
                (x, z) => new Decorator.Choice(d.Pick(Meadow, false), d.Rand(.7f, 1.4f), false, false, .05f));
            DistantTrees(d, Conifer, .7f, 1000);
            DistantMountains(d, 11);
        }
    }
}
