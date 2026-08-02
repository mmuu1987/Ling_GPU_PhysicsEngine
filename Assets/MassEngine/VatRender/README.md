# VatRender — VAT 动画、LOD 分类与间接绘制

把 GPU 内存里的 50k Agent 变成屏幕上的动画士兵：
VAT（顶点动画纹理）播放 + 三级 LOD + DrawMeshInstancedIndirect。

## 组件

| 文件 | 职责 |
|---|---|
| `VATProfile.cs` | VAT 烘焙产物资产：三级 LOD 的 mesh/位置纹理/法线纹理 + 四个片段（idle/move/attack/death）的帧数据。由 VAT 烘焙工具生成 |
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
