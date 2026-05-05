# Stage5 双阵营攻守战斗方案优化与实施计划

## Summary
- 需要先优化 `mass_gpu_engine_roadmap.md` 的第五阶段。当前文档把“阵营、状态机、攻击、掉血、死亡”压在一层里，过于粗，且默认把 `teamID/HP` 直接塞进 `AgentData`，这会把渲染共享数据契约一起放大，风险偏高。
- 第五阶段首版建议改成“**双阵营攻守 MVP** + **4 状态状态机**”：攻击方沿现有单张 painted flow field 推进，防守方以守点/短追击为主，先把双阵营、寻敌、停步互砍、扣血、死亡、动画切换完整闭环跑通。
- 现有工程里没有接好的死亡 VAT，但仓库里已经有可复用死亡动画源，首版把死亡动画资产制作纳入 Stage5，而不是继续推迟。

## Key Changes
### 1. 路线图细化
- 将第五阶段改写为 5A~5E：
  - `5A` 双阵营出生与战斗数据契约
  - `5B` 邻域寻敌与 4 状态状态机
  - `5C` GPU 伤害累积与死亡结算
  - `5D` VAT 动画状态映射
  - `5E` 攻守验证场景与调参
- 明确延期项：
  - 不在首版做“每阵营独立多张 flow field”
  - 不做受击僵直、撤退、技能、远程攻击
  - 不做通用 N 阵营框架，先固定 2 阵营

### 2. 数据与接口
- 保持当前渲染共享 `AgentData` 尽量精简，继续以 `currentState` 作为渲染可见状态。
- 不把全部战斗字段都塞进 `AgentData`；新增 compute-only buffer：
  - `teamIdBuffer`
  - `hpBuffer`
  - `targetAgentIndexBuffer`
  - `attackCooldownBuffer`
  - `homePositionBuffer`
  - `pendingDamageBuffer`
- `currentState` 明确枚举为：
  - `0 Idle`
  - `1 Move`
  - `2 Engage`
  - `3 Attack`
  - `4 Dead`
- `GPUInstancingManager_Stage5` 新增 Inspector 配置：
  - `attackerCount`
  - `attackerSpawnCenter/Size`
  - `defenderSpawnCenter/Size`
  - `targetAcquireRadius`
  - `attackRange`
  - `attackDamage`
  - `attackInterval`
  - `maxHp`
  - `defenderGuardRadius`
  - `defenderAggroRadius`
  - `defenderMaxChaseDistance`
  - `deathClipDuration`
- `instanceCount` 继续保留为总人数；`defenderCount = instanceCount - attackerCount`。

### 3. 双阵营攻守行为
- 出生逻辑改为两块独立出生区：
  - 前 `attackerCount` 个 agent 为攻击方
  - 其余为防守方
- 攻击方默认行为：
  - 无目标时沿现有 `PaintedFlowFieldAsset_Stage5` 推进
  - 有目标但未进攻击距离时进入 `Engage`
  - 进攻击距离后停步互砍
- 防守方默认行为：
  - 无目标时停留或回到 `homePosition`
  - 敌人进入 `defenderAggroRadius` 后才离岗接敌
  - 若追击超过 `defenderMaxChaseDistance` 或丢失目标，则回防
- 寻敌规则：
  - 基于现有 spatial hash 的 3x3 邻域查询
  - 忽略同阵营与 `Dead`
  - 选择最近敌人为当前 target

### 4. GPU 调度与战斗结算
- 将现有 `ClearGrid -> BuildSpatialHash -> SimulateAndClassify` 扩展为：
  - `ClearGrid`
  - `BuildSpatialHash`
  - `ClearPendingDamage`
  - `EvaluateStateAndAccumulateDamage`
  - `ResolveDamageSimulateAndClassify`
- `EvaluateStateAndAccumulateDamage` 负责：
  - 选 target
  - 决定 `Move/Engage/Attack/Dead`
  - 倒计时 `attackCooldown`
  - 到攻击点时通过 `InterlockedAdd(pendingDamageBuffer[target], attackDamage)` 累积伤害
- `ResolveDamageSimulateAndClassify` 负责：
  - 应用本帧累计伤害
  - HP 归零后切 `Dead`，清空速度，退出后续寻敌/攻击/碰撞推进
  - 按状态分别做 flow field 推进、追敌、停步朝向、碰撞、LOD 分类、动画时间推进
- 活体参与碰撞与寻敌；死亡体不再参与这两类逻辑。

### 5. 动画与 VAT
- 第五阶段首版使用 3 组战斗动画资源：
  - 移动：现有 Run/MoveFWD 资源
  - 攻击：现有 `attack` 或 `Attack01_SwordAndShield`
  - 死亡：`Die01_Stay_SwordAndShield.fbx`
- 用现有 VAT Baker 为 Move、Attack、Death 分别烘焙 Stage5 专用 VAT 资产。
- 近景/中景 VAT shader 增加按 `currentState` 选 clip 的能力：
  - `Move`、`Engage` 使用 Move clip
  - `Attack` 使用 Attack clip
  - `Dead` 使用 Death clip
- 远景 billboard 不做完整死亡 VAT；首版策略为：
  - 活体继续 billboard
  - 死亡体不再进入 far LOD 可见列表
- `Dead` 状态的死亡动画只播放一次，结束后保持死亡定格。

## Test Plan
- 文档验证：
  - `mass_gpu_engine_roadmap.md` 的第五阶段被拆成可执行子阶段，且明确“攻守 MVP”而不是泛化多阵营。
- 数据/出生验证：
  - 攻击方与防守方人数正确
  - 两个出生区正确分离
  - 防守方初始停留在守区
- 战斗验证：
  - 攻击方会沿 flow field 推进
  - 防守方只有在敌人进入 aggro 半径后才接敌
  - 两方接触后能稳定停步互砍，不穿模堆叠失控
  - 多个攻击者围攻同一目标时，伤害按累计正确结算
- 死亡验证：
  - HP 归零后切 `Dead`
  - 死亡动画只播一次
  - 死亡体不再参与寻敌、攻击、碰撞
  - 活体不会继续锁定已死目标
- 回归验证：
  - 现有 Stage5 的 flow field、spatial hash、LOD、VAT 渲染链路不回退
  - 单阵营旧用法在不启用双阵营参数时仍可运行

## Assumptions
- 已锁定首版为“攻守模式”，不是双流场独立目标，也不是泛化 N 阵营。
- 已锁定首版渲染状态机为 5 状态：`Idle / Move / Engage / Attack / Dead`。
- 攻击方继续使用当前单张 `PaintedFlowFieldAsset_Stage5`；防守方首版不新增第二张 flow field，而是依赖守点与短追击逻辑。
- 死亡动画资源使用仓库内现成素材作为来源并重新烘焙 VAT；如果烘焙结果不稳定，再退回“死亡后静态定格”作为兜底，但这不作为首选方案。
