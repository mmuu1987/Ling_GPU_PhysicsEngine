# P3 车道 ABBA 第三次运行失败后的保护恢复(2026-10-08)

接手自 `HANDOFF-P3-20261008.md` 第一节。本轮只做恢复与核对,未运行任何 Unity 测试,未提交/推送/建分支。

## 失败根因
- run 3 的 B1 窗口(`p3-lane-abba2-2`)Unity 编译失败:
  `TerrainNavigationGrid.cs(135,18): CS0246 'TerrainNavigationBurstWorkspace' 找不到`。
- 车道候选 Grid 引用了 Burst 落地文件中的类型,但本轮按 scope 部署的是原版 Runtime/asmdef(Burst absent),候选文件组合不自洽。
  对应交接文档第七节"未验证:车道改动在原版 Runtime 上的组合"。
- Unity 超时退出(exitCode 1, timedOut),runner 因 projectLock 存在按规则拒绝自动恢复(行为正确)。

## 恢复执行(脚本 `Tools/InteractionRefinement/p3_recover_abba3_20261008.py`,回执 `recovery-executed-01.json`)
1. 确认无 Unity.exe / WarSandbox.exe 进程(仅 Unity Hub / Licensing Client,不持项目锁)。
2. 按 registry.json 哈希审计 7 个登记文件:当前字节均为已知状态(候选或原版),无未知用户改动,未触碰任何未知字节。
3. 回写 2 个候选态文件为原版字节并哈希复核:
   - Assets/MassEngine/Terrain/TerrainNavigationGrid.cs (1c9143a7...)
   - Assets/Game/Editor/InteractionRefinementP3Qualification.cs (74718e10...)
   其余 5 个已是原版,未动。
4. JIT:当前生成态(398 文件)归档至 `generated-JIT-final/`,从 `original-JIT-escrow/`(432 文件)恢复 `Library/BurstCache/JIT`。
5. 删除超时击杀残留的 0 字节 `Temp/UnityLockfile`(删除前再次确认无 Unity 进程)。

## 恢复后复核
- Temp/UnityLockfile 不存在;HEAD 仍为 aea3699;index 未动。
- `Assets/MassEngine/Terrain` 下 `TerrainNavigationBurstWorkspace` 命中 0 处。
- `TerrainCardinalRouteCacheTests.cs`(+meta)为凌晨 Cardinal 等价测试按记录保留的文件,未删除;run 3 保护检查标记它是登记表遗漏,下次运行需补登记。

## 下一步(未执行,待确认)
1. 修正车道候选生成(`make_lane_files_20261008.py`):基于原版 Grid 仅加车道 O(1) 直线阻挡表,移除对 Burst 工作区类型的引用;或显式声明与 Burst 落地文件的依赖组合。
2. 把 CardinalTests(+meta)补入保护登记后,重跑完整 A1/B1/B2/A2(第四次),不得只跑 B 窗口。
3. 车道候选的提交/分支化仍等待用户明确确认。
