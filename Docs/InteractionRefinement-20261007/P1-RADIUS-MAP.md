# P1：物理半径、模型单位和预览圆环来源映射

日期：2026-10-07。范围：只读显示与独立模型预览；**不是战斗参数修改方案，也不是拥挤原因结论**。

## 1. 已核对的主链

`UnitTypeConfig.flockingConfig.agentRadius`
→ `UnitTypeBase.BuildGpuSettings()`
→ `DefaultFlockingModule.Contribute()`
→ `UnitTypeGpuSettings.agentRadius`
→ GPU 各使用路径。

- 当前扫描到的 **262 个 UnitTypeConfig 全部使用 `MassEngine.DefaultSwordUnit`**。扫描记录：`Logs/InteractionRefinement-20261007/P1/source-trace.json`。
- 群体配置存在时，默认模块上传 `max(0.01, 配置半径)`；缺少配置时保留 `UnitTypeGpuSettings.CreateDefaults()` 的 0.45。不是从阴影、模型宽度或攻击距离推导半径。
- P1 的 `UnitPreviewRadius.Read` 直接调用现有 `DefaultSwordUnit.BuildGpuSettings()` 获取结果，不复制默认常数、不写回配置。未验证的自定义模块不实例化、不假装已知其物理半径；显示不可用。
- 图鉴是模板只读数据，不是正在战斗的 Agent 实时回读。P1 不增加半径统计字段、全局覆盖键或保存格式。

## 2. 各消费者并不具有同一个“有效半径”

记 `r` 为上传后的基准半径，`s = max(0.01, |scale.x|, |scale.z|)`。

| 路径 | 已有计算与语义 | 缩放／钳制注意事项 |
|---|---|---|
| `AgentDataCommon.hlsl:985–987` | `GetAgentRadius` 原样读取 `r` | 此处没有乘缩放 |
| 同文件 `QueryCombatNeighborhood:1283,1355–1364` | 接触体半径 `r × s`；两体半径之和用于前方友军判断、软接触分离 | 软接触触发条件是距离小于两体半径和的 **0.9 倍**；不是严格不可穿透的圆形碰撞器 |
| 同文件 `:1366` | 普通 separation 使用 `r_self + r_other` | 不乘 Agent 缩放 |
| 同文件 `ApplyStaticObstacleBounds:427` | 静态障碍膨胀使用 `max(0.05, r)` | 不乘 Agent 缩放 |
| `AgentCombatSimulation.compute:543–548` | 障碍前瞻使用 `max(4, 6r)`，绕障调用传 `r` | 不应再无差别乘一遍缩放 |
| `ProjectileUnitCollision.hlsl:18,72` | 目标胶囊／溅射半径使用 `max(0.05,r) × s` | 与接触路径的半径最小值不同 |
| `ProjectileGpuManager.cs:375`、`RangedClearance.hlsl:47` | 发射点偏移使用 `max(0.05,r) × max(|scale.x|,|scale.z|)`，还受目标距离的 1/4 限制 | 不是同一个硬碰撞边界；此处水平缩放没有额外 0.01 下限 |
| `RangedClearance.hlsl:105–106` | 半径先按目标胶囊口径缩放，再得出 `clamp(4 × radius, 0.5, 4)` | 只用于该清距逻辑 |
| `AgentDataCommon.hlsl:1243–1246`、`AgentCombatSimulation.compute:119–123` | 交战槽参考 `max(0.05,r)`，并与攻击距离组合 | 不是攻击范围本身，也不是纯物理半径圆 |
| 攻击退出／朝向平滑的半径消费者 | 使用 `r` 构成缓冲／平滑距离 | 不能用预览圆代替这些范围 |
| `ScenarioPhysics.cs:89–97` | 出生密度提示按 `max(0.05, 配置半径)`，缺省 0.45，方格堆积参考 `1/(4r²)` | 属于初始布阵提示，不是运行时拥挤测量 |
| `MassEngineManager.Terrain.cs:50–62` | 地形清距综合配置／已注册 GPU 半径，并保留 `terrainClearanceFloor` 的较大历史值 | 重置与场景重入的缓存边界必须在后续可编辑阶段独立验证 |
| `MassGpuBufferManager.cs:301–302` | 最大半径缓存取已上传设置的最大值，初始 0.05 | 没有在这里折入 Agent 的任意水平缩放 |

