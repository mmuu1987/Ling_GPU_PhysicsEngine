# P7：军团内GPU框选与批量选中环检查点

2026-10-07。用户明确授权“行吧，进入P7吧”。**P7开发验证检查点通过，停在P7；未进入P8，也未制作38包。**

状态：`P7_DEVELOPMENT_SELECTION_CHECKPOINT_PASSED`。这里的“通过”指下面列出的自动逻辑、GPU、图像和回归检查点，不冒充真人鼠标易用性或任意规模性能签收。普通试玩仍不开放局部选择入口：P8必须先统一全部命令作用域。

## 1. 已实现

- 先选当前军团，再在非UI战场区域左键拖框。6物理像素阈值；反向拖动使用同一归一化矩形。
- GPU按真实teamId、hp和士兵地面中心屏幕投影筛选；只选当前军团存活成员，排除镜头后方、近远裁剪面外和视口外。
- 采用投影选取，允许选到遮挡后成员；不增加逐兵Collider或逐兵射线。选中环仍使用正常深度测试，可能被地形遮挡，不能拿环的可见数量替代选区数量。
- 松手时冻结view、projection、pixelRect、选框与裁剪距离。异步处理期间相机移动不改变这次投影。快速请求使用一组在途回读＋最新待处理矩形；等待旧回读时保留的是松手相机参数，实际成员位置来自随后GPU处理时刻，不伪称完全冻结松手瞬间的世界状态。
- 选区拥有独立epoch、team、request和成员version。重新框选立即进入Pending，不暴露旧选区供新命令使用；旧结果、切军团和换buffer代次的返回都丢弃。
- 替换式选择；空框是Empty，不回退WholeArmy。切军团、Esc和生命周期清理只改选区，不修改历史局部命令。
- GPU按当前hp剔除死亡成员；常规存活刷新间隔0.25秒，异步结果到达后更新数量/成员版本。渲染器另查当前hp，死亡环不必等待CPU回读才隐藏。数量属于最近确认的GPU样本，存在采样与回读延迟。
- 一个网格＋间接实例化draw绘制所有选中环，无逐成员GameObject。环中心直接读取当前agentBuffer，半径来自实际单位GPU设置和scale；不改VAT、teamId、64-byte AgentData或战斗内核。
- 显示整军团／确认中／局部已选N与军团存活M／零选中／不可用，多个历史命令序号显示“混合命令”，不伪报统一目标。

## 2. 输入和命令安全

优先级：模态/UI → 部署编辑 → 目标选点 → 框选。相机右键/中键/Alt/滚轮动作不与框选共享捕获；拖框时相机输入仅通过独立捕获查询阻止，不使用解锁其他系统输入的方式。失焦、帮助、待确认操作、切军团、离开/改变视口和结束/重开释放捕获、丢弃旧请求。

开发预览仅由显式`BeginSelectionPreviewForDevelopment`启用；没有新增普通UI开关、快捷键、序列化开关或启动自动入口，非Development Player拒绝启用。预览期间四个uGUI命令按钮置灰，按钮回调、热键共用方法和小地图共用的移动/追加方法全部拒绝下令。GPU确认错误保持不可用，不悄悄转回整军团指挥。

直接用于测试的Controller整军团API保持原有语义。P7没有给玩家接通移动/攻击/撤退/待命的局部作用域，这是P8工作，不是被漏掉的普通入口。

## 3. 实体身份与历史命令快照

选中ID是agentBuffer的稳定槽索引，不是LOD可见实例序号。检查UploadInitialData、模拟双buffer交换与可见列表路径：本局内没有实体压缩重排；重新分配/Reset时以manager buffer对象＋agentBuffer对象身份失效整代选区。LOD列表变化不改变实体身份。

P7选择epoch与P6局部命令通道epoch是两套生命周期标识，不能互相代用。后续P8必须验证选区仍属当前局/军团，再把成员副本和version交给当前P6通道，不能把后来的框选当作旧命令成员。

实际开发接桥证据（非玩家入口）：

`Selected=1024; receipt snapshot=1024; submitted=1024; GPU executing=1024; memberVersion=7; immutable caller copy; clearing selection preserves receipt. Explicit developer bridge, no P8 player entry.`

不仅比较显示文字：GPU确认的选择数量、不可变成员副本、P6 receipt成员快照、memberVersion、已提交人数及实际GPU执行人数均作一致性断言；随后取消选择仍保留已发命令。

## 4. GPU、真实图像与资源记录

受控GUI Editor，D3D11／GTX1060 3GB。投影测试使用交错team0/team1、死亡/镜头后方/远面外的可控GPU数据；另有真实Green 2048场景的历史局部命令与选择接桥。控制fixture不是正式混编大军性能结论。

`Interleaved team0/team1 in actual GPU buffers; dead, behind-camera, beyond-far rejected; captured matrix survives camera move before dispatch; indices=0,2,7; team alive=5. Controlled fixture, not authored-roster gameplay performance.`

`Automatic camera to isolated RenderTexture; actual cyan ring pixels=180; no per-member GameObjects; logical GPU bytes=16412; no FPS claim.`

