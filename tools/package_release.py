"""Package a DLL built from a frozen, allowlisted source snapshot and its notices."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parents[1]
LEGAL_FILES = (
    "LICENSE", "THIRD_PARTY_NOTICES.md", "THIRD_PARTY_LICENSES.txt",
)
REFERENCE_NAMES = (
    "Il2Cppmscorlib.dll", "Unity.InputSystem.dll", "UnityEngine.CoreModule.dll",
    "UnityEngine.UI.dll", "Assembly-CSharp.dll",
)
DEPENDENCY_NAMES = ("MelonLoader.dll", "0Harmony.dll", "Il2CppInterop.Runtime.dll")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def source_snapshot(root: Path) -> dict[str, bytes]:
    """An explicit list avoids collecting game binaries, credentials or local data."""
    files = json.loads((root / "release-files.json").read_text(encoding="utf-8"))
    if not isinstance(files, list) or len(files) != len(set(files)):
        raise ValueError("release-files.json must be a list of unique paths")
    result = {}
    for name in files:
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name:
            raise ValueError(f"Unsafe source path: {name}")
        if path.parts[0] in {".git", "artifacts", "bin", "obj"}:
            raise ValueError(f"Local output is not a release source: {name}")
        if path.suffix.lower() in {".dll", ".exe", ".pak", ".pdb", ".sav", ".bin", ".zip"}:
            raise ValueError(f"Binary is not a release source: {name}")
        local = root / name
        if not local.resolve().is_relative_to(root.resolve()) or local.is_symlink():
            raise ValueError(f"Source path escapes checkout: {name}")
        result[name] = local.read_bytes()
    # New build sources must not silently disappear from the delivered archive.
    required = set()
    for pattern in ("*.cs", "*.csproj", "*.py", "tests/*.cs", "tests/*.csproj",
                    "tests/*.py", "scripts/*.ps1", "tools/*.py"):
        required.update(p.relative_to(root).as_posix() for p in root.glob(pattern))
    missing = required - result.keys()
    if missing:
        raise ValueError(f"Build/test sources missing from release-files.json: {sorted(missing)}")
    verify_licenses(result)
    return result


def verify_licenses(snapshot: dict[str, bytes]) -> None:
    for name in LEGAL_FILES:
        if not snapshot.get(name):
            raise ValueError(f"Required legal document missing: {name}")


def archive_bytes(files: dict[str, bytes]) -> bytes:
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data)
    return output.getvalue()


def legal_payload(snapshot: dict[str, bytes]) -> dict[str, bytes]:
    verify_licenses(snapshot)
    return {name: snapshot[name] for name in LEGAL_FILES}


def make_bundle(snapshot: dict[str, bytes], dll: bytes, build_info: dict) -> tuple[bytes, bytes, dict]:
    verify_licenses(snapshot)
    sources = dict(snapshot)
    sources["BUILD-INFO.json"] = json_bytes(build_info)
    source_zip = archive_bytes(sources)
    version = re.search(rb'"MoM Direct Controller", "([0-9.]+)"', snapshot["ControllerMod.cs"])[1].decode()
    source_url = f"https://github.com/ashandarei/MoM-GoA-AP/releases/download/v{version}/MoM-GoA-AP-source.zip"
    payload = legal_payload(snapshot)
    payload["MoMController.dll"] = dll
    payload["SOURCE.txt"] = (
        f"MoM-GoA-AP {version} - corresponding source and build instructions\n"
        f"Download: {source_url}\n"
        f"SHA-256: {sha256(source_zip)}\n"
        "The source download is separate; it is not needed to run the mod.\n"
    ).encode()
    metadata = {
        "schema_version": 1, "version": version, "loader_version": "0.7.3",
        "sha256": sha256(dll),
        "source": {"url": source_url, "sha256": sha256(source_zip)},
        "source_commit": build_info["source_commit"],
        "source_snapshot_sha256": build_info["source_snapshot_sha256"],
        "files": {name: sha256(data) for name, data in sorted(payload.items())},
    }
    payload["manifest.json"] = json_bytes(metadata)
    return archive_bytes(payload), source_zip, metadata


def package(root: Path, game_dir: Path, output_root: Path) -> Path:
    snapshot = source_snapshot(root)
    inventory = {name: sha256(data) for name, data in sorted(snapshot.items())}
    source_hash = sha256(json_bytes(inventory))
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=root))
    # Inventory only the externally supplied references; never copy them.
    dependencies = {}
    for folder, names in (("net6", DEPENDENCY_NAMES), ("Il2CppAssemblies", REFERENCE_NAMES)):
        for name in names:
            dependencies[name] = sha256((game_dir / "MelonLoader" / folder / name).read_bytes())
    version_match = re.search(rb'"MoM Direct Controller", "([0-9.]+)"', snapshot["ControllerMod.cs"])
    if not version_match:
        raise ValueError("Cannot determine MoMController version")
    version = version_match[1].decode("ascii")
    build_info = {
        "source_commit": commit, "includes_uncommitted_changes": dirty,
        "source_snapshot_sha256": source_hash, "source_files": inventory,
        "dotnet_sdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
        "external_reference_sha256": dependencies,
        "build_command": "dotnet build MoMController.csproj -c Release -p:GameDir=<local-game-dir>",
        "note": "The included source bytes, not source_commit alone, define this build. External libraries are dynamically loaded and excluded.",
    }
    name = f"MoM-GoA-AP-{version}-{commit[:8]}-{source_hash[:12]}"
    destination = output_root / name
    if destination.exists():
        raise FileExistsError(f"Refusing to overwrite existing package: {destination}")
    with tempfile.TemporaryDirectory(prefix="mom-source-") as temporary:
        frozen = Path(temporary)
        for path, data in snapshot.items():
            dest = frozen / path
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
        subprocess.run(["dotnet", "build", str(frozen / "MoMController.csproj"), "-c", "Release",
                        "--nologo", f"-p:GameDir={game_dir}"], check=True)
        # MSBuild must not have rewritten any delivered source/configuration.
        for path, data in snapshot.items():
            if (frozen / path).read_bytes() != data:
                raise ValueError(f"Build changed source input: {path}")
        for folder, names in (("net6", DEPENDENCY_NAMES), ("Il2CppAssemblies", REFERENCE_NAMES)):
            for library in names:
                if sha256((game_dir / "MelonLoader" / folder / library).read_bytes()) != dependencies[library]:
                    raise ValueError(f"External reference changed during build: {library}")
        dll = (frozen / "bin/Release/net6.0/MoMController.dll").read_bytes()
        binary_zip, source_zip, metadata = make_bundle(snapshot, dll, build_info)
    output_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".mom-package-", dir=output_root) as temporary:
        staged = Path(temporary) / "bundle"
        staged.mkdir()
        (staged / "MoM-GoA-AP-runtime.zip").write_bytes(binary_zip)
        (staged / "MoM-GoA-AP-source.zip").write_bytes(source_zip)
        (staged / "manifest.json").write_bytes(json_bytes(metadata))
        staged.rename(destination)
    return destination


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    release = commands.add_parser("package")
    release.add_argument("--game-dir", type=Path, required=True)
    release.add_argument("--output", type=Path, default=ROOT / "artifacts/release")
    stage = commands.add_parser("stage-notices")
    stage.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "package":
        destination = package(ROOT, args.game_dir.resolve(), args.output.resolve())
        print(f"Built matching source/binary package: {destination}")
    else:
        for path, data in legal_payload(source_snapshot(ROOT)).items():
            destination = args.output / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
        print(f"Staged complete notices and license references: {args.output}")


if __name__ == "__main__":
    main()
