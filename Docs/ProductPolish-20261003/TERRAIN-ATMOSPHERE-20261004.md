# 14版：丘陵外围远景与薄雾（2026-10-04）

## 试玩
工程根目录下 `Builds/TerrainAtmosphere-20261004-14/Start-Terrain.cmd`，1920×1080独占全屏。
菜单→林地丘陵·远山薄雾→进入→布阵→应用并开战。
13、11旧包保留；新场景独立，不替换所有战场。

## 本轮范围
用户认可在现有丘陵基础上扩展视觉背景、加入外围雾效、限制镜头范围，而不是扩大可玩地图。
- 原256×256可玩地形、Surface/System/Scenario引用及导航/部署范围不变。
- 新1408×1408视觉背景，仅原方形外生成三角形；120棵远景树，无碰撞体、不参与导航，树不投射阴影。
- 世界XZ半径190到540逐渐融合天空色；原战斗区最远角半径181.02，不受该雾影响。关闭全局Fog，非体积雾。
- 边界512顶点匹配原地面的高度、法线和颜色，避免几何裂缝及明暗接缝。
- 仅新场景启用镜头包络：最大距离360、水平半径240、最高Y300、最低8并保持地面上方安全距离。相机校正同步移动环绕锚点。
- 已确认的角色预览视角、透视和双轴拖动未改。

## 验证
- 构建GUID da2f325063db4a18ac2f36e71a98b422；0 error、20 warning。
- 独立EXE：256/256单位移动、10240次贴地采样；最大贴地误差约0.00000191米。四向最大拉远、俯视、镜头范围与纯背景检查通过。
- 已核看实际EXE默认及四向拉远、高处俯视截图；证据在 `Logs/TerrainAtmosphere-20261004/player-14/`。
- 追加只读Editor验收：512个边界顶点的位置/法线/颜色一致；背景三角形不侵入原方形；无背景碰撞体；原玩法资产引用不变；雾区不侵入可玩区、外边缘已完全渐隐且颜色空间匹配天空；48组越界输入的镜头限幅通过。
- 最终哈希复核81个受保护文件未变，包括旧11/13程序、13源资产及角色预览；git diff --check通过。TimeManager、EditorBuildSettings及3个原Native材质保持恢复状态。
- 回执：build.json、player-check-14.json、acceptance.json、final-audit.json，均位于 `Logs/TerrainAtmosphere-20261004/`。

## 边界与后续
自动化通过不等于人工输入手感验收：osInputTest=false，未验证完整自然胜负、512单位或大规模性能。本轮额外远景有渲染开销，但没有扩大模拟地图；不声称性能零成本。
这不是正式美术完成或发行签收；原地面及道路细节仍较简单。待用户试玩确认拉远观感和镜头限制是否合适，再按反馈微调。
商店选型资料已存 `_DownloadedModels/TerrainAssetShortlist-20261004/`，商店原包仍未领取/下载/导入，非本轮前提。

## 维护交接
- 资产：Assets/Game/TerrainAtmosphere14/；源实现：TerrainAtmosphereBuilder.cs、BattlefieldAtmosphereBounds.cs、BattlefieldBackdropFog.shader、TerrainAtmospherePlayerSmoke.cs。
- MyCameraManager.cs新增ConstrainWorldPosition，由14场景包络调用；未改通用输入算法。
- TerrainAtmosphereAcceptance.cs为追加Editor-only验收，不进入Player，无需因此重建已验证14包。
- 14 Prepare/Build拒绝覆盖既有目录；不要盲目重跑或覆盖已交付包。下一次修改使用新编号。
- 复用证据，仅必要测试。没有commit/push授权，本轮未提交。
