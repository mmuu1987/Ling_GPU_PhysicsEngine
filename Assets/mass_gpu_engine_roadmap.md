# “Mass” GPU Physics Engine 实施方案路线图

本方案旨在将庞大的海量单位并行运算引擎拆解为可落地的、循序渐进的独立模块。我们的核心原则始终不变：**面向数据，GPU 计算，极力避免 CPU 与显存之间的数据回传和 CPU 的循环检算。**

实施路径分为**六个主要阶段**，每个阶段都有明确的交付目标和测试标准。建议严格按照此顺序推进，前置模块是后续模块的地基。

---

## 第一阶段：基础渲染与显存数据流转（GPU Instancing Foundation）
**目标：** 实现十万个基础几何体（如 Cube）同屏渲染，并全权由 GPU 控制移动位置。
**核心：** 跑通 Compute Buffer 和 Indirect Draw 的全生命周期。

*   **步骤 1：定义核心数据结构 (Data Struct)**
    在 HLSL 和 C# 中声明完全一致的结构体（例如 `struct AgentData { float3 position; float3 rotation; float3 scale; }`）。
*   **步骤 2：分配与初始化 (Initialization)**
    在 C# 端创建 `ComputeBuffer`，将十万个初始状态的结构体数组一次性 PUSH 给显存。
*   **步骤 3：编写基础计算着色器 (Compute Shader)**
    编写一个基础的 Compute Shader 进行简单的位置累加（例如所有物体统一朝 Z 轴以固定速度移动）。
*   **步骤 4：核心渲染指令 (DrawMeshInstancedIndirect)**
    抛弃常规的 GameObject。编写或修改一个 Surface/Unlit Shader（支持 Instancing），并在 C# 中使用 `Graphics.DrawMeshInstancedIndirect` 调用，让显卡直接根据缓冲池的数据盲画十万个 Cube。
*   **里程碑验收：** 同屏 10 万个 Cube 流畅移动，帧率极高，Unity Profiler 中 CPU 几乎没有开销。

---

## 第二阶段：无 CPU 蒙皮动画管线（Vertex Animation Textures - VAT）
**目标：** 将四四方方的 Cube 替换为可以播放“跑、死、攻击”等动画的人形角色网格。
**核心：** 抛弃 SkinnedMeshRenderer 和 Animator，通过贴图驱动顶点移动。

*   **步骤 1：工具链制作（VAT Baker）**
    开发或寻找一款 Unity 插件，将 FBX 动画的时间帧和每个顶点的坐标位移烘焙成一张或者多张 EXR 纹理图像。
*   **步骤 2：扩展 Agent 结构体**
    为 `AgentData` 添加 `int currentState` 和 `float currentAnimationTime` 字段。
*   **步骤 3：渲染材质修改**
    修改你在第一阶段写的 Shader，使其在顶点着色器（Vertex Shader）阶段能根据个体的 `currentState` 和 `currentAnimationTime` 读取顶点的位移贴图，并附加到原始顶点上。
*   **步骤 4：Compute Shader 驱动时间**
    修改 Compute Shader，让个体的 `currentAnimationTime` 随 `deltaTime` 增长并循环。
*   **里程碑验收：** 十万个同一个模型不同骨骼动作阶段的 3D 小人同屏跑步。

---

## 第三阶段：空间划分与 GPU 碰撞引擎（GPU Spatial Hashing）
**目标：** 人物聚拢时不会穿模在一起，具有物理排他碰撞。
**核心：** 彻底放弃内置物理引擎（PhysX），通过空间哈希降维打击 $O(N^2)$ 的复杂度。

*   **步骤 1：定义空间网格**
    设定战场隐性网格单元（比如每个网格 $2 \times 2$ 米）。
*   **步骤 2：构建哈希阶段 (Hash Pass)**
    编写新的 Compute Shader。在物理更新前，计算出每一个 agent 目前处在哪个网格ID里，并通过 GPU 的原子操作（InterlockedAdd）将其索引插入到一个“网格信息列表”中。
*   **步骤 3：碰撞推挤阶段 (Collision Pass)**
    在物理更新时，让每个 agent 只查询自己所在的格子以及周围 8 个相邻格子的 agent 列表。将其视为简单的球型（Sphere Collider），如果两点距离小于直径，则根据朝向互加排斥速度。
*   **里程碑验收：** 同屏个体重叠到一起时，会迅速像水波一样散开且互不穿模挤压。

---

## 第四阶段：向量场海量寻路系统（Flow Fields Navigation）
**目标：** 十万人知道往哪走，且知道怎么绕过巨大的静态障碍物。
**核心：** 抛弃独立 A* 寻路，构建流场数据。

*   **步骤 1：流场生成模块（可在 CPU 或者 GPU 只执行一次）**
    根据地形网格、障碍物，针对目标终点进行广度优先搜索扩散，生成一张携带方向向量的 2D 贴图或二维数组（这就是该地形的“洋流方向”）。
*   **步骤 2：方向获取阶段 (Velocity Pass)**
    将该贴图或数组发送给 GPU。由于我们的个体只有位置（x,z），所以能在 Compute shader 里面将其作为 UV 来采样地形流场向量图。
*   **步骤 3：应用推进力**
    将流场采样到的朝向作为个体当前的朝向和速度更新依据。
