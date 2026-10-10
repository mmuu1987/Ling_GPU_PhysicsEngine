from p3_output_20261008 import *
import textwrap
sys.stdout.reconfigure(encoding='utf-8')
assert not processes() and not(ROOT/'Temp/UnityLockfile').exists()
registry=json.loads((OUT/'registry.json').read_text(encoding='utf-8'))
original=(OUT/'reference.cs.txt').read_bytes()
payload=json.loads((ROOT/'Tools/InteractionRefinement/p3-output-20261008-payload.json').read_text(encoding='utf-8'))
frozen=original.decode('utf-8').replace('\r\n','\n');frozen=frozen[frozen.index('namespace MassEngine'):].replace('TerrainNavigationGrid','P3OriginalNavigationGrid')
text=(OUT/'test-original.cs.txt').read_bytes().decode('utf-8')+'\n'+frozen+'\n'+payload['tests']
expected=text.encode('utf-8');actual=(ROOT/TEST_PATH).read_bytes()
assert hashlib.sha256(expected).hexdigest()==registry['testSha256']
assert actual==text.replace('\n',os.linesep).encode('utf-8'),'Not solely our known Windows newline expansion; refuse overwrite'
(OUT/'test-newline-failure.cs.txt').write_bytes(actual)
(OUT/'decision-failure-01.json').write_bytes((OUT/'decision.json').read_bytes())
(ROOT/TEST_PATH).write_bytes(expected)
assert project_check(full=True)['passed']
save(OUT/'newline-repair.json',{'onlyKnownNewlineExpansion':True,'expectedSha256':sha(ROOT/TEST_PATH),'unityNotStartedInFailedAttempt':True})
source=(ROOT/'Tools/InteractionRefinement/p3_output_20261008.py').read_text(encoding='utf-8')
start=source.index("    state={'status':'running'")
end=source.index("\nif __name__=='__main__':main()",start)
tail=textwrap.dedent(source[start:end]).replace('p3-output-before-01','p3-output-before-02')
exec(compile(tail,'output-resume-reviewed-tail','exec'),globals())
