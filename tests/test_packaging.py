"""Verify actual source delivery and rejection of unsafe/incomplete release inputs."""

import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("package_release", ROOT / "tools/package_release.py")
packager = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(packager)


class ReleasePackagingTests(unittest.TestCase):
    def setUp(self):
        self.snapshot = packager.source_snapshot(ROOT)

    def test_runtime_and_separate_source_have_matching_hashes_and_complete_notices(self):
        build_info = {"source_commit": "test", "source_snapshot_sha256": "test",
                      "source_files": {name: packager.sha256(data) for name, data in self.snapshot.items()}}
        binary, sources, manifest = packager.make_bundle(self.snapshot, b"our DLL", build_info)
        with zipfile.ZipFile(io.BytesIO(binary)) as archive:
            self.assertEqual(["MoMController.dll"], [n for n in archive.namelist() if n.endswith(".dll")])
            self.assertNotIn("source.zip", archive.namelist())
            self.assertEqual(manifest, json.loads(archive.read("manifest.json")))
            self.assertEqual(packager.sha256(sources), manifest["source"]["sha256"])
            self.assertIn(manifest["source"]["url"].encode(), archive.read("SOURCE.txt"))
            self.assertEqual("0.7.3", manifest["loader_version"])
            self.assertEqual(packager.sha256(b"our DLL"), manifest["sha256"])
            for name in packager.LEGAL_FILES:
                self.assertEqual(self.snapshot[name], archive.read(name))
            self.assertFalse(any("licensing-review" in name or "licensing-comparisons" in name for name in archive.namelist()))
            for name, expected in manifest["files"].items():
                self.assertEqual(expected, packager.sha256(archive.read(name)))
        with zipfile.ZipFile(io.BytesIO(sources)) as archive:
            self.assertEqual(set(self.snapshot) | {"BUILD-INFO.json"}, set(archive.namelist()))
            for name, data in self.snapshot.items():
                self.assertEqual(data, archive.read(name))
            self.assertEqual(build_info, json.loads(archive.read("BUILD-INFO.json")))

    def test_missing_license_or_source_input_is_rejected(self):
        del self.snapshot["THIRD_PARTY_LICENSES.txt"]
        with self.assertRaisesRegex(ValueError, "legal document missing"):
            packager.make_bundle(self.snapshot, b"DLL", {})

    def test_unsafe_or_binary_paths_are_rejected_before_reading(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("../outside.txt", "C:/secret.txt", "artifacts/secret.txt", "GameAssembly.dll", "foo\\bar.txt"):
                with self.subTest(name=name):
                    (root / "release-files.json").write_text(json.dumps([name]), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        packager.source_snapshot(root)

    def test_new_source_file_must_be_explicitly_included(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "release-files.json").write_text("[]", encoding="utf-8")
            (root / "FutureFeature.cs").write_text("// new source", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "FutureFeature.cs"):
                packager.source_snapshot(root)

    def test_unknown_local_files_are_not_collected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, data in self.snapshot.items():
                dest = root / name
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
            (root / "private-credentials.txt").write_text("not for distribution", encoding="utf-8")
            snapshot = packager.source_snapshot(root)
            self.assertNotIn("private-credentials.txt", snapshot)

    def test_source_archive_is_deterministic(self):
        self.assertEqual(packager.archive_bytes(self.snapshot), packager.archive_bytes(dict(reversed(list(self.snapshot.items())))))


if __name__ == "__main__":
    unittest.main()
