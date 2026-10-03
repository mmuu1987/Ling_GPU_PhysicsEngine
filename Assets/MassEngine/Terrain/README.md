# Terrain — 连续地表与战场导航

本模块提供静态单层地表快照、CPU/GPU 采样及 M6.2 战场接入。
实施决定与预算见 [M6.1 连续地表原型](../../方案设计/M6.1连续地表原型.md)。

- `TerrainSurfaceAsset`：制作期高度/禁行数据；`TryCreateSurface` 完整校验后生成独立快照。
- `TerrainSurface`：世界空间高度、面梯度/法线、格通行性和切向速度。XZ 原点与尺寸显式给出，
  高度为世界 Y 米；顶点按 Z 行排列，固定左下至右上对角线；数组不对外暴露。
- `TerrainSurfaceGpu`：一次上传高度和通行格，两块共享缓冲；调用者负责 Dispose，允许重复释放。
  `MassEngineManager.Terrain` 经 `TerrainNavigationRuntime` 持有，与分配、重开及卸载同步释放。
- `Shaders/TerrainSurface.hlsl`：与 CPU/可见网格一致的三角面插值；越界返回 `w=-1`，
  有效返回 `height/gradientX/gradientZ/walkable`；速度沿原 XZ 朝向贴面，保持原速度模长。
- `TerrainPrototype`：720m 世界、257×257 顶点、48m 高地、宽坡道、窄出口、峡谷和人工禁行区。
  原型网格也可用于 MeshCollider；颜色用于粗看通行性，精确规则以格掩码为准。
- `TerrainSurfaceProbe.compute`：与等输入/输出的平面 kernel 对照；测试/预算使用，未加到旧场景每帧路径。

CPU 测试覆盖非共面格、边界、禁行、坡度、速度、隔离和坏数据；GPU 测试执行真实 shader，
并用 MeshCollider 射线对照可见网格与采样高度。原 `AgentData` / `UnitTypeGpuSettings` stride 不变。

`TerrainNavigationGrid` 将地表禁行格、最大兵种半径、边界留白及静态矩形障碍合并为共享导航掩码。
八邻接禁止切角，边代价按实际三角面分段长度计算；连通域用于命令验证，多源 Dijkstra 为各军团生成独立方向切片。
动态目标通过已有敌军密度缓冲异步读回；固定命令直接求解。该路径当前在 CPU 求流场，单位移动与战斗仍在 GPU；
不能把 M6.1 采样预算或平面 FPS 当成本路径性能。不可达/等待流场的零方向不会启用直线追踪后备。

M7.3 将已完成的动态读回先复制为稀疏目标快照，再按军团轮转，每个 Manager Tick 最多求解一张动态流场。
所有完成的原生读回都在当帧消费，避免跨帧访问已失效的 GPU 回读数据；等待期间继续使用上一张有效流场。
队列最多各军团一项，新的动态请求不会覆盖未处理快照；显式移动/防守命令取消该军团的旧请求和排队目标，显式目标仍立即求解。
这限制的是同帧求解数量，不是单次 Dijkstra 的硬毫秒上限。每张就绪流场最多等待其余就绪军团各一次，轮转防止低 ID 军团长期占用处理机会。
`TerrainMaxDynamicQueueWaitMilliseconds` 记录快照就绪到开始求解的最大墙钟等待，用来复查更新延迟；随场景/重开重置。

`TerrainNavigation.hlsl` 为决策帧和 LOD 轻帧共同提供贴面积分、路径穿格检查及三维距离。
`TerrainCollision.hlsl` 对弹道段穿过的每个格做精确三角面检测；障碍命中与目标球体入口按最早时刻排序。
高低差没有伤害加成。死亡和暂停单位也保持地表高度，尸体视觉下沉继续由原渲染路径负责。

地形 ID/版本、原点和尺寸必须与目录/战场一致，提供者缺失或损坏时拒绝进入，不能改按平面处理。
高度或通行数据改动必须提升资产版本；本轮不支持运行时地形编辑。存档仍用既有 schema 1 的地形身份字段，
不将高度数组写进部署存档。完整脚印校验、点击射线和镜头适配由游戏层消费同一快照。
原平面场景不会因为本模块存在就自动加载地形、分配显存或多派发 kernel。

M6.3提供只读诊断：`MassEngineManager.TerrainGpuBytes`、`TerrainCompletedFields`、
`TerrainTotalSolveMilliseconds`、`TerrainMaxSolveMilliseconds`。计数随该场景/重开的运行时实例重置；
求解计时只覆盖CPU Dijkstra，未包含方向上传，不能据此推导战斗FPS。完整独立对局和采样口径见
[M6.3记录](../../方案设计/M6.3地形闭环验收.md)。
