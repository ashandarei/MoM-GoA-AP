# MoM-GoA-AP

MelonLoader mod for the Epic Games Store PC version of **KINGDOM HEARTS Melody of Memory**, used by the Kingdom Hearts III Garden of Assemblage Archipelago integration.

The mod provides:

- Standard DualSense USB and Bluetooth support through the game's Unity Input System.
- Direct song launches with difficulty, party, play style, and starting item selection.
- Temporary song sessions with normal profile saving disabled.
- Durable, request-scoped song results for Archipelago clients, with automatic exit after an eligible clear has been written.
- Windows foreground permission handoff to KH3 when MoM closes.

The assembly and installed payload remain named `MoMController.dll` to retain the existing KH3 client contract.

## Build and install

Requires Windows, the EGS version of MoM, a .NET SDK capable of targeting .NET 6, Python 3.10+, and **MelonLoader x64 0.7.3**. Generated game assemblies are local build references and are not included in this repository.

1. Install [MelonLoader 0.7.3](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3) in the game directory. Preserve any existing loader installation before replacing its files.
2. Launch MoM through Epic once to generate `MelonLoader/Il2CppAssemblies`, then close it.
3. From this checkout, run:

   ```powershell
   .\scripts\build-install-mom-controller.ps1
   ```

The script detects the Epic installation, builds the DLL, writes the distributable files to `artifacts/MoM`, and installs the DLL into the game's `Mods` directory. Existing mod DLLs are backed up under `UserData/MoMController-backups`.

Use `-GameDir "C:\path\to\KH_MoM"` to select an installation. Use `-SkipGameInstall` to build and stage the payload without changing the installed game. `-RestartGame` explicitly stops the matching MoM process and relaunches it through Epic after installation; it discards that running session.

The `artifacts/MoM` output contains the DLL, version/hash manifest, project license,
third-party notices, and consolidated third-party license texts.

## Build a source/binary package

```powershell
.\scripts\package-mom-release.ps1 -GameDir "C:\Program Files\Epic Games\KH_MoM"
```

The helper builds from the explicit `release-files.json` source list and writes
`MoM-GoA-AP-runtime.zip`, a separate matching `MoM-GoA-AP-source.zip`, and
an installer `manifest.json` under `artifacts/release`.
The source archive includes the actual build inputs, tests, scripts, SDK version
and dependency hashes. Game/dependency binaries and local data are excluded.
New build/test sources must be added to the source list.

Publish both ZIPs together on the matching `v<version>` GitHub release. The
runtime ZIP contains `MoMController.dll`, `manifest.json`, the three legal files,
and `SOURCE.txt` linking to the corresponding source ZIP with its SHA-256.
Source is available separately and does not need to be downloaded to run the mod.
Keep both assets available for recipients of that binary.

For manual installation, copy `MoMController.dll` to the game's `Mods` folder
with MoM closed. Keep the remaining runtime files in
`UserData/MoMController` so the game's own license files are not overwritten.
ArchipelagoMine's Install/Repair flow downloads and verifies the runtime release
and installs this same DLL/license payload; it does not download the source ZIP.

The packaging helper does not install or launch a game, publish a release,
or overwrite an existing package directory.

## Launch a song

With MoM closed and the mod installed:

```powershell
.\scripts\start-mom-song.ps1 -SongId 0 -Difficulty Beginner
```

Song ID `0` is **The 13th Struggle** in the validated EGS catalog. Omit `-SongId` to use the first installed, region-filtered track. The launcher waits for actual playback; `-NoWait` returns after Epic dispatch.

| Option | Values |
| --- | --- |
| `-Difficulty` | `Beginner` (default), `Standard`, `Proud` |
| `-Party` | `Classic` (default), `Days`, `3D`, `BBS` |
| `-PlayStyle` | `Normal` (default), `OneButton`, `Performer` |
| `-StartingItem` | `None` (default), `Potion`, `MegaPotion`, `SummoningStar` |

Default launches use a temporary profile. **Saving remains disabled for the entire session**, even after leaving the song. Restart normally through Epic to resume saved play. `-UseSaveProfile` instead takes the slower normal-profile menu route and permits normal saving; party and item overrides require the temporary profile.

## Read results

Requests and launch acknowledgments live in `UserData/MoMSongLaunch-*.json`. Attempts are retained in `UserData/MoMBridge/results/<eventId>.json` independently of game saves.

```powershell
python read_results.py --bridge-dir "C:\Program Files\Epic Games\KH_MoM\UserData\MoMBridge" --request-id <launch-guid> --song-id 0 --difficulty 0
```

The stdlib-only `mom_results.py` validates each event against the requested UUID, song, difficulty, and play style. Callers must retain the launch-to-seed/location mapping and acknowledge event IDs only after applying results. Reading never deletes files, sends AP checks, or advances a cursor. Use `eligibleForChallenge` from validated results; a stale snapshot alone must never grant a check.

## Verify

These checks do not launch either game or require game assemblies. Python 3.10+ and a .NET 6 runtime/SDK are sufficient for the test harness:

```powershell
python -m unittest discover -s tests -v
dotnet run --project tests/CompletionExit.Tests.csproj -c Release
```

The full mod build additionally requires the installed game's generated interop assemblies:

```powershell
dotnet build MoMController.csproj -c Release "-p:GameDir=C:\Program Files\Epic Games\KH_MoM"
```

See [development and bridge notes](docs/development-notes.md) for the protocol, controller implementation, diagnostics, and historical live validation.

## Origin and integration

This repository is the source of truth for the MoM-side mod, launch tooling and result reader. It was extracted from [ashandarei/ArchipelagoMine](https://github.com/ashandarei/ArchipelagoMine); [extraction metadata](docs/extraction.json) records the original paths and hashes before the standalone changes. Earlier development history remains in ArchipelagoMine.

The KH3 APWorld, client UI, challenge assignment, barriers, and UE4SS runtime remain in ArchipelagoMine. This repository contains the MoM-side mod, launch tooling, and reusable result reader. No game binaries, generated interop assemblies, saves, or local bridge data are included.

The source repository's [license](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md) are retained.

## Licensing

The inherited project license is [GPL version 3](LICENSE). The SDL-derived
controller decoder uses the permissive zlib license, with its attribution and
alteration notice retained. External libraries keep their own terms; see
[third-party notices](THIRD_PARTY_NOTICES.md) and the
[complete third-party texts](THIRD_PARTY_LICENSES.txt).

The current controller decoder/layout replace the previous Unity-source
adaptations. The mod still uses the installed game's Unity APIs; game and
engine binaries are not distributed. Version 0.1.4 passes the offline checks
and local USB/Bluetooth input smoke tests. See the development notes for the
observed inputs and remaining test coverage.
