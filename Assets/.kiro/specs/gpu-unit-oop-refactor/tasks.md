# Implementation Plan: GPU Unit OOP Refactor (Stage7)

> 2026-07 重构版。历史版本的勾选与事实不符（多项 [x] 验证的是永不执行的 CPU 影子层，
> "shader 从 Stage6 逐字复制"的说明也与实际分叉 ~1000 行的现状矛盾），已按当前实现全部
> 重新核对。架构决策与机制说明见 design.md。

## 已完成（2026-07 重构核对）

- [x] 1. 核心数据模型
  - AgentData 56 字节（Sequential），属性测试锁定 stride
  - UnitTypeGpuSettings 112 字节 GPU 参数记录，与 HLSL UnitTypeSettings 逐字段一致
  - AgentState 枚举 + AgentStateMachine（GPU 状态语义的 C# 镜像规格）
  - _Requirements: 9.2, 10.1, 10.2_

- [x] 2. ScriptableObject 配置体系（只读输入）
  - UnitTypeConfig / SpawnConfig / MovementConfig / FlockingConfig / AnimationConfig /
    CombatConfig / RenderConfig / ScenarioConfig_Stage7 / Stage7SystemConfig 等
  - ConfigValidator 纯校验：Error（Spawn 缺失 / unitCount≤0 / teamId∉{0,1} / 类名非法）
    跳过注册；Warning 用内建默认值；不写回资产
  - _Requirements: 1.4, 2.1, 4.4, 5.4, 7.6_

- [x] 3. 模块层（架构决策 A：参数贡献者）
  - IUnitParameterContributor + ISpawn/IMovement/IFlocking/IAnimation/ICombatModule
  - Default 模块实现 Contribute(ref UnitTypeGpuSettings)；DefaultSpawnModule 为唯一
    CPU 行为模块
  - UnitTypeBase（模块组装 + BuildGpuSettings + VAT 时长合并）、DefaultSwordUnit、
    UnitTypeRegistry（注册/offset/unitTypeIndex/settings 聚合/队伍目标解析）
  - _Requirements: 1.1, 1.2, 1.5_

- [x] 4. VAT 与渲染运行时
  - VatProfileReader（初始化时一次性反射解析）
  - ResolvedUnitTypeRuntime（mesh-纹理强制配对 + 预填 MaterialPropertyBlock + 各 clip 时长）
  - MassGpuRenderDispatcher：按兵种 × LOD 间接绘制，渲染路径零反射
  - _Requirements: 6.1, 6.2, 6.3, 6.4_

- [x] 5. GPU 管线
  - MassGpuBufferManager：按兵种 × LOD 分桶的可见索引/args；unitTypeIndex/unitTypeSettings
    旁路缓冲；hp/pendingDamage/position 双缓冲；分配后清零流场缓冲；释放顺序与
    Editor/Play 释放语义修正
  - ComputePipelineOrchestrator：SpatialHash → RuntimeFlow(条件) → DensityMap →
    Combat → 每兵种 LOD 分类；IDispatchListener 测试钩子；缺失 kernel 一次性日志
  - HLSL：队伍真理统一为 teamIdReadBuffer（删除 attackerCount 区间推断）；
    GetUnitSettings(index) 取代全部 attacker*/defender* 参数 uniform；
    ResolveAliveState 状态推导；hold-position 防守方保留 separation 并钳制于 guardRadius；
    寻敌格半径改为 C# 下发（超限警告）；按 clip 时长回绕动画；预览写入按开关门控
  - _Requirements: 5.2, 8.2, 9.1, 9.2, 9.3, 10.2, 10.3_

- [x] 6. 场景入口与运行时状态
  - MassGpuSystemManager_Stage7：流场门控三要素分解（enabled/reason/cadence）+
    dynamicFlowUpdateInterval 节流 + dirty 立即重建；点击目标为运行时覆盖
    （SetFlowTargetOverride，不触碰配置资产）；OnEnable/OnDisable 配对；
    分配签名重建守卫；ShaderSet fail-fast；frustum 无分配缓存
  - Stage7ClickFlowTargetSetter 改为写运行时覆盖
  - _Requirements: 1.3, 4.1, 4.2, 4.3_

- [x] 7. 可观测性
  - Stage7BattleTelemetry（AsyncGPUReadback 存活数/战斗时长/流场重建计数）+
    Stage7BattleTelemetryHUD_Stage7
  - Stage7FlowFieldPreviewHUD_Stage7（预览纹理展示；kernel 写入由
    runtimeFlowPreviewEnabled 门控）
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 8. 测试重写
  - EditMode：数据布局/字段预算（无后缀过滤）/生成包含性/三兵种参数通道/运行时改参生效/
    钳制与默认值/registry 守卫/状态模型镜像/派发顺序（dispatch 钩子）/双缓冲交换
  - PlayMode：真实 GPU 派发 + 回读的黄金值测试（伤害量化累积、死亡、状态迁移合法性、
    未开战冻结）；无 compute 能力环境自动跳过
  - _Requirements: 7.2, 7.3, 7.4, 7.5, 9.1, 9.3, 10.2, 10.4_

