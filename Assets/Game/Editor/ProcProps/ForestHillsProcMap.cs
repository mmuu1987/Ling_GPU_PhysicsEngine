using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 森林丘陵·程序化道具 (pilot, round 2 2026-10-13): same terrain, obstacles, splat, seed and scenery rules as
    /// <see cref="ForestHillsMap"/> (all delegated), but
    /// - every prop is procedural (<see cref="ProcPropLibrary"/>), swapped BEFORE the third-party checks, so the
    ///   intended birch / broadleaf mix actually appears (the original map rejects those heavy prefabs) and the map no
    ///   longer needs the third-party prop files;
    /// - ground textures are procedural (ProcProps/Ground) with a brighter Q版 palette;
    /// - extra variety passes: ferns, mushrooms, fallen logs, dead trees, flower patches, pebbles.
    /// Own Id / Folder, so the original map is never touched.
    /// </summary>
    public sealed class ForestHillsProcMap : MapSpec
    {
        readonly ForestHillsMap inner = new ForestHillsMap();
        GroundLook look;

        public override string Id => "forest-hills-proc-1024";
        public override string Folder => "ForestHillsProc";
        public override string DisplayName => inner.DisplayName + "·程序化道具";
        public override string Description => inner.Description + "\n（程序化版本：树木、岩石、花草、远山与地面均为程序化低多边形 / 程序化贴图）";
        public override string Briefing => inner.Briefing;
        public override int Seed => inner.Seed;
        public override int TerrainVersion => inner.TerrainVersion;
        public override Vector2 ControlPoint => inner.ControlPoint;
        public override float ControlPointRadius => inner.ControlPointRadius;
        public override Vector3 PreviewCamera => inner.PreviewCamera;
        public override Vector3 PreviewTarget => inner.PreviewTarget;
        public override Vector2[] MustConnect => inner.MustConnect;

        public override GroundLook Look
        {
            get
            {
                if (look != null) return look;
                var o = inner.Look;
                look = new GroundLook
                {
                    GrassA = ProcPropLibrary.GroundPath("GrassA"), GrassB = ProcPropLibrary.GroundPath("GrassB"),
                    Dirt = ProcPropLibrary.GroundPath("Dirt"), Rock = ProcPropLibrary.GroundPath("Rock"), Forest = ProcPropLibrary.GroundPath("Forest"),
                    Tiling = o.Tiling, ForestTiling = o.ForestTiling,
                    // textures are already in the Q版 palette: white tints, light palette pull
                    PaletteDark = new Vector4(.076f, .275f, .036f, 1), PaletteLight = new Vector4(.262f, .571f, .102f, 1), Stylize = .2f,
                    SlopeRock = o.SlopeRock, OutsideMix = o.OutsideMix, Haze = o.Haze, HazeStart = o.HazeStart, HazeEnd = o.HazeEnd,
                    RidgeBase = o.RidgeBase, FarMountains = o.FarMountains,
                };
                return look;
            }
        }

        public override float Height(float x, float z) => inner.Height(x, z);
        public override bool Blocked(float x, float z) => inner.Blocked(x, z);
        public override Color32 Splat(float x, float z, float height, float slope) => inner.Splat(x, z, height, slope);

        public override void Decorate(Decorator d)
        {
            d.Remap = ProcPropLibrary.CreateRemap();
            d.RemapEarly = true;
            inner.Decorate(d);
            Variety(d);
        }

        /// <summary>Extra props only this map has. Runs after the shared rules, so it never changes their layout.</summary>
        void Variety(Decorator d)
        {
            string[] fern = ProcPropLibrary.Pool("Fern"), mush = ProcPropLibrary.Pool("Mushroom"), fallen = ProcPropLibrary.Pool("FallenLog");
            string[] dead = ProcPropLibrary.Pool("Dead"), patch = ProcPropLibrary.Pool("FlowerPatch"), pebbles = ProcPropLibrary.Pool("Pebbles");
            // wood edge undergrowth
            d.Scatter("林缘蕨类", -470f, -470f, 470f, 470f, 7f, 700,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 5f) && d.Chance(.35f),
                (x, z) => new Decorator.Choice(d.Pick(fern, false), d.Rand(.8f, 1.4f), false, false, .1f));
            d.Scatter("林下蘑菇", -470f, -470f, 470f, 470f, 9f, 450,
                (x, z) => d.Blocked(x, z) && !d.DeepBlocked(x, z, 4f) && d.Chance(.22f),
                (x, z) => new Decorator.Choice(d.Pick(mush, false), d.Rand(.6f, 1.1f), false, false, .05f));
            // inside the woods
            d.Scatter("林中倒木", -470f, -470f, 470f, 470f, 28f, 140,
                (x, z) => d.DeepBlocked(x, z, 6f) && d.Chance(.3f),
                (x, z) => new Decorator.Choice(d.Pick(fallen, false), d.Rand(.8f, 1.2f), false, false, .15f));
            d.Scatter("枯树", -490f, -490f, 490f, 490f, 40f, 60,
                (x, z) => d.Blocked(x, z) && d.Chance(.18f),
                (x, z) => new Decorator.Choice(d.Pick(dead, false), d.Rand(8f, 13f), false, false, .3f));
            // open ground outside the battle core (same rule as the meadow flowers: edge distance > 260 m)
            d.Scatter("花丛", -460f, -460f, 460f, 460f, 14f, 500,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > 260f && d.Chance(.6f * Smooth(.45f, .7f, Noise(x, z, .009f, 41f))),
                (x, z) => new Decorator.Choice(d.Pick(patch, false), d.Rand(.5f, .9f), false, false, .05f));
            d.Scatter("碎石", -460f, -460f, 460f, 460f, 24f, 300,
                (x, z) => !d.Blocked(x, z) && EdgeDistance(x, z) > 260f && d.Chance(.14f),
                (x, z) => new Decorator.Choice(d.Pick(pebbles, false), d.Rand(.3f, .6f), false, false, .05f));
        }
    }
}
