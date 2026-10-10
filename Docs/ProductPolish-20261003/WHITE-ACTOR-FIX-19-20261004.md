# 19版：角色发白根因修复与构建防护

## 交付
入口：`Builds/Scenery-20261004-19/Start-Terrain.cmd`，1920×1080独占全屏。
GUID `847b43b569f34c068e839c5710ddddb0`，0 error / 20 warning。19版实际EXE已核看，男战士恢复原有橙色贴图，守方骑士正常。默认合计2048，保留紧密阵型及高人数确认入口。
18旧EXE保留为失败样本，不再作为试玩包；17与其他旧版保留。未commit/push。

## 根因和因果证据
- 原男战士的运行材质仍引用GUID `a63884258dc521b4085d9bc96fe60c86`，但对应 `Assets/RPG Tiny Hero Duo/Texture/Albedo.png` 及 `.meta` 在工作树缺失。
- 18版任务开始前保存的 `Logs/Scene18-20261004/git-status-before.txt` 已列出这两个文件和Texture.meta被删除。不能归咎于18布景或512景观贴图压缩；现有证据不能确定谁或哪一步删除了源文件。
- 男战士的VAT网格、动画仍在其他目录，所以仍能移动，材质Shader的_BaseMap默认值又是white；因此可以构建成功、无运行异常，却显示白人。守方默认实际为剑盾骑士，使用独立knight_texture，未同样丢图。
- 新检查在未修复状态真实发现4个材质悬空引用并抛BuildFailedException；默认男战士近/中/远均报告缺图，骑士三档均有图。
- 恢复原图和原GUID后，68个引用角色配置检查错误归零；19实际EXE恢复原色，未通过涂橙色/修改Shader/更换模型掩盖问题。
- 上一轮仅验证新增景观及功能/保护清单，未覆盖所有角色的传递依赖，是检查覆盖缺口；本轮补齐。

## 修复内容
1. 从Git `aea369904b87aca537a2d2ba32c2d449167df6ac` 恢复最少3个文件：Texture.meta、Albedo.png、Albedo.png.meta；未恢复整个被删除源包。
2. 在 `Assets/Game/RuntimeDependencies/TinyHero/Albedo.png` 创建逐字节相同的运行时副本，保留原512导入设置；PNG SHA256 `d0e04a6b74dbc3728ff5f2912a2f89c562c7129581a0d6e11c558ae28c7ab216`。
3. 将LitInstancedAgent、FarInstancedAgent及对应_Defender四个材质的_BaseMap改为运行时副本。最终复核确认四个文件**仅贴图GUID变化**，颜色、Shader和其他参数未改。
4. 实际角色依赖闭包已不再包含原源包Albedo，只包含运行时副本。因此清理原模型包不再直接删掉这项运行依赖。运行时目录本身仍不可随意删除。
5. 19使用独立场景/目录/预览图；继承18的299轻量景观、512地图、11张256/512景观贴图，不改变角色体型、地形导航、军团规则或已认可预览组件。

## 防回归机制
- `Assets/Game/Editor/ActorMaterialBuildGuard.cs` 实现IProcessSceneWithReport，在实际构建场景处理中自动执行，适用于Unity构建流程，不依赖某个手工Builder主动调用。
- 遍历场景Scenario、Catalog（含兼容旧ID的隐藏模板）、RosterPolicy引用角色，检查有效近/中/远网格、材质、Shader、VAT位置/法线纹理。
- 对材质中**明确序列化为已赋值的贴图引用**检查是否能解析；文件或meta/GUID丢失则抛BuildFailedException，报告具体角色、LOD、材质、属性、GUID。合法纯色材质的有意空贴图不误报。
- 已验证：真实缺图负例被拦；注入悬空GUID负例被拦；近、中、远材质分别置空均被拦；合法纯色材质通过；修复场景通过。实际19构建的菜单和战场均有自动回调通过记录。
- 生成 `actor-dependency-manifest.json`，包含1108个依赖文件/metadata及哈希，供清理前核查；新增资源后须更新，不能把过时快照当永久保证。
- 开发版专用smoke在运行中再次核对默认两种角色×三个LOD的6项颜色贴图绑定；仅显式--scene19-smoke启动，不干扰正常试玩。
- 辅助颜色回归排除HUD区域：旧18两档橙色像素8/90，新19为2322/54136，均通过阈值；这只是特定场景的辅助检查，不是通用图像正确性证明，另已人工核看实际截图。

## 验证结果与边界
- 2048和50000实际EXE：初始0重叠，出生点全可行走，全员移动>2米，每档15360贴地抽样，最大误差约1.91e-6米；预设/撤销重做/载入取消/开战确认/暂停通过；已筛查运行异常为空。
- GTX1060 3GB、720p Development、固定诊断相机、15～17秒：约366 / 21.5 FPS。五万人仍非流畅性验收；不将18错误显示时的89 FPS当作有效对照。未再加压十万/二十万，未长压、未1080p性能或完整自然胜负验收。osInputTest=false。
- 1137个旧保护文件全部哈希一致；1108个本轮角色依赖与加固后快照一致。原商店素材、旧EXE和两个预览组件源码未变。
- git diff --check仍为2，来自此前已存在的3个Robot Native材质和ProjectSettings空字段尾空格；未为了让检查变绿覆盖既有状态。

## 可重复检查与操作约束
`python Logs/run-white19.py After` 可重复运行正/负回归；不要重跑Before（修复后不再应缺图）、Harden、Prepare或Build去覆盖现有产物。后续修改另定新修订目录。
构建阻断针对已引用且纳入检查的依赖；不能承诺所有未知视觉问题永不出现。不要禁用检查强行出包。清理源包前应核对运行依赖，尤其不要把VAT数据纹理当普通颜色图压缩。
本轮仅修复显示及依赖防护；最终美术满意度、长期性能仍待用户试玩。

## 证据
`Logs/WhiteActorFix-20261004/`：before.json、restore.json、after-original-restore.json、harden.json、after.json、guard-regression.json、actor-dependency-manifest.json、build.json、player-check.json、color-regression.json、final-audit.json。
`Logs/ActorMaterialGuard/passed-scenes.log`：构建回调记录。
`player-2048/`、`player-50000/`：实际EXE截图、角色绑定和运行日志。