**结论：不能把“所有地方的物理半径 = r × 最大水平缩放”当成已存在的全局约定。P1 不统一、不改写这些路径。**

## 3. 模型尺度与实际锚点

1. `VatBaker.cs:385–398` 将渲染器顶点经 `root.worldToLocalMatrix × renderer.localToWorldMatrix` 转到烘焙根空间，再写入 VAT。预览读取同一份 VAT，不根据屏幕大小归一化角色。
2. `DefaultSpawnModule.cs:62–64` 生成 Agent 的位置和 `scale = Vector3.one`。
3. `MassEngineManager.Terrain.cs:150–158` 将 Agent 的 Y 放在地形采样高度；没有用模型包围盒中心替代 Agent 位置。
4. `VatRender/Shaders/LitInstancedAgentShader.shader` 和 `VatInstancedNoShadow.shader` 使用 Agent 的缩放、旋转、平移变换渲染 VAT 顶点。预览走相同的 resolved mesh/material/clip，并上传自己独立的一个 Agent。
5. P1 预览里的 Agent 位置是 `(0,0,0)`，圆环中心也固定在这个真实根锚点的地面平面。模型不对称、长剑或翅膀可以改变取景中心，但不能移动圆环或改变其物理半径。
6. 默认预览比例为 1×，与现有默认生成模块相同。仅诊断 API 可设非 1 的预览比例；它只影响独立预览缓冲和取景，不能写入模板、出生设置或实际战斗。

## 4. 圆环所代表的内容

- 独立深金色网格、独立材质、独立 Shader；不借用软地面阴影、不修改官方 VAT 材质。
- 圆环中心线的单位几何半径为 1，实际绘制半径为**现有接触路径的名义体半径 `r × s`**。
- 明确标注“非硬碰撞边界 / 攻击范围”。接触算法允许重叠且存在力学行为，不能把圈内无重叠当作已验证保证。
- UI 始终使用 1×，故图鉴所列基准半径与预览接触圈半径相同。非 1 诊断例分别验证模型的向量缩放和圆环的最大水平轴口径；不把圆环拉成模型包围椭圆。
- 128 段环形网格，中心线半径精确；笔画半宽是半径的 1.2%。像素测量应扣除笔画／栅格误差，不能把外沿当成数值半径。
- 模型与圆环共同进入默认取景 Bounds，使用同一视图／投影。旋转不改变距离，滚轮改变相机距离，不修改物理尺寸。放大允许用户主动裁切，复位恢复完整取景。
- 摄像机取景中心与物理锚点是两个不同概念，严禁混用。

## 5. 验证记录位置和阶段边界

- 已知长度、投影、对称／非对称、大小及非 1 水平缩放：`UnitPreviewRadiusPlayModeTests`，以独立 Unity Camera 的射线与世界 Y=0 平面反求深金色像素位置。
- 世界坐标断言：独立 Camera 投影与预览矩阵比较；所有环心线采样点处于默认取景内；旋转／缩放不改变半径。
- UI：实际图鉴与放大视图离屏渲染，分别验证 1280×720、1920×1080；不是操作系统鼠标验收。
- 资源：反复换模型／隐藏控件，检查 Mesh、Material、RenderTexture 数量回到基线，并检查原 renderer 的 ComputeBuffer／CommandBuffer 引用释放。
- **测试结果以 P1-REPORT 和对应 process.json／results.xml 为准。本文列的是已实现的验证方法，不先行宣称测试通过。**
- P2 编辑入口仍关闭。不同消费者的历史语义不能靠预览“修正”；需要任何战斗语义调整时另行记录并回归。
