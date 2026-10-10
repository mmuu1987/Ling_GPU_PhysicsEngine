"""Bounded P3 cardinal-route follow-up; never build, stage, or touch player saves.
Run init once after reviewing the new tests. Test/performance runners reuse existing
isolation, with current P8 protection plus exactly one known production variant.
"""
from common_p8 import *
from common_p8 import project_check as p8_project_check
import sys, re, csv, statistics, math

P3 = LOG / 'P3'
DEST = P3 / 'cardinal-20261008-01'
NAV = 'Assets/MassEngine/Terrain/TerrainNavigationGrid.cs'
TEST = 'Assets/MassEngine/Tests/EditMode/TerrainCardinalRouteCacheTests.cs'
OWNED = [TEST, TEST + '.meta']


def unity_process_snapshot():
    code = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'Unity.exe' -or $_.Name -eq 'WarSandbox.exe' } | Select-Object ProcessId,Name,CommandLine,ExecutablePath | ConvertTo-Json -Compress"
    r = subprocess.run(['powershell', '-NoProfile', '-Command', code], capture_output=True, encoding='utf-8', check=True, timeout=45)
    data = json.loads(r.stdout) if r.stdout.strip() else []
    return [data] if isinstance(data, dict) else data


def all_unity_processes():
    return [p for p in unity_process_snapshot() if p['Name'].lower() == 'unity.exe']


def processes():
    # Project lock is per-project; global Editor preferences have a separate restore gate.
    data = unity_process_snapshot()
    root = str(ROOT).lower().replace('/', '\\')
    return [p for p in data if root in ((p.get('CommandLine') or '') + (p.get('ExecutablePath') or '')).lower().replace('/', '\\') or not p.get('CommandLine')]


def project_check(full=False):
    r = p8_project_check(full)
    registry = json.loads((DEST / 'registry.json').read_text(encoding='utf-8'))
    valid = all((ROOT / p).is_file() and sha(ROOT / p) == h for p, h in registry['tests'].items())
    current = sha(ROOT / NAV)
    variant = next((k for k, h in registry['variants'].items() if h == current), None)
    valid = valid and variant is not None
    r['changed'] = [x for x in r['changed'] if x['path'] != NAV or variant is None]
    r['unexpectedNew'] = [p for p in r['unexpectedNew'] if p not in registry['tests']]
    r['cardinalVariant'] = variant
    r['ownedTestHashesMatch'] = valid
    r['passed'] = valid and not r['changed'] and not r['unexpectedNew']
    return r


def optimized_bytes(original):
    text = original.decode('utf-8')
    replacements = [
        ('        private readonly byte[] neighbours;', '        private readonly byte[] neighbours;\n        // Immutable cardinal-edge runs: exact route queries without rescanning each edge.\n        private readonly int[] rowRuns;\n        private readonly int[] columnRuns;'),
        ('            neighbours = new byte[CellCount];', '            neighbours = new byte[CellCount];\n            rowRuns = new int[CellCount];\n            columnRuns = new int[CellCount];'),
        ('            BuildEdges();\n            BuildComponents();', '            BuildEdges();\n            BuildCardinalRuns();\n            BuildComponents();'),
        ('''            int step,bit;
            if(from/ResolutionX==to/ResolutionX){step=to>from?1:-1;bit=step>0?0:4;}
            else if(from%ResolutionX==to%ResolutionX){step=to>from?ResolutionX:-ResolutionX;bit=step>0?1:5;}
            else return false;
            for(int cell=from;cell!=to;cell+=step)if((neighbours[cell]&(1<<bit))==0)return false;
            return true;''', '''            if (from / ResolutionX == to / ResolutionX) return rowRuns[from] == rowRuns[to];
            if (from % ResolutionX == to % ResolutionX) return columnRuns[from] == columnRuns[to];
            return false;'''),
        ('        private void BuildComponents()', '''        private void BuildCardinalRuns()
        {
            // BuildEdges installs both directions together. Start a new run at each
            // missing west/south edge, not merely at blocked cells. IDs are cell indices;
            // no floating-point geometry or Dijkstra costs/ordering are changed.
            for (int cell = 0; cell < CellCount; cell++)
            {
                rowRuns[cell] = cell % ResolutionX != 0 && (neighbours[cell] & (1 << 4)) != 0
                    ? rowRuns[cell - 1] : cell;
                columnRuns[cell] = cell >= ResolutionX && (neighbours[cell] & (1 << 5)) != 0
                    ? columnRuns[cell - ResolutionX] : cell;
            }
        }

        private void BuildComponents()''')]
    for old, new in replacements:
        assert text.count(old) == 1, old
        text = text.replace(old, new)
    return text.encode('utf-8')


