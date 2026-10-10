from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
results=[]
for name in ['burst-startup-20261008-01','burst-prewarm-20261008-01']:
 folder=LOG/'P3'/name;s=json.loads((folder/'summary.json').read_text(encoding='utf-8'));assert s['protectionPassed'] and s['sourceRestored'] and s['cacheRestored'] and s['otherCacheUnchanged'] and not s['adopted']
 archive=folder/'generated-JIT';matches=[]
 for p in archive.rglob('*'):
  if p.is_file() and p.suffix.lower() in ['.dll','.pdb','.cm','.hash'] and p.stat().st_size<64*1024**2:
   data=p.read_bytes()
   if b'TerrainNavigationSolveJob' in data or 'TerrainNavigationSolveJob'.encode('utf-16le') in data:matches.append(p.relative_to(archive).as_posix())
 for phase in s['phases']:
  q=phase['result'];views=[]
  for v in q['cacheViews']:
   paths={x['path'].replace('\\','/') for x in v['files']}
   views.append({'stage':v['stage'],'utc':v['utc'],'files':len(paths),'dlls':sum(p.lower().endswith('.dll') for p in paths),'jobNameMatchingPathsPresent':sorted(paths.intersection(matches))})
  compact={k:v for k,v in q.items() if k not in ['cacheViews','reuseMs']}
  compact.update(label=phase['label'],tag=phase['tag'],cacheFilesBeforeLaunch=phase['cacheFilesBeforeLaunch'],cacheFilesAfterExit=phase['cacheFilesAfterExit'],cacheViews=views,jobNameMatchingArchiveFiles=matches)
  results.append(compact)
summary={'phases':results,'sourceAndOriginalCacheRestored':True,'allProtectionPassed':True,'adopted':False,'P3Closed':False,'interpretation':'Job-name strings in generated cache are corroboration, not compiler hit/miss telemetry or exclusive compilation time. Empty project JIT at launch is proven; machine-wide cold state and player/AOT performance are not.'}
save(LOG/'P3'/'burst-prewarm-20261008-01'/'combined-analysis.json',summary)
assert project_check(full=True)['passed'] and user_check()['passed'] and not processes() and not(ROOT/'Temp/UnityLockfile').exists()
print(json.dumps(summary,ensure_ascii=False,indent=2),flush=True)
