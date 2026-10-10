from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
W=LOG/'P3'/'burst-preparation-gate-20261008-01'
s=json.loads((W/'summary.json').read_text(encoding='utf-8'));assert s['protectionPassed'] and s['sourceRestored'] and s['cacheRestored'] and s['otherCacheUnchanged']
q=s['phases'][0]['result'];compact={k:v for k,v in q.items() if k not in ['cacheViews','pollMs','heartbeatGapMs','reuseMs']}
compact['pollMedianMs']=statistics.median(q['pollMs']);compact['pollTotalMs']=sum(q['pollMs']);compact['heartbeatGapsOver33ms']=sum(x>33 for x in q['heartbeatGapMs']);compact['heartbeatGapsOver100ms']=sum(x>100 for x in q['heartbeatGapMs'])
compact['heartbeatLargest10ms']=sorted(q['heartbeatGapMs'],reverse=True)[:10]
compact['cacheFilesBeforeLaunch']=s['phases'][0]['cacheFilesBeforeLaunch'];compact['cacheFilesAfterExit']=s['phases'][0]['cacheFilesAfterExit']
compact['cacheViews']=[{'stage':v['stage'],'files':len(v['files']),'dlls':sum(f['path'].lower().endswith('.dll') for f in v['files'])} for v in q['cacheViews']]
compact['protectionPassed']=True;compact['prewarmIntegrated']=False;compact['formalGatePassed']=False
tests=ET.parse(P8/'p3-burst-preparation-gate-cold-01'/'results.xml').getroot()
compact['testsPassed']=int(tests.get('passed','0'));assert compact['testsPassed']==14
compact['policyTestsAreInjected']=True
compact['probeDoesNotReplaceJobAlgorithm']=True
save(W/'compact-analysis.json',compact)
print(json.dumps(compact,ensure_ascii=False,indent=2),flush=True)
