"""Fast packaging contract tests; synthetic files, no Unity or user data required.
Run: python -m unittest discover -s Tools/tests -v
"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('package_playtest', Path(__file__).parents[1] / 'package_playtest.py')
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)


class PackagePlaytestTests(unittest.TestCase):
    guid = '1234567890abcdef1234567890abcdef'

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.previous_root = packager.ROOT
        packager.ROOT = self.root
        source = self.root / 'Builds/Reviewed'
        source.mkdir(parents=True)
        for name in packager.RUNTIME:
            path = source / name
            if name in ('D3D12', 'MonoBleedingEdge', 'WarSandbox_Data'):
                path.mkdir()
                (path / 'fixture.bin').write_bytes(b'fixture runtime')
            else:
                path.write_bytes(b'fixture runtime')
        (source / 'WarSandbox_Data/Managed').mkdir()
        (source / 'WarSandbox_Data/boot.config').write_text('build-guid=' + self.guid + '\n')
        docs = self.root / 'Docs/Playtest'
        docs.mkdir(parents=True)
        (docs / 'Start-WarSandbox.cmd').write_text('@echo off\nWarSandbox.exe\n', encoding='ascii')
        (docs / '版本与已知问题.txt').write_text('OLD: must not ship with new build', encoding='utf-8')
        (self.root / 'Logs').mkdir()
        (self.root / 'Logs/build.log').write_text('Used Assets and files from the Resources folder, sorted by uncompressed size:\n 1.0% Assets/Fixture.asset\n-------------------------------------------------------------------------------\n')
        (self.root / 'Logs/current.txt').write_text('CURRENT: no inherited performance claims', encoding='utf-8')

    def tearDown(self):
        packager.ROOT = self.previous_root
        self.temp.cleanup()

    def package(self, **kwargs):
        options = dict(source_build='Reviewed', expected_guid=self.guid,
                       build_log='Logs/build.log', review_report='Logs/current.txt')
        options.update(kwargs)
        return packager.package('Candidate', **options)

    def test_current_guid_source_notes_and_isolated_audit(self):
        self.package()
        output = self.root / 'Builds/Candidate'
        manifest = packager.verify(output)
        self.assertEqual(self.guid, manifest['buildGuid'])
        self.assertEqual('Builds/Reviewed', manifest['sourceBuild'])
        self.assertTrue((self.root / 'Logs/Candidate/package-audit.json').is_file())
        self.assertFalse((self.root / 'Logs/M7HumanPlaytest/package-audit.json').exists())
        self.assertEqual((self.root / 'Logs/current.txt').read_text(), (output / '版本与已知问题.txt').read_text(encoding='utf-8'))
        self.assertTrue((self.root / 'Builds/Candidate.zip.sha256').is_file())

    def test_rejects_wrong_source_guid_before_copy(self):
        with self.assertRaises(ValueError):
            self.package(expected_guid='0' * 32)
        self.assertFalse((self.root / 'Builds/Candidate').exists())

    def test_new_source_requires_current_notes(self):
        with self.assertRaises(ValueError):
            self.package(review_report=None)

    def test_refuses_overwriting_an_existing_package(self):
        self.package()
        archive = self.root / 'Builds/Candidate.zip'
        before = packager.digest(archive)
        with self.assertRaises(FileExistsError):
            self.package()
        self.assertEqual(before, packager.digest(archive))

    def test_verify_detects_runtime_tampering(self):
        self.package()
        output = self.root / 'Builds/Candidate'
        (output / 'WarSandbox.exe').write_bytes(b'changed runtime')
        with self.assertRaises(AssertionError):
            packager.verify(output)

    def test_rejects_unsafe_source_name(self):
        with self.assertRaises(ValueError):
            self.package(source_build='../Reviewed')


if __name__ == '__main__':
    unittest.main()
