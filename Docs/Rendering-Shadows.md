# 阴影接收规则（2026-10-12）

## 结论
自定义着色器接收主光源阴影时，必须用带距离淡出的写法：

```hlsl
Light l = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
```

禁止用单参数的 `GetMainLight(shadowCoord)`。以后新增的地图（预计还有 7～8 张）、地面、道具、远景、雾，以及兵种着色器，都按这条执行。

## 为什么
- 主光源阴影贴图只覆盖镜头前"阴影距离"以内的区域。PC 画质原来是 52.77 m，现在改成 80 m，只有 1 级级联。
- 单参数的 `GetMainLight(shadowCoord)` 不做距离淡出，只用 `BEYOND_SHADOW_FAR` 检查深度方向，不检查水平方向。
- 超出阴影范围的地面会读到阴影贴图最边缘那圈像素（贴图是钳制采样）。只要有单位的影子落在贴图边上，就会被沿一个方向无限拉长，在地面上形成几百米的深色长带。
- 大型单位最明显：影子大，而且镜头离得远，正好落在阴影范围边缘。小兵也会出现，只是带子很细。
- 加大阴影距离只能把边界推远，镜头拉得更远时还会复现。带距离淡出的写法会让阴影在到达阴影距离前淡为 0，永远读不到贴图边缘，不受镜头距离和地图大小影响。

## 新地图检查清单
1. 优先复用 `Assets/Game/Content/Battlefields/Maps1024/Shared/` 下的 `BattlefieldGround1024.shader` 和 `BattlefieldProps1024.shader`，这两个已经修好。
2. 如果新写或复制了着色器，全文搜索 `GetMainLight(`：
   - 接收阴影的地方必须是三参数写法，第二个参数传世界坐标（`float3`）；
   - 不接收阴影的 `GetMainLight()`（无参数）不受影响。
3. 着色器要保留 `#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN`，否则不接收阴影，淡出也不会生效。
4. 场景验收：把大型单位放在镜头前 50～100 m，前后推拉镜头，地面不应出现从单位延伸出去的长条阴影。

## 本次修改（工作区，未提交）
三参数写法，每个文件改一行：
- `Assets/Game/Content/Battlefields/Maps1024/Shared/BattlefieldGround1024.shader`
- `Assets/Game/Content/Battlefields/Maps1024/Shared/BattlefieldProps1024.shader`
- `Assets/Game/Shaders/`：`Art20Ground`、`Art21Ground`、`Season22Ground`、`SeasonalGround22`、`SeasonalGround22B`、`SeasonalProps22`、`BattlefieldBackdropFog`
- `Assets/Game/Experiments/ForestPrototype/TerrainPlanB.shader`（实验着色器，顺带统一）
- `Assets/MassEngine/VatRender/Shaders/LitInstancedAgentShader.shader`：兵种近景接收阴影

不受影响的着色器：`Art20Distant`、`Art21Distant`、`Season22Distant`、`VatInstancedNoShadow`，它们用的是无参数 `GetMainLight()`，本来就不接收阴影。

## 阴影距离设置（2026-10-12）
- 玩家在「设置 → 画面 → 阴影距离」里调整，范围 50～250 m，步长 10 m，默认 100 m。目前只有新界面（UI Toolkit）有这一项。
- 设置保存在 `WarSandboxGraphicsSettings.json`，和声音、镜头设置放在同一目录。会话开始时，以及每次调整时，写入当前 URP 资产的 `shadowDistance`。
- 编辑器里退出 Play 时，会把 URP 资产还原成原来的值，所以运行时的调整不会改动 `PC_RPAsset.asset`。
- `PC_RPAsset.asset` 里的阴影距离（现在是 160 m）只在没有读取设置的情况下生效。移动端 `Mobile_RPAsset` 是 50 m。
- 代码：`Assets/Game/Scripts/WarSandboxGraphicsSettings.cs`；`Game.asmdef` 为此新增引用 URP Core/Universal Runtime。
- 性能：兵种只有近景才投影，开销不随阴影距离增加；多出来的是范围内场景物件的阴影绘制。阴影贴图仍为 4096，距离越远，近处影子越柔和。

## 提交时要同步
- `LitInstancedAgentShader` 的这一行要同步到 14 份 P9 副本。
- 可选：加一个 EditMode 测试，扫描 `Assets/Game`、`Assets/MassEngine` 下的 `.shader`/`.hlsl`，禁止出现 `GetMainLight(shadowCoord)` 或 `GetMainLight(TransformWorldToShadowCoord(...))` 这类单参数接收阴影写法，防止新地图回退。
