# 图鉴与选兵展示去重 · 2026-10-03

## 已完成
- 图鉴从66条可见模板收敛到33个模型代表；不删除资源、不修改Catalog.asset、不更换内部编号。68个旧身份与29个可选战场保持。
- 重复展示主要来自：男角色11→1、女角色15→1、剑盾骑士9→1、斧盾骷髅2→1，共减少33项。原已隐藏的2个模板不恢复。
- 按实际近景Mesh与Material引用去重。已核对武器随近景网格烘焙；MaleFar128属于同角色LOD预算变体。保留统一库的roster-male/female/knight/skeleton-warrior作为代表。
- 新WarSandboxRosterChoices仅用于展示。图鉴显示同外观的一个代表；本地ChoiceTemplates只在当前合法模板中去重，同模型近战/远程用途仍分开保留，不注入越过rosterPolicy的全局代表。
- 完整Templates保留供SelectTemplate/ValidateDraft/旧方案校验使用，当前Draft、场景编成、参数、模板编号及修订号均不迁移或改写。
- 图鉴全局数值修改仍按原模板编号生效，不自动扩散到同模型的旧变体，界面已提示；没有新增变体管理界面。

## 验证
- `Logs/RosterDedup-20261003/run-01/EditMode.xml`：62/62通过。包含新去重测试、现有本地方案与RuntimeDeployment定向测试，非全套。
- 新测试验证33代表、68身份仍可原样解析、29个可选战场的候选非空且不越权；近战/远程用途保留；不会将战场内旧配置替换成未被允许的全局代表。
- `PlayMode.xml`：1/1通过。实际图鉴33个按钮及原详情/布阵/战斗流程；截图`00-deduplicated-library.png`。
- 新独立EXE自动回调检查通过，退出0，包含图鉴33项、进入战场、详情返回、开战、暂停、手动结算。不是OS输入/全场景实际战斗/自然胜负验收。

## 新试玩包
`Builds/ToyUI-20261003-02/Start-ToyUI.cmd`，1920×1080独占全屏启动。
GUID：726fdd6090014c2f97ec2c4e7f913ee5。Windows x64 Development，32场景，构建成功，0错误、20警告。
构建/独立进程证据：`Logs/ToyUIBuild-20261003-02/`；原Version08和ToyUI-01不覆盖。

## 保留与交接
本轮只改游戏层展示筛选、选兵呈现接线与测试/构建辅助；原战场、角色资源、GPU核心未修改，Unity重写的TimeManager/3个旧材质恢复运行前字节。源文件备份和引用盘点保存在`Logs/RosterDedup-20261003/`。本次未提交推送，不自动解除V1/人工验收门禁。逐项展示决定见`ROSTER-DISPLAY-DECISIONS.csv`。
