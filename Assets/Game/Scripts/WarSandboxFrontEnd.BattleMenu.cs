using UnityEngine;

namespace MassEngine.Game
{
    /// <summary>
    /// Battle HUD layout redesign, phase 1: the compact battle HUD carries the in-battle system strip (设置 / 战场目录 / 退出) in its
    /// ≡ menu. While it does, the UI Toolkit strip stays hidden; the entries below call exactly what the strip buttons call.
    /// 帮助 is not duplicated: the HUD's 操作帮助 already opens the same Guide30 page when one exists.
    /// </summary>
    public sealed partial class WarSandboxFrontEnd
    {
        /// <summary>Set every frame by the compact battle HUD while its menu carries the system strip; cleared when it hides.</summary>
        public static bool BattleHudOwnsSystemMenu { get; set; }
        /// <summary>Set by the deployment HUD while its overview header carries 帮助 / 设置 / 战场目录 / 退出 (2026-10-17).</summary>
        public static bool DeployHudOwnsSystemMenu { get; set; }

        private static WarSandboxFrontEnd battleMenuFront;
        private static WarSandboxFrontEnd BattleMenuFront()
        {
            if (battleMenuFront == null) battleMenuFront = Object.FindFirstObjectByType<WarSandboxFrontEnd>();
            return battleMenuFront;
        }

        /// <summary>Same as the strip's 设置.</summary>
        public static void BattleOpenSettings() => WarSandboxSceneSession.Instance?.OpenSettings();

        /// <summary>Same as the strip's 战场目录 (asks for confirmation when the battle requires it).</summary>
        public static void BattleReturnToCatalog()
        {
            var front = BattleMenuFront();
            if (front == null || front.session == null) return;
            front.homeOpen = false;
            front.RequestReturn();
        }

        /// <summary>Same as the strip's 退出 (always asks for confirmation).</summary>
        public static void BattleQuit()
        {
            var front = BattleMenuFront();
            if (front == null || front.session == null) return;
            front.RequestQuit();
        }
    }
}

