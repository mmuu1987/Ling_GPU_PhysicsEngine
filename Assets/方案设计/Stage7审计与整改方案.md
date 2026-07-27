# Stage7 系统性审计与整改方案

> **执行状态（2026-07-26）：全量整改已完成。** WS1-WS6 + 速赢清单全部落地（方案 A：模块 = GPU 参数贡献者）。
> Unity 6000.3.14f1 批处理验证：EditMode 17 项 + GPU 黄金值测试 3 项全部通过。
> 交付后又经 5 维度多智能体对抗性评审（13 条发现 → 证伪后存活 11 条），11 条已全部修复
> （含 major 一条：流场目标清除后方向场不清零导致"幽灵目标"行军）。
> 剩余需在编辑器内人工完成的事项见 `.kiro/specs/gpu-unit-oop-refactor/tasks.md` 的"待办"节
> （性能基线 10000v10000、场景 Play 目检）。
>
> **第二轮（同日）：目录全量搬迁完成。** 应用户"引擎式功能堆叠+组件文档"的要求：
> 引擎迁至 `Assets/MassEngine/`（Core/Spatial/FlowField/Crowd/Simulation/VatRender/
> UnitTypes/Diagnostics 每模块配 README），游戏层迁至 `Assets/Game/`，
> Stage1-7 六代历史目录整体归档 `ArchivedStages/`（双向引用审计零悬挂）。
> 架构总览见 `Assets/MassEngine/README.md`。

> 审计方式：6 维度并行审计（C# 架构 / GPU 管线 / HLSL / 测试有效性 / 资产场景 / 逐条需求符合度），
> 每条发现经独立对抗性证伪。共提出 82 条，存活 59 条（3 critical / 27 major / 29 minor），收敛为 5 个根因。
> 审计基准：`.kiro/specs/gpu-unit-oop-refactor/`（requirements.md / design.md / tasks.md）。
> 日期：2026-07-26。

## 一、总体结论

这个项目在"形式"上高度系统化，在"契约"上几乎没有系统性。目录分层、ScriptableObject 配置树、spec 三件套、属性测试、Gizmo 与 Editor 工具一应俱全，命名、分层、注释风格也相当一致——它看起来比绝大多数同类工程更"有章法"。但每一层与相邻层之间的连接都是断的：

- IUnitType 的 5 个扩展点只有 DefaultSpawnModule 通电，逐帧行为 100% 由 4 个 compute shader 决定；
- ScriptableObject 既是输入又被运行时反写（ApplyVatProfileToConfig、Stage7ClickFlowTargetSetter、ConfigValidator.EnsureRuntimeDefaults）；
- 14 条 Correctness Property 有 11 条测的是永不执行的 CPU 影子实现，tasks.md 却据此把 9.2/9.3/10.2/11.1/12.3/14.1 打了勾；
- 最刺眼的证据：随包 Stage7_Test.unity 按下 Play 后攻方根本不推进（ShouldRebuildAttackerFlow 恒为 false，flowFieldDirectionsBuffer 从未被写入），而全部测试依旧全绿。

所以"缺乏系统性"的直觉是准确的，但准确的说法不是"代码乱"——代码相当整齐——而是**"规格、测试、C# 架构三者描述的是同一个系统，而真正运行的是第四个系统（HLSL），且没有任何一条契约把它们绑在一起"**。好消息是缺陷高度聚集：59 条发现收敛到 5 个根因，其中 4 个是接线与边界问题，可以在不重写 GPU 管线的前提下逐步修复。

## 二、三条 critical

