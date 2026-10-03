# 本轮实际核对结果

2026-10-03。对象 GUID `eea0c353522542919393aa8b05eaaea0`；证据根 `Logs/Version08Delivery-20261003-01`。

## 文件与来源

- 当前目录 **364 文件 / 913,881,684 字节**；完整路径/大小/SHA256 在 `package-manifest.json`。该数字包含调试产物，并非最终发行白名单。
- 当前实际 Managed DLL **142**，原生 DLL **8**；其中 FBX 两程序集与 Unity.Formats.Fbx.Runtime 确实随包。
- 精确提取构建 Used Assets 大小行 **3,129 条**，见 `used-assets-exact.json`。初次宽匹配得到 3,176 个路径，仅作为检索索引，不作为 Used Assets 数量；原始 `build-assets.json` 保留。
- `BUILD-RESOURCE-MAP.tsv` 对应 **96 条重点资源记录 / 32 个 Generated 家族**，家族未映射项为零。覆盖模型派生件、原 VAT、UnityChan、字体及已知外部脚本；不是全工程依赖闭包证明，也不把配置/脚本记录等同于实际场景挂载。
- `PACKAGE-DEPENDENCIES.json`：构建日志出现的 **15 个包**均找到当前 lock 版本匹配缓存及原文；**27 份包内许可/嵌套通知**纳入候选资料。其他来源材料与标志/指引一起共 **68 个原文字节副本**，见 `NOTICE-FILES.json`，全部来源/副本 SHA256 对应。
- `ThirdPartyNotices/` 位于此交付资料目录，不在候选游戏目录；最终随包、条款接受方式与 About 放置义务仍未完成。

## 原生组件证据复用

`NATIVE-CORRESPONDENCE.json` 比对 09-30 已鉴定的 11 个组件：10 个逐字节相同，包含 UnityPlayer、两 Mono DLL、WinPix、两 D3D12 DLL、Onigwrap 和三个 FBX 相关程序集。可以复用其精确版本鉴定和许可原文，不代表旧包所有结论均可迁移。

`lib_burst_generated.dll` 不同：当前 SHA256 `3a19a456cbafddd33398de9b126953c65c5194934fd0e17fe79a7e8055d05686`。它是当前项目编译生成件，记录当前 Burst 1.8.29 的许可及构建来源，不冒称旧字节相同；本差异本身不是运行故障。

构建日志明确包含 UnityChanAtlas/UnityChanVAT、LargeSpider04 及 PlatformerEnemy01/PlatformerSkull01。不能把旧阶段“蜘蛛曾被拒绝”或“头部角色已隐藏”推导为本包未包含；隐藏选择与保留兼容资源分开。家族数量不是独立模型数量。

## 工具过程保留

首次声明索引脚本因遇到 Used Assets 中的非 Assets/Packages 行提前停止而触发断言，未把残缺清单报成通过；修正提取器后通过。首次依赖映射启动早于索引落盘，因输入缺失退出；输入完成后原脚本重跑成功。两次失败输出均在 MCP 进程记录保留；未因此修改游戏代码/资源/测试阈值。

## 发行仍待决定

本轮已备齐可定位的交付资料与原文候选；资源官方取得事实不再重问。席位/Unity资格、最终主体/渠道、用户条款与接受方式、UCL/FBX 指示位置、正式分发组合及人工签收仍必须实际落实。见 `RELEASE-GATES.md`。
