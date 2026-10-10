from pathlib import Path
import json,hashlib
R=Path(__file__).resolve().parents[2];C=R/'outputs/P9Capacity-20261009-01';A=R/'Docs/ReferenceStudies/UEBS2-20261009'
a=json.loads((A/'verification.json').read_text(encoding='utf-8'));p=json.loads((C/'preflight.json').read_text(encoding='utf-8'));assert a['allFileHashesMatch'] and a['zipVerified'] and p['productionRevision']==3
n=R/'NEXT_TASK.md';old=n.read_bytes();backup=C/'NEXT_TASK-before-capacity.md';assert not backup.exists();backup.write_bytes(old)
header='''# 当前主线：借鉴已离线归档，先做容量基线，再改局部派发

用户2026-10-09授权：借鉴而不照搬UEBS2架构，继续此前计划，并将依据存到本机以便断开参考MCP。

## 本轮已完成
- `Docs/ReferenceStudies/UEBS2-20261009/README.md`：离线入口；REVIEW/ADOPTION-CONTRACT/NEXT-STEPS/历史更正及带行号摘录。
- `Docs/ReferenceStudies/UEBS2-20261009-offline.zip`：归档副本。10个内容文件SHA全部核对，ZIP逐项解压校验通过，verification.json含哈希。
- 不再需要参考MCP，不再调用它。不写入/运行参考工程，不把反编译代码搬进Assets。
- `outputs/P9Capacity-20261009-01/preflight.json`：容量前置审计完成。冻结P12 revision3的12个生产文件及manifest；P12代码、HEAD/index未变。
- 重要更正：我们已有历史100k独立包和200k Editor压力结果。2048是近期局部命令诊断规模，不是历史最大规模。旧数字不可当当前37/P12成绩。

## 尚未完成/下一步
新容量运行测试尚未启动，无新FPS结论。先确认可复用基线、实际场景/布阵空间、各档完整显存预算与测试停止条件，再用隔离/限时的渲染采样逐档测2048/1万/5万/10万/20万。不得把20万人塞进小规模布阵导致人为拥堵；不盲目跑最高档，不抢占用户未知进程。

先测无局部指挥底座，再实现一个“紧凑成员列表+按实际成员人数派发”候选。维持每agent恰一次、global排除、真实ID的LOD节奏、epoch/sequence和取消确认。当前单组4096/总16384上限不自动放宽。

之后再减少CPU全体快照依赖，拆分330ms规划各段计时。GPU增量导航/更强分帧预算仅在瓶颈有证据后展开，不共享非线程安全导航工作区。4.95s冷Attack和44s冷进场仍开放，不能用预热隐藏；不要继续只围着shader微调而跳过容量对照。

母dirty tree/P11/37不改；不提交合并、不发38、Burst默认关、不代签真人。此NEXT_TASK不进git。原revision3接续已归档到 `outputs/P9Capacity-20261009-01/NEXT_TASK-before-capacity.md`。旧一次性脚本不可重跑。
'''
assert n.read_bytes()==old;n.write_text(header,encoding='utf-8');(C/'NEXT_TASK-current.md').write_text(header,encoding='utf-8');print('Capacity handoff saved; archive self-contained; runtime measurement not started.')
