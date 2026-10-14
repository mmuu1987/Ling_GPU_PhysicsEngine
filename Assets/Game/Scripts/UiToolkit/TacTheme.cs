using UnityEngine;

namespace MassEngine.Game.Ui
{
    /// <summary>Copied from Assets/Game/UiPrototype (Editor-only) for runtime use. Colours used by the code-drawn maps / backdrop. USS can't reach Painter2D content, so the two skins live here.</summary>
    public sealed class MapPalette
    {
        public Color reliefLow, reliefHigh, battleLow, battleHigh;
        public Color grid, gridMajor, contour, contourMajor;
        public Color atk, def;
        public float zoneFill, zoneLine;
        public Color blocked, invalid;
        public float blockedFill, blockedHatch, blockedLine;
        public float compFill, compFillSel, dotLighten, dotSize, dotSpacing;
        public Color selMark, camFill, camLine, border, ticks, handleFill, handleLine, selDash;
        public bool sweep;
        public float lineScale;
    }

    public static class TacTheme
    {
        /// <summary>The game ships the Q 版 skin only (true). false = the rejected B 战术指挥 look, kept so the shared drawing code stays identical to the prototype.</summary>
        public static bool Toy = true;

        public static MapPalette Map => Toy ? ToyPal : TacPal;

        static Color C(int hex, float a = 1f) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, a);

        static readonly MapPalette TacPal = new MapPalette
        {
            reliefLow = new Color(0.025f, 0.06f, 0.075f), reliefHigh = new Color(0.07f, 0.17f, 0.2f),
            battleLow = new Color(0.035f, 0.075f, 0.075f), battleHigh = new Color(0.11f, 0.2f, 0.17f),
            grid = new Color(0.4f, 0.85f, 1f, 0.06f), gridMajor = new Color(0.4f, 0.85f, 1f, 0.14f),
            contour = new Color(0.55f, 0.9f, 1f, 0.08f), contourMajor = new Color(0.55f, 0.9f, 1f, 0.18f),
            atk = new Color(0.93f, 0.35f, 0.24f), def = new Color(0.24f, 0.56f, 0.95f),
            zoneFill = 0.05f, zoneLine = 0.55f,
            blocked = new Color(0.94f, 0.33f, 0.25f), invalid = new Color(0.94f, 0.33f, 0.25f),
            blockedFill = 0.12f, blockedHatch = 0.32f, blockedLine = 0.7f,
            compFill = 0.18f, compFillSel = 0.3f, dotLighten = 0.25f, dotSize = 2f, dotSpacing = 5f,
            selMark = new Color(0.24f, 0.8f, 0.96f, 0.95f), camFill = new Color(1f, 1f, 1f, 0.06f), camLine = new Color(1f, 1f, 1f, 0.8f),
            border = new Color(0.17f, 0.35f, 0.45f, 1f), ticks = new Color(0.5f, 0.75f, 0.85f, 0.6f),
            handleFill = Color.white, handleLine = new Color(0.02f, 0.08f, 0.12f, 1f), selDash = new Color(1f, 1f, 1f, 0.85f),
            sweep = true, lineScale = 1f,
        };

        static readonly MapPalette ToyPal = new MapPalette
        {
            reliefLow = C(0x8CC867), reliefHigh = C(0xAAD77A),
            battleLow = C(0x7DBE58), battleHigh = C(0x9CCF6A),
            grid = new Color(1f, 1f, 1f, 0.13f), gridMajor = new Color(1f, 1f, 1f, 0.28f),
            contour = C(0x3F7A2A, 0.14f), contourMajor = C(0x3F7A2A, 0.26f),
            atk = C(0xF26B3A), def = C(0x3D8BF0),
            zoneFill = 0.16f, zoneLine = 0.9f,
            blocked = C(0x8B7A66), invalid = C(0xF0453A),
            blockedFill = 0.45f, blockedHatch = 0.35f, blockedLine = 0.85f,
            compFill = 0.55f, compFillSel = 0.75f, dotLighten = 0.55f, dotSize = 2.6f, dotSpacing = 5f,
            selMark = Color.white, camFill = new Color(1f, 1f, 1f, 0.22f), camLine = Color.white,
            border = C(0x4E7F37), ticks = C(0x4E7F37, 0.7f),
            handleFill = Color.white, handleLine = C(0x3B3F4C), selDash = Color.white,
            sweep = false, lineScale = 1.6f,
        };
    }
}
