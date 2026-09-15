# Mod 系统方案（已无限搁置，保留作重启资料）

> **状态：无限搁置**（2026-09-14 深夜用户决策：mod 整体排到复杂地形 M6 之后，非 V1 承诺，随时可重启）。
> 本文与 §1 的 UEBS2 调研结论全部留档。重启入口：本方案 + M5 兵种模型绑定里程碑的编辑器 VAT 烘焙工具
> （烘焙数学与本文 §2 同源，复活时把编辑器工具运行时化 + 补不可信输入校验即可）。
> 注（2026-09-15）：所述编辑器烘焙工具已由 M5.1 移植进 `Assets/MassEngine/Editor/`（现役版，
> 菜单 `MassEngine/VAT Baker`）；归档四版仍在 `ArchivedStages/MassGPUPhysics_Stage{2,3,5,6}/Editor/`。
> 其烘焙数学与本文 §2 一致，重启时以现役版为基准做运行时化。
>
> 立项：2026-09-14，同晚按用户意见修订为 **glTF 角色导入方向**。
> 用户决策：mod 角色由玩家自己制作（Blender 等任意工具），我们不做编辑器工具，只在客户端**运行时导入 + 校验**，
> 不合格不入库；配套只出一份详细的模型制作文档。复杂地形顺延 M6、内容收口顺延 M7，美术方向后续另行启动。
> 参考调研：UEBS2 反编译分析（`F:\Game\Ultimate.Epic.Battle.Simulator...\_analysis`，本文引用其结论，不复制代码）。

## 1. UEBS2 mod 体系调研结论

**加载侧（LoadMods.cs）**：本地 StreamingAssets 目录 + Steam Workshop 订阅目录；`.bgmod` 纯文本 key=value 只帶数值；
视觉来自 **AssetBundle**（Army.cs:893 `AssetBundle.LoadFromFile`）——**modder 必须用 Unity 构建包**，这正是我们要摆脱的掣肘。
单位按 animset 克隆基础预制、覆盖约 25 项数值，归类 "Modded Units" 注入兵种列表。

**烘焙侧（CharacterRenderer3.BakeAnimations，回答"UEBS 怎么做烘焙"）**：
UEBS 的烘焙也发生在**导入时（一次性）**，不在每帧：
1. 对每个动画 clip，按固定关键帧数逐帧 `Anim.Play(clip, 0, t); Anim.Update(0)` **驱动 Unity Animator** 摆好骨架；
2. 读出全部骨骼的父空间位置 + 朝向基向量，写进扁平数组 `BoneKeyFrames[帧×骨骼]`，上传 `BoneKeysBuff` ComputeBuffer；
3. 运行时由 `SkinCompute` 计算着色器按实例动画状态在关键帧间插值出骨骼矩阵（`MatrixBuffer`），顶点着色器逐实例蒙皮。
它必须借助 Unity Animator 是因为输入是 AssetBundle 里的 AnimationClip 对象。**概念可借（导入即烘焙成引擎自有数据），
依赖不可借（我们没有也不想要 Animator 参与运行时链路）。**

**已确认的坑（全部规避）**：无版本/无校验（`int.Parse` 无容错，坏一行崩游戏）、弹种/声音用内置数组索引引用、
导入即 DontDestroyOnLoad 常驻无卸载、空 bundle/缺字段/重名只报错跳过。

## 2. 我们的技术链（关键事实：引擎已是 VAT 动画管线）

当前 `Assets/MassEngine/VatRender`：单位本来就是动画兵——`VATProfile`（三级 LOD 的 mesh + 位置/法线纹理 +
idle/move/attack/death 四段 clip 窗口 + 帧率）→ `MassGpuRenderDispatcher` 按兵种×LOD 间接实例绘制，
shader 逐实例按 `currentState` 选段采样 `fmod(animTime×clipRate, clipCount)`，死亡钉末帧。
**内置六兵种就是这条管线烘出来的。**

因此 mod 角色的完整链路（渲染侧零新代码）：

```
玩家产出 .glb（网格+骨骼+蒙皮权重+四段动画+嵌入贴图）
  → 运行时 glTF 解析（glTFast 或精简解析器）
  → 校验（不合格直接拒绝入库，见 §4）
  → 运行时 VAT 烘焙器：采样动画曲线 → 摆骨架 → 蒙皮 → 写位置/法线纹理 + clip 窗口
  → 组装与内置模板同构的 VatProfileData / ResolvedUnitTypeRuntime
  → 注入 UnitTypes 目录 + 布阵模板列表（"Mod 兵种"分组）
```

**为什么选 glTF(.glb)**：开放标准、骨骼/蒙皮/动画/材质全包含、Blender/Maya/3ds Max 原生导出、
二进制 .glb 单文件自包含（贴图嵌入）、运行时可解析；FBX 是专有格式且无运行时解析路径，直接排除。
Unity 官方运行时加载器为 [com.unity.cloud.gltfast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.13/manual/index.html)
（Unity 6 可用，支持蒙皮与动画导入，`GLTFAST_SAFE` 面向不可信输入，`GLTFAST_KEEP_MESH_DATA` 保留 CPU 可读网格供烘焙；
我们不用它的材质/实例化——只用原始数据喂自己的 VAT 管线，shader 变体坑与我们无关）。
备选：自写 ~2-3k 行精简解析器（只取 mesh/skin/animation，无第三方依赖）；M5.1 时做最终选型与体积评估。

