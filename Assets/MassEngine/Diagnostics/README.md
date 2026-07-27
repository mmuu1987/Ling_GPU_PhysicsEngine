# Diagnostics — 遥测与 HUD

回答"系统到底在不在跑"的仪表层。全部非阻塞，不影响仿真。

| 文件 | 职责 |
|---|---|
| `BattleTelemetry.cs` | AsyncGPUReadback 采样（默认 0.5s）：双方存活数、战斗时长（暂停不计时）、流场重建计数。Manager 自动持有 |
| `BattleTelemetryHUD.cs` | OnGUI 面板：帧时间/FPS、存活数、战斗时长、重建计数。挂在 manager 旁即可 |
| `FlowFieldPreviewHUD.cs` | 屏幕角落展示攻/防流场预览纹理（预览写入由 RuntimeFlowConfig.runtimeFlowPreviewEnabled 门控，关闭时 GPU 零开销） |

HUD 还会在空间哈希格满溢出时显示红色告警（GRID OVERFLOW: N/frame）——溢出的单位
会静默掉出邻域查询（分离失效、穿插成团），看到该行就调大 maxAgentsPerCell 或 cellSize。

注意：存活数来自异步回读，比仿真滞后几帧；这是特性不是缺陷。

性能基线测量流程：场景挂 BattleTelemetryHUD → 10000v10000 →
记录帧时间（对比基准见引擎 README"已知边界"）。