| # | 发现 | 位置 | 后果 |
|---|------|------|------|
| 1 | 出货场景永不生成流场 | MassGpuSystemManager_Stage7.cs:456-466 门控恒 false | Play 后攻方原地不动，Requirement 4.2/4.3 全程未兑现 |
| 2 | flowFieldDirections / densityMap 分配后从未初始化却每帧被读 | MassGpuBufferManager_Stage7 分配路径 | 读未定义显存；行为依赖驱动巧合 |
| 3 | 14 条属性测试中 11 条验证的是运行时永不执行的 CPU 影子层 | Stage7PropertyTests.cs | 删掉全部战斗/LOD shader 测试仍全绿；绿灯不代表任何事实 |

## 三、五个根因

### 根因 1：抽象边界画在了 CPU/GPU 的错误一侧（设计文档本身不自洽）

design.md:202-380 以完整签名规定了 MovementModule/FlockingModule/AnimationModule/CombatModule/FlowFieldVisualizer 这一套 CPU 行为模块，实现忠实照做——于是产生约 390 行永不被调用的影子逻辑（UnitTypeBase.cs:38-45 构造它们，除 SpawnModule 外无任何运行时消费者）。真实行为在 HLSL，模块只能改变生成位置。这不是编码偏离设计，而是设计本身与 GPU-driven 架构不自洽：模块边界被定义为"仿真边界"，而仿真根本不在 CPU 上发生。IFlowFieldVisualizer 是最极端样本——零引用死代码，真实可视化被硬编码进 kernel 与编辑器 Gizmo，Requirement 3.2/3.3 在 Player 构建中不成立。

### 根因 2：GPU 侧身份与参数通道被硬编码为两阵营，且并存两套互相矛盾的阵营真理

所有兵种参数以 Attacker*/Defender* 标量对上传（ComputePipelineOrchestrator.cs:103-170），AgentData 上没有 unitTypeIndex，PipelineFrameContext 只有两个 UnitTypeGpuSettings 槽位；渲染侧同样只有 attacker/defender 各 3 份可见索引与 DrawArgs。更糟的是阵营判定有两套：战斗/LOD/渲染读 `teamIdReadBuffer==1`，而 RuntimeFlow 的两个密度核用 `[0, attackerCount)` 索引区间推断阵营，bufferOffset 却只按 ScenarioConfig.unitTypes 数组顺序累加、无排序无断言。当前出货配置恰好是 [attacker(0), defender(1)]，所以今天行为正确——但 Requirement 1.5 承诺的"只加子类+资产"一旦执行，立即同时触发参数被静默覆盖、重复绘制、indexCount 错配、流场指向己方，且全程无任何日志。

### 根因 3：配置数据流被反向污染，同时大量旋钮没有消费者或语义错配

ScriptableObject 从"只读输入"退化成"运行时可写状态"：ApplyVatProfileToConfig 每次 Initialize 覆写 RenderConfig/AnimationConfig（防守方 farMesh 的 SuperLowLOD 作者选择每次被替换为 profile 的 LowLOD）；Stage7ClickFlowTargetSetter 把点击目标永久写进 MovementConfig；ConfigValidator 还会往共享资产里塞运行时实例。反方向是一批 Inspector 可见却永远到不了 GPU 的旋钮：attractionStrength（Requirement 5.3/5.4 实际未实现）、MovementConfig.flowFieldResponsiveness/flowFieldWeight（4.4 只兑现 maxSpeed）、AnimationConfig 三个 interval、defender 的 separationSkipInterval；再加上语义接错的 animationDuration = deathClipDuration（idle 141 帧被截断在 44 帧）和被 `#define` 静默截断到 2 格的 targetAcquireRadius（配置 8m 实际轴向约 6m）。设计师调参会遇到三种结果：生效、无效、被静默改回。

### 根因 4：帧门控与资源生命周期把正交概念耦合，丢掉了 Stage6 已有的工程护栏

