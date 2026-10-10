# P3 Burst 后备：真实 Runtime / 场景 / 渲染资格验证

## 结论
真实 Runtime 连接及当前 Green 场景生命周期通过；原150项回归、3个实际启动配置 Runtime 用例、1个场景用例、6个有渲染 GPU 用例，共160次成功用例执行（不是160个新用例）。新候选正式 ABBA：**通过（仍仅后备，未启用）**。
候选始终是临时后备。所有轮次的源码、原项目 JIT、保护文件均已恢复；没有自动采纳、打包、提交或关闭 P3/P9。旧候选正式 ABBA 失败仍有效，不能被新试验覆盖。

## 真实连接与边界
- TerrainNavigationRuntime 在正常 Tick 中推进 Editor 准备策略；尚未 Ready 时 Upload 使用原托管求解。就绪且许可/实例仍有效时使用相同同步 IJob.Run 原生内核。
- 实际启动禁用和强制同步标志在分配与探测之前拒绝该准备路径。没有操作持久化 Burst 偏好开关。
- 点/区域/动态密度目标、两个排队队伍每 Tick 一个、未完成 GPU 回读时 Dispose、释放重建/旧实例 Rebuild 拒绝均有实际验证。求解算法、严格浮点、目标顺序、刷新、round-robin、同步 Run 未改；没有并行导航。
- 冷 Runtime：确实见到 Waiting；2次原托管、4次原生真实求解、0次完整 managed-native 求解，127次探测，逐位一致；2创建/2释放/0活跃。等待12680.2922ms，最大 Tick38.0708ms、探测15.6904ms，最终 Ready。
- 实际禁用/强制同步：各6次原托管、0原生/0探测/0工作区，结果一致、Unavailable。它们不证明运行中切换偏好、真实编译故障或超时恢复；这些只有此前注入策略证据。

## 当前场景生命周期
启动时项目 JIT 为空不代表进入 Green 时仍未编译完成。首次场景夹具错误要求入口必须 Waiting，失败保留；仅修正此前提后，生产候选逐字节不变。
有效场景用例入口已 Ready，命令后确认新的原生导航工作；重开释放、取消布阵、应用布阵并重建、返回释放、无效已加载世界失败后重试、直接 Green→Menu 均通过。4创建/4释放/0活跃。
load4193.0754ms；命令到新原生场152.1807ms不是编译耗时；最大准备帧间隔72.0609ms、探测8.8905ms。场景本身没有见到 pending 托管回退，冷 Runtime 用例独立证明它。该批1280×720生命周期测试不是正式渲染 FPS 或人工加载体验验收。

## 渲染试验及所有失败留档
1. qualification-01：登记表漏了未改动的已采纳测试文件，保护检查在启动 Unity 前拒绝。只补齐精确原字节登记，不扩大允许修改范围。
2. qualification-02：有渲染 GPU6通过。A1采样中相机从(250,280,-384)缩近至约(21.71,31.62,-33.35)，cameraFixed=false，窗口无效；没有运行候选窗口，不用其性能数字判门槛。原因不能直接归咎用户操作。
3. qualification-03：图形 Editor 在观测器标记/性能数据出现之前正常退出码0；无编译错误记录，退出来源未确定。不能据此宣称夹具已运行或产品失败。
4. qualification-04：执行器子 runner 误导入上一版登记表，被精确哈希保护在 Unity 启动前拒绝。属于本次自动化夹具错误，不是产品性能结果。
5. qualification-05：runner 显式绑定本脚本模块并断言；新的完整 ABBA 目录，不复用以上任何无效窗口。复用 GPU6之前逐字节核对同一生产候选，未声称重新执行了GPU6。
后续夹具仅通过现有 LockInput API 隔离测试自有相机输入、取消待处理运动、要求原宏观位置（不强行改写视角），只测 manager.cullingCamera，并检查 FOV。所有 A/B 用相同观测器，结束恢复锁状态；没有修改游戏相机源码、加载/UI或OS输入。

