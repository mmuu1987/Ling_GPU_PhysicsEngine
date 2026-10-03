# VatRender — VAT 动画、LOD 分类与间接绘制

把 GPU 内存里的 50k Agent 变成屏幕上的动画士兵：
VAT（顶点动画纹理）播放 + 三级 LOD + DrawMeshInstancedIndirect。

## 组件

| 文件 | 职责 |
|---|---|
| `VATProfile.cs` | VAT 烘焙产物资产：三级 LOD 的 mesh/位置纹理/法线纹理 + 四个片段（idle/move/attack/death）的帧数据。烘焙工具见下 |
| `VatProfileReader.cs` | 初始化时一次性（反射，兼容任意烘焙器的 profile 形状）读入纯数据结构 |
| `ResolvedUnitTypeRuntime.cs` | 解析结果：每 LOD mesh/材质/阴影 + 各片段时长 + **预填 MaterialPropertyBlock**。强制 mesh 与其 VAT 纹理配对（配错采样必花），冲突时警告并以 profile 为准——**绝不写回配置资产** |
| `MassGpuRenderDispatcher.cs` | 每兵种 × LOD 一次间接绘制；MPB 预填后每帧只 SetBuffer 两次，渲染路径零反射 |
| `Shaders/AgentLodClassification.compute` | 每兵种一次派发：按距离分 near/mid/far append 索引，顺带推进动画时间（每 Agent 每帧恰好一次） |
| `Shaders/VatInstancedNoShadow.shader` | 远/中景 VAT shader（无阴影） |
| `Shaders/LitInstancedAgentShader.shader` | 近景 URP 光照 VAT shader |
| `Materials/` | 攻/防 × 近/远 四份在用材质 |
| `RenderConfig` / `AnimationConfig` / `LodConfig` | 兵种渲染配置 / 移动动画速度区间 / 全局 LOD 半径与动画降频 |

## 动画语义

- shader 在 Attack/Death 时优先按战术状态选片段，其余按 `presentationState` 选 Idle/Move：`frame = fmod(animTime × clipRate, clipCount)`，
  Death 钳在末帧。
- 时间累加器按**当前片段自身时长**回绕（相位对齐，循环无跳变）；
  各片段时长按兵种经 settings 通道上传。
- 移动倍率为实际速度 /（兵种参考速度 × scale.y），再由 `moveAnimationSpeedMin/Max` 钳制；参考速度为0时回退到 maxSpeed。停止后选Idle，而非继续最低倍率Move。
- LOD 动画降频：near/mid/far interval（LodConfig，全局）。

## LOD 与剔除

距离 lodCenter 的 near/mid 半径分级 + 可选视锥剔除（cullingRadius 外扩）。
死亡 Agent 不进 far 桶。每兵种 3 个 append buffer + 3 个 args buffer，
数量随兵种数扩展（UAV 数量恒定——分类按兵种分次派发）。

## 如何验证

EditMode：可见索引/args 随兵种数扩展的测试。
运行时：mesh-纹理配对冲突会打警告并指名 RenderConfig 槽位。

## VAT 烘焙工具（M5.1，已移植复活）

烘焙工具在 `Assets/MassEngine/Editor/`，菜单 `MassEngine/VAT Baker`：

| 文件 | 职责 |
|---|---|
| `VatBaker.cs` | 核心：四段 clip 逐帧采样（`SampleAnimation` + `BakeMesh`）→ 位置/法线纹理 + clip 窗口 + cleanMesh |
| `VatLodReducer.cs` | Low LOD 自动减面：顶点聚类得低模，再按簇把逐帧纹理取平均 |
| `VatBakerWindow.cs` | 编辑器窗口外壳，采样与布局全部走 `VatBaker`，不另写一份 |
| `VatBakeResult.cs` | 事务化产出：所有新建对象归它持有，失败整体回滚；只新建资产，不覆盖 |
| `VatBakeUtility.cs` | 布局推导、内存预算、半精度范围校验、纹理创建 |
| `VatProfileValidation.cs` | profile 契约校验（配对、窗口重叠、纹理容量） |
| `VatRebakeComparison.cs` | 批处理对拍：重烘内置兵种并与既有资产逐字段比对（M5.3 的门） |
| `UnitTypeBinder.cs` | M5.2 兵种绑定核心：LOD 落位、绑定校验、从模板新建整套兵种 |
| `UnitTypeBindingWindow.cs` | M5.2 绑定向导窗口，逻辑全走 `UnitTypeBinder` |

历史版本在项目根 `ArchivedStages/MassGPUPhysics_Stage{2,3,5,6}/Editor/VATBakerWindow_Stage*.cs`
（Stage6 = 现役 Male/Female profile 的产出者），仅作参考，不在编译范围内。

**纹理布局约定**（烘焙与 shader 必须一致）：`x = vertexID % textureWidth`、
`y = frame * rowsPerFrame + vertexID / textureWidth`；格式 `RGBAHalf` + `Clamp` + `Point`。

M7.3 的 `VatLodReducer.CreateFarVariant` 可从既有采样创建新远景变体：共享近/中景资源，原Low作为Mid回退保留，只新建Far网格及配套VAT；拒绝覆盖目标路径。首发Male已用此路径从994降到98顶点，旧profile保持原样；详见 [GPU拆分与远景预算](../../方案设计/M7.3GPU拆分与远景预算.md)。