ShouldRebuildAttackerFlow 把"是否启用动态流场 / 重建频率 / 是否配置静态目标"三件正交的事塞进一个布尔表达式，结果出货配置下动态流场一次都不跑，一旦点击又变成无节流的每帧重建（Stage6 有 dynamicFlowUpdateInterval，Stage7 删了）；densityMap 的 Clear/Build 寄生在同一个开关上。生命周期成对失衡：OnDisable 释放一切但没有 OnEnable；编辑模式下 Object.Destroy(RenderTexture) 抛异常中断整个 ReleaseAll；重建守卫只比较 agent 数量。Stage6 已解决的问题被退回：每帧 6 次未缓存反射读 VAT profile、每帧 frustum 数组堆分配、AsyncGPUReadback 战斗遥测整体缺失——最后一条正是这些问题长期不被发现的直接原因。

### 根因 5：验证与文档构成自我确认闭环

属性测试全部指向 CPU 影子层；其中还有结构上无法失败的断言（Property 2 的后缀过滤跳过了 57 字段的 PipelineFrameContext）、恒真算术（Property 11 的 4×7=28，从未推进冷却）、从未触发的分支（Property 10 没有 hp≤0 候选）、以及断言运行时每天都在发生的转换为"非法"（Idle→Attack / Move→Idle / Attack→Engage）。规格侧同样失真：requirements.md:135、design.md:548-555、AgentStateMachine.cs:10-15 三张状态表互相矛盾且都管不到真正跑的 SetCrowdState；tasks.md 声称 shader 从 Stage6 逐字复制，实际已分叉约 1010 行并新增 2 个 kernel。GPU 侧零覆盖的直接后果：hold-position 防守方跳过 separation、hp 缺读快照的 dispatch 内竞态等真实缺陷无人发现。

## 四、整改工作流（按依赖排序）

### WS1｜让出厂场景真正跑起来 + 最小可观测性（M）

必须最先做：后面每条工作流的验收都依赖"能看到行为变化"。

1. 拆开 MassGpuSystemManager_Stage7.cs:456-466 三重耦合门控：`needFlow = flowFieldEnabled && (HasConfiguredFlowTarget || runtimeDynamic*FlowEnabled)`；重建频率单独由 rebuildRuntimeFlowEveryFrame 与新增 dynamicFlowUpdateInterval（回补 RuntimeFlowConfig，默认 0.25-0.5s，参照 Stage6）控制。
2. rebuildDensityMap 与流场重建解耦：有存活 agent 就每帧 Clear+Build；跳过时把 densityAvoidanceStrength/densitySpeedPenalty 上传为 0，并处理 shader 里直接消费 centerPressure/aheadPressure 的两处。
3. Allocate 后一次性清零 flowFieldDirections / defenderFlowFieldDirections / densityMap，消除"读未写显存"。
4. Initialize 末尾预热派发一次流场，保证第一帧就有有效方向。
5. 移植 Stage6 的 AsyncGPUReadback 遥测 → Stage7BattleTelemetry：双方存活数、战斗时长、流场重建次数、平均速度。
6. Initialize fail-fast 校验 ShaderSet.IsValid；缺 kernel 日志加一次性标志。
7. 记录 10000v10000 帧时间基线（含 Stage6 对照），作为 Requirement 9.4 的比较基准。

风险：修好门控后帧时间必然上升（现在的"快"部分来自流场从不重建），SelectRuntimeFlowTargets 是 numthreads(1,1,1) 串行 kernel，必须与节流同批上线。

### WS2｜配置资产只读化（M）

1. 新增 ResolvedUnitTypeRuntime：Initialize 阶段把 VAT profile 解析成运行时结构（mesh/纹理配对 + 预填 MaterialPropertyBlock），删除对 RenderConfig/AnimationConfig 的全部运行时写入（mesh 与 VAT 纹理的配对逻辑保留，落在运行时结构上）。
2. RenderDispatcher 消费缓存，移除渲染路径每帧约 216 次反射；提供 ContextMenu 手动刷新。
3. ClickFlowTargetSetter 改写 manager 上的 per-team 运行时覆盖，不再触碰 MovementConfig 资产；Reset 时清空。
4. ConfigValidator 拆成纯校验；缺 SpawnConfig 升级为 Error 并跳过，杜绝静默按 50000 分配缓冲。
5. 死参数裁决：AnimationConfig 三个 interval 删除（LodConfig 已取代）；attractionStrength、flowFieldResponsiveness/flowFieldWeight 要么删+改 spec，要么登记进 WS4 通道。
6. Stage7SampleAssetCreator 改为非破坏式。

