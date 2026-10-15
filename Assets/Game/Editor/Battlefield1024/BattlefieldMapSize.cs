using UnityEngine;

namespace MassEngine.Game.Editor
{
    /// <summary>
    /// 2026-10-14 (程序化地图 2048, approved "do all of steps 0–4 with 4 templates"): the size-dependent constants of
    /// Battlefield1024Generator as one profile. MapSpec.Size defaults to S1024, whose values are exactly the old
    /// constants, so the existing 1024 maps generate byte-for-byte as before. S2048 is used only by the 2048 maps.
    /// </summary>
    public sealed class BattlefieldMapSize
    {
        public string Tag;
        public int Vertices;                 // gameplay vertices per side, 4 m step
        public float Half, WorldSize, EdgeBlock, OpenCore;
        /// <summary>Validation footprint (two 100k armies at density 0.7 / aspect 2.2), mirrored on x.</summary>
        public Vector2 ArmyCenter, ArmySize;
        /// <summary>Points (±x, 0) used by the connectivity check; inside both deployment footprints.</summary>
        public float ConnectProbe;
        public string TemplateScene, Root, Derived, LogDir;
        public int ControlSize;
        public float BackdropOuter;
        /// <summary>Bake a per-map CameraGround24 (camera floor / pan anchor) from the generated ground + backdrop.</summary>
        public bool BakeCameraGround;

        public static readonly BattlefieldMapSize S1024 = new BattlefieldMapSize
        {
            Tag = "1024", Vertices = Battlefield1024Generator.Vertices, Half = Battlefield1024Generator.Half,
            WorldSize = Battlefield1024Generator.WorldSize, EdgeBlock = Battlefield1024Generator.EdgeBlock,
            OpenCore = Battlefield1024Generator.OpenCore, ArmyCenter = Battlefield1024Generator.ArmyCenter,
            ArmySize = Battlefield1024Generator.ArmySize, ConnectProbe = 200f,
            TemplateScene = Battlefield1024Generator.Template, Root = Battlefield1024Generator.Root,
            Derived = Battlefield1024Generator.Derived, LogDir = Battlefield1024Generator.LogDir,
            ControlSize = 1024, BackdropOuter = 3072f, BakeCameraGround = false,
        };

        /// <summary>2048 × 2048: 513² vertices at 4 m, 40 m blocked edge band (playable ≈1968 m), deployment centres
        /// ±400 m so the fronts of two 100k-size footprints are ≈545 m apart (user: spawn gap ≈550 m).</summary>
        public static readonly BattlefieldMapSize S2048 = new BattlefieldMapSize
        {
            Tag = "2048", Vertices = 513, Half = 1024f, WorldSize = 2048f, EdgeBlock = 984f, OpenCore = 600f,
            ArmyCenter = new Vector2(400f, 0f), ArmySize = Battlefield1024Generator.ArmySize, ConnectProbe = 400f,
            TemplateScene = Battlefield2048Setup.TemplateScene, Root = "Assets/Game/Content/Battlefields/Maps2048",
            Derived = "Assets/ThirdParty/_Derived/Maps2048", LogDir = "Logs/Battlefield2048",
            ControlSize = 2048, BackdropOuter = 5120f, BakeCameraGround = true,
        };
    }
}
