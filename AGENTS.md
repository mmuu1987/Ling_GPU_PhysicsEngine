# 给 AI 助手的工程约定

> 新开会话先读完本文再动手。这里是**工作方式**约定，不是代码说明 ——
> 代码说明在各模块自己的 README。

## 第一原则：初级阶段，别被细节带进沟里

这个工程处在**初级阶段**。目标是把 GPU 战争沙盒推到「跑得动、看得见、玩得起来」，
不是把引擎打磨到无瑕。**推进主线的价值远高于修补细节。**

一件事该不该现在做，只看一条：**它是否阻塞主线？**

- **阻塞**（编译失败、崩溃、功能根本不出现、主路径上的测试挂了）→ 现在修
- **不阻塞**（观感瑕疵、局部穿插、单元测试能复现但真机看不出、个位数百分比的性能回退）
  → **记进 `NEXT_TASK.md` 的路线图，继续往前**

下面这些反模式都是真实踩过的，不是假想：

- **测试失败先分类再动手**：分「测试前提错」和「引擎缺陷」两类。前提错就改测试，
  别改引擎去迁就一个写错的 fixture。例：`DamageAccruesAtAttackIntervalAndKillsAtZeroHp`
  曾假设 4v4 必然 1:1 分配目标 —— 交战槽位容量是 8，两打一本来就合法。
- **真机看不出来的现象默认降级**：18 个 agent 的确定性复现 ≠ 50k 真机的问题。
  记录下来，除非用户明确要求，不要为它改引擎。
- **两条内部规则打架时改规则，别改架构**：曾有「public 字段预算 ≤30」与
  「stride 必须 144 字节所以必须留 padding」互相矛盾。正解是让预算只数数据字段
  （零运行时改动），不是拆 GPU 结构体（56 处 HLSL 点访问 + 跨平台打包风险，收益为 0）。
- **别为一个决定写论文**：权衡讲三五句给结论。长篇量化论证只留给真正动引擎数据契约的
  改动（例如多军团导航的 buffer 维度扩展）。
- **报告结论先行**：细节写进路线图文档，别堆在对话里。

## 硬边界

- **`Assets/pelican-cycling.svg`(+`.meta`) 不属于任何任务** —— 禁止删除、覆盖、提交
- 提交只用显式 `git add <路径>`，**永远不要 `git add .`**
- 仓库走 PR 流程，不直接推 `main`

## 跑测试

```
"D:/soft/Unity6/6000.3.14f1/Editor/Unity.exe" -runTests -batchmode -projectPath E:/GitHub/Ling_GPU_PhysicsEngine/Ling_GPU_PhysicsEngine -testPlatform PlayMode -testResults <out>/playmode.xml -logFile <out>/playmode.log
```

- **绝不加 `-nographics`**：引擎大量使用带 random write 的 RenderTexture，无图形设备时
  `RenderTexture.Create` 报 "format unsupported for random writes"，造成 4 项与改动无关的假失败
- Unity 锁是 **per-project** 的（锁文件在项目自己的 `Temp/UnityLockfile`）。别的工程的编辑器
  开着不影响；按进程名 `tasklist | grep Unity.exe` 判断会误判、自造假阻塞。真占用的信号是 log 里
  `another Unity instance is running with this project open`（返回码 1，不产生 xml）
- `ProjectSettings/TimeManager.asset` 每次 batchmode 都会被改写成等价的新序列化格式，
  提交前 `git checkout --` 掉
- 当前基线：PlayMode 29 项、EditMode 63 项，全绿

## 文档在哪

- **路线图**：`ROADMAP.md`（仓库根）—— 长期方向与阶段划分，接手任务先读这份
- **当前任务交接**：`NEXT_TASK.md`（仓库根，**不进 git**）—— 上一个人停在哪、手上还捏着什么。
  可能不存在（说明没有在途任务），内容一次性，做完就该被下一份覆盖
- 性能档位与产品决策：`Assets/Game/PerformanceBaseline.md`
- 引擎各模块的行为规格：`Assets/MassEngine/*/README.md` —— 改 kernel 前先读对应那份
- 游戏层：`Assets/Game/README.md`
