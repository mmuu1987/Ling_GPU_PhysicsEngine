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

- shader 按 `currentState` 选片段：`frame = fmod(animTime × clipRate, clipCount)`，
  Death 钳在末帧。
- 时间累加器按**当前片段自身时长**回绕（相位对齐，循环无跳变）；
  各片段时长按兵种经 settings 通道上传。
- 移动动画速度随速度在 `moveAnimationSpeedMin/Max` 间插值（AnimationConfig）。
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

**为什么放在 `MassEngine.Editor` 而不是 `Game.Editor`**：`UnitTypeConfig` / `ScenarioConfig` /
`ConfigValidator` / `RenderConfig` 都在 `MassEngine` 程序集里，这样零 asmdef 改动就能全用到
（`Game.Editor` 看不见 `MassEngine.Editor`，反向则畅通）。
