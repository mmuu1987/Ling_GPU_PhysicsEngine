from p3_density_runtime_20261008 import *
sys.stdout.reconfigure(encoding='utf-8')
rows=[]
for tag in ['p3-residual-2','p3-residual-3']:
 folder=LOG/'P3'/tag
 frames=list(csv.DictReader((folder/'frames.csv').open(encoding='utf-8')))
 wanted={int(x['frame']) for x in frames[1:]}
 events=sorted([{k:int(v) for k,v in x.items()} for x in csv.DictReader((folder/'stages.csv').open(encoding='utf-8')) if int(x['kind'])==1],key=lambda x:x['t0'])
 last={};n=same=0
 for x in events:
  if x['frame'] in wanted:
   n+=1;same+=int(last.get(x['team'])==x['goals'])
  last[x['team']]=x['goals']
 rows.append({'tag':tag,'dynamicUpdates':n,'sameGoalCountAsPreviousSameTeam':same,'upperBoundExactSequenceReuseFraction':same/n if n else 0})
print(json.dumps(rows,indent=2),flush=True)
