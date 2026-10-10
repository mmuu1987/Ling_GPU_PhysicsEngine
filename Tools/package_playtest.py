"""Package the already-verified Windows player without rebuilding or changing content."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BUILD_GUID = '90c14fa91efa4ae7b3758eff6a9e572f'
RUNTIME = ('WarSandbox.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe',
           'WinPixEventRuntime.dll', 'D3D12', 'MonoBleedingEdge', 'WarSandbox_Data')


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def verify(output):
    manifest = json.loads((output / 'package-manifest.json').read_text(encoding='utf-8'))
    assert re.fullmatch(r'[0-9a-f]{32}', manifest['buildGuid'])
    for item in manifest['files']:
        path = output / item['path']
        assert path.is_file() and path.stat().st_size == item['bytes'], item['path']
        assert digest(path) == item['sha256'], item['path']
    boot = (output / 'WarSandbox_Data/boot.config').read_text(encoding='utf-8-sig')
    assert 'build-guid=' + manifest['buildGuid'] in boot
    command = (output / 'Start-WarSandbox.cmd').read_text(encoding='ascii')
    assert '--terrain-cycle' not in command and '--war-sandbox-settings-file' not in command
    assert not (output / 'Measure-Desktop.cmd').exists()
    print('Verified', len(manifest['files']), 'files; runtime GUID', manifest['buildGuid'])
    return manifest


def package(name, source_build='M73FarLod', expected_guid=BUILD_GUID, build_log='Logs/M73FarLod/create-verify-build.log', review_report=None):
    if not re.fullmatch(r'[A-Za-z0-9_-]+', name):
        raise ValueError('Use a single safe output folder name.')
    if not re.fullmatch(r'[A-Za-z0-9_-]+', source_build) or not re.fullmatch(r'[0-9a-f]{32}', expected_guid):
        raise ValueError('Provide a safe source build name and its actual expected GUID.')
    if source_build != 'M73FarLod' and not review_report:
        raise ValueError('A new build requires current version/validation notes; never reuse old FPS claims.')
    source = ROOT / 'Builds' / source_build
    output = ROOT / 'Builds' / name
    archive = output.with_suffix('.zip')
    if output.exists() or archive.exists():
        raise FileExistsError('Refusing to overwrite a playtest package: ' + str(output))
    boot = (source / 'WarSandbox_Data/boot.config').read_text(encoding='utf-8-sig')
    if 'build-guid=' + expected_guid not in boot:
        raise ValueError('Source player is not the reviewed build.')
    log_path = ROOT / build_log
    log = log_path.read_text(encoding='utf-8-sig')
    section = log.split('Used Assets and files from the Resources folder, sorted by uncompressed size:', 1)[1]
    section = section.split('-------------------------------------------------------------------------------', 1)[0]
    assets = [match.group(1) for line in section.splitlines()
              if (match := re.search(r'%\s+(.+)$', line))]
    output.mkdir()
    runtime_files = []
    for name in RUNTIME:
        src, dst = source / name, output / name
        if src.is_dir():
            shutil.copytree(src, dst)
            inputs = sorted(p for p in src.rglob('*') if p.is_file())
        else:
            shutil.copy2(src, dst)
            inputs = [src]
        for path in inputs:
            relative = path.relative_to(source)
            assert digest(path) == digest(output / relative), relative
            runtime_files.append(relative.as_posix())
    for path in (ROOT / 'Docs/Playtest').iterdir():
        if path.is_file():
            shutil.copy2(path, output / path.name)
    if review_report:
        shutil.copyfile(ROOT / review_report, output / '版本与已知问题.txt')
    packages = []
    for package_id in sorted({p.split('/')[1] for p in assets if p.startswith('Packages/')}):
        version_match = re.search(r'^\s*' + re.escape(package_id) + r'@([^\s]+)\s+\(location:', log, re.M)
        build_version = version_match.group(1) if version_match else None
        item = {'id': package_id, 'buildVersion': build_version, 'noticeFiles': []}
        for cache in (ROOT / 'Library/PackageCache').glob(package_id + '@*'):
            info = json.loads((cache / 'package.json').read_text(encoding='utf-8-sig'))
            if build_version != info.get('version'):
                continue
            candidates = list(cache.glob('*'))
            if package_id == 'com.unity.mathematics':
                candidates.append(cache / 'Unity.Mathematics/Noise/LICENSE')
            for path in candidates:
                if not path.is_file() or path.suffix.lower() not in ('', '.md', '.txt'):
                    continue
                if not (path.name.lower().startswith('license') or 'third party notices' in path.name.lower()):
                    continue
                relative = Path('ThirdPartyNotices') / package_id / path.relative_to(cache)
                target = output / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
                item['noticeFiles'].append(relative.as_posix())
            break
        packages.append(item)
    managed = sorted(p.name for p in (output / 'WarSandbox_Data/Managed').glob('*.dll'))
    inventory = {'evidence': 'Unity Used Assets section plus the actual copied player files; not a legal clearance.',
                 'buildLogSha256': digest(log_path), 'buildReportedAssets': assets,
                 'packages': packages, 'managedAssemblies': managed,
                 'sourceProvenanceToConfirm': ['RPG Tiny Hero Duo models/texture/animations and derived VAT',
                                               'SazenGames Skeleton helper scripts reported in the build'],
                 'unityChanModelReportedInBuild': any('Art/Source/ModelTrials/UnityChan' in p for p in assets)}
    write_json(output / 'dependency-inventory.json', inventory)
    if review_report:
        notes = ('战争沙盒 · 新构建资源记录\n\n'
                 '运行文件来自 package-manifest.json 所列新构建；全部运行文件逐项校验为源构建副本。\n'
                 '本次构建实际使用的资源、程序集和 ' + str(len(packages)) + ' 个软件包见 dependency-inventory.json。\n'
                 'ThirdPartyNotices 保留匹配构建版本的本地软件包许可证与声明。\n\n'
                 '角色模型、纹理、动作与派生 VAT 来源于工程中的 RPG Tiny Hero Duo；构建也可能包含 SazenGames 辅助脚本。\n'
                 '具体是否入包以本次清单为准，不沿用旧包的资源数量或性能成绩。\n'
                 '资源购买/取得主体和最终发布许可仍须项目拥有者确认；清单不等于法律授权审查。\n'
                 '人工验收与 V1 发布均待确认。\n')
        (output / '资源与依赖说明.txt').write_text(notes, encoding='utf-8')
    files = [{'path': p.relative_to(output).as_posix(), 'bytes': p.stat().st_size, 'sha256': digest(p)}
             for p in sorted(output.rglob('*')) if p.is_file()]
    manifest = {'packagePurpose': 'Human playtest candidate; not V1 sign-off', 'buildGuid': expected_guid,
                'createdUtc': datetime.now(timezone.utc).isoformat(), 'sourceBuild': 'Builds/' + source_build,
                'recompiled': False, 'runtimeByteIdentical': True, 'runtimeFiles': runtime_files,
                'humanAcceptance': 'pending', 'v1Released': False, 'files': files}
    write_json(output / 'package-manifest.json', manifest)
    verify(output)
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zip_file:
        for path in sorted(output.rglob('*')):
            if path.is_file():
                zip_file.write(path, Path(output.name) / path.relative_to(output))
    with zipfile.ZipFile(archive) as zip_file:
        assert zip_file.testzip() is None
    archive.with_suffix('.zip.sha256').write_text(digest(archive) + '  ' + archive.name + '\n', encoding='ascii')
    evidence = ROOT / 'Logs' / output.name
    evidence.mkdir(exist_ok=True)
    write_json(evidence / 'package-audit.json', {'folder': output.relative_to(ROOT).as_posix(),
        'archive': archive.relative_to(ROOT).as_posix(), 'archiveSha256': digest(archive),
        'archiveBytes': archive.stat().st_size, 'packageFiles': len(files) + 1,
        'runtimeFiles': len(runtime_files), 'runtimeByteIdentical': True, 'buildGuid': expected_guid,
        'humanAcceptance': 'pending', 'v1Released': False})
    print('Packaged:', archive, '| MiB:', round(archive.stat().st_size / 2**20, 2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--name', default='M7HumanPlaytest-20260928')
    parser.add_argument('--verify', action='store_true')
    parser.add_argument('--source-build', default='M73FarLod')
    parser.add_argument('--expected-guid', default=BUILD_GUID)
    parser.add_argument('--build-log', default='Logs/M73FarLod/create-verify-build.log')
    parser.add_argument('--review-report', help='Current UTF-8 version notes, required for a non-legacy source build')
    args = parser.parse_args()
    if args.verify:
        if not re.fullmatch(r'[A-Za-z0-9_-]+', args.name):
            raise ValueError('Use a single safe output folder name.')
        verify(ROOT / 'Builds' / args.name)
    else:
        package(args.name, args.source_build, args.expected_guid, args.build_log, args.review_report)