### WS3｜架构决策：模块层去留 —— 推荐方案 A（L）

**方案 A（推荐）**：删除 CPU 影子仿真层，把模块边界重新定义为"GPU 参数与资源的作者边界"。IMovementModule 等接口的方法从 ComputeDesiredVelocity/FindNearestEnemy 之类改为 `Contribute(ref UnitTypeGpuSettings)` / `BindResources(sink)`，由 UnitTypeRegistry 在构建 per-unit-type 参数时调用（正好落在现有空钩子 OnBuffersBound 上）。保留 DefaultSpawnModule 与 profile→clip 解析。

推荐理由：(1) 50k+ agent 的 GPU-driven 渲染器，逐 agent CPU 逻辑要么每帧回读（与 9.2/9.3 冲突且引入 2-3 帧延迟），要么每秒数百万次虚调用，不可行；(2) A 恰好兑现 1.2/1.5 的字面要求：新增兵种 = 参数贡献者 + SO，管线只遍历注册表；(3) 影子层除测试外零引用，删除成本极低。

**方案 B（不推荐）**：CPU 模块作为可执行规范驱动 HLSL parity 测试。代价是维护 C#→HLSL 一致性矩阵，而原子顺序/分帧寻敌/格半径截断会让 parity 断言要么脆弱要么放宽到无意义。B 的真正价值可用便宜得多的 PlayMode 回读黄金值测试获得（WS6）。

附带：AgentStateMachine 二选一——改成与 HLSL 对齐的规格表由 WS5 落实，或删除；同步改写 design.md 的模块签名、状态表、调度图（补 density 阶段）。

### WS4｜按兵种的 GPU 参数通道（XL，分两步交付）

1. 新增 unitTypeIndexBuffer（旁路缓冲，刻意不进 AgentData，保住 56 字节步幅）。
2. UnitTypeGpuSettings 补齐缺失项后改为 `StructuredBuffer<UnitTypeGpuSettings>` 上传；删除约 40 个 Attacker*/Defender* 标量 uniform。
3. HLSL 的 UsesDefenderSettings/Get* 系列改为 `settings[unitTypeIndex[index]]`；RuntimeFlow 密度核改 `teamIdReadBuffer != targetTeamId` 早退，彻底删除 attackerCount 区间假设。
4. 可见索引与 DrawArgs 改"按兵种 × LOD"动态分配；渲染派发遍历 registry 而非 FindFirstConfigForTeam。
5. PipelineFrameContext 瘦身；删除 FindFirstConfigForTeam 及 registeredTypes[0] 兜底。
6. EditMode 测试：3 兵种（team0 两个 + team1 一个）参数各自到达 GPU。

先做参数通道（不动渲染分桶）验证 9.4，再做分桶。注意 Mobile RP 下 StructuredBuffer 兼容性。

### WS5｜GPU 行为语义归一（M）

1. animationDuration 不再取 deathClipDuration，改由 VAT profile totalFrameCount/frameRate 派生（WS4 后升级为 per-clip）。
2. LOCAL_TARGET_SEARCH_CELL_RADIUS 从 #define 改为 C# 下发 uniform，由 targetAcquireRadius/cellSize 推导，超限 Warning。
3. hold-position 防守方保留 separation，位移 clamp 在 guardRadius 内。
4. HLSL 状态收敛到单一 ResolveState；统一三张互相矛盾的状态表为 GPU 实际语义（含 any→Dead、Engage→Idle）。
5. hp 增加读快照缓冲消除 dispatch 内竞态，或在 design.md 记录为可接受近似。
6. 每步用 WS1 遥测 + 固定种子对比，防手感被静默改变。

