# UI 原型 · Q 版 / B 战术指挥（UI Toolkit）

纯 UI 原型，**不含任何游戏逻辑**，所有数据均为模拟数据（`Runtime/UiProtoBData.cs`）。只用于在编辑器里评估视觉风格和交互手感。

## 打开方式

菜单 **Tools → UI 原型**：

| 菜单 | 作用 |
|---|---|
| Q 版 · 打开预览（进入 Play） | 用 `UiProtoToy.unity` 进入 Play，退出后回到当前场景 |
| Q 版 · 截图（自动进 Play，自动退出） | 6 张 1920×1080 截图输出到 `outputs/UiProtoToy/` |
| Q 版 · 重建场景 | 重新生成 `UiProtoToy.unity` |
| B 战术指挥 · 打开预览 / 截图 / 重建场景 | 同上，B 版输出到 `outputs/UiProtoB/` |

两套皮肤共用同一套 UXML、控件和交互：
- Q 版 = `RootToy.uxml` + `UiProtoToy.uss`（`@import` B 版样式后覆盖），地图配色在 `Runtime/TacTheme.cs` 的 `ToyPal`。
- `UiProtoBApp.toy = true` 时会把装饰性英文标签换成中文或隐藏。

不会改动当前打开的场景（使用 `playModeStartScene`），也不会加入 Build Settings。

## 页面与快捷键

底部原型导航栏，或用 **F5–F9** 直接切换：作战选择 / 配兵布阵 / 战斗 HUD / 结算 / 设置。

- **作战选择**：点击选择，双击或 Enter 进入部署，↑↓ 切换。
- **配兵布阵**：在地图上拖动编成；工具「缩放」拖控制点改尺寸；R 旋转 90°；Tab 切换编成；Ctrl+Z / Ctrl+Y 撤销 / 重做；Delete 删除。
  - 放进禁行地形或越出部署区时，草稿变为无效并标红：保存、应用和开战都被禁用，Esc 撤回到上一次有效布阵（与计数锁 B 方案一致）。
- **战斗 HUD**：Space 暂停；A / M / H / R 下达进攻、移动、防守、撤退；1–9 或 Tab 选择军团；F 跟随；F3 全景；点击小地图移动视角。
- **结算 / 设置**：Esc 关闭；设置项只改界面状态，不写入游戏配置。

## 目录

```
UiPrototype/
  Game.UiPrototype.asmdef      仅编辑器可编译（defineConstraints: UNITY_EDITOR），不引用任何游戏程序集
  Runtime/
    TacElements.cs             自绘框体（切角、角标、发光）、图标、背景
    TacControls.cs             分段按钮、滑条、开关、循环选择器
    TacMaps.cs                 地形等高线地图、部署地图（拖拽 / 缩放 / 校验）
    UiProtoBApp.cs             入口：字体、页面切换、提示条、截图
    UiProtoBScreens.cs         各页面控制器（只操作模拟数据）
    UiProtoBData.cs            模拟数据
  Editor/UiProtoBSetup.cs      上面三个菜单
  UI/                          UXML 布局、UiProtoB.uss 样式、主题、Textures/
  UiProtoB.unity               由菜单生成
```

## 说明

- 样式全部在 `UI/UiProtoB.uss`，颜色令牌在 `:root`（`--c-*`），改色只改这里。
- 字体在运行时从系统字体创建（Bahnschrift / 微软雅黑 UI / Consolas），**不往仓库里放字体文件**（仓库是公开的）。正式接入前需要换成有授权的字体。
- 框体、地图都由 `generateVisualContent` 矢量绘制，可以任意缩放，不依赖切图。
- 接入正式游戏时，保留 UXML / USS / 控件层，把 `UiProtoBScreens.cs` 中对模拟数据的读写换成真实数据接口即可；控件层不需要改。
- 删除整个 `Assets/Game/UiPrototype/` 目录即可完全移除，不影响其他代码。