*   **里程碑验收：** 放置障碍物后，海量单位能像水流一样自动绕过障碍并流向预定终点。

---

## 第五阶段：极简 GPU 状态机与战斗逻辑（State Machine & Combat）
**Stage5 实施更新：双阵营攻守 MVP**

首版固定为 2 阵营攻守模式，而不是通用 N 阵营框架。攻击方继续沿单张 `PaintedFlowFieldAsset_Stage5` 推进，防守方以出生点为 `homePosition`，只在敌人进入仇恨半径后短追击，超过最大追击距离则回防。

本阶段不把 `teamID/HP/target/cooldown` 塞进渲染共享的 `AgentData`。`AgentData` 继续只承载位置、旋转、缩放、速度、`currentState` 与 VAT 时间；战斗字段使用 compute-only SoA buffer：`teamIdBuffer`、`hpBuffer`、`targetAgentIndexBuffer`、`attackCooldownBuffer`、`homePositionBuffer`、`pendingDamageBuffer`。

GPU 调度顺序拆为：`ClearGrid -> BuildSpatialHash -> ClearPendingDamage -> EvaluateStateAndAccumulateDamage -> ResolveDamageSimulateAndClassify`。状态枚举固定为 `0 Idle / 1 Move / 2 Engage / 3 Attack / 4 Dead`；`Move/Engage` 共用 Move VAT，静止守点使用 Idle VAT，`Dead` 不再参与寻敌、攻击、碰撞和 far LOD billboard。

**目标：** 实现发现敌人、停止移动、互相砍杀、掉血与死亡。
**核心：** 在 Compute Shader 中实现分支逻辑，通过原子操作控制多人对单人造成的伤害。

*   **步骤 1：增加阵营与战斗数据**
    保持 `AgentData` 渲染共享布局不变，新增 compute-only buffer 保存 `teamID`、`HP`、目标、冷却、守点与待结算伤害。
*   **步骤 2：逻辑判断阶段 (Logic Pass)**
    利用阶段三搭建的**空间哈希结构**查询周围格子：一旦查找到不同 `teamID` 且距离达到攻击范围的个体，将 `currentState` 切换为“攻击状态”。若没查找到，则切回“移动状态”。
*   **步骤 3：极简伤害计算**
    攻击状态播放到特定时间帧时，调用 `InterlockedAdd` 扣减目标显存里的 HP。（注意：在 GPU 上写数据必须考虑竞态问题，原子操作能确保不会导致十个人砍同一个人血量只减一次）。
*   **步骤 4：死亡判定**
    如果有人的 HP $\leq 0$，则将状态设为“死亡”，动画播放倒地并彻底停止在下一个帧的物理与逻辑更新计算。
*   **里程碑验收：** 蓝红两军冲锋相撞，前排开始砍杀，血条耗尽倒地，后排继续涌上。

---

## 第六阶段：总控制调度与优化整合（Integration Pipeline）
**目标：** 将所有阶段完美拼合，形成一个可伸缩框架。
**核心：** GPU 运算有严格的前置后置依赖逻辑。

*   **步骤 1：整合 C# Dispatch 调度器**
    搭建一个 `MassEngineManager.cs` 的单例。保证在 `Update()` 或 `LateUpdate()` 里的 `Dispatch` 顺序严死合缝：
    **生成寻路流场 (如果有变) $\rightarrow$ 清空并建立空间哈希 $\rightarrow$ 逻辑判断(状态机与攻击) $\rightarrow$ 应用碰撞与速度 $\rightarrow$ 更新动画时间 $\rightarrow$ Graphics.DrawMeshInstancedIndirect。**
*   **步骤 2：内存结构化重构**
    将各种各样的计算整理好（尽量使用 Struct of Arrays 思想优化显存缓存命中率，但在初期使用 Array of Structs 较易于理解）。
*   **步骤 3：视锥剔除与 LOD（高级拓展）**
    引擎里如果真的有十万人，但在镜头外的应该只做碰撞和逻辑更新，不参与绘制。将屏幕内的物体的 ID 分离到一个针对渲染的 Buffer 里进行渲染，可极大提高显卡效率。

**最终形态交付：** 我们就拥有了一套独立于传统 Unity Scene 逻辑外的完全由显卡推算的“Mass”引擎。
---

## 第四阶段实施更新：Painted Flow Field 落地方案

当前 Stage4 主路径已切换为 **Painted Flow Field Asset**：手绘流场和预设流场直接写入 `PaintedFlowFieldAsset_Stage4`，运行时 GPU 只采样该资产。旧的 CPU 目标点/Dijkstra 代码生成流场已移出运行链路。

运行时流程：

* C# 在初始化时读取 `PaintedFlowFieldAsset_Stage4`，并上传 `ComputeBuffer<float2>`。
* Compute Shader 在 `SimulateAndClassify` 中根据 Agent 的世界 XZ 坐标采样方向。
* 采样方向只作为 `desiredVelocity`，不会直接覆盖位置。
* 第三阶段的 spatial hash collision 继续负责局部排斥和防重叠。

已新增详细实施与验证文档：

* `MassGPUPhysics_Stage3/stage4_flow_field_navigation_implementation.md`

目前保留的预设：
* **Uniform Direction**：整张图按指定 0-360 度方向流动。
* **Converge To Point**：整张图速度方向指向指定 XZ 点。
