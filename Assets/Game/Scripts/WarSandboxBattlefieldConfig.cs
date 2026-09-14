using System;
using UnityEngine;

namespace MassEngine.Game
{
    [Serializable]
    public struct WarSandboxBattlefieldRules
    {
        public WarSandboxGameMode gameMode;
        public Vector3 controlPointCenter;
        [Min(2f)] public float controlPointRadius;
        [Min(5f)] public float controlPointCaptureSeconds;
        public bool staticObstaclesEnabled;
        [Min(0f)] public float staticObstacleClearance;
        public StaticObstacleRect[] staticObstacles;

        public static WarSandboxBattlefieldRules Default => new WarSandboxBattlefieldRules
        {
            gameMode = WarSandboxGameMode.Annihilation,
            controlPointRadius = 30f,
            controlPointCaptureSeconds = 20f,
            staticObstacleClearance = 2f,
            staticObstacles = new StaticObstacleRect[0]
        };

        public WarSandboxBattlefieldRules Copy()
        {
            WarSandboxBattlefieldRules copy = this;
            copy.staticObstacles = staticObstacles != null
                ? (StaticObstacleRect[])staticObstacles.Clone() : new StaticObstacleRect[0];
            return copy;
        }

        public bool TryValidate(out string error)
        {
            error = null;
            if (gameMode != WarSandboxGameMode.Annihilation && gameMode != WarSandboxGameMode.ControlPoint)
                error = "gameMode must be Annihilation or ControlPoint.";
            else if (!IsFinite(controlPointCenter.x) || !IsFinite(controlPointCenter.y) || !IsFinite(controlPointCenter.z))
                error = "controlPointCenter must contain finite coordinates.";
            else if (!IsFinite(controlPointRadius) || controlPointRadius < 2f)
                error = "controlPointRadius must be finite and at least 2 meters.";
            else if (!IsFinite(controlPointCaptureSeconds) || controlPointCaptureSeconds < 5f)
                error = "controlPointCaptureSeconds must be finite and at least 5 seconds.";
            else if (!IsFinite(staticObstacleClearance) || staticObstacleClearance < 0f)
                error = "staticObstacleClearance must be finite and non-negative.";
            else if (staticObstacles != null && staticObstacles.Length > StaticObstacleMath.MaxObstacleCount)
                error = "staticObstacles must contain at most " + StaticObstacleMath.MaxObstacleCount + " rectangles.";
            if (error != null) return false;

            // Disabled layouts remain authored data. Reject corruption instead of discarding it.
            if (staticObstacles != null)
                for (int i = 0; i < staticObstacles.Length; i++)
                    if (!staticObstacles[i].IsValid)
                    {
                        error = "staticObstacles[" + i + "] needs finite coordinates and both dimensions greater than 0.01 meters.";
                        return false;
                    }
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [CreateAssetMenu(menuName = "MassEngine/War Sandbox Battlefield Rules")]
    public sealed class WarSandboxBattlefieldConfig : ScriptableObject
    {
        public WarSandboxBattlefieldRules rules = WarSandboxBattlefieldRules.Default;

        public bool TryCreateSnapshot(out WarSandboxBattlefieldRules snapshot, out string error)
        {
            snapshot = default;
            if (!rules.TryValidate(out error)) return false;
            snapshot = rules.Copy();
            return true;
        }
    }
}
