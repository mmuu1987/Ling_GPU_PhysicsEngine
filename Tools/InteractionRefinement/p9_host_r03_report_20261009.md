# P9 第三版：统一积分/写回，真实冷缓存验证

2026-10-09。当前工程 `outputs/P12`，生产候选 revision 3，仍 **NOT accepted**。Unity 6000.3.14f1 / D3D11 / GTX 1060 3GB；Burst 默认关闭。

## 1. 本轮实际改动

已执行上一份报告的下一步，而非仅提出方案：

- v14 比较基线、辅助函数单次写回、循环单出口三种结构，共 18 个 CPU 编译样本。辅助函数收益不一致；循环单出口显著恶化，部分核达到数十秒，未部署。首次 v14 因原生编译器嵌套 include 解析失败，原失败记录保留，v14b 使用展开源码完成筛查。
- v15 将暂停/死亡/LOD/正常决策先计算，再汇合到一个积分和写回位置。保留 Hold 的约束顺序和正常移动的转向顺序，不增加 GPU dispatch 或 scratch buffer，不移除墙体、切角、边界、导航掩码或射界逻辑。
- 新增 **144 项 GPU 差分全部通过**：六类 stance/type × 12 场景 × 单帧/32帧，整数精确、浮点容差 1e-4；包含暂停、死亡、LOD、地形/墙体和远程 launch 状态。这不是弹丸飞行专项，也不是 144 种独立场景；历史 194 项原型证据另列，不混算覆盖率。
- 部署为 revision 3 时仅替换一个生产 compute 文件；生产清单仍为 12 文件。使用 `LP_STANCE` 条件保护，**legacy global 入口保留上一版逻辑**。revision 2 全部生产文件及 manifest 已归档。

### 六类 CPU 编译筛查（秒，单次 O3，不是 Unity 点击耗时）

| 内核 | 本轮基线 | 统一积分/写回 |
|---|---:|---:|
| Attack 近战 | 5.870 | 4.876 |
| Attack 远程 | 12.646 | 9.793 |
| Move 近战 | 3.863 | 3.062 |
| Move 远程 | 6.494 | 5.686 |
| Hold 近战 | 6.740 | 3.962 |
| Hold 远程 | 10.040 | 6.456 |

## 2. 第三版真实冷缓存 A/B/A

仍是 2048 单位、选中 4 个的原诊断。仅隔离 P12 指定 combat shader 缓存，不清母工程、全局或驱动缓存。

| 冷命令最大 yield 间隔 | 原版 | 第一版候选 | 第三版候选 |
|---|---:|---:|---:|
| Attack | 64.7468 秒 | 7.0529 秒 | **4.9468 秒** |
| Hold | — | 6.7250 秒 | **3.9285 秒** |
| Retreat | — | 3.7270 秒 | **3.0499 秒** |
| Move（本诊断顺序中在 Retreat 后） | — | 2.0798 ms | **2.0667 ms** |

Move 已复用 Retreat 使用的 Move 类内核，**不能将此数值当作 Move 首次独立冷编译成绩**。

第三版 cold B 的 observed/plan/maxgap，单位 ms：

| 命令 | observed | plan | maxgap |
|---|---:|---:|---:|
| Attack | 5293.0659 | 328.3582 | 4946.8357 |
| Hold | 3933.3945 | 0.0624 | 3928.5203 |
| Retreat | 3083.1249 | 27.1275 | 3049.9341 |
| Move | 32.0905 | 23.3559 | 2.0667 |
| 第二次 Attack | 29.5966 | 24.1593 | 2.1285 |

- 最大 Attack 停顿采样包围段 ShaderCompiler CPU **4937.5ms**，Unity CPU 515.625ms；0.5 秒轮询包围，不是精确 Profiler 分段。
- warm A Attack observed 371.1381ms、plan 325.9316ms；restored A observed 371.1887ms、plan 332.9321ms。两者最大间隔包围段 ShaderCompiler CPU 都为 0。
- 第一版→第三版的前三类冷停顿进一步下降约 29.9% / 41.6% / 18.2%。仍为秒级停顿，不予验收。
- 冷诊断前四命令 observed 合计 **12.3417 秒**，上一版 18.0342 秒。整段测试 **74.5560 秒**，上一版 79.4264 秒，原版 122.1108 秒。不得将单个 Attack 的改善比例外推为整个流程改善。
- 缓存 **55 文件 / 2828792 字节**精确恢复；restored run 后仍精确。后续功能测试可能合法生成缓存，不把恢复时点证明外推为缓存永远不再变化。

这些都是单轮样本。六类核的 CPU/GPU 差分已补齐，但真实冷点击仍沿用原近战选四场景；远程和混合兵种的首次冷命令专项仍待补齐。

## 3. 没有忽略加载阶段

从已有阶段 CSV 提取 `radius-save-reenter → entered` 区间。这包含进场、加载和初始化，并非纯 shader 编译计时：

