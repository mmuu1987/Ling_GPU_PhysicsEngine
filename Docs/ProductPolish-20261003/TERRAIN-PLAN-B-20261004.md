# 方案B：Q版林地丘陵独立试装13（2026-10-04）

用户最初选择方案A，随后明确表示没时间领取Unity商店资源，改为先执行方案B。当前不再等待Unity账号/素材领取，不擅自购买素材。

## 交付与范围

- **入口** `Builds/TerrainPlanB-20261004-13/Start-Terrain.cmd`，1920×1080独占全屏。开始游戏→林地丘陵→进入→布阵→应用并开战。
- 独立新地图目录/菜单，**不是替换11版全部战场**。11及旧包保留。12为内部第一轮验证包，13补齐布阵高程/禁行图；后续不要覆盖13。
- 新地图资产位于 `Assets/Game/TerrainPlanB12/`（目录名沿用首次生成编号），独立Catalog/LaunchMenu/ForestHills，稳定地形ID `forest-hills-b12@1`。复制配置，不修改原Version08 Catalog/场景/角色战斗数值。
- 默认双方各128人、共256；本图暂限512。只验证256，不宣称512性能或更大规模验收。

## 素材及授权

直接从Kenney官网下载Nature Kit 2.1，包内License.txt确认CC0，可用于个人/教育/商业用途。
- 页面 https://kenney.nl/assets/nature-kit
- 压缩包 https://kenney.nl/media/pages/assets/nature-kit/37ac38a37b-1677698939/kenney_nature-kit.zip
- SHA256 `fa7974a0d342bfe63c38664ba9f8ec1a4aab8ea25f099bdc56870e33588c4d9d`；本地与工程机器下载字节一致。
- 原包缓存 `_DownloadedModels/TerrainPlanB/`，只选取12个FBX及许可导入 `Assets/ThirdParty/KenneyNaturePlanB/`，不导入第三方脚本。
- 选取树/岩石/草/花，自建URP材质并统一绿叶、棕色树干与灰石；原始FBX未修改。场景含170棵树、32个岩石实例、130个地面装饰。
- EXE附Kenney许可与来源说明，同时保留原角色资源ThirdPartyNotices、CREDITS、RESOURCE-NOTICES。

## 地形接入

- 256×256世界尺寸，129×129高度顶点；林缘与两个岩群掩码为禁行区。利用现有TerrainSurface/Navigation/GPU，不改战斗内核。
- 视觉地面由同一个高度场生成，三角划分与地表采样一致；丘陵、土路和植被自行组织，不冒称商店完整场景。
- 两条土路是视觉指引，不是速度加成，也不强制单位沿路行军；绿地区域仍可通行。
- 新增 `WarSandboxDeploymentHUD.TerrainMap.cs`：128²缓存高程/导航禁行图，浅色较高，深灰禁行，带5米高程带；在地形导航快照变化时重建，销毁HUD时释放，平地不显示此图。现有UI改为在编成色块下方显示此图。
- `WarSandboxDeploymentHUD.UGUI.cs`仅增加地形底图/图例；主HUD增加纹理销毁。原编成完整脚印、坡度、禁行、连通性校验保留。
- 两个相机预览组件源文与10/11镜像逐字一致，透视/默认角度/双轴拖动不变。新战场独立设置初始观察位置。

## 证据

- `Logs/TerrainPlanB-20261004/test-04/PlayMode.xml`：1条地形集成流程通过。临时在启动Unity前注册独立菜单/地图到EditorBuildSettings，运行后恢复原字节；单纯进入PlayMode后添加场景不足以刷新运行时场景表（test-01/02失败原因，非绕过验证）。专用runner为 `Logs/test-terrain-plan-b.py`；普通无场景注册的全套运行明确跳过本独立夹具。
- 集成覆盖：菜单进入、有效初始布阵、连通性、岩群布阵拒绝、布阵纹理禁行位置与上下方向、开战、GPU资源、采样位置合法和贴地、移动及运行末段暂停。
- Windows x64 Development构建：GUID `9ed788ee49ad41e09e3a80cb994bcadc`，2个场景，0error/20warning。
- `Logs/TerrainPlanB-20261004/player-13/receipt.json`：passed=true，exit0，GUID一致；256/256单位移动超过2米，40次采样×256=10240个位置，采样高度1.154～21.106米，最大地表位置误差0.000001907米；禁行布阵拒绝=true、两部署区连通=true。
- `02-deployment.png` / `03-battle-wide.png` / `04-battle-detail.png`均为独立EXE截图，已核看最终13布阵底图及战场画面。不是生成效果图。
- 该检查约20秒采样，**不是完整自然胜负、复杂绕障碍压力、远程攻击地形遮挡专项、OS鼠标操作或512/100k性能验收**。不要扩大结论。

## 当前未做与下一步

这是第一张可运行地形试装，不是完成环境美术打磨。还未加入水域、桥下桥上叠层、高地攻击加成、森林隐蔽、泥地减速。已有道路仅视觉用途。后续优先丰富地形构图、林缘过渡/地面细节，并验证强制绕障碍和完整自然交战；不要把所有战场批量替换为本图。

所有测试/构建结束，TimeManager和3份Native材质已恢复运行前字节，测试EditorBuildSettings同样恢复；11EXE哈希未变。整个预览/产品打磨/本轮地形系列均未commit/push。