def protect_external_restore(text):
    old = "  for rel,(path,data) in external_saved.items():"
    new = "  external_other_editors = all_unity_processes()\n  receipt['externalEditorRestoreSkippedForOtherUnity'] = external_other_editors\n  for rel,(path,data) in (external_saved.items() if not external_other_editors else []):"
    assert text.count(old) == 1
    return text.replace(old, new)


def init():
    assert not DEST.exists(), 'Never overwrite evidence'
    assert not processes() and not (ROOT / 'Temp/UnityLockfile').exists()
    check = p8_project_check(full=True)
    assert not check['changed'] and set(check['unexpectedNew']) == set(OWNED), check
    assert user_check()['passed']
    original = (ROOT / NAV).read_bytes()
    optimized = optimized_bytes(original)
    DEST.mkdir()
    (DEST / 'reference.cs.txt').write_bytes(original)
    (DEST / 'optimized.cs.txt').write_bytes(optimized)
    for p in OWNED:
        q = DEST / 'tests-reviewed' / p
        q.parent.mkdir(parents=True, exist_ok=True)
        q.write_bytes((ROOT / p).read_bytes())
    registry = {'productionPath': NAV, 'variants': {'reference': hashlib.sha256(original).hexdigest(), 'cached': hashlib.sha256(optimized).hexdigest()}, 'tests': {p: sha(ROOT / p) for p in OWNED}}
    save(DEST / 'registry.json', registry)
    save(DEST / 'initial-protection.json', {'currentP8': check, 'userData': user_check(), 'gitHead': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(), 'indexSha256': sha(ROOT / '.git/index') if (ROOT / '.git/index').is_file() else None})
    prepare_runners()


def prepare_runners():
    import ast
    assert project_check(full=True)['passed']
    assert not (DEST / 'rendered_runner.py').exists() and not (DEST / 'test_runner.py').exists()
    rendered = (ROOT / 'Tools/InteractionRefinement/run_p3_rendered.py').read_text(encoding='utf-8')
    rendered = rendered.replace('from common_p3 import *', 'from p3_cardinal_followup import *')
    rendered = protect_external_restore(rendered)
    ast.parse(rendered)
    tests = (ROOT / 'Tools/InteractionRefinement/run_p8.py').read_text(encoding='utf-8')
    tests = tests.replace('from common_p8 import *', 'from p3_cardinal_followup import *')
    tests = tests.replace("filters={'scoped-kernels'", "filters={'terrain-cardinal':'MassEngine.Tests.TerrainCardinalRouteCacheTests;MassEngine.Tests.TerrainNavigationGridTests;MassEngine.Tests.TerrainSurfaceTests;MassEngine.Tests.LocalOrderPlanTests;MassEngine.Game.Tests.TerrainSurfaceQueriesTests;MassEngine.Game.Tests.WarSandboxTerrainIntegrationTests','scoped-kernels'")
    tests = protect_external_restore(tests)
    ast.parse(tests)
    (DEST / 'rendered_runner.py').write_text(rendered, encoding='utf-8')
    (DEST / 'test_runner.py').write_text(tests, encoding='utf-8')
    print(json.dumps(project_check(full=True), ensure_ascii=False, indent=2), flush=True)