- [x] 9. 卫生
  - 删除零引用死文件：AgentComputeShader_Stage6.compute（1230 行）、4 个未引用 shader
    副本、14 个 "New * Config" 孤儿资产、空 Stage7Configs.cs、过期 TestResults xml
  - Stage7SampleAssetCreator 改为非破坏式（仅填充新建资产、场景另存、含地面/点击/遥测）

- [x] 14. LOD 降频模拟（2026-07-26，用户指定优先；billboard 方案按用户决定放弃）
  - 近/中/远决策频率 1/2/4（LodConfig 可调），轻路径每帧保伤害结算/位置积分/写回
  - dt 补偿 + 累积制冷却保证 DPS/速度不变；64 线程组对齐错峰
  - 新增 PlayMode 黄金测试：全帧率 vs 1/4 帧率击杀时刻一致（±20 帧）

- [x] 15. 场景意图化与物理自洽（2026-07-27，源于 200k v 200k 卡死事故）
  - SpawnConfig 意图化：只摆 center+人数+密度/宽深比，脚印自动推导；spawnSize 变手动覆盖
  - ScenarioPhysics 物理账本：密度/越界/格子溢出/流场覆盖，警告带具体建议数值
  - 编辑器 MassEngine/Auto-Fit Scenario 一键配平 world/grid/flow（Undo + SetDirty）
  - LodConfig.maxRenderDistance 渲染能见度上限（0=不限），封顶最坏可见实例数
  - 现有 200k 场景已切换到自动脚印（spawnSize 清零）

- [x] 16. 寻敌迂回修复 + 观测补盲（2026-07-27 夜间）
  - 根因：流场格 2m→5m 后密度阈值（按"每格人数"标定）失配，压力常年饱和，
    避让力把冲锋单位从敌人面前推开（"走到跟前又迂回"）
  - 修复：密度阈值语义改为每平方米（densityComfortPerSqm 0.6 / densityPressureRangePerSqm 1.2，
    采样值÷格面积），与格子尺寸永久解耦；接战状态避让衰减 ×0.35（冲锋意志压过怕挤）
  - SampleAheadDensity 重构消除 d3d11 编译警告；LOD 半径重标 near30/mid120（中层人口随
    半径平方增长，300m 圈住 14 万中模单位是渲染炸弹）、maxRenderDistance 500
  - 空间哈希格满溢出遥测：spatialHashStats 缓冲 + 异步回读 + HUD 红字告警
    （溢出者静默掉出邻域查询，40 万规模的必备仪表）

- [x] 17. 夜间自主缺陷挖掘与修复（2026-07-27，用户授权的自主任务）
  - 5 维度挖掘 43 条 → 证伪存活 39 条 → 全部 critical/major + 大部分 minor 已修复
  - 战斗行为：动态流场 Z 收敛（消灭"质心线罚站"）、残局全局质心兜底（仗一定能打完）、
    停止半径下限 0.75×流场格（消灭目标线抖动带）、避让只偏转不反向（援军不再临阵脱逃）、
    HOLD 守方接战迟滞（消灭系统性单边换血）
  - GPU 契约：冷却每通道多次结算（DPS 不随镜头变化）、寻敌节奏改决策计数（任意降频
    区间对齐）、分离节流门删除（纯陷阱零收益）、FLOW_FIELD 守方追击链删除（曾致永久
    无法索敌）、flowFieldWeight 0.2 暗底删除
  - 健壮性：重建签名改从 scenarioConfig 派生（空场景可恢复）、shader 缺失不再改写
    序列化开关、battleStarted Inspector 翻转接入记账、非法 teamId 有声拒绝
  - 账本扩展：流场覆盖 Z 轴、导航源短路、按兵种半径的堆积极限、LOD 交叉校验
  - 新增测试：EditMode +5（账本各项+集成脚印+出货场景锁定+门控断言）、
    PlayMode +4（渲染桶计数/幽灵清场/行军速度一致性/暂停冻结）
  - 遗留未做（登记）：SelectFlowTargets 并行化（用户已降优先级）、PlayMode 队伍交错
    fixture（TG-05）、密度每平米 GPU 金值测试（TG-01）、≥128 人跨组测试（TG-06）
    ——以上四项已于 2026-07-27 白天批（item 18）全部完成

