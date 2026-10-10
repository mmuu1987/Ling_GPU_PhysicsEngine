from common_p3 import *
import csv,statistics,math,json,sys
sys.stdout.reconfigure(encoding='utf-8')
for tag in ['qualification-perf24-a','qualification-perf48-a','qualification-perf48-b','qualification-perf24-b']:
 with (P3/tag/'frames.csv').open() as f:rows=list(csv.DictReader(f))
 seconds=[float(x['seconds']) for x in rows];delta=[1000*(y-x) for x,y in zip(seconds,seconds[1:])];d=sorted(delta)
 print(json.dumps({'tag':tag,'samples':len(rows),'elapsed':seconds[-1]-seconds[0],'sumUnityFrameMs':sum(float(x['frameMs']) for x in rows),'renderedFps':(len(rows)-1)/(seconds[-1]-seconds[0]),'wallMedianMs':d[math.ceil(len(d)*.5)-1],'wallP95Ms':d[math.ceil(len(d)*.95)-1],'maxFrameIdGap':max(int(b['frame'])-int(a['frame']) for a,b in zip(rows,rows[1:]))}))