def run(kind, tag, extra):
    assert kind in ['performance', 'editmode', 'playmode']
    if kind == 'performance': assert extra == ['gui', '48'], 'Only the fixed rendered official-48 comparison is allowed'
    assert re.fullmatch('[a-z0-9-]+', tag)
    log = DEST / (tag + '.runner.log')
    assert not log.exists()
    env = os.environ.copy()
    env['PYTHONPATH'] = str(ROOT / 'Tools/InteractionRefinement') + os.pathsep + env.get('PYTHONPATH', '')
    runner = DEST / ('rendered_runner.py' if kind == 'performance' else 'test_runner.py')
    args = [sys.executable, '-X', 'utf8', str(runner), kind, tag] + extra
    before = project_check(full=True)
    assert before['passed'], before
    save(DEST / (tag + '.variant.json'), before)
    with log.open('w', encoding='utf-8') as f:
        result = subprocess.run(args, cwd=ROOT, env=env, stdout=f, stderr=subprocess.STDOUT)
    print(json.dumps({'tag': tag, 'kind': kind, 'variant': before['cardinalVariant'], 'exitCode': result.returncode, 'runnerLog': str(log)}, ensure_ascii=False), flush=True)
    if result.returncode:
        raise RuntimeError('Unity runner failed; inspect runner log and process.json before continuing')
    folder = (P3 if kind == 'performance' else P8) / tag
    receipt = json.loads((folder / 'process.json').read_text(encoding='utf-8'))
    assert receipt['postCheck']['cardinalVariant'] == before['cardinalVariant'], 'Source variant changed during the run'
    if kind == 'editmode' and extra == ['terrain-cardinal']:
        import xml.etree.ElementTree as ET
        root = ET.parse(folder / 'results.xml').getroot()
        cases = [n for n in root.iter('test-case') if 'TerrainCardinalRouteCacheTests.' in n.get('fullname', '')]
        assert len(cases) == 42 and all(n.get('result') == 'Passed' for n in cases), 'All 42 new equivalence guards must execute and pass'
        assert int(root.get('skipped', '0')) == 0 and int(root.get('failed', '0')) == 0
        save(DEST / (tag + '.equivalence.json'), {'newCasesPassed': len(cases), 'allTests': root.attrib})


def select_variant(name):
    assert name in ['reference', 'cached']
    assert not processes() and not (ROOT / 'Temp/UnityLockfile').exists()
    assert project_check(full=True)['passed']
    original = (ROOT / NAV).read_bytes()
    data = (DEST / ('reference.cs.txt' if name == 'reference' else 'optimized.cs.txt')).read_bytes()
    registry = json.loads((DEST / 'registry.json').read_text(encoding='utf-8'))
    assert hashlib.sha256(data).hexdigest() == registry['variants'][name], 'Refuse corrupted or changed variant payload'
    assert hashlib.sha256(original).hexdigest() in registry['variants'].values()
    assert not processes() and not (ROOT / 'Temp/UnityLockfile').exists() and (ROOT / NAV).read_bytes() == original
    (ROOT / NAV).write_bytes(data)
    assert project_check()['cardinalVariant'] == name
    print('Selected known variant: ' + name, flush=True)