- [x] 18. 白天查漏补缺批（2026-07-27，用户授权自主任务；前四笔提交后进行）
  - SelectRuntime*FlowTargets 并行化：每扇区 64 线程组 groupshared 归约，
    残局兜底移入 Generate（需跨扇区视野）；派发组数 = clamp(sectorCount,1,8)
  - 新 PlayMode 金值测试 ×3：DynamicSectorSelectionSteersFlowAtEnemyCluster
    （扇区路径+兜底路径+stats 契约）、DensityMapCountsAliveAgentsPerCell（TG-01，
    含死亡剔除）、InterleavedTeamsAcrossThreadGroupsFightAndClassifyCorrectly
    （TG-05+TG-06：256 人交错队伍跨 4 线程组，战损双向+分类计数）
  - fixture 参数化：BuildScenario(attackers, defenders) + 动态流场/密度阈值旋钮
  - CameraControls 清理（原 item 13 待办）：SceneViewCamera 四件套去 _Stage7 后缀、
    MyCameraManager_Stage7→RigCameraManager、LocalRotationAndScale_Stage7→
    CameraMouseOrbit（guid 全保持；场景在用的 MyCameraManager/LocalRotationAndScale
    不动）

- [x] 19. 第二轮缺陷挖掘消化（2026-07-27 白天，5 个夜间未覆盖维度：生命周期/渲染/
  API/编辑器/文档漂移；20 条发现证伪 1 条，存活 19 条全部处置）
  - 生命周期：shader 缺失阻断改为可自恢复（Update 每秒廉价重探测 + 阻断期跳过全量
    缓冲分配）；GPU device reset 看门狗（spatialHashStats[3] 哨兵 + 遥测回读校验 +
    自动重建）；Initialize 签名提前落盘（异常不再逐帧重试风暴）+ 网格索引容量 long
    守卫；AllocationSignature 增加 scenarioConfigId + teamLayoutHash（资产替换/
    teamId 热改经签名路径触发完整重建）
  - 渲染：近景层豁免视锥/距离剔除（唯一投影层，屏幕边缘阴影不再消失）；两个 agent
    shader 补 DepthOnly + DepthNormals pass（SSAO/DoF/深度效果不再穿透单位）；
    LodConfig.farIncludeDead=true（120m 尸体消失线可关）；DrawLod 缺槽位一次性告警
  - API：UnitTypeRegistry.Register 改 internal（游戏层误用即静默停摆的陷阱面）+
    FillGpuSettings 失配一次性 LogError；IMovementModule 补 ClearTarget + 语义
    XML 文档 + 同队被遮蔽目标一次性告警
  - 编辑器：示例创建器出生点按脚印推导 + 新建资产走账本配平 + 场景覆盖确认对话框；
    BattleTelemetryHUD 改 Repaint-only + 4Hz 缓存重建（消除稳态每帧 GC 分配）；
    ScenarioGizmos 目标球按运行时规则渲染（主开关关/被遮蔽 → 灰色 + ignored 标注）
  - 文档：Simulation/README 决策流去 chase 残留 + 寻敌决策计数 + 巡航闭式解；
    design.md Select 并行化与巡航复合修正；tasks.md 场景/类名标识符更新

## 待办（需要在 Unity 编辑器内完成）

- [ ] 10. 性能基线（Requirement 9.4）
  - 用户目标：200000v200000（40 万）为基础规模；阶梯 25k→50k→100k→200k/边
  - 10000v10000 记录帧时间，与 Stage6 同规模对照
  - 已知偏差项见 design.md"已知偏差"；节流参数 dynamicFlowUpdateInterval 是显式旋钮
  - 打开动态流场后，用固定种子录像对比战场行为（寻敌半径实际扩大、hold-position 防守方
    开始互相推开、动画回绕周期变化都属于预期变更）

- [ ] 11. 场景核对
  - Game/Scenes/WarSandbox.unity 在编辑器中 Play 验证：攻方沿流场推进、接战、遥测 HUD 数据合理
  - 按需将 BattleTelemetryHUD / FlowFieldPreviewHUD 挂到场景 manager 上

- [x] 12. 目录全量搬迁（2026-07-26 完成）
  - 引擎 → Assets/MassEngine（8 模块 + Tests，每模块 README）；游戏层 → Assets/Game
  - Stage1-7 历史目录归档至仓库根 ArchivedStages/（归档区零反向引用，已审计）
  - 近景 shader 迁入 VatRender（GUID 保持，材质引用未断）；命名空间 → MassEngine

- [ ] 13. 未来扩展（不阻塞当前版本）
  - N 队 N 流场（数组化 flowFieldDirections 并按队伍派发）
  - ~~SelectRuntimeFlowTargets 并行化~~（2026-07-27 完成，见 item 18）
  - ~~CameraControls 改名清理~~（2026-07-27 完成，见 item 18）

## Notes

- GPU shader 与 Stage6 已实质分叉（新增 DensityMap 阶段、per-unit-type settings、
  hp 双缓冲、状态推导函数等），"与 Stage6 逐字一致"不再是本阶段的约束；9.4 的对比需
  在文档标注偏差项。
- 所有配置资产为只读输入；发现运行时写回配置资产的代码一律视为缺陷。