| 版本/缓存 | 进场区间 |
|---|---:|
| 原版 cold | 40.6223 秒 |
| 第一版候选 cold | 43.9659 秒 |
| 第三版候选 cold | **44.4088 秒** |
| 第三版 warm A | 4.1932 秒 |
| 第三版 restored A | 4.2504 秒 |

因此加载侧没有解决；也不能宣称完全没有成本转移。第三版只是降低了部分命令编译成本。

进一步执行了 **v16 全局入口 CPU 筛查**：同样的统一积分结构，非 Local 全局核 40.8115→30.6552 秒，Local fallback 核 72.5700→55.2790 秒。它仍很慢，而且目前 **只做了 CPU 筛查，尚未 GPU 差分，也没有部署**；不能当作已降低 44 秒进场耗时的证明。

## 4. 第三版完整生产回归

| 测试 | 结果 |
|---|---|
| 成员表、default/null ShaderSet | 5/5 |
| Golden | 3/3 |
| 原版回归 | 60/60 |
| 独立 host 路由 | 4/4 |
| 冷诊断初次 / warm / cold / restored | 四轮均通过功能检查 |
| 联合自然战斗 | 1/1，129.6652 秒，AttackerVictory |
| 2048 单位三轮重复进出 | 1/1，47.1184 秒 |

联合测试包含自然结算、重启、秋/绿场景切换、实际失败 UI 返回后重载以及资源释放。未强制指定胜方。

暖态固定镜头，每轮 300 帧：

| 轮次 | 中位 wall-frame ms | P95 ms | 最大 ms |
|---|---:|---:|---:|
| 1 | 10.9990 | 12.6468 | 40.0306 |
| 2 | 10.8560 | 12.0408 | 14.8728 |
| 3 | 10.8483 | 12.2803 | 20.2944 |

这不是 Player FPS 签收；第一轮有 40ms 尖峰，不能写成“全程稳定 30FPS”。三轮卸载后的纹理/RenderTexture/材质/GameObject 数保持 1134/34/223/61，native workspaces 均为 0。Unity allocated 为 1561601211 / 1563711873 / 1563845865 字节；reserved 后两轮为 2032254976 字节。短窗口不能证明长期无泄漏。

## 5. 保护、版本与证据

第三版全量内容 SHA256 审计通过：母工程保护文件、P11 原源文件、用户文件、HEAD/index 均无差异；P12 **9761 个声明文件**及文件集完全匹配。生产清单 12 文件、prototype harness 44 文件、独立 diagnostic harness 2 文件。没有遗留本轮 Unity 进程，没有提交、合并、发布 38、默认开启 Burst，或代签真人/Player 验收。

证据根目录：`outputs/P9ColdFix-20261009-01/`。

- `compiler-specialization-v14b.json`、`compiler-specialization-v15.json`、`compiler-specialization-v16.json`
- `fixed-unified-01.json`：新增 144 GPU 差分
- `host-r03-cold-analysis.json`、`host-r03-cache-aba.json`、`host-r03-stage-intervals.json`
- `fixed-membership-03`、`fixed-golden-02`、`fixed-regression-03`、`fixed-hosttrace-04`
- `fixed-cold-05..08`、`fixed-joint-02`、`fixed-repeat-02`
- `host-r03-final-audit.json`、`host-candidate-revision02/`、当前 `patch-manifest.json`

单次部署、归档、A/B/A 和准备脚本不可原样重跑；不要用旧生成器覆盖第三版或重建 meta GUID。

## 6. 下一步未完成项

1. 继续缩减选中核的剩余复杂度，必要时做真正的跨 GPU dispatch 阶段拆分；本轮只统一了同一 kernel 内的控制流，没有实施多阶段 GPU scratch 管线。
2. 对 v16 全局候选补齐无 Local、混合 Local、全军 stance、charge、暂停/死亡/LOD 的 GPU 差分；未通过前不替换全局入口。之后再实测加载/命令分段冷缓存，不能只报 CPU 编译秒数。
3. 暖态约 330ms 规划独立处理。静态检查发现 `MassEngineManager.LocalOrders` 的 factory 会在 local 通道首次请求某半径时构建新的 `TerrainNavigationGrid`；`LocalOrderPlan.TryCreate` 的计时包含此调用。**尚未测量构图/连通分量/流场各段耗时，不能直接认定全部 330ms 都来自构图。**
4. 如果考虑复用已有导航图，只能复用完全匹配的不可变拓扑并保留独立工作数组；现有 `CreateFlowField` 明确非并发/非重入，不能直接把全军和 local 的同一个对象交给并行规划。
5. 补齐远程/混合兵种首次冷点击、独立冷 Move、类型掩码被直接 SetData 绕过的同步边界，以及改令/取消/死亡/重置/多 manager 覆盖审视。

**当前结论：第三版实质改善且上述回归通过；冷态停顿、加载侧成本和暖态规划均未完成性能验收。**
