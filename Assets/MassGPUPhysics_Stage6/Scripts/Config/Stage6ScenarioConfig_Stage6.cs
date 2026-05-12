using UnityEngine;

[CreateAssetMenu(fileName = "Stage6ScenarioConfig", menuName = "MassGPUPhysics/Stage6/Config/Scenario Config")]
public sealed class Stage6ScenarioConfig_Stage6 : ScriptableObject
{
    [Header("Scenario Identity")]
    [Tooltip("场景名称，用于 UI 和日志标识。")]
    public string scenarioName = "Default Scenario";
    [TextArea(2, 4)]
    [Tooltip("可选的场景描述文本。")]
    public string description;

    [Header("Teams")]
    [Tooltip("攻击方阵营配置。")]
    public Stage6TeamConfig_Stage6 attackerTeamConfig;
    [Tooltip("防守方阵营配置。")]
    public Stage6TeamConfig_Stage6 defenderTeamConfig;

    [Header("Battle Flow")]
    [Tooltip("Manager 在 Start 时是否自动读取本 Scenario 里的全部配置。")]
    public bool autoApplyOnStart = true;
    [Tooltip("Manager 应用配置后，是否同步 Spawn Config 中的兵力数量到 Instance Count / Attacker Count。")]
    public bool applyUnitCounts = true;
    [Tooltip("应用配置后是否立刻开始战斗（battleStarted = true）。关闭后双方会先进入列阵 Idle 等待手动开战。")]
    public bool autoStartBattle;
    [Tooltip("是否启用双阵营战斗模式。默认开启；关闭后走单阵营测试逻辑。")]
    public bool enableTwoTeamCombat = true;

    [Header("Debug")]
    [Tooltip("运行时在屏幕左上角显示最小战斗统计 HUD（存活、死亡、战斗时间等）。")]
    public bool showBattleTelemetry = true;

    public bool HasAttacker => attackerTeamConfig != null;
    public bool HasDefender => defenderTeamConfig != null;
    public bool HasBothTeams => HasAttacker && HasDefender;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
            scenarioName = "Default Scenario";
    }
}