### WS6｜把绿灯变成信号（M）

1. 随 WS3 删除 Property 4-14 的影子层测试；保留 Property 1（56 字节）与 Property 3（生成区域）。
2. 新增 PlayMode GPU 回读测试（16-64 agent）：真实派发战斗 kernel，断言 N 周期伤害 = N×attackDamage、死者/同阵营不入寻敌、HP≤0→Dead、状态转换合法、零流场无位移。
3. 新增架构不变量测试：多兵种参数到达 GPU、dispatch 顺序（可注入 recorder）、compute-only 缓冲分离。
4. Property 2 反射扫描去掉后缀过滤，覆盖 PipelineFrameContext/UnitTypeGpuSettings。
5. 修正 tasks.md 与事实不符的 [x]（11.1/12.3/9.2/9.3/10.2/10.3/14.1），Notes 的"shader 未修改"改写为事实（已分叉约 1010 行 + 2 个新 kernel）。

## 五、速赢清单（安全、小、立即有价值）

1. 删除零引用的 AgentComputeShader_Stage6.compute（1230 行/14 kernel，仍在参与编译）及 AgentDataCommon 中只被它调用的 FindNearestEnemy / AccumulateSeparation。
2. 删除与 Stage6 逐字相同且零引用的 4 个 shader 副本（LitInstanced/Instanced/Billboard/PaintedFlowFieldPainter）；在 design.md 记录近景材质对 Stage6 shader guid 的真实依赖。
3. 删除 runtimeFlowStats[1]/[2] 的死原子操作（无读者，Stage6 已删）。
4. ReleaseRenderTexture 补 isPlaying ? Destroy : DestroyImmediate；combatBuffers.ReleaseAll 提到 RT 释放之前。
5. 补 OnEnable 与 OnDisable 配对，修"禁用再启用后静默停摆"。
6. BuildFrustumPlanes 改无分配重载 + 字段缓存。
7. Initialize 检查 ShaderSet.IsValid（该属性至今零调用）。
8. RegisterFromScenario 加临时守卫：同队第二个 UnitTypeConfig 或 teamId∉{0,1} 时 LogError 拒绝——WS4 之前把"静默错误"变成"明确不支持"。
9. 重建守卫从 agent 数量扩展为分配签名（+gridCellCount+maxAgentsPerCell+flowFieldResolution）。
10. 14 个孤儿资产（Setting/New * Config.asset 系列，零引用且带误导性数值）移入 _Unused/ 或删除。

## 六、主要风险

- **性能方向反转**：现在的"快"部分来自缺陷（流场从不重建）。WS1 修好后帧时间必然上升，必须先建基线，让节流参数成为显式旋钮，否则会被误判为"重构失败"。
- **SelectRuntimeFlowTargets 串行瓶颈**：numthreads(1,1,1) 扫 16384 cell，进入每帧路径后立即成为主瓶颈。
- **WS4 触碰 56 字节承诺**：必须用旁路 unitTypeIndexBuffer，不得塞进 AgentData（会连带打破 VAT 顶点解包）。
- **WS5 改变战场手感**：寻敌半径实际扩大 1.5-2 倍、防守方开始互相推开、动画周期变化。需固定种子对比录像。
- **测试数量表面下降**：删影子层后可见测试从 16 条掉到 3-4 条，PlayMode GPU 测试无头 CI 跑不了；须与 WS6 同批交付避免误读。
- **Stage7 shader 已与 Stage6 分叉约 1010 行**："对照 Stage6 修"的直觉是危险的；9.4 的对比需在文档中列明已知偏差项。
- **工作区有大量未提交改动**：开工前应把与整改无关的改动分离，否则每个工作流的 diff 都混入噪声。
