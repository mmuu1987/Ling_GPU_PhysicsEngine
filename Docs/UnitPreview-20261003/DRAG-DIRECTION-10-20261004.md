# 10版：修正预览拖动双轴方向（2026-10-04）

用户确认09透视与角度已解决，但左拖模型右转、上拖模型下转。本轮只反转共用Renderer.Rotate的两轴增量：Yaw += dx*0.4；Pitch -= dy*0.25。相机环绕与模型画面中的转动方向相反；09取消投影/UV翻转之后，旧输入符号不可继续沿用。

保留Yaw152/Pitch28/FOV30、CPU投影与正常剔除、未翻转UV、滚轮距离、旋转固定取景、假阴影及背景；图鉴、放大预览、军团详情同时生效。不修改模型/材质/VAT/战场相机。

方向测试更新为当前映射，顶底夹具相应改变到达低角度的拖动符号。Logs/UnitPreviewCameraDebug-20261004/drag-10/：14/14通过（包括四角色标准Camera对照与深度遮挡）。这些自动测试不替代用户实际鼠标手感验收。

入口：Builds/ToyUI-20261004-10/Start-ToyUI.cmd，1920×1080独占全屏。GUID 2fd37f1458ff44d89d70b4559054c9aa；构建成功，0error/20warning。Logs/ToyUIBuild-20261004-10/player-check.json：独立EXE通过，33代表预览，osInputTest=false。09及旧包保留，09EXE哈希未变；TimeManager/三份Native材质运行前字节已恢复。未commit/push。

下一步仅请用户复查左右、上下拖动的实际手感；用户已确认的透视与默认角度不再调整。
