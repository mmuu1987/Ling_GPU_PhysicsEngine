# 新 Windows 构建与低负载独立验证

> **历史专题 / 入口提示（2026-10-03）：** 本页保留该阶段原始记录；当前正式入口已由 Version08 取代，见[工程导航](Engineering-20261003/README.md)与最新机器人交付记录。下文“当前”“下一步”及构建命令不作为现在的执行清单。旧手工测试仍暂停，其他模型转换未授权；技术结果与人工认可范围按原记录保留。


工程根目录：`E:\GitHub\_worktrees\WarSandboxBattlefieldRules`。Unity Hub 打开此目录，不是 `Assets`。
入口场景：`Assets/Game/M71LaunchPresets/LaunchMenu.unity`。

## 当前交付准备状态（2026-09-30）

本轮只有[V1候选外置补充资料](V1Candidate-20260930/README.md)，**不要因为阅读本页就执行构建/探针/打包命令**。原RangedPlaytest候选、GUID与ZIP不变；当前100k手动结果29.43/29.70FPS，两窗均<30。资源许可仍待确认，尤其根LICENSE收集不能替代实际运行组件/嵌套字体的义务核对。只有后续另有明确授权且确需新构建时，才使用下面的维护流程。

## 安全构建入口

Unity 菜单：**MassEngine → Launch Presets → Build Reviewed Windows (new folder)**。
命令行 executeMethod：`MassEngine.Game.Editor.WarSandboxLaunchPresetsBuilder.BuildReviewedWindows`。

- 默认输出 `Builds/M7MotionReview-<UTC时间>`；也可通过该 Unity 子进程的 `WAR_SANDBOX_REVIEW_BUILD` 环境变量指定 `M7MotionReview-...` 的安全目录名。
- 已存在的目标目录直接拒绝，不覆盖早期候选包。失败构建的目录也不要复用，另取编号。
- 只构建现有场景，不调用 Prepare、重新生成内容或烘焙 VAT。
- Development / Windows x64；构建后恢复原有 EditorBuildSettings 和 frame-timing 配置。
- 构建日志、进程退出码、`WarSandbox_Data/boot.config` 中的 build-guid 都应保存。再以独立玩家进程的 `Application.buildGUID` 核对，不能只看 EXE 时间戳；Unity 的 EXE 引导程序本身可能保持不变，更新的游戏代码在 Data/Managed 等文件中。
- 串行运行；不要加 `-nographics`，不要终止用户自己的编辑器或游戏。可使用 `-force-d3d11 -job-worker-count 2` 和 BelowNormal 优先级。

本轮执行脚本和原始证据保存在 `Logs/AgentReviewBuild/`。脚本有只终止自有进程树的看门狗。为绕过既往 .NET 构建工具的内存问题，只对构建子树中的 .NET 进程设置各自的 1 GiB GC heap limit（不是 Unity 总内存上限）；没有修改 Windows 系统环境或页文件。

## 低负载功能自测

新构建的开发播放器支持以下参数（路径换成自己的**绝对、未使用路径**）：

```text
WarSandbox.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540 -force-d3d11 -job-worker-count 2
  --terrain-cycle --terrain-cycle-phase=motion-review
  --terrain-cycle-output=<新的绝对路径>/report
  --terrain-cycle-plans=<另一新的绝对路径>/plans
  --war-sandbox-settings-file=<独立新目录>/settings.json
  -logFile <新的绝对日志路径>
```

以上展示的是一条命令的参数，请按所用 shell 合并成一行并正确引用含空格的路径。

该模式：

1. 沿用正常首次自动进入 512 人准备战场的路径。
2. 在开阔、山地、据点、三方四个小预设中，检查布阵方案保存/改动/重载/应用、自然结算、结果稳定、满员重开、再次开战、回菜单和场景/GPU资源释放。
3. 验证源配置未改、已保存方案未改；使用隔离声音设置，只在内存静音，不保存偏好。
4. 限制目标帧率为 30，**不进入十万人预设**，不是性能模式；历史性能探针仍保持原行为。
5. `report.json` 记录 GUID、PID、阶段、结算、弹道总数、错误和渲染帧数。必须同时检查退出码 0、completed/passed、四局/四次应用/四次释放、无错误以及预期 GUID。
6. 截图先保存再断言；稀疏抽样颜色不足时扫描全部像素，仍保留原有 >24 个量化颜色和 <5% 抽样洋红像素门槛，并输出诊断计数。

边界：世界图像使用 URP 离屏渲染；按钮验证调用真实 uGUI onClick，不等于人工鼠标/键盘、桌面 Present、音效听感或 UI 遮挡验收。画面门槛只是空白/大面积缺 Shader 检查，不保证每个材质或每个角色细节无问题。单局自然结算上限 480 秒，首局另检查小于 300 秒；不注入胜负、不加速战斗以获得通过。

## 打包，避免把旧成绩带进新包

`Tools/package_playtest.py` 保留旧命令兼容性，**默认仍指向历史 M73FarLod**。新包必须显式提供：

```text
python Tools/package_playtest.py --name <新的包目录名>
  --source-build <Builds下新构建目录名> --expected-guid <实际32位GUID>
  --build-log <本次构建日志相对路径> --review-report <本次版本与验证说明相对路径>
```

- 非历史源构建必须提供当前版本说明；不能沿用旧包的帧率/通过项。
- 源 GUID 不符即拒绝；输出目录或 ZIP 已存在即拒绝。
- 运行文件逐项对照源构建；生成文件 SHA-256 清单、ZIP CRC 校验、ZIP SHA-256 和依赖/许可证记录。
- 审计写入 `Logs/<新包名>/package-audit.json`，不覆盖历史包审计。
- manifest 的 `recompiled=false` 只表示**打包步骤复制现有构建**，不表示源构建未经本轮重新编译。
- 校验：`python Tools/package_playtest.py --name <包目录名> --verify`。
- 快速脚本回归：`python -m unittest discover -s Tools/tests -v`，使用临时合成运行文件，不启动 Unity。

普通试玩双击包内 `Start-WarSandbox.cmd`，不带自测参数；保留整个目录，不要只搬走 EXE。自测限帧不改变普通游戏。人工接受度与 V1 签收仍由用户完成。
