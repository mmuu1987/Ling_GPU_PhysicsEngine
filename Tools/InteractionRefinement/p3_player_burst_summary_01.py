import json
j=json.load(open('outputs/P3PlayerBurst-20261009-01/validation-01/journal.json',encoding='utf-8'))
o={'status':j['status'],'errors':j.get('errors'),'allRunsValid':j.get('allRunsValid'),'abbaPassed':j.get('abbaPassed'),'burstArtifacts':j.get('burstArtifacts'),'patch':j.get('temporaryGatePatch'),'buildSummary':j.get('buildSummary'),'gates':j.get('abbaGates')}
o['runs']=[]
for r in j['runtimeRuns']:
    x=r['result'] or {}
    o['runs'].append({'label':r['label'],'valid':r['valid'],'exit':r['exitCode'],**{k:x.get(k) for k in ('frames','medianMs','p95Ms','p99Ms','maxMs','meanMs','over33','over50','screenWidth','screenHeight','phaseAtSampleStart','phaseAtSampleEnd','playerBurstDefine','burstArgument','battleStartToSampleS','navAtSampleStart','navAtSampleEnd','error')},'log':[l for l in r.get('notableLogLines',[]) if 'xception' in l][:5]})
rs=j['restoration']
o['restoration']={k:v for k,v in rs.items() if not isinstance(v,(dict,list))}
o['restoration']['projectSettingsMatches']=rs.get('projectSettings',{}).get('matchesOriginal')
o['restoration']['assetCleanup']={k:v for k,v in rs.get('assetCleanup',{}).items()}
o['unity']=[{'label':u['label'],'exit':u['exitCode'],'err':u['scriptCompilationErrors'][:3],'cfg':u['playerBuildConfigLines'][:2]} for u in j['unityRuns']]
s=json.dumps(o,ensure_ascii=False,indent=1)
open('outputs/P3PlayerBurst-20261009-01/validation-01/summary.json','w',encoding='utf-8').write(s)
print(s)
