from common_p8 import *
from PIL import Image
import base64
folder=ROOT/'Docs/InteractionRefinement-20261007/P8-media';p=folder/'P8-command-capture.mp4';assert not p.exists();p.write_bytes(base64.b64decode((P8/'media-export/mp4.b64').read_text(encoding='ascii')))
assert sha(p)=='37c2b3b8a79703ce8bffdfe72338f6b05973b8a78c43346ea7b90a81c258dd86'
image=Image.open(folder/'P8-selected.png');image.crop((450,240,540,320)).resize((720,640)).save(folder/'P8-selected-detail.png')
save(P8/'media-encoding.json',{'sourceGifSha256':sha(folder/'P8-command-capture.gif'),'mp4Sha256':sha(p),'mp4Bytes':p.stat().st_size,'encoding':'Temporary local imageio-ffmpeg/libx264, yuv420p, 30fps, CRF19, even-size padding, no audio; GIF timing preserved; uploaded and SHA256 verified. Temporary copies removed at final cleanup.','detailImage':'Original selected frame crop [450,240,540,320], enlarged to 720x640, not higher-resolution source.'});print('VERIFIED MP4',p.stat().st_size,sha(p))
