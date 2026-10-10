using System;
using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Formation facing (2026-10-10 playtest-38 feedback). Entry.facing: 0 = auto (face the nearest enemy formation),
    /// 1..4 = manual quarter turn, yaw = (facing - 1) * 90 degrees. Yaw follows AgentDataCommon.FaceDirection:
    /// 0 = +Z (map up), 90 = +X (map right). Agents keep the spawn yaw until they first move or acquire a target.
    /// </summary>
    public static class WarSandboxDeploymentFacing
    {
        public const int Auto = 0;
        private static readonly string[] Names = { "北 ↑", "东 →", "南 ↓", "西 ←" };
        private static readonly string[] Arrows = { "↑", "→", "↓", "←" };

        public static bool IsValid(int facing) => facing >= Auto && facing <= 4;
        public static string Name(int quarter) => Names[((quarter % 4) + 4) % 4];
        public static string Arrow(int quarter) => Arrows[((quarter % 4) + 4) % 4];
        public static float YawDegrees(int quarter) => (((quarter % 4) + 4) % 4) * 90f;

        public static int ResolveQuarter(WarSandboxDeploymentEntry[] entries, int index) =>
            entries == null ? 0 : ResolveQuarter(entries.Length, i => entries[i], index);

        public static int ResolveQuarter(WarSandboxDeploymentDraft draft, int index) =>
            draft == null ? 0 : ResolveQuarter(draft.Count, i => draft[i], index);

        public static float ResolveYaw(WarSandboxDeploymentEntry[] entries, int index) => YawDegrees(ResolveQuarter(entries, index));

        private static int ResolveQuarter(int count, Func<int, WarSandboxDeploymentEntry> get, int index)
        {
            if (index < 0 || index >= count) return 0;
            var self = get(index);
            if (self.facing >= 1 && self.facing <= 4) return self.facing - 1;
            float best = float.PositiveInfinity; Vector2 delta = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                if (i == index) continue;
                var other = get(i);
                if (other.teamId == self.teamId) continue;
                var d = new Vector2(other.center.x - self.center.x, other.center.z - self.center.z);
                float sqr = d.sqrMagnitude;
                if (sqr < best) { best = sqr; delta = d; }
            }
            if (float.IsPositiveInfinity(best) || best < 1e-6f)
            {
                // No enemy: face across the long side (front along Z -> face +X), never along the battle line.
                var size = self.Size;
                return size.z >= size.x ? 1 : 0;
            }
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)) return delta.x >= 0 ? 1 : 3;
            return delta.y >= 0 ? 0 : 2;
        }
    }
}
