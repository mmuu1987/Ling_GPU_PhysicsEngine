# 骑兵马体放大 + 超大级别第一批：巨龙

用户要求："马儿太小了，估计小了一倍左右，这个得改下，改完之后继续扩充超大级别的角色"。超大级别用户选择 **6–8m 档（不改公共VAT管线）**，优先 **巨龙**。

## 正式入口

`E:\GitHub\_worktrees\WarSandboxBattlefieldRules\Builds\UnifiedDragons-20260930-01\Start-Roster.cmd`

GUID `f6913b1357d94aa2aa7fe80e6810438d`。这是本轮最新统一包：包含放大后的骑兵、两条巨龙、此前全部角色入口。默认战场为进化巨龙适配场。
仅需单看骑兵放大，可用 `Builds\UnifiedCavalry-20260930-03`（GUID `dd5350dd83774c08ba81b5d83aa57168`）。旧01/02包不覆盖、保留。

## 一、骑兵：马从1.8m放大到3.5m

- 新输出 `MountedKnight02`（旧 `MountedKnight01` 保留）。马整高 1.8m → **3.5m（≈1.94倍）**，骑手保持 1.95m 原比例，组合整体高约 4.29m，保守半径 3.3m。
- 马变高后原"脚蹬垂在马腹外侧"的腿长不够（骑士腿短），改为 `SolveScaledFeet`：双脚贴在马背两侧上缘（相对鞍位 ±0.252m，马背半宽 0.424m），膝角 99°–115°，仍是坐姿而非直腿。
- 骨骼目标误差：脚 3e-7m、骨盆 2.4e-7m，动画重放 1.6e-6m（门槛 0.25mm）。
- VAT 独立参考最大误差 **2.008mm ≤ 2.5mm**；Full 10872 / Low 2130 顶点，145帧@24FPS，纹理约 38MB（配方预算48MiB）。
- 对比图：左旧1.8m、右新3.5m（见证据目录 `Logs/CharacterPipeline/MountedKnight02-9f209759`）。
- 战斗参数不变（攻击距离=半径+1.05、HP300、攻30、速3.5），**放大只改体型，不自动变强**；大体型带来的半径/接敌距离已随尺寸更新。

## 二、超大级别：两条官方CC0巨龙

来源：Quaternius Ultimate Monsters（官方页面链接的公开Google Drive，Flying/FBX），`Dragon.fbx`、`Dragon_Evolved.fbx`，License为CC0，与上一批怪物License逐字节一致（sha256相同）。沿用上一批的官方怪物图集（只读）。

| 角色 | 翼展 | 体长（含动作） | 悬停顶高 | 半径 | Full/Low | 帧 | VAT最大误差 | HP/攻击 |
|---|---:|---:|---:|---:|---|---:|---:|---|
| 飞龙（超大） | **7.55m** | 6.53m | 5.65m | 4.3m | 2740/1986 | 94 | 2.172mm | 1000/60 |
| 进化巨龙（超大） | **7.40m** | 6.03m | 5.23m | 4.2m | 4352/1994 | 110 | 2.163mm | 1500/90 |

动作映射：Flying_Idle→待机，Fast_Flying→移动，Headbutt→攻击，Death→倒地。两条龙都是**悬停飞行姿态**：源文件原点就是地面，身体悬在空中，倒地动作坠落到地面；所有帧均不低于地面。

### 为什么不是完整8m：精度上限（如实说明）

公共VAT是半精度坐标，独立几何闸门比较的是**三维距离误差 ≤2.5mm**：
- 单轴在 2–4m 取整误差≤0.98mm，4–8m≤1.95mm，≥8m≤3.9mm。
- 一个顶点**两个轴同时 ≥4m**（例如翼尖同时在横向与高度/前冲方向都超过4m）就可能达到 1.95×√2≈2.76mm，超门槛。

