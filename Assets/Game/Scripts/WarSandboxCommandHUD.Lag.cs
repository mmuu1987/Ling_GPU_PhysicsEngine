using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// 2026-10-17 (no unit cap): unit counts are not limited, so the HUD says so when the player's machine actually
    /// struggles: measured frame rate under LagFps for LagWindows one-second windows in a running battle shows one line
    /// (lowest priority, never covers command feedback) for LagShowSeconds, then stays quiet for LagCooldownSeconds.
    /// </summary>
    public sealed partial class WarSandboxCommandHUD
    {
        private const float LagFps = 20f, LagShowSeconds = 6f, LagCooldownSeconds = 60f, LagWarmupSeconds = 5f;
        private const int LagWindows = 3;
        private float lagWindowStart = -1f, lagRunningSince = -1f, lagShowUntil, lagQuietUntil, lagShownFps;
        private int lagWindowFrame, lagSlowWindows;

        /// <summary>The lag line while it is due, else empty. Safe to call any number of times per frame.</summary>
        private string LagHint()
        {
            float now = Time.realtimeSinceStartup;
            if (controller == null || controller.Phase != WarSandboxBattlePhase.Running)
            {
                lagWindowStart = lagRunningSince = -1f; lagSlowWindows = 0; lagShowUntil = 0f;
                return "";
            }
            if (lagRunningSince < 0f) lagRunningSince = now;
            if (lagWindowStart < 0f) { lagWindowStart = now; lagWindowFrame = Time.frameCount; }
            else if (now - lagWindowStart >= 1f)
            {
                float fps = (Time.frameCount - lagWindowFrame) / (now - lagWindowStart);
                lagWindowStart = now; lagWindowFrame = Time.frameCount;
                lagSlowWindows = now - lagRunningSince >= LagWarmupSeconds && fps < LagFps ? lagSlowWindows + 1 : 0;
                if (lagSlowWindows >= LagWindows && now >= lagQuietUntil)
                {
                    lagShownFps = fps; lagShowUntil = now + LagShowSeconds; lagQuietUntil = now + LagCooldownSeconds;
                }
            }
            return now < lagShowUntil
                ? "画面有点卡（约 " + Mathf.Max(1, Mathf.RoundToInt(lagShownFps)) + " fps）：单位很多时会这样，可以调低「画面」或减少兵力"
                : "";
        }
    }
}