选中环图像来自自动相机渲染到256×256独立RenderTexture；隔离到专用测试layer，非生成图。先验证三个环的真实像素，再只移动agentBuffer中的一名成员、不重新框选，验证环已出现在新位置。这里没有用Draw调用计数冒充可见渲染，也没有用这张隔离图冒充正常战场HUD截图。

![真实GPU选中环](../../Logs/InteractionRefinement-20261007/P7/selection-04/p7-gpu-rings.png)

![移动GPU位置后](../../Logs/InteractionRefinement-20261007/P7/selection-04/p7-gpu-rings-moved.png)

`Observed 4 async mask/count pairs in 1.1s; each pair=8200 logical bytes; no synchronous agent data readback in production selector.`

2048总成员时：选择mask与索引8×N bytes、两计数8 bytes、间接参数20 bytes，共16412 bytes逻辑ComputeBuffer容量。不包含mesh/material、驱动内部append计数/分配开销或显存峰值。每组异步读回4×N＋8＝8200 bytes。仅在开发预览打开时存在；关闭释放。主动框选可额外触发处理，常规存活维护不是逐帧回读。

不逐帧同步GetData全部agent，也不调用WaitForCompletion/WaitAllRequests。测试夹具中的同步GPU数据读写只用于建立确定场景和验证结果，不进入生产选择器。

## 5. 测试与回归

| 最终采用运行 | 通过/总数 | 跳过 |
|---|---:|---:|
| selection-04 | 6/6 | 0 |
| local-01 | 5/5 | 0 |
| legacy-kernels-01 | 67/67 | 0 |
| deployment-01 | 6/6 | 0 |
| scenarios-01 | 3/3 | 0 |
| editmode-02 | 730/730 | 0 |

合计817次测试执行，不是817个互不重复的新测试。730 EditMode包含原715＋15新选择状态/投影用例。6项P7 GPU/UI/渲染测试覆盖交错军团、死亡、捕获矩阵、快速连框、team切换、buffer reset、空选、批量环及其移动、真实uGUI禁用与强制回调、历史命令和成员快照接桥。

`Production pointer handler driven with explicit synthetic frames: UI and camera priority, focus loss, mouse-up projection, team switch, leaving/resizing viewport and help-open cancellation. Not OS input acceptance.`

`Two real P6 local Hold receipts; broad live GPU selection displays mixed commands; four real uGUI buttons are disabled and forced onClick invokes remain safe; all five legacy HUD order methods reject during preview (including append/minimap common move path). Esc and team change preserve both sequences. Explicit Controller whole-army API retains original semantics and clears local groups.`

输入验证采用生产pointer处理器的合成帧和真实uGUI按钮回调，不是OS真人鼠标操作。窄窗口可读性、不同DPI下手感、长时间频繁拖框、超大军团和跨硬件性能仍待后续人工/发布资格复核。未声称P7帧时性能等效；P6既有性能边界照旧。

旧回归覆盖局部GPU5项、旧核67项、P4/P5交互6项和P6实际场景3项。完成回归后只在测试文件追加选择到P6 receipt的一致性断言，再跑selection-04；audit-02确认相对audit-01只有该测试文件改变，生产候选没有改变。没有冒称此前回归重跑了第二遍。

## 6. 失败记录与修正

- selection-01：1/6。五项受控夹具把manager.enabled设false，触发OnDisable释放buffer，造成空引用；改用enableGpuDispatch=false冻结调度而保留buffer。这是夹具设置错误，未修改引擎Release语义。
- selection-02：5/6。初始环像素已成功，但RenderTexture沿用了小的normalized viewport，移动后固定像素区域断言失败。测试改为显式全纹理视口/aspect=1后继续验证，不是移动了一个假CPU图标。
- selection-03：6/6。作为完整初验保留；selection-04增加实际成员快照接桥后再次6/6。
- 开发预览遇到初始化错误时保持命令入口禁用；缺少明确军团时提示先选军团。没有扩大普通玩家入口。

所有失败、日志与图像保留；没有提高超时掩盖这些失败。

## 7. 保护和下一步

9675基线＋27批准source/meta核验。既有生产变更限HUD四文件与相机输入一文件；四个既有测试只兼容P7隔离参数。新增选择状态、GPU选择器、HUD partial、三个shader/include资产、两个测试及P7隔离器，共九个新文件及meta。

MassEngine生产Core/模拟HLSL、正式兵种资产、48参数、存档格式与37包保持；P3暂缓项和P4/P5真人待验没有被冒充解决。用户4份数据、37包387文件、HEAD/stage、GUID和462项归档审计通过，无归档引用迁移。旧P6报告不改；计划/状态原版本留publication-docs-before。结束时无Unity/锁的终核见finalization.json。

**停在P7开发验证检查点。下一阶段需用户授权后再进入P8，统一按钮、热键、小地图和局部四命令。** 不自动生成38、不开始Onboarding，不重复既有Assets/Builds整理。

电脑报告：`Docs/InteractionRefinement-20261007/P7-REPORT.md/html`；证据：`Logs/InteractionRefinement-20261007/P7/`；工具：`Tools/InteractionRefinement/`。