**我们的烘焙 vs UEBS 的烘焙**：glTF 动画本身就是关键帧轨道（四元数/位移，按节点），我们直接采样自己的骨骼数学
（不需要 Animator 等价物），逐帧摆骨架 → 蒙皮 → 写 VAT 纹理。烘焙产物与内置兵种**同构**，shader/调度/LOD 全复用。
一次性成本：典型人形（≤5k 顶点 × 4 段 × 30fps × ~1s）≈ 60 万顶点帧，CPU 蒙皮数秒内完成，纹理几 MB（半精度），
导入时带进度条即可。V1 mod 用单 LOD（近中远同一网格，VatProfile 已容忍缺省 LOD）；顶点/骨骼/时长上限在校验器卡死。

## 3. Mod 包契约

一个目录 = 一个 mod（沿用 M4.2 方案库已验证的版本化 JSON/稳定 ID/原子写入/先校验后应用模式）：

```
<Mods 根>/<mod-id>/
  mod.json          # 清单：格式版本 1、id、名称、作者、版本、兼容内容版本、简介
  units/*.json      # 兵种条目：稳定模板 ID + 数值（血量/伤害/射程/攻速/密度/尺寸等现有维度）
                    #   + visualRef: 同目录 glb 文件名 + 四段动画命名映射 + 缩放/朝向修正
  units/*.glb       # 自包含视觉：网格+骨骼+蒙皮+动画（idle/move/attack/death 命名约定）+嵌入贴图
  battlefields/*.json # 战场 mod（纯数据，无美术）：规则 + 障碍布局，字段语义对齐 BattlefieldConfig
```

- 根目录：`persistentDataPath/WarSandboxMods`（主）+ 游戏旁 `Mods/`（便携，只读扫描）。
- ID 规则：`modid:unitid` 命名空间；与内置/其他 mod 冲突不加载并给明确错误。引用一律稳定字符串 ID，禁数组索引。
- 数值放 mod.json、视觉放 .glb：两 concerns 分离，数值平衡改动不碰模型文件。
- `mods-state.json` 记录启用状态，默认停用、玩家手动启用；单 mod 失败隔离不崩（UEBS 教训）。

## 4. 校验器（M5.1 核心，编辑器与运行时同一份代码）

mod.json 层：格式版本、必填字段、非法数值、超限数值、大小上限、重复 ID、版本不兼容。
glb 层（对照 UEBS 崩溃点清单 + 我们的新增面）：
- 结构：可解析、单场景、网格/骨骼/蒙皮权重存在且非退化（≥2 权重骨骼，UEBS 同款检查）；
- 上限：顶点数（默认 10k）、骨骼数（默认 128）、单段时长（默认 ≤5s）、文件大小（默认 50MB）、贴图数/尺寸；
- 动画：**必须含 idle/move/attack/death 四段**（命名约定，允许别名映射表）；缺段拒绝；
- 材质：只用 .glb 内嵌贴图与标准 PBR 通道，外部 URI 引用拒绝；V1 不接受的扩展（Draco/KTX/meshopt 按选型决定）明确拒绝并提示导出设置；
- 每类错误独立错误码 + 中文提示，指向制作文档对应章节。

## 5. 游戏内 Mod 管理器（M5.3）

战场目录页"Mod 管理"入口（uGUI，风格同方案库）：列表（名称/作者/版本/内容计数/启用开关/错误详情展开/删除）；
启用后 mod 兵种入布阵模板列表（"Mod 兵种"分组）、mod 战场入战场目录；mod 角色启用时执行导入校验+VAT 烘焙
（带进度），完成前该兵种不可选。方案库对 mod 兵种的引用做联动校验：mod 禁用/缺失 → 方案载入明确拒绝，不静默替换。

## 6. 制作文档（替代编辑器工具，M5.4）

《Mod 角色制作指南》：建模/绑骨/动画的规格与上限、四段动画命名、比例与朝向约定（导出后自动缩放到兵种格）、
材质/贴图要求、Blender 导出 glb 的逐步设置、常见拒绝原因对照表、示例工程（一个能通过校验的最简人形）。
文档随版本更新；校验器错误信息直接引用文档章节号。

## 7. 里程碑拆分

- **M5.1 glTF 导入与校验**：glTFast/自写解析器选型落定；glb 解析为引擎中立数据（网格/皮肤/动画曲线）；
  mod.json + glb 全量校验器与对抗用例（UEBS 崩溃点逐条 + 新增面）；EditMode 覆盖。
- **M5.2 运行时 VAT 烘焙器**：动画采样 → 骨架摆位 → 蒙皮 → VAT 位置/法线纹理 + clip 窗口；
  产出与内置模板同构的 VatProfileData；烘焙进度/失败恢复/内存上限；与既有六兵种回归对比测试。
- **M5.3 管理器与注入**：管理页 UI、启用时导入+烘焙、mod 兵种/战场注入布阵与目录、方案库联动校验；
  独立构建烟测：启用 mod 兵种 → 布阵 → 开战自然结算 → 重启持久。
- **M5.4 文档与闭环验收**：制作指南 + 示例工程；编辑产出 → 导入 → 布阵开战 → 重启载入闭环；
  损坏/超限/缺段 mod 隔离不崩（三例以上）；用户人工试玩验收。
- 战场数据 mod（§3 battlefields）在 M5.1 契约内一并定义，M5.3 随管理器接入。

## 8. 与后续里程碑的衔接

- M6 复杂地形：BattlefieldMod 契约届时扩展高度/通行性字段并递增格式版本；VAT 管线天然支持贴地（纹理存局部空间，实例变换在 shader）。
- 美术方向定型后：材质/特效升级直接作用于 VAT 管线（内置与 mod 兵种同时受益）；多 LOD 自动减面、KTX2 压缩、
  骨骼级动画（UEBS 式 BoneKeys，省纹理显存）列为届时评估项，不在 V1 预建。
- V1 非目标不变：外部代码/脚本 mod、Steam Workshop/社区分发平台、联机 mod 校验、FBX/OBJ 等非 glTF 格式。
