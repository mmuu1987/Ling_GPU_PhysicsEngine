# 当前任务：18版轻量景观候选已生成，角色发白阻塞交付

用户已自行导入四包，授权低面数+贴图512/1024独立18版。MCP已恢复，实际盘点、场景生成、构建和2048/5万人功能短测完成；但新包进攻方发白，视觉验收失败，不能报完成或称性能提升。旧17及其他旧包保留。

详见 Docs/ProductPolish-20261003/SCENERY-18-20261004.md。299装饰/约12.3万新增三角面，11张256/512压缩副本，原素材不改。1137保护哈希一致。18候选在Builds/Scenery-20261004-18，已加未验收警示。下一步先定位角色视觉差异，不重跑Prepare/Build覆盖既有目录，不改已认可预览组件；没有正在后台继续运行的修复任务。

商店授权任务已被用户叫停，不再用Cookie，也不再要求重新领取。此前商店未核验入库记录仅为历史，用户后来已自行导入。
未commit/push。不要无故重跑全套、扩地图或恢复旧E1。

---
历史记录（旧商店阻塞状态不再是当前下一步）：

# 最新任务：四套免费素材加入商店My Assets，尚未核验成功

用户明确选择商店My Assets（非工程Assets），在外忙，希望代理领取此前四主候选。已通过官方网页逐项点击Add to My Assets；前三项点选标准条款确认，刷新仍显示Add；TriForge另有确认提示未继续。0/4核验成功，不能声称已入库。没有付费结账/营销订阅/下载/导入，17版游戏不变。不要重复循环点击或以HTTP200冒充成功。

详见 Docs/ProductPolish-20261003/ASSETSTORE-MYASSETS-20261004.md；电脑资料目录内也有MyAssets领取进度.md。最终网页回执 Logs/AssetStoreClaim-20261004/official-browser-final.json。账号/许可阻塞具体原因未确定，待用户方便时正常浏览器重新登录并确认，不索取新Cookie或密码。未commit/push。

---
# 最新任务补充：商店下载被授权检查阻塞

用户提供本机Cookie文件授权下载场景素材；已优先尝试Supercyan 168396。商店session可识别会话，但包接口401、官方浏览器下载接口403。没有下载到unitypackage，没有导入或修改游戏。需用户在商品页确认已加入My Assets，并在Unity Hub/Editor登录同一账号，才能继续官方下载通路；不要索取新Cookie或绕过许可。凭证文件已加入本机Git排除，未提交。
详见 Docs/ProductPolish-20261003/ASSETSTORE-DOWNLOAD-STATUS-20261004.md。下方17版游戏交付状态不变。

---
# 当前交接：17版紧密阵型与合计十万人试验已交付

用户在16版尝试每方五万越界，认可紧密0.7；明确选择“合计十万人试验入口”，默认低人数、性能不足不继续加压。当前入口：`Builds/TightFormation-20261004-17/Start-Terrain.cmd`（1080p独占全屏）。GUID 9dfa4e8bbd614b07a4b4ac35e8f0787d，0error/20warning。

默认合计2048/密度0.4。右侧阵型编辑新增标准0.4/紧密0.7（自动尺寸、比例2.2，不改人数位置/角色大小）。右上十万人压力试验需确认载入双方各五万，可撤销、不自动开战；超过5万开战再确认。原越界/禁行/碰撞保护保留。

每方5万0.7占地约180×396，能放进512地图。发现原8%出生点抖动在紧密排列会重叠，新增SpawnConfig.formationJitterFraction默认0.08，仅17模板设0.02；旧模板默认不变。Editor及实际EXE十万个初始点按直径1.1检查，0重叠，最小间距1.149904米；不代表交战全过程无接触。

2048/5万/10万三档EXE功能检查通过，每档15360贴地抽样，全员移动>2米；预设/撤销重做/取消/载入/开战确认通过，实拍已核看。GTX1060 3GB、1280×720 Development短时诊断约304/20/16 FPS，十万人明显不流畅，不能宣称性能验收。osInputTest=false，未自然胜负/长压/1080p性能。没有继续试20万人。

独立Root Assets/Game/TightFormation17，沿用512地图。174保护文件哈希一致，16/14/13/11保留，角色预览不动。文档：Docs/ProductPolish-20261003/TIGHT-FORMATION-20261004.md；证据：Logs/TightFormation-20261004/。

下一步等试玩反馈；若推进流畅性先定位性能瓶颈，每方十万仍需更大空间。不要自动扩大到1024、不要恢复旧E1/重跑全套，不覆盖已有Build/Prepare。未commit/push，无授权不要提交。

---
## 历史交接（16及之前）

# 当前交接：16版512扩图已完成，二十万人目标尚未达成

最新入口：`Builds/TerrainScale-20261004-16/Start-Terrain.cmd`，1080p独占全屏。GUID bab70d90f6a34ec58f53febde0ef6840，0error/20warning。15仅内部首包，最终修正了道路配色。

用户确认长宽翻倍512×512，目标每方十万；已扩真实地形/模拟/导航，保持2米导航精度；树石挪外围，模型和预览不缩放不改。独立Root Assets/Game/TerrainScale15。14/13/11保留。默认合计2048，试验上限50000。

