# Ling GPU Physics Engine

Unity 6 GPU 海量单位战争模拟实验工程。单位的空间哈希、流场导航、群体运动、战斗、弹道、LOD 分类和 VAT 渲染主要在 GPU 上完成，C# 负责配置、资源生命周期、调度与诊断。

## 当前能力

- 多兵种、多军团的大规模 Agent 模拟
- GPU 空间哈希、动态/静态流场与密度避让
- 近战、远程弹道、伤害和状态机
- VAT 动画、三级 LOD、视锥裁剪与间接绘制
- 战争沙盒编辑器、运行时命令和异步遥测

## 打开与验证

使用 Unity `6000.3.14f1` 打开本目录。测试程序集：

- EditMode：`MassEngine.Tests`、`Game.Tests`
- PlayMode：`MassEngine.PlayModeTests`

GPU PlayMode 测试需要支持 Compute Shader 的图形设备。

## 目录

- `Assets/MassEngine/`：引擎实现与模块文档
- `Assets/Game/`：战争沙盒玩法层
- `Assets/方案设计/流场三维扩展方案.md`：仍未实施的三维导航方向
- `ArchivedStages/`：旧阶段完整快照，仅供历史追溯

## 文档入口

- [产品总策划案](GAME_DESIGN.md)
- [执行路线图](ROADMAP.md)
- [引擎总览](Assets/MassEngine/README.md)
- [游戏层](Assets/Game/README.md)
- [性能基线](Assets/Game/PerformanceBaseline.md)
- [弹道系统](Assets/MassEngine/Projectiles/README.md)

产品方向以 `GAME_DESIGN.md` 为准，阶段状态见 `ROADMAP.md`；当前任务交接仅记录在本地 `NEXT_TASK.md`。
模块细节以对应目录的 `README.md` 和当前代码为准，不把策划目标当成已实现能力。
