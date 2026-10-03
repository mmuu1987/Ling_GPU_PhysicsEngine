# 给 AI 助手的工程约定

> 新开会话先读完本文再动手。这里是**工作方式**约定，不是代码说明 ——
> 代码说明在各模块自己的 README。

## 第一原则：初级产品已形成，以实际试玩问题驱动打磨

2026-10-03用户确认：**初级阶段游戏产品制作基本完成，接下来逐步打磨功能、产品细节和功能细节。**
这不是V1正式发行签收，也不代表全部专项验证通过；阶段状态见 `ROADMAP.md` 和 `Docs/ProductPolish-20261003/README.md`。

判断工作是否值得现在做，要看**对当前玩家体验的影响、复现依据、收益与改动范围**：

- 崩溃、数据丢失、无法操作或主路径回归 → 优先处理。
- 真实试玩中不好用、误导、难辨认、反馈不足等细节 → 可以作为本轮主任务，不因“不阻塞启动”一律延期。
- 没有实际收益依据的底层洁癖、难复现的微小差异、无必要的大规模压测 → 留档，不自动扩张为引擎重构。
- 每轮聚焦一个问题：明确预期/非目标，小范围实现，做必要定向验证，需要时出独立新包，再收用户反馈。没有指定的问题时，不擅自挑一批新功能开工。
- 角色数量、同模型不同名字和更大的单位数字不作为主要进度指标；保护旧战场/方案和已验收行为。

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
- `ProjectSettings/TimeManager.asset` 可能被 batchmode 重写；先保存运行前状态，只恢复本次自动改写，不覆盖用户原有修改。旧材质URP版本标记同样先核对再恢复。
- 29项PlayMode / 63项EditMode属于早期历史记录，不是现役全量基线。最新定向结果与适用包见工程导航；复用证据，仅补改动所需测试，纯文档更新不启动Unity/GPU回归。

## 文档在哪

- **产品总策划**：`GAME_DESIGN.md`（仓库根）定义 V1 范围与交付门槛；先明确任务对应的产品里程碑
- **路线图**：`ROADMAP.md`（仓库根）—— 长期方向与阶段划分，接手任务先读这份
- **当前任务交接**：`NEXT_TASK.md`（仓库根，**不进 git**）—— 上一个人停在哪、手上还捏着什么。
  可能不存在（说明没有在途任务），内容一次性，做完就该被下一份覆盖
- 性能档位与产品决策：`Assets/Game/PerformanceBaseline.md`
- 引擎各模块的行为规格：`Assets/MassEngine/*/README.md` —— 改 kernel 前先读对应那份
- 游戏层：`Assets/Game/README.md`
