from common_p5 import *
import sys
sys.stdout.reconfigure(encoding='utf-8');print(json.dumps({'active':processes(),'lock':(ROOT/'Temp/UnityLockfile').exists(),'project':project_check(),'userData':user_check()},ensure_ascii=False,indent=2))
