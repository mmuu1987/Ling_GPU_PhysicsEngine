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

当前试玩入口：`Builds/M73FarLod/Start-WarSandbox.cmd`，首次进入 512 人准备战场；
目录提供开阔对冲、山地绕行、中央争夺、三方混编和 100k 标准规模选项。
Unity 场景入口为 `Assets/Game/M71LaunchPresets/LaunchMenu.unity`。
M7.2补齐全军团结算、手动结束、基础音效与设置；M7.3已完成山地错峰、12轮切场及首发远景优化。完整前台ABBA两组100k交战31.07/30.59 FPS，达到本参考机720p既定镜头/时段的平均约30 FPS目标。下一步为人工试玩，M7/V1尚未签收；旧包和失败证据保留。

人工试玩独立包：`Builds/M7HumanPlaytest-20260928.zip`（约79.4MiB），解压后运行 `Start-WarSandbox.cmd`。同GUID运行文件核对和五预设烟测通过，附操作说明、空白反馈表及独立日志入口；详见 [试玩包准备](Assets/方案设计/M7人工试玩包准备.md)。

## 目录

- `Assets/MassEngine/`：引擎实现与模块文档
- `Assets/Game/`：战争沙盒玩法层
- `Assets/方案设计/流场三维扩展方案.md`：仍未实施的三维导航方向
- `ArchivedStages/`：旧阶段完整快照，不在 Unity 编译范围内，仅供历史追溯。
  注意：其中的 VAT 烘焙工具（Stage2/3/5/6 四版 `VATBakerWindow_Stage*.cs`）是 M5.1 移植的源材料，
  现役版本已移到 `Assets/MassEngine/Editor/`；归档版不要当作废弃代码清理。

## 文档入口

- [产品总策划案](GAME_DESIGN.md)
- [执行路线图](ROADMAP.md)
- [引擎总览](Assets/MassEngine/README.md)
- [游戏层](Assets/Game/README.md)
- [性能基线](Assets/Game/PerformanceBaseline.md)
- [弹道系统](Assets/MassEngine/Projectiles/README.md)
- [兵种模型制作管线（M5.4）](Assets/方案设计/兵种模型制作管线.md)
- [M5.3 重烘外观与 110k 性能回归](Assets/方案设计/M5.3重烘回归记录.md)
- [M5.4 UnityChan 试玩与验收记录](Assets/方案设计/M5.4试验模型验收记录.md)
- [M6.1 连续地表原型与预算](Assets/方案设计/M6.1连续地表原型.md)
- [M6.2 山地可玩整合与验证](Assets/方案设计/M6.2山地可玩整合.md)
- [M6.3 地形闭环、跨进程方案与性能记录](Assets/方案设计/M6.3地形闭环验收.md)
- [M7.1 首发预设与轻量入口](Assets/方案设计/M7.1首发预设与轻量入口.md)
- [M7.2 全军团结算、反馈与设置](Assets/方案设计/M7.2结算反馈与设置.md)
- [M7.3 参考机采样与持续切换](Assets/方案设计/M7.3参考机采样与持续切换.md)
- [M7.3 山地错峰与长时复测](Assets/方案设计/M7.3山地错峰与长时复测.md)
- [M7.3 普通窗口帧率验证](Assets/方案设计/M7.3普通窗口帧率验证.md)
- [M7.3 GPU拆分与远景预算](Assets/方案设计/M7.3GPU拆分与远景预算.md)
- [M7 人工试玩与 V1 交付清单](Assets/方案设计/M7人工试玩与V1交付清单.md)

产品方向以 `GAME_DESIGN.md` 为准，阶段状态见 `ROADMAP.md`；当前任务交接仅记录在本地 `NEXT_TASK.md`。
模块细节以对应目录的 `README.md` 和当前代码为准，不把策划目标当成已实现能力。
