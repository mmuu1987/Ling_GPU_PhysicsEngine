# 预览相机完整链路修正：深度遮挡与默认正面（2026-10-04）

用户提供四张08版、切换角色后未拖动的默认截图：男战士后脑露出脸部、骑士/骷髅/游侠部件穿插。本轮以该状态复现，不再靠修改模型资源或反复调俯仰角掩盖问题。

## 根因与修正

1. **投影矩阵被重复做GPU API转换。** `CommandBuffer.SetViewProjectionMatrices`应接收Unity CPU投影。此前先调用`GL.GetGPUProjectionMatrix(..., true)`再交给它，导致D3D reversed-Z转换重复，深度顺序反向：远处表面能覆盖近处表面。08手动反转剔除仅让顶底标记通过，没纠正深度，因此不能作为正确渲染基线。
2. 移除预转换，直接传CPU `Matrix4x4.Perspective`；同时移除补偿性的反向剔除和RawImage纵向UV翻转。每个坐标转换只进行一次，由图形后端负责API深度处理。绘制前使用正常剔除、结束恢复进入前状态。
3. **默认镜头原来在角色背面。** 模型约定朝+Z，旧Yaw=-28对应镜头在-Z侧；深度错误又让脸部从背面穿出。默认/复位Yaw改为152°，得到正面偏侧视角。仍俯视28°、垂直FOV30°；不重新调整缩放或阴影。

正常游戏仍复用离屏CommandBuffer预览服务；独立真正的Unity透视Camera用于验证基准，不以“创建了Camera组件”冒充生产实现变化。测试让Camera自身管理投影、裁切、剔除、深度，使用同模型/材质/VAT帧/观察中心/距离，与预览路径独立对照。

## 证据：不仅判断画面非空

- `Logs/UnitPreviewCameraDebug-20261004/before-01/`：两种现役shader × 两种三角形顺序，4/4均检出错误。标准Camera：近绿13924像素、远红0；08预览：近绿1824、远红12100。交换绘制顺序仍错，证明不是依赖绘制排序的偶然遮挡。
- `after-01/`修正后4/4通过；标准Camera与预览均为近绿13924、远红0，轮廓交并比>0.99。
- 最终`final-02/PlayMode.xml`：**14/14通过**。包括上述深度/绘制顺序验证、两shader顶底标记和真实RawImage输出、33代表动画/阴影/资源生命周期、旋转固定距离/FOV/滚轮复位及原军团草稿流程。
- 增加四个实际角色同帧Camera对照：`roster-male`、`roster-knight`、`roster-skeleton-rogue`、`roster-ranger`。最终默认视角轮廓IoU均为1；平均归一化色度误差分别0.007571017、0.001621386、0.006947051、0.004378865，均小于0.08。灯光环境不同允许少量亮度/色差，不宣称全RGBA逐像素相同。
- 四角色参考图与预览图在`final-02/character-camera-*.png`、`character-preview-*.png`；已核看脸/头发、头盔、斗篷/身体、武器遮挡。测试固定待机相位，不拿不同帧做对照。

## 本轮交付

- `Builds/ToyUI-20261004-09/Start-ToyUI.cmd`，1920×1080独占全屏，GUID `1b057a4b1d0b426fbf81c7b0b60c3a6a`。
- Windows x64 Development，32场景，913766823bytes，0error/20warning。08及旧包保留，08EXE SHA256未变。
- `Logs/ToyUIBuild-20261004-09/player-check.json`：独立EXE exit0/passed=true/livePreviews=33，同GUID；图鉴、军团详情、返回草稿、开战、暂停、手动结算通过。不是完整OS拖动/全部自然胜负或100k/长期性能验收。
- 保留蓝灰底板、阴影中心alpha45%、拖动增量方向；不改模型、材质资源、VAT、战场相机或68模板/33代表/29可选战场。旧左侧静态头像未重生成，本轮不要以旧静态图的错误可见面反向调整实时模型。
- 测试/构建恢复TimeManager和三份Native材质运行前状态。新增`UnitPreviewCameraParityTests.cs`及meta与预览系列一并待提交；本轮未commit/push。
- 下一步让用户复查09四个角色默认正面俯视和连续旋转。相机视觉风格可以后续微调，但不可回退到重复GPU投影转换、反向深度或以材质Cull Off掩盖问题。