def verify():
    build = ROOT / 'Builds/CaptureDefault-20261006-37'
    old = json.loads((BASE / 'current37.sha256.json').read_text(encoding='utf-8'))
    changes = [r['path'] for r in old if not (build / r['path']).is_file() or sha(build / r['path']) != r['sha256']]
    current = {p.relative_to(build).as_posix() for p in build.rglob('*') if p.is_file()}
    unexpected = sorted(current - {r['path'] for r in old})
    initial = json.loads((DEST / 'initial-protection.json').read_text(encoding='utf-8'))
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    index = sha(ROOT / '.git/index') if (ROOT / '.git/index').is_file() else None
    report = {'project': project_check(full=True), 'userData': user_check(), 'current37': {'checked': len(old), 'changed': changes, 'unexpectedNew': unexpected, 'passed': not changes and not unexpected}, 'headUnchanged': head == initial['gitHead'], 'indexUnchanged': index == initial['indexSha256'], 'activeProcesses': processes(), 'projectLock': (ROOT / 'Temp/UnityLockfile').exists()}
    report['passed'] = report['project']['passed'] and report['userData']['passed'] and report['current37']['passed'] and report['headUnchanged'] and report['indexUnchanged'] and not report['activeProcesses'] and not report['projectLock']
    save(DEST / 'final-protection.json', report)
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)
    assert report['passed']


def analyze(tags):
    assert len(tags) == 4 and len(set(tags)) == 4, 'Require four unique ABBA runs'
    results = []
    fixed = None
    for index, tag in enumerate(tags):
        folder = P3 / tag
        receipt = json.loads((folder / 'process.json').read_text(encoding='utf-8'))
        assert receipt['passed']
        perf = receipt['performance']
        assert perf['separationStrength'] == 48 and perf['units'] == 2048 and perf['width'] == 1920 and perf['height'] == 1080 and perf['cameraFixed'] and perf['renderedEditor']
        invariant = {k: perf[k] for k in ['scene', 'cameraStart', 'cameraEnd', 'gpu', 'graphicsApi', 'vSyncCount', 'targetFrameRate']}
        if fixed is None: fixed = invariant
        else: assert invariant == fixed, 'Comparison settings changed'
        variant = json.loads((DEST / (tag + '.variant.json')).read_text(encoding='utf-8'))['cardinalVariant']
        assert variant == ['reference', 'cached', 'cached', 'reference'][index]
        assert receipt['postCheck']['cardinalVariant'] == variant
        rows = list(csv.DictReader((folder / 'frames.csv').open(encoding='utf-8')))
        assert 35 <= float(rows[0]['seconds']) < 36 and 50 <= float(rows[-1]['seconds']) < 51
        assert all(int(b['frame']) == int(a['frame']) + 1 and float(b['seconds']) > float(a['seconds']) for a, b in zip(rows, rows[1:]))
        wall = [1000 * (float(b['seconds']) - float(a['seconds'])) for a, b in zip(rows, rows[1:])]
        def q(values, f):
            ordered = sorted(values)
            return ordered[min(len(ordered)-1, math.ceil(len(ordered)*f)-1)]
        result = {'tag': tag, 'variant': json.loads((DEST / (tag + '.variant.json')).read_text(encoding='utf-8'))['cardinalVariant'], 'samples': len(wall), 'wallMedianMs': statistics.median(wall), 'wallP95Ms': q(wall, .95), 'wallP99Ms': q(wall, .99), 'wallMaxMs': max(wall), 'over50ms': sum(x > 50 for x in wall), 'over33_33ms': sum(x > 1000/30 for x in wall), 'performance': receipt['performance']}
        results.append(result)
    save(DEST / 'performance-comparison.json', {'scope': 'Same 2048, official 48, fixed 1920x1080 rendered Editor, 35s warmup + 15s sample; not EXE or human acceptance. Wall deltas recomputed; lagging CPU/GPU counters not same-row attribution.', 'runs': results})
    print(json.dumps([{k:v for k,v in r.items() if k != 'performance'} for r in results], ensure_ascii=False, indent=2), flush=True)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    command = sys.argv[1]
    if command == 'init': init()
    elif command == 'prepare-runners': prepare_runners()
    elif command == 'run': run(sys.argv[2], sys.argv[3], sys.argv[4:])
    elif command == 'select': select_variant(sys.argv[2])
    elif command == 'verify': verify()
    elif command == 'analyze': analyze(sys.argv[2:])
    else: raise ValueError(command)
