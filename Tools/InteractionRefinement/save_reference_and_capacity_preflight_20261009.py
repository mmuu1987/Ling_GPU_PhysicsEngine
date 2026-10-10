from pathlib import Path
import json,hashlib,zipfile,datetime,subprocess,os,re
R=Path(__file__).resolve().parents[2];A=R/'Docs/ReferenceStudies/UEBS2-20261009';F=R/'outputs/P9ColdFix-20261009-01';P=R/'outputs/P12'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
m=json.loads((A/'manifest.json').read_text(encoding='utf-8'))
assert all(sha(A/f)==v['sha256'] and (A/f).stat().st_size==v['bytes'] for f,v in m['files'].items())
Z=A.parent/'UEBS2-20261009-offline.zip';assert not Z.exists() and not(A/'verification.json').exists()
with zipfile.ZipFile(Z,'x',compression=zipfile.ZIP_DEFLATED) as z:
 for f in sorted(m['files']):z.write(A/f,arcname='UEBS2-20261009/'+f)
 z.write(A/'manifest.json',arcname='UEBS2-20261009/manifest.json')
with zipfile.ZipFile(Z) as z:
 assert z.testzip() is None
 for f,v in m['files'].items():assert hashlib.sha256(z.read('UEBS2-20261009/'+f)).hexdigest()==v['sha256']
v={'finished':datetime.datetime.now().isoformat(),'verifiedFiles':len(m['files']),'allFileHashesMatch':True,'zipVerified':True,'zipPath':str(Z),'zipSha256':sha(Z),'referenceMcpRequired':False,'referenceProjectModified':False}
(A/'verification.json').write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8');print('OFFLINE ARCHIVE VERIFIED',json.dumps(v,ensure_ascii=False),flush=True)
# Capacity stage 0 is read-only source/evidence preflight, NOT a runtime benchmark.
C=R/'outputs/P9Capacity-20261009-01';C.mkdir()
b=json.loads((R/'outputs/P9Combined-20261009-01/mother-before.json').read_text(encoding='utf-8'));env=os.environ.copy();env['GIT_OPTIONAL_LOCKS']='0'
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,env=env,text=True).strip();assert head==b['head'] and sha(R/'.git/index')==b['index']
patch=json.loads((F/'patch-manifest.json').read_text(encoding='utf-8'));assert patch['revision']==3 and all(sha(P/f)==h for f,h in patch['files'].items())
h=json.loads((F/'prototype-harness.json').read_text(encoding='utf-8'));assert all(sha(P/f)==x for f,x in h.items())
for f in patch['files']:
 t=C/'frozen-production-files'/f;t.parent.mkdir(parents=True,exist_ok=True);t.write_bytes((P/f).read_bytes())
(C/'patch-manifest-r03.json').write_bytes((F/'patch-manifest.json').read_bytes())
(C/'source-manifest-reference.json').write_bytes((R/'outputs/P9Combined-20261009-01/source-manifest.json').read_bytes())
(C/'harness-manifest-r03.json').write_bytes((F/'prototype-harness.json').read_bytes())
paths=['Assets/MassEngine/Core/MassGpuBufferManager.cs','Assets/MassEngine/Core/CombatBufferSet.cs','Assets/MassEngine/Core/LocalOrderChannel.cs','Assets/Game/Editor/InteractionRefinementP3Qualification.cs','Assets/Game/Scripts/WarSandboxVatPerformance.cs']
for f in paths:
 t=C/'source-inspection'/f;t.parent.mkdir(parents=True,exist_ok=True);t.write_bytes((P/f).read_bytes())
for f in ['Assets/Game/PerformanceBaseline.md','Docs/CURRENT_DELIVERY.md','AGENTS.md']:
 t=C/'historical-evidence'/f;t.parent.mkdir(parents=True,exist_ok=True);t.write_bytes((R/f).read_bytes())
s=(P/paths[0]).read_text(encoding='utf-8');combat=(P/paths[1]).read_text(encoding='utf-8')
assert 'CongestionWordsPerAgent = 12' in combat and 'EngagementSlotsPerTarget = 8' in s
assert 'agentPositionReadBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 2)' in s and 'homePositionBuffer = new ComputeBuffer(AgentCount, sizeof(float) * 3)' in s
rows=[]
for n in [2048,10000,50000,100000,200000]:
 rows.append({'agents':n,'baseAgentAndCombatLogicalBufferBytes':n*212,'visibleIndicesBytesPerUnitType':n*12,'localPositionHpReadbackBytesPerSnapshot':n*12,'runtimeTestExecuted':False})
r={'finished':datetime.datetime.now().isoformat(),'phase':'stage-0 source and evidence preflight','status':'preflight-complete-runtime-not-started','referenceArchive':str(A),'referenceMcpRequired':False,'productionRevision':3,'productionFilesFrozen':len(patch['files']),'head':head,'indexUnchanged':True,'productionUnchanged':True,'runtimeTestsExecuted':False,'newPerformanceClaim':False,'tiers':rows,'budgetDefinitions':{'baseAgentAndCombat':'212*N bytes = agent64 + positions16 + unitType4 + combat128; excludes all other resources, NOT total VRAM','visibleIndices':'12*N*unitTypeCount','mixedAndTeamGrids':'4*gridCells*(1+teamCount)*(1+maxAgentsPerCell)','projectiles':'68*actualProjectileCapacity + args; cap must be read from actual scenario','localOptional':'68*N + 60*flowCells; plus 64KiB constant layout and CPU table; selection separate','notIncluded':['VAT and textures','meshes','terrain and navigation textures','driver/editor allocations','CPU snapshots and graph workspaces','all auxiliary resources']},'historicalEvidence':{'has100kPlayerMeasurements':True,'hasHistorical200kEditorPressureMeasurement':True,'canReuseAsCurrentVersionResult':False},'next':['Confirm current runtime scene/formation can physically support each tier without artificial crowd packing','Inventory actual total graphics allocation including models, terrain, grids and projectile capacity before allowing high tiers','Use an isolated bounded rendered harness with local orders disabled; keep Editor and Player Present metrics separate','Run smallest tiers first; stop on errors, budget/geometry failure or invalid timing; no automatic 200k launch','Only then build and compare a compact-member dispatch candidate']}
(C/'preflight.json').write_text(json.dumps(r,ensure_ascii=False,indent=2),encoding='utf-8')
(C/'README.md').write_text('# Capacity baseline stage 0\n\nOffline reference archived and byte-verified. P12 revision03 production files frozen without modifying P12.\n\nNo new Unity/Player runtime benchmark has started. Source-derived buffer figures are partial logical sizes, not VRAM capacity approval or FPS predictions. Historical 100k/200k evidence exists but does not qualify the current version.\n\nSee preflight.json and the offline reference NEXT-STEPS.md for next gated steps.\n',encoding='utf-8')
assert all(sha(P/f)==x for f,x in patch['files'].items()) and sha(R/'.git/index')==b['index']
print('CAPACITY PREFLIGHT COMPLETE; NO RUNTIME BENCHMARK STARTED',str(C),flush=True)
