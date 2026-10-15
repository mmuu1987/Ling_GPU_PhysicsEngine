using MassEngine.Game.Ui;
using UnityEditor;

namespace MassEngine.Game.Editor
{
    /// <summary>New Q 版 UI Toolkit screens vs. the old uGUI ones. Works in and out of Play mode; the switch is applied on the next frame.</summary>
    internal static class WarSandboxUiToolkitMenu
    {
        const string Path = "Tools/战争沙盒 UI/使用新界面（UI Toolkit）";

        [MenuItem(Path, false, 0)]
        static void Toggle()
        {
            WarSandboxUiToolkit.Enabled = !WarSandboxUiToolkit.Enabled;
            Menu.SetChecked(Path, WarSandboxUiToolkit.Enabled);
            UnityEngine.Debug.Log("[WarSandboxUiToolkit] " + (WarSandboxUiToolkit.Enabled ? "新界面（UI Toolkit）" : "旧界面（uGUI）"));
        }

        [MenuItem(Path, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(Path, WarSandboxUiToolkit.Enabled);
            return true;
        }

        // Battle HUD layout redesign: compact layout (new) vs. the previous UI Toolkit layout. Applied on the next frame.
        const string CompactPath = "Tools/战争沙盒 UI/战斗界面：紧凑布局（新）";

        [MenuItem(CompactPath, false, 1)]
        static void ToggleCompact()
        {
            WarSandboxCommandHUD.CompactBattleHud = !WarSandboxCommandHUD.CompactBattleHud;
            Menu.SetChecked(CompactPath, WarSandboxCommandHUD.CompactBattleHud);
            UnityEngine.Debug.Log("[WarSandboxUiToolkit] 战斗界面：" + (WarSandboxCommandHUD.CompactBattleHud ? "紧凑布局（新）" : "原布局"));
        }

        [MenuItem(CompactPath, true)]
        static bool ToggleCompactValidate()
        {
            Menu.SetChecked(CompactPath, WarSandboxCommandHUD.CompactBattleHud);
            return true;
        }
    }
}
