# 第四阶段 Flow Field Navigation 实施说明

## 当前路线

Stage4 现在只使用 `PaintedFlowFieldAsset_Stage4` 作为导航流场来源。旧的 CPU 目标点/Dijkstra 生成器已经从运行链路中移除。

运行时流程：

1. Painter 窗口创建、适配、分配、手绘或预设生成 `PaintedFlowFieldAsset_Stage4`。
2. `GPUInstancingManager_Stage3` 在初始化或重建时读取该资产。
3. C# 将资产方向场上传为 `ComputeBuffer<float2>`。
4. `AgentComputeShader_Stage3.compute` 根据 agent 的世界 XZ 坐标采样方向。
5. 采样方向作为 `desiredVelocity` 混入 `agent.velocity.xz`。
6. 第三阶段 spatial hash collision 继续负责局部分离与防重叠。

未指定 painted asset 时，Manager 会上传一张零方向流场，并在 Console/Preview 中提示缺少资产。

## 文件说明

- `Scripts/MassEngine/PaintedFlowFieldAsset_Stage4.cs`
  - 保存每个格子的方向、速度和权重。
  - 支持手绘流场数据。
  - 支持 `GenerateUniformDirection` 和 `GenerateConvergeToPoint` 两个预设生成函数。

- `Editor/PaintedFlowFieldPainterWindow_Stage4.cs`
  - 提供 painted flow field 资产创建、Fit、Assign、Load、Save。
  - 提供手绘画布。
  - 提供 Uniform Direction 和 Converge To Point 预设按钮。

- `Scripts/GPUInstancingManager_Stage3.cs`
  - 只上传 `PaintedFlowFieldAsset_Stage4`。
  - 将 `flowFieldDirections` 绑定到 simulation kernel。
  - 每帧上传 flow field 相关 uniform 参数。
  - 提供 Play Mode 右键菜单 `Stage4/Rebuild Flow Field`。

- `Scripts/MassEngine/MassSpatialHashGridSettings_Stage3.cs`
  - 提供 Manager 和 flow field preview 共用的模拟范围/grid 参数计算。

- `Shaders/AgentComputeShader_Stage3.compute`
  - 使用 `StructuredBuffer<float2> flowFieldDirections`。
  - 在最终速度限幅和位置积分之前，将采样方向混入 `agent.velocity.xz`。

## Inspector 设置

- `Enable Flow Field Navigation`
  - 开启或关闭 Stage4 flow field 转向。

- `Painted Flow Field Asset`
  - Stage4 导航使用的手绘/预设流场资产。

- `Flow Field Cell Size`
  - Painter 执行 `Fit To Manager` 时使用的资产网格尺寸。

- `Flow Field Responsiveness`
  - agent 速度朝流场方向转向的速度。

- `Flow Field Weight`
  - 流场转向影响权重。`0` 表示禁用转向，`1` 表示完整使用采样方向。

## Painter 预设

- `Generate Uniform Direction`
  - 整张图按 `Uniform Angle` 的 0-360 度方向流动。
  - `0` 对应世界 `+X`，`90` 对应世界 `+Z`。

- `Generate Converge To Point`
  - 每个格子指向 `Converge Target XZ`。
  - `Converge Stop Radius` 内方向置零，避免中心点附近持续抖动。

## 验证步骤

### 1. 编译/导入检查

1. 打开 Unity。
2. 等待脚本和 shader 导入完成。
3. 确认 Console 中没有 C# 编译错误，也没有 compute shader 编译错误。
4. 选中挂有 `GPUInstancingManager_Stage3` 的测试对象。
5. 确认 Inspector 中出现 `Painted Flow Field Asset` 和 flow field preview。

预期结果：

- Unity 编译通过。
- Play Mode 可以正常启动。
- 未指定 painted asset 时，Console 只提示缺少 painted flow field，agent 不会受到流场推动。

### 2. Painted Asset 检查

1. 打开 `MassGPUPhysics/Stage4/Painted Flow Field Painter`。
2. 创建或选择 `PaintedFlowFieldAsset_Stage4`。
3. 点击 `Fit To Manager`。
4. 点击 `Assign To Manager`。
5. 使用 `Generate Uniform Direction` 或手绘一段流场。
6. 点击 Play。

预期结果：

- Manager 使用该 painted asset 上传 `ComputeBuffer<float2>`。
- agents 按 painted asset 里的方向移动。
- 第三阶段 separation 仍然生效，agents 不应该明显堆叠穿插。

### 3. 预设检查

1. `Uniform Angle = 0` 时生成整图 `+X` 方向流场。
2. `Uniform Angle = 90` 时生成整图 `+Z` 方向流场。
3. 设置 `Converge Target XZ` 后点击 `Generate Converge To Point`。

预期结果：

- Uniform Direction 会让群体整体按指定方向移动。
- Converge To Point 会让群体从四周汇聚到目标点附近。
- 目标点 `Converge Stop Radius` 内方向为零。

## 已知限制

- 当前只有一个全局 painted flow field。
- 动态拥堵还没有反馈到 flow field。
- 空白/零方向区域不会主动导航，只响应碰撞分离、阻尼和边界处理。

## 后续方向

- 添加更丰富的预设，例如环流、分段通道、噪声扰动。
- 支持多张 flow field，用于不同队伍或 squad 目标。
- 根据密度/grid counts 叠加动态 flow weight 或动态避让。
