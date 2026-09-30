# 进度日志

- 开始：计划已写入 PLAN.md。当前可用包：Builds/UnifiedRoster-20260930-06。
- ✅ 4a：已写 `Docs/HANDOFF.md`，内容包括硬规则、当前构建、数据链、溅射扩展、既有失败和脚本。
- ✅ 2：火球落点特效和橙红拖尾。
  - 核心为可选扩展，默认关闭。开启条件：`impactMaterial` 非空，且存在溅射兵种。
  - 详见 Projectiles/README "落点特效"章节，以及 HANDOFF。
  - 过程中的一次返工：
    - -07 包用 HDR 配色，没有 Bloom 时被截成淡黄色，拖尾看起来也像黄白细线；
    - 已改为 LDR 橙红配色，拖尾加粗到 0.8，火圈改用 alpha 混合，并从 0.6 倍半径起快速扩到满半径；
    - 结果为 Prepared07 集成和 -08 包。
  - 可视化验证：新增真实 URP 相机的像素测试和"加载巨龙战场开战截图"测试。近景火焰像素从约 2.2k 提升到约 11k。
  - 回归：PlayMode Projectile 58/58；实机 players07 在 -08 上 5/5。
  - 证据图：`Logs/AgentImpactFx/scene-close-*.png`、`scene-overview-*.png`。
  - 未接入：unified-large 自定义战场不显示特效，伤害照常。
- ✅ 3：巨龙 vs 密集方阵。详见 `PHALANX.md`。
  - 量化结果：密集阵平均每发伤及 5–11 人，松散阵约 5 人，关闭溅射不到 1 人。现有数值合理，未修改。
  - 新增两个方阵战场，兵力按实测势均力敌点配置：2 飞龙 vs 90 骑士，2 进化巨龙 vs 160 骑士。包为 `-11`，集成目录 Prepared11。
  - 发现的问题：超过 64 人的密集阵会让网格溢出。已发布战场的人数上限就是 64，不受影响；方阵战场的人数上限和每格上限都设为 256。
  - 回归：实机 players08 在 -11 上 5/5；`PhalanxBattlefieldsAsShipped` 通过（零溢出，特效生效）。
  - 待用户决定：进化巨龙方阵场三次都是骑士略胜（剩 15–29 人），是否把骑士降到 150？
- ✅ 6：骑兵冲锋。详见 `CHARGE.md`。
  - 移速 3.5 → 6 m/s；冲到敌前的第一击 ×3（30 → 90），速度需 ≥ 60% 最高速度。
  - 引擎原本没有冲锋机制，做了可选核心扩展：`CombatConfig.chargeDamageMultiplier`（默认 1 = 关闭），外加 compute 关键字 `MASS_MELEE_CHARGE`；关闭时仍是原内核变体。
  - 包 `-12`，骑兵集成 Cavalry/Prepared06，总集成 Dragons/Prepared12。
  - 测试：`MeleeChargeTests` 3/3；内核 66/67、Projectile 58/58（与之前相同）；实机 players09 在 -12 上 5/5。
  - 过程中的坑：测试清理逻辑在纯 `[Test]` 之后卸载了测试运行器自己的场景，导致整个 PlayMode 运行卡死（前 4 次运行超时）。已修复，写进 HANDOFF。
  - 待用户决定：2 骑兵 vs 16 步兵仍是步兵胜（剩 4 人，旧版剩 7 人）；是否加强，或做"后撤再冲"AI？
- ✅ 5：3 个既有失败测试已修复。
  - `MassEnginePropertyTests` 两项 NRE：`ComputePipelineOrchestrator.BindCombatBuffers` 里直接调用了 `combat.SetFloat("projectileQueryRadius", …)`，而这两项测试故意传入空 shader，只记录派发顺序（同函数其他地方都通过判空的 SetBuffer/SetTexture 调用；弹道分支也有判空）。已补一行判空，有 shader 时行为不变。
  - `UnclearableBodyWaitsWithinBudget…`（只在套件里失败）：测试间状态泄漏。夹具的 SetUp 没有重置 `attackerFlow*` 和 LOD/节奏字段；Congestion 等测试开启 flow（目标点 6m 外）后没有还原，这个远程测试里 Advance 姿态的单位就跟着残留的 flow 走了 10m。已在 SetUp 中把这组字段恢复为声明时的默认值（字段注释写明"defaults = full rate, flow off"）。
  - 结果：PlayMode 内核套件 **67/67**；EditMode 431/433。原版备份在 `Logs/AgentCharge/pre-edit/`。
  - 剩余 2 项（`WarSandboxCatalogTests.ShippingCatalog…`、`WarSandboxLaunchPresetTests.QuickDefault…110kContent`）不在这 3 项之内：工作区里 09-27/09-29 就已存在、尚未提交的 M7.x 修改把 `EditorBuildSettings.asset` 换成了 M71 LaunchPresets 场景，并改了 Settings 下的 Render/Combat 资产。那是另一条工作线的进行中改动，没有动。**待用户决定**：是那边改动未完成，还是测试需要跟着更新？
- ✅ 7：新增两个地面超大单位，巨型恶魔 `Demon01` 和巨型暴龙 `Dino01`，来自 Quaternius Ultimate Monsters Big 包，CC0。
  - 新文件 `GiantBuilder.cs`：PrepareOne 从巨龙流程复制，DragonBuilder 和公共管线都没改。
  - 产物：`Generated/Demon01`、`Generated/Dino01`，集成目录 `Giants/Prepared01`，包 **-13**。
  - 受 VAT ±7.9m 限制，身高为 4.66m / 4.86m，横向跨度约 8m。
  - 已发布的 2 vs 16 骑士场巨人全胜；平衡点约 30–40 名骑士。待用户选择 A/B/C，见 GIANTS.md。
  - 测试：`GiantBattlefieldTests`，shipped 1/1、sweep 1/1；实机 players11 在 -13 上 7/7。
- ✅ 1：-13 实机 7/7；PlayMode 120/120；EditMode 431/433（2 项已知）。截图审查发现底部许可文字与底栏重叠（既有问题）。见 REGRESSION.md。
- ✅ 4b：COMMIT-PLAN.md。344 项未提交（从 M5.3 起）。待拍板：D1 大文件/LFS、D2 约 1.2GB 被取代版本、D3 EditorBuildSettings、D4 混改文件拆分方式。
- **通宵 7 项全部完成。**

## 2026-10-01 用户决定及落实
1. 巨人战场保持碾压展示，不改。
2. 进化巨龙方阵场骑士 160 → **150**（`DragonBuilder.IntegrateFire10` → Dragons/Prepared13）。实测两次都是巨龙险胜，只剩 1 条龙（Logs/AgentPhalanx/phalanx-shipped-13.json）。
3. 加强骑兵：扫描结果（CavalryStrengthTests，cav-strength-02）显示冲锋倍率 ×5 无效（步兵 100 HP，×3 已能一击秒杀），HP ×1.5 只是险胜。最终选**伤害 30 → 45**，冲锋首击 135（`CavalryBuilder.Prepare07` → Cavalry/Prepared07）。正式场景连测 3 次都是骑兵胜，2 名骑兵都存活，剩 230/600 HP，32 秒结束。
4. 2 个 Game EditMode 失败测试：不处理。
5. 全部提交：直接提交，不用 LFS；被取代的版本也提交；只排除 Unity 测试临时场景和 .local-mcp-audit；不 push。
- 新包 **Builds/UnifiedRoster-20260930-14**（`GiantBuilder.Integrate02` → Giants/Prepared02，`Build02`）。实机 players12 **7/7**。
