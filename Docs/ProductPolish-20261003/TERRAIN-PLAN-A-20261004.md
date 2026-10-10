# 方案A执行记录：现成Unity地形场景改造（2026-10-04）

> 后续用户明确改为先执行方案B：无需继续等待Unity商店领取。本页仅保留先前方案A记录；当前以方案B独立地形试装为准。

## 用户决策
先执行方案A（改造现成Unity环境场景）；实在不行再考虑方案B。不得擅自改成自己搭建地形并冒充A，不购买素材。

## 当前状态：等待素材包，不是已完成导入
首选 Pure Poly / Free Low Poly Nature Forest，Asset Store ID 205742，免费3.0。
官方页：https://assetstore.unity.com/packages/3d/environments/landscapes/free-low-poly-nature-forest-205742
已核实页面：34个资源；一个素材展示场景、两个demo场景；3.0新增250x250 Terrain地图与人工摆放物体；默认URP，列Unity6000.2兼容。是否适合Unity6000.3.14f1与本项目GPU战斗仍需实测。官方页面信息不是已经打开过实际场景。
备选方案A包：Polytope / Low Poly Environment - Nature Free，ID187052；含一个demo场景，默认Built-in，需导入包内URP版本。不要把付费完整版场景误作免费包内容。

2026-10-04已只读检查工程Assets、_DownloadedModels和Windows标准Asset Store缓存；无上述两个地形包。缓存只有已有角色和Mirror包。未读取账号凭据，未绕过商店领取，未购买或导入素材。

## 用户需要完成的一步
用自己的Unity账号免费Add to My Assets；在Unity Package Manager的My Assets下载205742即可，暂不Import。下载到工程机器默认Asset Store缓存后可继续读取；也可把合法取得的unitypackage放到工程根_DownloadedModels/TerrainPlanA/或上传会话。无需提供账号密码。

## 拿到包后的动作
1. 校验包来源/许可/哈希，先检查包内容与脚本，避免未知初始化脚本修改主工程。
2. 优先在工程外隔离Unity试装项目中导入，使用同版本Unity和URP；保留GUID与作者目录，记录缺失依赖。不要直接导入第三方ProjectSettings覆盖当前工程。
3. 打开原有demo地形场景，先核看真实地形起伏、材质、与现有角色比例。记录原图，不拿宣传图充当实装证据。
4. 复制为独立改造场景，保留原地图地形和可用布局，清理多余相机/演示脚本及过密装饰，不伪装成自建方案B。
5. 根据实际Terrain/mesh检查现有TerrainSurfaceAsset烘焙适配；地表渲染、采样、禁行、阵型脚印与GPU导航保持一致。不能认为有Collider就能约束GPU单位。
6. 添加明确的部署区与通路，验证上下坡、绕障碍、远程地表交互、当前常用兵力性能，再决定正式接入当前战场目录与新试玩包。

## 不变项
11版原包及旧包保留，10版相机/双轴拖动不再调整；未授权commit/push。先交付实际导入的场景证据和可行性判断，不宣称素材到手就完成地形战斗。

## 转向条件
首选包若不适合，先检查另一个方案A候选；如果两者的地图布局/地形结构/改造成本都不合适，报告具体原因，再讨论方案B，不静默切换。
