from p3_output_20261008 import *
import textwrap,ctypes
sys.stdout.reconfigure(encoding='utf-8')
assert not processes()
registry=json.loads((OUT/'registry.json').read_text(encoding='utf-8'))
original=(OUT/'reference.cs.txt').read_bytes()
assert sha(ROOT/TEST_PATH)==registry['testSha256']
lock=ROOT/'Temp/UnityLockfile'
if lock.exists():
    receipt=json.loads((P8/'p3-output-before-02/process.json').read_text(encoding='utf-8'))
    assert receipt['ownedPid']==25520 and receipt['exitCode']==1 and receipt['compileErrors'] and not receipt['otherProcessesAfter']
    assert lock.stat().st_size==0
    assert datetime.datetime.fromisoformat(receipt['startedAt']).timestamp() <= lock.stat().st_mtime <= datetime.datetime.fromisoformat(receipt['finishedAt']).timestamp()
    k=ctypes.WinDLL('kernel32',use_last_error=True)
    k.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p];k.CreateFileW.restype=ctypes.c_void_p
    k.CloseHandle.argtypes=[ctypes.c_void_p]
    h=k.CreateFileW(str(lock),0x80000000,0,None,3,0x80,None)
    assert h not in (None,ctypes.c_void_p(-1).value),'Lock still held; stop'
    k.CloseHandle(h)
    assert not processes()
    lock.rename(OUT/'owned-compile-failure-UnityLockfile')
    save(OUT/'owned-lock-recovery.json',{'ownedPid':25520,'mtimeWithinOwnedRun':True,'exclusiveOpenSucceeded':True,'action':'Preserved unlocked owned compile-failure artifact by rename; no user lock deletion'})
old=(ROOT/TEST_PATH).read_bytes();text=old.decode('utf-8')
assert text.count('TerrainSurface.Finite')==4
text=text.replace('TerrainSurface.Finite','P3ReferenceFinite')
needle='        private static bool Finite(Vector2 value)'
assert text.count(needle)==1
text=text.replace(needle,'        private static bool P3ReferenceFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);\n\n'+needle)
(OUT/'test-internal-access-failure.cs.txt').write_bytes(old)
(OUT/'registry-before-internal-fix.json').write_bytes((OUT/'registry.json').read_bytes())
(OUT/'decision-failure-02.json').write_bytes((OUT/'decision.json').read_bytes())
(ROOT/TEST_PATH).write_bytes(text.encode('utf-8'))
registry['testSha256']=sha(ROOT/TEST_PATH);save(OUT/'registry.json',registry)
assert project_check(full=True)['passed']
source=(ROOT/'Tools/InteractionRefinement/p3_output_20261008.py').read_text(encoding='utf-8')
start=source.index("    state={'status':'running'");end=source.index("\nif __name__=='__main__':main()",start)
tail=textwrap.dedent(source[start:end]).replace('p3-output-before-01','p3-output-before-03')
exec(compile(tail,'output-resume2-reviewed-tail','exec'),globals())