重要容量结论：原实际密度0.4，十万人每方需要25万平方米。512整图仅26.2万平方米；两侧各212×424，面积理论约3.6万人/方。二十万人正常密度被越界校验拒绝，不能硬压密度。建议下一步1024×1024给机动留空间（未实施，等用户确认）。

验证：15内部2048/1万/5万分级通过；最终16复核2048/5万，全部移动>2米，各20480次贴地抽样，四向远景/镜头范围通过，实拍已核看。1024边界顶点、48组镜头限幅静态通过。117保护文件哈希一致。GTX1060 3GB、1280×720诊断五万人仅约20.3 FPS，不能称流畅，更不能外推20万人或1080p。osInputTest=false，未完整自然胜负/长压/20万性能验收。

详见 Docs/ProductPolish-20261003/TERRAIN-SCALE-20261004.md；证据 Logs/TerrainScale-20261004/player-check-16.json 等。不要覆盖既有Prepare/Build。未commit/push，无授权不要提交。下一步需同时考虑更大地图与大规模性能优化，等用户确认；不要自动恢复旧E1或重跑全套。

---
## 历史交接（14及之前）

# 当前交接：14版外围远景与薄雾已交付，待用户试玩反馈

最新入口：`Builds/TerrainAtmosphere-20261004-14/Start-Terrain.cmd`（1920×1080独占全屏）。用户批准在13丘陵上扩视觉背景＋雾＋镜头限幅，不扩大256×256玩法区。13/11保留，角色预览不动。

14独立资产Root：Assets/Game/TerrainAtmosphere14/。1408背景、120背景树，无碰撞/导航；径向190→540渐隐，非体积雾/全局距离Fog。镜头距离360/水平240/最高300。

GUID da2f325063db4a18ac2f36e71a98b422；构建0error/20warning。EXE 256/256移动、10240贴地采样，四向远景及镜头检查通过；追加Editor接缝512顶点和48组镜头限幅通过；81个保护文件哈希复核通过。证据：Logs/TerrainAtmosphere-20261004/。osInputTest=false，不代表人工手感、完整胜负或512性能验收。

详见 Docs/ProductPolish-20261003/TERRAIN-ATMOSPHERE-20261004.md。不要重跑覆盖14 Prepare/Build；后续新编号。未commit/push，无授权不要提交。

下一步：等待用户试玩拉远、旋转、俯视和移动镜头，按反馈细调。不是美术完成签收，不自动恢复旧E1或重跑全套。商店资料已存电脑，原包尚未领取/导入。

---
## 保留13版交接（历史）

# 当前交接：方案B独立Q版丘陵试装13

用户因没时间领取Unity商店素材，明确从方案A改为先执行方案B。已直接下载Kenney Nature Kit 2.1，核验CC0及SHA256，自行组织一张真实丘陵地图；不再等待用户下载A。

入口：Builds/TerrainPlanB-20261004-13/Start-Terrain.cmd，1080p独占全屏。独立菜单→林地丘陵→布阵→开战；11原包/原Catalog/原战场仍保留，12是内部首轮包。当前不是整合替换全部战场。GUID 9ed788ee49ad41e09e3a80cb994bcadc，2场景，0error/20warning。

资产：Assets/Game/TerrainPlanB12/（目录沿用初始编号），Assets/ThirdParty/KenneyNaturePlanB/（12个原始FBX+许可；自行改材质配色）；包缓存_DownloadedModels/TerrainPlanB/。新Editor/TerrainPlanBBuilder.cs的Prepare拒绝覆盖现有Root，Polish改变当前试装资产；不要盲目重跑。下一包新编号。

生产新增：TerrainPlanBPlayerSmoke.cs仅显式--terrain-plan-b-smoke启用；WarSandboxDeploymentHUD.TerrainMap.cs缓存128²高度/导航禁行图，.UGUI接入图例与底图，HUD.OnDestroy释放。原Renderer/Widget逐字未改，保持10版确认过的视角和拖动。未改GPU战斗内核。

测试：Logs/TerrainPlanB-20261004/test-04/PlayMode.xml 1/1；player-13独立EXE passed/exit0，同GUID，256/256移动>2m，10240个位置采样，高度约1.15～21.11m，最大贴地误差约0.00000191m，岩群部署拒绝/连通性通过。已核看最终13布阵高程图与实拍战场。约20秒检查，不是自然胜负、强制绕障碍压力、远程地形遮挡专项或512/大规模性能验收；本图限512仅保护范围，验证量为256。

专用测试runner Logs/test-terrain-plan-b.py 在Unity启动前临时注册独立场景到EditorBuildSettings，finally恢复；不能在PlayMode已启动后才注册，否则SceneSession无法加载。普通未注册场景的全套运行明确跳过该独立夹具。test-01/02因此失败；prepare前两次失败是OpenScene导致未重载的资产引用失效，已修复，失败产物仅在Logs归档。

详见Docs/ProductPolish-20261003/TERRAIN-PLAN-B-20261004.md。包附KenneyCC0以及原模型第三方声明。11EXE哈希未变；TimeManager/3Native材质/测试BuildSettings恢复。未commit/push，保护继承目录及所有既有改动，不git add .。

下一步用户查看地图方向；继续美术构图与地面细节、完整自然交战/绕障碍验证。两条土路目前仅视觉指引，不强制沿路或加速；无高地加成/森林隐蔽/水域/桥洞叠层。不要宣称地图美术已完成，不回头改已确认的角色预览。
