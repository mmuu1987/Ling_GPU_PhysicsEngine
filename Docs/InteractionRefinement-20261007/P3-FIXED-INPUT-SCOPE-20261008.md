# P3 CreateFlowField 固定输入分析：范围与口径

用户授权继续固定输入分析，不重新采纳已撤回的Cardinal缓存。

## 当前执行
- 执行器：Tools/InteractionRefinement/p3_fixed_replay_20261008.py
- 证据：Logs/InteractionRefinement-20261007/P3/fixed-input-20261008-01/
- 原始捕获与重放：同级fixed-input-20261008-01-capture/
- 完成或失败后自动生成：P3-FIXED-INPUT-20261008.md。状态以summary.json为准，不以进程已启动当作成功。

## 方法
保持正式Green、2048人、分离48与刷新率。取35–39、39–43、43–47秒三个窗口中双方各一份真实动态CPU目标列表，共六份；在CreateFlowField返回、Lane修正前复制原始方向结果。此复制会扰动捕获窗口，因此不将该窗口用于性能改善结论。

捕获结束后，在同一不可变导航图上重放原顺序目标与同一停止半径。每份先核对现场结果，再进行3对预热和12对原实现/分段计时版交错重放。完整结果按每个float的原始位逐项比较，含零方向；不是只比数量、长度或近似方向。计时版从现役CreateFlowField方法机械复制，只加四段边界时钟，堆与比较、邻居顺序、边代价和浮点表达式不改。

四段：验证与结果分配/工作数组重置、目标入堆、Dijkstra遍历、方向输出。所有结果对拍及磁盘写入都在计时外。分配字节和GC代数次数分列，不将分配等同GC暂停；没有显式GC.Collect。无新优化候选，不做全场ABBA重复。

## 可重现输入格式
固定图为BinaryWriter小端：字符串P3_FIXED_GRAPH_V1（.NET 7-bit长度前缀）、int32分辨率X/Z、float32 CellSize/OriginX/OriginZ、float64 maximumX/Z、float32 Clearance、int32 CellCount；随后N个uint32 walkable、N个byte neighbours、4N个float64 edgeCosts。完整地表不另复制：CreateFlowField本身使用已冻结合法边及代价，不在重放中调用地表采样。
输入：字符串P3_FIXED_INPUT_V1、float32 stopRadius、int32目标数、目标序列float32 x/y。预期输出：int32格数、逐格float32 x/y。图/输入/预期输出/报告均记录SHA256。

## 保护
原Cardinal等价测试及meta继续保留。三份临时诊断源码原字节备份，捕获/重放结束并确认无项目占用后只恢复本轮仍匹配的字节。失败即停止，保留编译/运行日志。37包和Git不改；不调用历史恢复脚本，不覆盖原交接证据。
