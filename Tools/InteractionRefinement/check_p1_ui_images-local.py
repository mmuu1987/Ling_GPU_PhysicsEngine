from PIL import Image
import numpy as np,json,hashlib
from pathlib import Path
root=Path('交互完善方案-20261007');audit=json.loads((root/'P1-AUDIT.json').read_text());known={Path(x['path']).name:x['sha256'] for x in audit['screenshots'] if '/preview-03/' in x['path']}
records=[]
for name in ['library-1280x720.png','expanded-1280x720.png','library-1920x1080.png','expanded-1920x1080.png']:
 p=root/'P1截图'/name;digest=hashlib.sha256(p.read_bytes()).hexdigest();assert digest==known[name]
 a=np.array(Image.open(p));mask=(abs(a[:,:,0].astype(int)-132)<=1)&(abs(a[:,:,1].astype(int)-72)<=1)&(abs(a[:,:,2].astype(int)-13)<=1)&(a[:,:,3]>240)
 ys,xs=np.where(mask);assert len(xs)>=40
 lo=np.array([xs.min(),ys.min()]);hi=np.array([xs.max(),ys.max()]);extent=(hi-lo)/2;assert np.all(extent>[25,12]);points=(np.column_stack([xs,ys])-(hi+lo)/2)/extent
 x,y=points[:,0],points[:,1];matrix=np.column_stack([x*x,x*y,y*y,x,y]);coef=np.linalg.lstsq(matrix,np.ones(len(x)),rcond=None)[0]
 q=np.array([[coef[0],coef[1]/2],[coef[1]/2,coef[2]]]);assert np.all(np.linalg.eigvalsh(q)>0)
 center=-.5*np.linalg.solve(q,coef[3:]);factor=1+center@q@center;d=points-center;radius=np.sqrt(np.einsum('ni,ij,nj->n',d,q,d)/factor);error=float(np.percentile(abs(radius-1),95));assert error<.1,(name,error)
 scale=a.shape[1]/1280;roi=np.array([1011,208,1204,500] if name.startswith('library') else [415,118,865,506])*scale
 assert lo[0]>=roi[0] and lo[1]>=roi[1] and hi[0]<roi[2] and hi[1]<roi[3]
 records.append({'file':name,'sha256':digest,'strictInkPixels':len(xs),'inkSrgb':[132,72,13],'tolerancePerChannel':1,'bboxTopLeftPixels':[int(v) for v in [*lo,*hi]],'ellipseRelativeResidualP95':error,'inActualRawImageRegion':True,'passed':True})
result={'passed':True,'note':'Post-capture narrow-colour and ellipse-locus check. Broad in-Unity goldInModelViewport counts may include similar character colours and are not a pure ring pixel count. This check qualifies visible UI ink, not physical metres (those use independent camera/plane tests).','runs':records}
(root/'P1-UI-PIXEL-REVIEW.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps(result,indent=2))
