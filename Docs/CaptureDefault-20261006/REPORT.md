# 据点默认开战修复与验证 · 37版

日期：2026-10-06。结论：**默认据点推进回归已修复，定向测试和独立EXE验证通过。**

## 交付入口

在远程工程根目录运行：

```text
Builds\CaptureDefault-20261006-37\Start-Game.cmd
```

完整路径：`E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\Builds\CaptureDefault-20261006-37\Start-Game.cmd`

- 构建GUID：`883f210b17e4424b95593f4a094efa70`
- Windows x64 Development，默认2048人；启动脚本为1920×1080独占全屏。请保留完整包目录，不只复制EXE。
- Unity 6000.3.14f1；构建0错误、20警告。
- 使用既有 `Assets/Game/Contact34/` 四个场景；未执行Prepare、复制新场景或覆盖34版。
- 现役入口统一记录在远程工程 `Docs/CURRENT_DELIVERY.md`。本报告中的工程路径均相对该远程工程，非Arena本地镜像。

## 修了什么

既有产品代码只修改 `Assets/Game/Scripts/WarSandboxBattleController.cs` 三处：

1. 私有下令入口新增默认关闭的 `allowCombatAlongRoute` 参数。
2. **仅默认据点开战**启用它：保留据点坐标、Move记录和单点路线，同时使用可交战的 `Advance=1`。
3. 玩家显式Move/Retreat仍为 `MoveOnly=3`，显式Hold仍为 `HoldHere=4`；Attack仍可恢复进攻。

显式据点目标仍优先于动态敌情目标。未修改引擎、HLSL、GPU数据结构、导航算法、人数、角色参数或场景资产。另新增16项契约测试及显式启动参数控制的诊断/构建工具；普通启动不启用诊断探针。

## 真实复现与修复对照

均为原默认阵容1024对1024，仅将本局规则切换为据点模式后正常开战。**首轮自然结算之前没有补发Hold/Attack/Move，没有修改HP或强制胜利。**

| 项目 | 修复前Editor | 修复后Editor | 修复后37版EXE |
|---|---:|---:|---:|
| 观测时间（实秒） | 90.00 | 73.39 | 70.32 |
| 双方GPU姿态 | MoveOnly / MoveOnly | Advance / Advance | Advance / Advance |
| 双方攻击数峰值 | 0 / 0 | 255 / 166 | 287 / 219 |
| 最终存活 | 1024 / 1024 | 809 / 0 | 763 / 0 |
| 结果 | 无结算、HP不变 | 自然歼灭结算 | 自然歼灭结算 |

修复前持续争夺约65.04秒仍无交战，确认了审计中的回归链路。修复后保留据点推进，并实际产生攻击、HP下降和伤亡。

硬件日志确认均使用 **NVIDIA GeForce GTX 1060 3GB / Direct3D 11**，未使用 `-nographics`。修复前证据来自Editor，不冒充旧34版EXE复现。

## 定向回归

- **EditMode 72/72**：新增16项、现有控制器56项。覆盖两队/小三队默认指令、保留据点目标、显式Move/Hold/Retreat/Attack覆盖、拒绝缺目标命令时保留状态及默认歼灭战行为。
- **Editor和EXE各29项衔接检查**：自然结果与GPU人数一致、终局冻结/拒绝普通指令、重开、显式覆盖、手动结束不虚构胜者、返回布阵保留草稿、重新应用、目录重入和状态清理。
- 小三队为每队64人的EditMode契约检查，不宣称三队GPU自然对局或高人数性能已通过。

同一新GUID的独立EXE结果：

| 用例 | 结果 | 存活位置通行检查次数 |
|---|---|---:|
| 默认据点战及衔接 | 通过 | 138,294 |
| 接敌后显式移动 | 通过 | 208,902 |
| 接敌后防守 | 通过 | 203,713 |
| 接敌后撤退 | 通过 | 208,849 |
| 开阔推进 | 通过 | 14,335 |
| 有限障碍绕行 | 通过 | 334,996 |
| **本轮合计** | **6/6** | **1,109,089** |

六个进程均正常退出，记录的运行时异常列表为空。检查使用1280×720窗口启动参数与API/uGUI回调；**不是OS真实键鼠验收**。

## 保护与证据

- 905个既有保护文件逐阶段核对，只有批准的控制器源码发生变化；引擎与场景保护项无意外变化。
- 34版的启动文件、EXE和关键程序集保持原字节哈希；未改旧包、真实方案或用户设置。
- 保留TinyHero Albedo及meta和材质构建门禁。本次四个场景的实际回调均检查68个单位配置；完整沿用第三方说明和许可证。
- 无commit/push；既有脏工作区与历史改动未回滚、打包暂存或整体整理。

主要远程证据目录：`Logs/CaptureDefault-20261006/`

| 证据 | 相对该目录的路径 |
|---|---|
| 原字节保护清单/备份 | `baseline-manifest.json`、`backups/` |
| 复现与修复后Editor原始采样 | `baseline/observations/receipt.json`、`fixed/observations/receipt.json` |
| 新用例首轮红灯、最终72项测试 | `tests-red/`、`tests-green/results.xml` |
| 最小产品差异 | `product-fix.diff`、`product-fix.json` |
| 构建及日志 | `build/build.json`、`build/Editor.log` |
| 六项独立EXE原始证据 | `player-capture/`、`player-move/`、`player-hold/`、`player-retreat/`、`player-open/`、`player-wall/` |
| 最终汇总/源码与产物哈希 | `delivery-verified/delivery.json`、`delivery-verified/changed-source-hashes.json` |

控制器SHA-256：

- 修复前：`c2e880508b95765c3040793f994ebe58f6dad8590519757a3f22084d45841802`
- 修复后：`e047e5514a914610117ca8922df0482c479735fe68bcb654e78f7a6af7dc171f`

### 验证过程中的两项说明

1. 首轮新增测试有14项捕获默认姿态回归，另1项发现旧平面接口接受NaN目标。该防御性输入缺口未纳入本次产品修复；原始红灯结果保留。本轮状态保留用例改为现有API明确定义拒绝的 `hasTarget=false` 命令，最终16项全部通过。详见 `test-fixture-note.json`。
2. 首次交付汇总错误地要求场景处理回调已有最终GUID，因此停止。核实原始记录后确认：Unity该阶段的GUID均为零，包括历史记录；本次四个真实门禁回调并未缺失。最终按本次独占构建日志的UTC时间窗核验，再关联最终BuildReport GUID。没有跳过门禁、改产品代码或重建包。失败记录和说明保留于 `material-binding-note.json`。

## 未覆盖与下一步

- 本轮默认据点模式由**歼灭条件**自然结束，不把它写成无人干预的据点倒计时胜利验收。
- 不宣称全量测试、全部场景自然对局、1080p人工手感、连续画面质量或高人数性能已验收。
- `osInputTest=false`；此前35轮的前台/真实键鼠项仍待用户在机器前配合，不自动重试。
- 下一步仅需试玩37版默认据点流程并反馈具体问题；不新增场景、内容或高人数任务。NaN接口防御问题另行排期。
