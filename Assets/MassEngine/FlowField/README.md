# FlowField — 双队伍流场导航

大规模推进的导航层：把"往哪走"从逐 Agent 寻路变成一张全场方向纹理格。
当前维护两张场：攻击方（team 0）与防守方（team 1），同队兵种共享本队场。

## 数据契约

| 缓冲 | 布局 | 说明 |
|---|---|---|
| `flowFieldDirections` / `defenderFlowFieldDirections` | `float2[res×res]` | 单位方向或零向量（零=无引导/到达停止半径） |
| `runtime*TargetDensity` | `uint[res×res]` | 敌方存活密度（动态选点输入） |
| `runtime*FlowStats` | `int[4]` | [0]=敌方存活数，[3]=选出的目标数 |
| `runtime*FlowTargets` | `float4[8]` | 每扇区目标点（xy=位置 z=权重 w=有效） |
| `runtime*FlowPreviewTexture` | RT ARGB32 | 预览（开关关闭时 kernel 零写入） |

**队伍判定只用 `teamIdReadBuffer`**：攻方场的目标 = 所有 `teamId != attackerTeamId`
的存活 Agent；不存在任何按索引区间的推断。

## 生成模式（优先级从高到低）

1. **点击覆盖**（`MassEngineManager.SetFlowTargetOverride`，运行时状态，不写资产）
2. **配置目标**（MovementConfig.useConfiguredFlowTarget：点或区域）
3. **动态寻的**（runtimeDynamic*FlowEnabled：按 Z 轴扇区统计敌方密度质心）

"无目标且无动态"时 Generate 会**显式清零**方向场——移除目标后部队立即失去引导，
不会走向幽灵目标。

## 行为保障（2026-07-27 夜间修复）

- **Z 收敛**：扇区内每格目标 Z 向该扇区敌方密度质心插值 35%——歼灭一条走廊后幸存
  攻方会转向存活敌群，而不是永远停在质心 X 线上发呆
- **残局兜底**：所有扇区敌方数都低于 min 阈值时，Select 发出全局质心目标——
  20 万打 35 个残兵的仗一定能收尾
- **停止半径下限**：生效停止半径 = max(配置值, 0.75×流场格)——目标线两侧不再出现
  "永动搓衣机"抖动带

## 重建节奏

- 静态目标：仅在 dirty（目标变更/初始化/StartBattle）时重建一次
- 动态寻的：`dynamicFlowUpdateInterval`（默认 0.35s）节流
- `rebuildRuntimeFlowEveryFrame`：调试用强制每帧

## 参数（RuntimeFlowConfig）

分辨率/格尺寸/原点、双队开关、重建间隔、预览开关与模式、
扇区数/停止半径/最小敌方数（动态选点）。
每 Agent 的流场权重与响应速度在 **MovementConfig**（按兵种，走 settings 通道）。

## 已知瓶颈

`SelectRuntime*FlowTargets` 是 numthreads(1,1,1) 串行扫描 O(res²)——
这就是重建必须节流的原因。并行化（分扇区 reduction）是登记在案的未来项。

## 如何验证

预览：RuntimeFlowConfig.runtimeFlowPreviewEnabled=true + 场景挂 `FlowFieldPreviewHUD`。
方向模式 = 色相环编码方向；密度模式 = 敌方密度热力图。
