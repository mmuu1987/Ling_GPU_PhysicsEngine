# P3 求解/Lane 细分段诊断:Dijkstra 主循环主导长帧(2026-10-08)

授权依据:用户选择"长帧方向——对 TerrainLaneApproach33.Apply 内部与求解做更细分段诊断"。诊断只用临时 UNITY_EDITOR 探针,跑完按字节恢复;无新优化候选、无重新资格声明、无采纳。

## 结论(本轮控制门通过,controlGatePassed=true)
85 个 >33.33ms 长帧(每帧恰好一次导航更新)的中位分解:

|项|窗口2 ms|窗口3 ms|
|---|---:|---:|
|整帧间隔|37.19|37.39|
|求解合计 CreateFlowField|23.74|23.46|
|— 距离/堆重置(65,536 格)|0.61|0.56|
|— 目标播种(225/229 个)|0.04|0.04|
|— **Dijkstra 主循环**|**21.21**|**21.08**|
|— 方向输出遍历|1.86|1.77|
|Lane Apply 合计|5.15|5.50|
|— 建表排序|0.16|0.17|
|— **主循环(52k 非零格,13.3k 过行列过滤)**|**5.01**|**5.32**|
|— 路由检查(2,360/2,565 次,计 2 次时钟读开销)|0.75|0.83|

计数:每长帧 pops=52,320(全可行格),relaxes≈72.6-72.7k,laneOverrides=routeCalls(全部通过)。

1. **Dijkstra 主循环 ≈21.1ms 是绝对主导**(帧间隔的 57%)。全场流场本质上要弹出全部 52,320 个可行格;托管堆实现的单格均摊 ~0.4µs。
2. **Lane 的成本在 52k 格主循环体,不在路由检查(仅 ~0.8ms)**——这定量解释了车道 O(1) 候选 ABBA 四窗 >33ms 帧 85/85 毫无变化:它优化的部分只占帧间隔 2%。
3. 路径推论:
   - Lane 可想的下一个单一机制候选(只遍历含目标行/列的格子,跳过 38.7k 次空转)理论上限 ~3-4ms,37.2→~33.5ms 仍压不过 33.33 线,更不可能满足 P99≤0.80x 门槛——**单做 Lane 小改动大概率不值得一次完整 ABBA**。
   - 求解 21ms 的量级改善,在"不并行导航、保持同步/严格浮点"的约束内,托管侧难以达成;**已验证的 Burst 后备(正式 ABBA P99 比例 0.67/0.74,>33ms 帧 2-6 vs 85-86)正是针对这一主导成本的现成答案**,等待落库/启用决策。

## 方法与边界
- original→probed→probed→original 四窗,Green/2048/48/1920×1080/固定相机/GTX1060/D3D11/vsync0,35s 预热+15s 采样,与 residual 诊断同口径。
- 控制门:两对 probed/original P99 ±5%、median ±10%,本轮通过(P99 37.09/37.91/40.21/38.89,>33ms 85/85/85/84)。粗墙钟探针;laneRouteMs 含每次 2 读时钟开销;中位数不可逐项相加;不做 GPU/GC 归因;无零分配声明。
- solve/lane 各 284 行记录,容量 32768 未扩;probed 窗口逐帧对齐 aligned-detail.csv/json。

## 过程记录(如实)
- 第 1 次启动:误用过时保护入口 p3_lane_early::project_check,启动断言即退,未部署任何字节。
- 第 2 次:循环预检因 GridTests 未入豁免名单而失败(裸 assert 空消息);finally 恢复完整,protectionPassed=true;solve-detail-20261008-01 目录留档为失败尝试。
- 第 3 次(solve-detail-20261008-02,窗口 p3-solve-detail2-1..4):完整通过。

## 保护与证据
- 最终审计 protectionPassed=true:3 个探针文件(Grid/Lane/观察器)逐字节恢复,GridTests 以采纳哈希 8bddd614 钉死校验,HEAD aea3699/index/Packages/用户 4 文件/37 包 387 文件未变,无进程/锁。
- 证据:Logs/.../P3/solve-detail-20261008-02/{summary,scope,registry}.json、original/probed 字节、p3-solve-detail2-1..4 窗口(probed 窗含 solve-detail.csv、lane-detail.csv、aligned-detail.*)。
- payload:Tools/InteractionRefinement/p3-solve-detail-20261008-payload.json(15 处替换);脚本 build_solve_detail_payload / p3_solve_detail(一次性,勿重跑)。

## 建议下一步(待用户确认,本轮不执行)
1. **优先:Burst 默认关闭后备正式落库**(主导成本 21ms 的已验证解,P99 门槛全过)——分支/PR 流程需确认。
2. Lane 行/列跳转小候选仅在 Burst 决策之后再评估是否仍有意义;单独做收益上限不足以过门槛。
3. 不建议继续托管侧 Dijkstra 微优化(约束内收益量级不符)。
