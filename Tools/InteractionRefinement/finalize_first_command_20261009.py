from pathlib import Path
import json,hashlib,subprocess,csv,datetime,os
R=Path(__file__).resolve().parents[2];P=R/'outputs/P12';Q=R/'outputs/P9FirstCommand-20261009-01';D=R/'outputs/P9ColdFix-20261009-01';O=R/'outputs/P9Combined-20261009-01';L=P/'Logs/InteractionRefinement-20261007/P8'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def save(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8')
assert subprocess.check_output(['powershell','-NoProfile','-Command',"@(Get-Process Unity,WarSandbox -ErrorAction SilentlyContinue).Count"],text=True).strip()=='0'
m=json.loads((Q/'candidate-manifest.json').read_text(encoding='utf-8'));assert not m.get('shaderPolicyTrial');assert len(m['additionalModifiedFiles'])==2
assert all(sha(P/f)==h for f,h in m['files'].items());r03=json.loads((D/'patch-manifest.json').read_text(encoding='utf-8'));assert all(sha(P/f)==h for f,h in r03['files'].items())
for n in ['fixed-firstverify-01','fixed-firstcold-01','fixed-firstcold-02','fixed-firstcold-03']:
 t=json.loads((Q/(n+'.json')).read_text(encoding='utf-8'));assert t['passed'] and t['motherProtection']['passed'] and t['candidateCodeMatchesDeclaredManifest'] and t['harnessUnchanged']
for n in ['shader-trial-cache.json','shader-trial02-cache.json']:assert json.loads((Q/n).read_text(encoding='utf-8'))['cacheRestoredExactly']
b=json.loads((O/'mother-before.json').read_text(encoding='utf-8'));cache=json.loads((R/'outputs/P9Capacity-20261009-01/verified-mother-stat-cache.json').read_text(encoding='utf-8'));assert cache['baselineSha']==sha(O/'mother-before.json')
for f,h in b['files'].items():
 p=R/f;assert p.is_file();st=[p.stat().st_size,p.stat().st_mtime_ns]
 if st!=cache['stats'].get(f):assert sha(p)==h,f
env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0';assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,env=env,text=True).strip()==b['head'];assert sha(R/'.git/index')==b['index']
user=Path(b['userRoot']);assert {f.relative_to(user).as_posix():sha(f) for f in user.rglob('*') if f.is_file()}==b['userFiles']
expected={f for f in b['files'] if f.split('/')[0] in ['Assets','Packages','ProjectSettings']};actual={p.relative_to(R).as_posix() for base in ['Assets','Packages','ProjectSettings'] for p in (R/base).rglob('*') if p.is_file()};assert actual==expected
for f in m['additionalModifiedFiles']:
 target=Q/'final-source'/f;assert not target.exists();target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes((P/f).read_bytes())
(Q/'P9FirstCommandTests.cs.frozen').write_bytes((P/'Assets/Game/Tests/PlayMode/P9FirstCommandTests.cs').read_bytes());(Q/'p9_first_command_run.py.frozen').write_bytes((R/'Tools/InteractionRefinement/p9_first_command_run_20261009.py').read_bytes())
def first(run):
 row=next(csv.reader((L/('cold-fix-'+run)/'cold-summary.csv').read_text(encoding='utf-8').splitlines()));return {'command':row[1],'members':int(row[2]),'callbackMs':float(row[4]),'observedReceiptMs':float(row[5]),'readbackMs':float(row[6]),'planMs':float(row[7]),'uploadMs':float(row[8]),'maxYieldGapMs':float(row[9])}
summary={'candidate':'navigation-sharing-01 on r03','accepted':False,'scope':'Partial improvement only. Residual cold shader stall remains open.','productionFilesChangedFromR03':m['additionalModifiedFiles'],'r03ShaderAndOtherTwelveProductionHashesUnchanged':True,'warmShaderFirstCommand':{'baselineRun':'fixed-cold-08','baseline':first('fixed-cold-08'),'candidateRun':'fixed-firstcold-03','candidate':first('fixed-firstcold-03')},'coldEvidenceNotFinalCandidateAcceptance':{'baseline':first('fixed-cold-07'),'navigationPlusRejectedConditionalPolicy':first('fixed-firstcold-01'),'rejectedUnconditionalPolicy':first('fixed-firstcold-02')},'correctness':{'navigationCasesPassed':3,'terrainConfigurations':12,'parallelFlowComparisons':36,'finalOriginalUiPrefixPassed':1,'syntheticUGUICallbacksNotHumanInput':True},'shaderTrials':'Both compiler-policy trials reverted; loop structure screen not deployed. No prewarming.','trialCachesRestoredExactly':True,'motherProtectionPassed':True,'noUnityProcessActive':True,'notValidated':['Cold shader stall fixed','Player','Human first-use experience','P3 full acceptance'],'finished':datetime.datetime.now().isoformat()}
save(Q/'final-summary.json',summary)
text='\n\n## 2026-10-09 18:06 首次命令导航共享候选01（部分有效，未验收）\n- P12 当前为 r03 + MassEngineManager.LocalOrders.cs / TerrainNavigationGrid.cs 两文件导航共享候选；仅精确匹配当前上下文/半径时共享只读拓扑，求解工作数组独立。\n- Shader 缓存存在的原首次UI流程：规划332.93→33.39ms，首次攻击回执观察371.19→54.78ms；不是第二次点击、不是冷Shader已修复。导航3/3、最终UI流程1/1通过。\n- 冷态约5秒Shader停顿仍开放。条件编译策略无收益，无条件策略首次攻击约7.15秒已拒绝；两者均撤回，r03十二生产hash全部恢复匹配，缓存均原样恢复；循环结构CPU筛选无收益，未部署。\n- 证据/原字节回滚备份/最终源文件：outputs/P9FirstCommand-20261009-01；说明：首次命令优化候选说明.md。母工程/HEAD/index/用户数据保护通过；未提交、合并、发布或代签。\n- 保留导航候选，不扩容量或渲染支线。后续仍是首次Shader长停顿主线；不要把本轮当作P3完成。\n'
with (R/'NEXT_TASK.md').open('a',encoding='utf-8') as f:f.write(text)
print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)
