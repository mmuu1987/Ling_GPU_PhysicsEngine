from common_p3 import *
import ctypes,sys
from ctypes import wintypes
sys.stdout.reconfigure(encoding='utf-8')
assert not processes();assert project_check()['passed'] and user_check()['passed']
p=ROOT/'Temp/UnityLockfile';prior=json.loads((P3/'lock-inspection-01.json').read_text(encoding='utf-8'));r=json.loads((P3/'wait-window-01/process.json').read_text(encoding='utf-8'))
assert r['ownedPid']==prior['ownedRun']['ownedPid'] and r['exitCode']==1
assert p.is_file() and p.stat().st_size==0 and sha(p)==prior['lock']['sha256']
st=p.stat();assert datetime.datetime.fromtimestamp(st.st_mtime).isoformat()==prior['lock']['modified'];assert datetime.datetime.fromisoformat(r['startedAt']).timestamp()<=st.st_mtime<=datetime.datetime.fromisoformat(r['finishedAt']).timestamp()
out=P3/'recovery-01';assert not out.exists()
k=ctypes.WinDLL('kernel32',use_last_error=True);k.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE];k.CreateFileW.restype=wintypes.HANDLE;k.CloseHandle.argtypes=[wintypes.HANDLE]
# Deny any concurrent read/write handle, while allowing our own rename under the held handle.
h=k.CreateFileW(str(p),0x80000000|0x40000000|0x00010000,4,None,3,0x80,None)
assert h!=ctypes.c_void_p(-1).value,('Lock is in use; do not force',ctypes.get_last_error())
try:
 assert not processes(),'Unity opened during recovery; leave lock untouched'
 assert p.stat().st_mtime_ns==st.st_mtime_ns and p.stat().st_size==0
 out.mkdir();p.rename(out/'UnityLockfile')
finally:k.CloseHandle(h)
assert not p.exists();assert sha(out/'UnityLockfile')==prior['lock']['sha256']
save(out/'receipt.json',{'authorization':'User selected allow: archive this owned test leftover only; no process termination','original':'Temp/UnityLockfile','archive':(out/'UnityLockfile').relative_to(ROOT).as_posix(),'ownedRunPid':r['ownedPid'],'sourceModified':prior['lock']['modified'],'exclusiveReadWriteCheckPassed':True,'activeProcessesAfter':processes(),'project':project_check(),'userData':user_check(),'finishedAt':datetime.datetime.now().isoformat()})
print('RECOVERED: only owned stale lock archived; no processes terminated')