第一次准备（Prepared01）按"单轴坐标 ≤7.8m"设计，结果 Death 实测 **2.756mm 失败**，与上述推导吻合。修正版对四个动作全部帧（倒地按管线同样拉伸采样到末帧）逐顶点计算三轴合成最坏误差，取 ≤2.35mm 的最大缩放。因此本管线下飞龙最大尺度是 **翼展约7.5m、体长约6–6.5m**；未降低门槛、未改编码。要真正做到10m+需改VAT编码（公共管线），按你的选择本轮不做。

两条龙都被精度上限夹在同一尺度，所以"进化巨龙"与"飞龙"体型接近，区别在造型（更多骨骼/角/腿）和更高的HP/攻击。

## 实际验证

- 素材：两份FBX+License按sha256校验后只新建、不覆盖，`Logs/AgentDragons/source-transfer-receipt.json`。
- 导入检查：单一蒙皮网格、无刚体附件、无blendshape；100倍静态导入缩放在自有Mesh/bindposes中规范化，源动画无真实缩放动画（最大偏差1.2e-7）。
- 规范化姿态与原动画逐帧对照最大 0.004mm（门槛0.25mm）。
- GPU截图：4动作×3 LOD + 独立参考，见 `Logs/CharacterPipeline/Dragon02-b786515b`、`Dragon_Evolved02-afb1dd26`。

六个独立进程串行（30FPS限帧、低负载），实际uGUI选择、保存/跨进程重载、自然结算、满编重开、GPU/运行副本释放，源JSON/方案/设置不变，没有注入HP或胜负：

| 用例 | 可选模板 | 初始→自然存活 | 自然战斗 |
|---|---:|---|---:|
| dragon seed | 8 | [3, 16] → [3, 0] | 37.17s |
| dragon reload | 8 | [3, 16] → [3, 0] | 37.22s |
| dragon-evolved seed | 8 | [3, 16] → [3, 0] | 40.13s |
| cavalry seed（放大后回归） | 2 | [3, 16] → [1, 0] | 54.28s |
| spider seed | 6 | [3, 16] → [2, 0] | 51.22s |
| all seed | 12 | [38, 36] → [22, 0] | 31.80s |

骑兵包03另跑五用例（骑兵seed/reload、蜘蛛、常规、全员）全部通过，`Logs/AgentCavalryScale/batch-player-receipt-03.json`。

巨龙数值是首版功能设定，不是平衡结论：3龙对16匹配尺寸骑士全胜且无损，**明显偏强**，若要对抗性需下调。巨龙仍走现有地面移动/近战（悬停只是视觉），没有真正飞行、空中寻路、对空规则、喷火或范围伤害。

## 工程路径

- 构建器：`Assets/Game/Editor/CharacterPipeline/DragonBuilder.cs`（新文件）；骑兵：`CavalryBuilder.cs` 新增 Prepare02/CreateMatchedContext03/SolveScaledFeet/Build03，旧方法未改。
- 配方：`Recipes/Dragon02.asset`、`Recipes/Dragon_Evolved02.asset`、`Recipes/MountedKnight02.asset`。
- 产物：`Generated/Dragon02`、`Generated/Dragon_Evolved02`、`Generated/MountedKnight02`。
- 准备与整合：`Assets/Game/Dragons/Prepared02`（`Integrated/Menu.unity`）；`Assets/Game/Cavalry/Prepared03`。
- 失败证据保留不复用：`Assets/Game/Dragons/Prepared01`、`Recipes/Dragon.asset`、`Generated/Dragon01`（精度失败）；`Assets/Game/Cavalry/Prepared02`（腿长失败）。

## 保护与未做事项

- 未commit/push，未 `git add`；未覆盖已有构建包/生成目录；未改旧C#、公共管线、引擎核心、`Assets/pelican-cycling.svg`。
- 整轮审计：以本轮第一次Unity运行前快照为基线，3046个受保护文件 0变化 0缺失，`Logs/AgentDragons/round-protection-audit.json`。
- 未跑10万人压测、无性能结论；未代签V1；不做法律放行（CC0仅记录来源）。
- `humanAcceptance=false`：比例、骑手贴合、龙翼穿插、悬停高度观感仍需人看。
