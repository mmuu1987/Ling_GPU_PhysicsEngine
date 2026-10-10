# 精简交接：37版保留，非法坐标防护源码已完成（未打包）

用户批准按审计建议复现、最小修复与定向回归。现役入口以 Docs/CURRENT_DELIVERY.md 为准，完整记录 Docs/CaptureDefault-20261006/REPORT.md。

## 最新源码任务：非法坐标防护（未打包）
- 用户离机期间完成已确认的命令输入缺口。仅WarSandboxBattleController.TryResolveMoveTarget加8行有限数值检查；直接Move/替换/追加/默认据点/派生Retreat共用，其他行为不改。
- 新增49项输入测试，修复前48失败/1正常对照通过；修复后49+16据点+56控制器+13地形=134/134全绿，0跳过。非法输入不误启战、不恢复暂停、不更改已有路线/姿态/其他军团；没有将坏坐标送GPU。
- 1298保护项仅该控制器变化，37完整目录387文件哈希不变；没有GPU对局/新EXE验收，没有38包、真实存档操作或commit/push。
- 当前源码SHA `7a51930a574aa4672adff67377208e751913706bb799d493a1aaef3392e87f91`；37包仍为原GUID，不能说37已包含这次防护。
- 记录 Docs/CommandInputSafety-20261006/REPORT.md，证据 Logs/CommandInputSafety-20261006/（red/green、原字节备份、diff、closeout）。以后必要出包时再纳入；真实键鼠继续等待用户方便，不自动重试。

## 当前交付
`E:\GitHub\Ling_GPU_PhysicsEngine\Ling_GPU_PhysicsEngine\Builds\CaptureDefault-20261006-37\Start-Game.cmd`
GUID `883f210b17e4424b95593f4a094efa70`；默认2048、1920×1080独占全屏；0错误/20警告。34和所有旧包保留。沿用Contact34四场景，只Build一次新目录，无Prepare。

## 37版改动与证据（历史）
- 产品源码只改 WarSandboxBattleController.cs 三处：私有allowCombatAlongRoute默认false，仅StartDefaultBattle的ControlPoint调用true；据点仍保留点目标/Move路线，但姿态Advance=1可交战。显式Move/Retreat=MoveOnly3、Hold=HoldHere4、Attack=Advance1不回退。SDK/HLSL/场景/人数/半径/角色不改。
- 真实GPU原始Editor复现90秒：双方MoveOnly、约65秒持续争夺、无攻击/HP变化/结算；修复后Editor73.39秒809/0、EXE70.32秒763/0自然歼灭结算。首轮没有补发指令、HP修改或强制胜利。
- EditMode72/72（新增16+原56）；两队/小三队契约覆盖。Editor/EXE各29项战后衔接检查；同GUID独立EXE六项（据点、移动、防守、撤退、开阔、障碍）全部通过，1,109,089次存活位置检查，运行时异常列表为空。非全量/性能/OS输入验收。
- 905保护项只有批准的控制器变化；保留TinyHero Albedo/meta、构建材质门禁与许可证。源文件原字节备份/差异、测试、六进程证据在 Logs/CaptureDefault-20261006/；正式汇总 delivery-verified/delivery.json。
- 材质回调执行时GUID为零（历史也如此），以本次独占构建日志UTC窗的四条真实回调关联最终GUID；初次汇总的错误假设与失败记录保留，没有重建或跳过门禁。
- 测试另记旧平面API接受NaN目标的问题，未为其扩改产品；本轮拒绝原子性用例采用现有hasTarget=false契约。详见test-fixture-note.json。
- README、Game README、ROADMAP、GAME_DESIGN的旧当前入口均转为历史并链接现役索引；没有commit/push，既有脏工作区保留，禁止git add .。

## 历史口径勿混用
34接敌指令SDK修复与33-r2导航修复继续保留；34历史五项通行检查为975,056，不是逾百万。36只有Editor44项（歼灭786/0；脚本化占点1024/1024），不是36包，也不是默认双方据点战验证。详见 Docs/CONTACT-COMMANDS-20261006.md、Docs/STRIPE-FIX-20261006.md、Docs/ENDING-CHECK-20261006.md。AdvanceLanes-20261005-33是历史失败包。

## 后续与约束
- 等用户试玩37版默认据点流程后按具体问题小步迭代。默认据点本轮由歼灭条件自然结算，不冒称无人干预倒计时占点胜利。
- 35连续画面/OS前台键鼠独立挂起：历史ownedWindowFound=true、setForegroundReturned=false，需用户在机器前配合；osInputTest=false，不自动重试。
- 场景开发暂停，继续uGUI，不换UI Toolkit，不扩512，不改认可Q版/09-10预览拖动，不改角色大小/导航半径，不自动压密或做高人数优化；保留高人数确认。
- 不覆盖旧EXE，不强关用户Editor，按项目锁区分其他Unity；禁止-nographics与重复既有Prepare/Build。商店/Cookie操作停止；保护pelican SVG/meta及19 RuntimeDependencies/TinyHero/Albedo/meta。
- 用户要求简洁回应；318旧文件归档archive/，不要整体解压。MCP可用但不打印凭据。旧23/22B/33-r2入口按历史保留。NEXT_TASK仅本地交接，不纳入新提交。