**三处刻意约定**：
- **Mid LOD 有意留空**：运行时对 mid/low 缺失有回退，现有 Male profile 同为 full + low 两级。
- **cleanMesh 存绑定姿态**（不是 Idle 首帧）：Low LOD 的顶点聚类以 cleanMesh 几何范围做归一化，
  基准只能取决于模型本身；若取首帧，改一段 Idle 动画就会连带改掉所有远处 LOD 的网格拓扑。
- **帧数用 float 运算**：`clip.length` 是 float，转 double 会放大表示误差（0.5333s × 30 在 float 下
  恰好是 16，double 下是 16.0000008 → 取上整变 17），既有资产是按 float 口径烘的。

已知差异（非阻塞）：重烘内置兵种的 Low LOD 顶点数为 1004，既有资产为 994。根因是附件网格
（Hair01/Head01_Male/Shield08/Eye01/Mouth01）在参考烘焙里被冻结在死亡动画末帧姿态、在本工具里
取绑定姿态，聚类归一化基准因此略不同；蒙皮部分 2041 顶点逐顶点一致。

**验收盲区（2026-09-16 审计后已补）**：布局、窗口、profile 校验、重烘对拍读的都是尺寸类字段，
它们来自 `CalculateLayout` / `BuildWindows`，与采样结果无关；`cleanMesh` 更是在任何 `SampleAnimation`
之前就捕获好的。所以"逐帧采样整体失效、每帧都等于绑定姿态"能产出尺寸全对、校验全过的纹理。
现在 `VatBakerTests` 有两条解码纹素的测试（真实 Humanoid prefab + 手工两骨骼 `SkinnedMeshRenderer`），
`VatRebakeComparison` 也加了纹素抽检：在 idle 窗口内取样若干顶点，帧间无位移即判失败。

## 兵种绑定向导（M5.2）

`Assets/MassEngine/Editor/UnitTypeBinder.cs`（核心）+ `UnitTypeBindingWindow.cs`（窗口），
菜单 `MassEngine/兵种绑定向导`。把 VAT profile 接到兵种的 `RenderConfig`，或从数值模板新建整套兵种。

**LOD 落位规则只表达一次**（`UnitTypeBinder.ResolveLodMeshes`），与 `ResolvedUnitTypeRuntime.Resolve`
的配对逐字一致：near ↔ `cleanMesh`；mid ↔ `hasMidLod ? midLodMesh : (hasLowLod ? lowLodMesh : cleanMesh)`；
far ↔ `hasLowLod ? lowLodMesh : (hasMidLod ? midLodMesh : cleanMesh)`。
测试 `BinderSlotsMatchWhatTheRuntimeActuallyResolves` 遍历 mid/low 四种组合做反查，
两边任何一处漂移都会红 —— 防的是"向导接一套、运行时用另一套"。

**子配置独占/共享**沿用现役约定：`spawnConfig` / `combatConfig` 每兵种独占，
`movementConfig` / `flockingConfig` / `animationConfig` 按模板共享。绑新模型必须勾选"独占 RenderConfig"：
共享的渲染配置属于模板兵种，往里写 profile 会连带把模板兵种也换掉模型，故该组合被显式拒绝。
创建完成后只保存本次新建资产与目标持久 Scenario，不调用全局 `SaveAssets`；调用者其他未保存改动仍保持 dirty，磁盘不被顺带写回。该边界由真实磁盘回归锁定。

**为什么放在 `MassEngine.Editor` 而不是 `Game.Editor`**：烘焙/绑定核心只依赖 `MassEngine` 的
`UnitTypeConfig`、`ScenarioConfig`、`ConfigValidator` 和 `RenderConfig`，不耦合具体游戏。
M5.4 的试验编排器由 `Game.Editor` 单向引用 `MassEngine.Editor`，复用核心来制作兵种和场景；
这些 Editor 程序集都不进入独立玩家构建。

## M5.3 GPU 外观与性能回归（2026-09-24）

菜单 `MassEngine/Regression/M5.3 GPU VAT Appearance`，实现为 `Editor/VatAppearanceRegression.cs` 和
`VatAppearanceRegressionCapture.cs`。在唯一临时目录新建 Male 重烘产物，实际绘制 Male 原/新及 Female 原版
三档 LOD、四段动作，导出 PNG、动画变化/死亡钳制检查和源文件哈希；不是只比较纹理尺寸。
Female 的现役三级路径为 4066/2018/39 顶点，本轮未重烘 Female，也未迁移默认兵种资产。

36 组 GPU 路径通过，EditMode 319/319（新增 27 项，含 6 项真实 GPU 测试）、PlayMode 55/55 全绿。
游戏层隔离开发构建的 110k ABBA 采样约 +3.2% 平均帧时，按非阻塞回退记录，不启动引擎优化。
强制 LOD 的离屏截图不代替场景光影/分类或用户签收；M5.4 试验模型完整人工流程仍未验收。

完整证据与复现：[M5.3 回归记录](../../方案设计/M5.3重烘回归记录.md)。
制作步骤与独占/共享边界：[兵种模型制作管线](../../方案设计/兵种模型制作管线.md)。

## 2026-09-29 现有素材下的表现修正

不新增Walk/Run，不重烘VAT。约束后的三维位移负责表现，战术Move/Engage不再强迫播放Move。
默认停下/启动阈值为0.05/0.12m/s（随scale.y缩放），非静止速度做轻量平滑；Idle使用自己的片段周期。
大型模型可在独立AnimationConfig中调 moveReferenceSpeed，不能直接改共享模板而误影响其他兵种；这只是步频近似，不是真正Walk素材。
远程Attack的进度由战斗周期写入，分类kernel不再重复推进，LOD不改变出手节奏；暂停冻结所有片段时间，Death年龄不被表现切换重置。
