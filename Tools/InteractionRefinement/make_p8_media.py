from common_p8 import *
from PIL import Image,ImageDraw,ImageFont
import csv,statistics,base64,re,sys
sys.stdout.reconfigure(encoding='utf-8');tag=sys.argv[1];out=P8/tag;assert json.loads((out/'process.json').read_text(encoding='utf-8'))['passed'];dest=ROOT/'Docs/InteractionRefinement-20261007/P8-media';assert not dest.exists();dest.mkdir()
rows=list(csv.DictReader((out/'p8-frames/timeline.csv').open(encoding='utf-8')));images=[];durations=[];labels={'drag':'框选中','selected':'选区确认','move':'移动','hold':'待命','retreat':'撤退','attack':'攻击'}
fontpath=Path(r'C:\Windows\Fonts\msyh.ttc');font=ImageFont.truetype(str(fontpath),19) if fontpath.exists() else ImageFont.load_default()
for r in rows:
 im=Image.open(out/'p8-frames'/r['file']).convert('RGB');canvas=Image.new('RGB',(im.width,im.height+40),'#173237');canvas.paste(im,(0,40));d=ImageDraw.Draw(canvas);d.text((14,8),'P8 自动验证抽帧（非真人 / 非实时录像） · '+labels[r['phase']]+' · 已选 '+r['selected'],font=font,fill='white');images.append(canvas);durations.append(700 if r['phase']=='drag' else 350 if r['phase']=='selected' else 200)
images[0].save(dest/'P8-command-capture.gif',save_all=True,append_images=images[1:],duration=durations,loop=0,disposal=2,optimize=False)
for phase in ['drag','selected','move','hold','retreat','attack']:
 matches=[(i,r) for i,r in enumerate(rows) if r['phase']==phase];i,r=matches[-1];Image.open(out/'p8-frames'/r['file']).save(dest/('P8-'+phase+'.png'))
result={'sourceTag':tag,'sampledFrames':len(rows),'gifDurationSeconds':sum(durations)/1000,'sourceDescription':'Actual GPU/UI GameView frames; fixed simulation delta 1/60 and capture every 12 frames. Gaps between phases/cost probes, therefore sampled replay, not continuous real-time or OS/human interaction recording. Raw PNGs and wall-time ledger retained.','windows':{}}
for p in sorted(out.glob('p8-cost-*.csv')):
 data=list(csv.DictReader(p.open(encoding='utf-8')));stats={}
 for col in ['wall_ms','cpu_frame_ms','gpu_frame_ms']:
  values=sorted(float(x[col]) for x in data if float(x[col])>0);stats[col]={'validSamples':len(values),'median':statistics.median(values) if values else None,'p95':values[min(len(values)-1,int(len(values)*.95))] if values else None,'mean':statistics.mean(values) if values else None}
 stats['frames']=len(data);stats['durationSeconds']=sum(float(x['wall_ms']) for x in data)/1000
 for col in ['selection_readback_pairs','command_snapshots','command_ack_requests']:stats[col+'_delta']=int(data[-1][col])-int(data[0][col])
 for col in ['selection_logical_gpu_bytes','command_logical_gpu_bytes']:stats[col]=max(int(x[col]) for x in data)
 result['windows'][p.stem.replace('p8-cost-','')]=stats
result['limitations']='Sequential unequal-work 240-frame windows, not balanced regression attribution or FPS/OS present/peak VRAM/general-scale qualification. CPU/GPU frame timing may include other Editor workloads.'
result['context']=(out/'p8-capture-context.txt').read_text(encoding='utf-8');save(P8/'media-and-cost.json',result)
blob=base64.b64encode((dest/'P8-command-capture.gif').read_bytes()).decode('ascii');export=P8/'media-export';assert not export.exists();export.mkdir();names=[]
for n,i in enumerate(range(0,len(blob),1000000)):
 name=f'gif-{n:02d}.b64';(export/name).write_text(blob[i:i+1000000],encoding='ascii');names.append(name)
save(export/'manifest.json',{'parts':names,'bytes':(dest/'P8-command-capture.gif').stat().st_size,'sha256':sha(dest/'P8-command-capture.gif')});print(json.dumps(result,ensure_ascii=False,indent=2));print('EXPORT',names)