## 新正式 ABBA
原门槛保持：B1/A1和B2/A2各自 P99≤0.80倍、median≤1.05倍、>33.33ms帧数≤0.50倍、>50ms帧数≤max(A,3)。固定35秒预热+15秒采样；不为 Ready 额外等待，不挑窗口。
条件为2048单位、separation48、1920×1080、GTX1060 3GB/D3D11、vsync0/target−1、宏观相机(250,280,-384)。候选首采样必须 Ready，到末尾保持同一 Runtime，真实原生次数增长、原托管不增长、完整 managed-native为0、探测次数不增长，释放后工作区平衡。准备阶段的零单元托管探测明确计数，不能误写成全程没有托管调用。

|窗口|median ms|P99 ms|>33.33ms|>50ms|
|---|---:|---:|---:|---:|
|A1|5.7300|40.9800|86|1|
|B1|5.3700|27.4600|2|0|
|B2|5.4200|28.4700|6|3|
|A2|5.4400|38.5700|85|0|

B1/A1：P99比例0.670083，median比例0.937173，门槛{"p99": true, "median": true, "over33": true, "over50": true}；通过。

B2/A2：P99比例0.738138，median比例0.996324，门槛{"p99": true, "median": true, "over33": true, "over50": true}；通过。

原生路径/释放审计：
```json
[
  {
    "candidateAvailable": true,
    "sameRuntime": true,
    "startState": "Ready",
    "endState": "Ready",
    "startNative": 199,
    "endNative": 284,
    "startManaged": 0,
    "endManaged": 0,
    "startManagedNative": 0,
    "endManagedNative": 0,
    "startProbes": 1,
    "endProbes": 1,
    "created": 3,
    "disposed": 3,
    "active": 0,
    "allJobBurstCalls": 287,
    "allJobManagedCalls": 0
  },
  {
    "candidateAvailable": true,
    "sameRuntime": true,
    "startState": "Ready",
    "endState": "Ready",
    "startNative": 199,
    "endNative": 283,
    "startManaged": 0,
    "endManaged": 0,
    "startManagedNative": 0,
    "endManagedNative": 0,
    "startProbes": 1,
    "endProbes": 1,
    "created": 3,
    "disposed": 3,
    "active": 0,
    "allJobBurstCalls": 286,
    "allJobManagedCalls": 0
  }
]
```

## 判定与后续边界
旧正式第一对 P99仅改善16.19%且median增加6.08%，失败保留。新结果独立判定；本次准备/生命周期通过不等于所有长帧已解决。
Burst标为可行后备，不默认开启。不将Editor异步准备推断为Player/AOT行为；实际编译器故障的游戏层错误处置仍未证明优雅恢复。意外异常释放后原样抛出，没有增加加载取消，也没有保证无分配/无卡顿。
后续不得通过重复挑样或放宽指标翻转失败。若继续性能工作，先单独诊断真实求解及上传的尾部成本，不混入正式采样；任何启用须保留适用范围与取舍，出包另获授权。P3/P9及人工/Player门槛保持开放。

## 保护与索引
原7个登记源文件/asmdef/观测器均恢复，其中原测试内容不参与A/B变化；原JIT目录树/哈希恢复，生成缓存归档，其他BurstCache不变。9693+30、用户4文件、37全部387文件、包/HEAD/index检查通过，无本项目Unity进程或锁。pelican SVG/meta未动，既有二进制堆/输出复用/Lane优化和人数/48保留；37本身未重建、没有包含这些后续源码收益的新包。
Runtime：P3/burst-runtime-gate-20261008-01；场景修正：P3/burst-runtime-scene-20261008-02；渲染/恢复：P3/burst-runtime-qualification-20261008-01至05，原始窗口P3/p3-runtime-fixedcamera-r3-abba-{1,2,3,4}（仅实际存在的窗口算已执行）；GPU6：P8/p3-runtime-qualified-gpu-02。以上均在Logs/InteractionRefinement-20261007下。compact-analysis.json为最新精简汇总，summary/registry/scope/original/candidate及失败日志保留；一次性执行器不可重跑。
