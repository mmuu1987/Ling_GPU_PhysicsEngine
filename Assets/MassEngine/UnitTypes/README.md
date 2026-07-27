# UnitTypes — 兵种组合层

"新增兵种零改管线"的实现层。一个兵种 = 一组模块 + 一组配置资产。

## 模块契约（架构决策 A）

逐帧仿真在 GPU 上，所以模块**不是**逐 Agent 行为对象，而是
**GPU 参数贡献者**（`IUnitParameterContributor.Contribute(ref UnitTypeGpuSettings)`）：

| 模块 | 贡献 |
|---|---|
| `ISpawnModule`（唯一 CPU 行为模块） | 初始摆放（一次性） |
| `IMovementModule` | maxSpeed/阻尼/流场权重与响应 + 持有本兵种流场目标声明 |
| `IFlockingModule` | 聚散全套参数 |
| `ICombatModule` | 寻敌/攻击参数 + MaxHp |
| `IAnimationModule` | 移动动画速度区间 |

`UnitTypeBase.BuildGpuSettings()`：默认值 → 各模块 Contribute →
VAT 片段时长合并（来自 RenderRuntime）。**每帧调用**，所以运行时改配置下一帧生效。

## 关键类

- `IUnitType` / `UnitTypeBase` / `DefaultSwordUnit`：兵种实体；
  子类在 `CreateModules()` 替换模块实现
- `UnitTypeRegistry`：注册（含校验拦截）、buffer offset 与 unitTypeIndex 分配、
  settings 数组聚合、队伍配置目标解析
- `UnitTypeConfig`（顶层）+ Spawn/…/Render 子配置 + `ScenarioConfig`（场景兵种清单）
- `ConfigValidator`：**纯校验**（不写资产）。Error（Spawn 缺失/unitCount≤0/
  teamId∉{0,1}/类名非法）→ 跳过注册并报错；Warning → 用内建默认值

## 生成区：意图化（2026-07 起）

设计师只写**意图**：`spawnCenter`（摆哪）+ `unitCount`（多少人）+
`formationDensity`（默认 0.5 人/m²，行军密度）+ `formationAspect`（阵面宽:纵深，默认 2）。
实际脚印由 `SpawnConfig.ResolveSpawnSize()` 推导——面积恒等于 人数÷密度，
物理上永远自洽；Gizmo 画的就是推导后的真实脚印。
`spawnSize` 保留为手动覆盖（两分量都 >0 才生效），用于卡口、楔形阵等故意的形状；
覆盖密度超过堆积极限（1.5/m²）时物理账本会点名警告。

## 新增兵种步骤

1. 建 `UnitTypeConfig` + 子配置资产（Create 菜单：MassEngine/…）
2. 需要自定义行为参数逻辑时：继承 `UnitTypeBase`，把类全名填进
   `unitTypeClassName`（默认 `MassEngine.DefaultSwordUnit`）
3. 加进 `ScenarioConfig.unitTypes` —— 完成，核心零改动

## 如何验证

EditMode：三兵种参数各自到达 GPU、offset/unitTypeIndex 正确、
自定义子类经类名实例化、非法 teamId 被拒、运行时改参下一帧生效。
