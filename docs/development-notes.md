# Development and bridge notes

These notes were imported from ArchipelagoMine. Historical live-test results
refer to that checkout. KH3 client integration remains in the upstream repository;
see the root README for standalone development commands.

MelonLoader mod for the EGS PC version of **KINGDOM HEARTS Melody of Memory**.
Adds standard DualSense **USB and Bluetooth** support to the game's existing Unity Input System.
No DS4Windows, Steam Input, virtual Xbox controller, external input service, or
keyboard emulation is used. The game reads its usual `Gamepad` controls, with its
normal input update timing.

The same mod accepts a one-time startup song request. By default, the launcher
starts the selected track with a temporary profile and saving disabled. The
optional `-UseSaveProfile` route loads the existing profile through Track Selection.

## Current scope

- Standard Sony DualSense (`054c:0ce6`) with 64-byte USB or 78-byte Bluetooth HID descriptors.
- USB `0x01` (64 bytes), Bluetooth minimal `0x01` (10/78 bytes) and extended `0x31` (78 bytes) reports
  are normalized before the game's Input System consumes them. Unknown packets
  are discarded, never interpreted as buttons or stick movement.
- Face buttons, D-pad, both sticks and clicks, shoulders, analog triggers,
  Create/select and Options/start.
- Existing native DualShock 4 layouts are left unchanged; a physical DS4 has not
  been tested as part of this work.
- DualSense Edge, rumble, adaptive triggers,
  touchpad gestures and automatic PlayStation prompt replacement are not included.
- USB and Bluetooth use separate narrowly matched layouts; DS4 and unrelated
  HID devices retain their existing layouts.

## Player installation

Open **MoM Installation** in the KH3 client. The Epic installation is detected
automatically; Browse selects another directory, and the selected path is also
used for song launches. With MoM closed, click **Install / Repair** once.
The client downloads the SHA-256-pinned official MelonLoader x64 0.7.3 archive
and installs the bundled `MoMController.dll`. No developer tools or compilation
are required. The first game launch prepares MelonLoader's runtime and interop
assemblies. **Test Launch** opens MoM through Epic and checks a fresh,
process-matched `UserData/MoMInstallReady.json` acknowledgment from our mod.

**Uninstall** is also one click with MoM closed. It removes the files this
client added and restores pre-existing files from verified backups. An existing
loader is reused only when it matches the tested version and is never owned by
our uninstaller. A loader installed by us is retained if other DLL mods/plugins
have since been added, or a previous version of our mod must be restored.
Externally modified files are preserved and reported. Saves and AP result files
are never removed. Thus uninstalling over a prior manual installation restores
that prior setup, rather than removing its pre-existing mods.

The journal and backups live in `.kh3ap-mom` under the game directory. Partial
installs can be repaired or uninstalled; the OS-held installation lock prevents
two clients from modifying the same installation concurrently. The download is
cached under `%LOCALAPPDATA%/KH3AP/cache` and verified again before reuse.

The standalone developer build script writes the compiled DLL, hash manifest,
complete license references and notices to `artifacts/MoM`. For source/binary
packaging, follow the root README and include the matching source archive.
This does not build a KH3 APWorld.

## Installation / development

1. Install the official [MelonLoader x64 v0.7.3 release](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.3)
   into MoM's game directory with the game closed. Preserve any existing loader
   files before replacing them. This installation used `version.dll` and the
   `MelonLoader` folder from `MelonLoader.x64.zip`.
2. Launch MoM through Epic once. Allow MelonLoader to generate the game's interop
   assemblies, then close the game.
3. From the repository root, build and install:

   ```powershell
   .\scripts\build-install-mom-controller.ps1
   ```

   The script discovers the EGS installation. `-GameDir` can specify it explicitly.
   `-RestartGame` explicitly stops the matching MoM process, installs the DLL,
   and launches through the matching Epic manifest URI. Do not use that switch
   during unsaved play. Existing mod DLLs are backed up under
   `UserData\MoMController-backups`. `-SkipGameInstall` builds source only.

4. Connect the DualSense by USB or pair it through Bluetooth, then launch through Epic normally. Leave Steam
   Input and DS4Windows out of the input path when testing this mod.

The only mod payload is `Mods\MoMController.dll`; no game assemblies are replaced.
To disable it, close MoM and move that DLL out of the `Mods` folder.

## Launch directly into a song

With the updated mod installed, run from the repository root:

```powershell
.\scripts\start-mom-song.ps1
```

Verified example: **The 13th Struggle**, music ID **0**, on Beginner:

```powershell
.\scripts\start-mom-song.ps1 -SongId 0 -Difficulty Beginner
```

The default fast launch skips startup logos, the title, profile loading and Track
Selection. It goes straight from core initialization to the game's song loader,
using a temporary profile, the selected party, and gamepad input.
`-Party` accepts `Classic` (default; Sora/Donald/Goofy), `Days` (Roxas/Xion/Axel),
`3D` (Riku/Meow Wow/Komory Bat), or `BBS` (Aqua/Ventus/Terra).
Normal style and no items are the defaults. `-PlayStyle` accepts `Normal`,
`OneButton`, or `Performer`; `-StartingItem` accepts `None`, `Potion`, `MegaPotion`,
or `SummoningStar`. Summoning Star also adds the team's Mickey to the party. Party and item overrides
require the temporary profile and cannot be combined with `-UseSaveProfile`.
The KH3 integration reads these choices from **Game Settings > Melody of Memory**,
including a difficulty choice: Seed Default, Beginner, Standard, or Proud.
Explicit difficulty choices override the seed's default for the next launch.
**Saving is disabled for this entire game session**,
including after leaving the song; scores and progress are not saved to the normal
MoM profile. Restart through Epic normally to resume ordinary saved play.

Fast-start sessions also close directly on Alt+F4, without a Yes/No dialog.
The temporary profile reports no active save operations because its backup
file systems are deliberately absent. During the final quit callback only,
the mod finalizes the native audio engine once and skips serial per-bank
unloading; process exit reclaims those allocations. Normal Unity/MelonLoader
shutdown and result flushing still run, without force-killing the process.
Normal saved sessions retain their confirmation dialog and full audio cleanup.

Local EGS validation on 2026-09-12: Alt+F4 during A Day in Agrabah (Beginner)
exited without a dialog in under a second. The native audio/final-cleanup span
dropped from about 4.53 seconds to 11 milliseconds. Evidence is retained in
`build/mom-quit/final-quick-result.log` in the original local checkout (not included here); timings will vary by machine.

The local test reached actual `Playing` in **19.5 seconds from Epic dispatch**,
including about 14 seconds on the mod's startup timer, versus about 58 seconds on
that timer for the previous menu route. The rendered song was also verified visually. Unity and song
assets still need to load, so this is not instantaneous. Other songs/difficulties
have not yet been validated.

Supply `-SongId` with MoM's numeric music ID to select a particular track;
`-Difficulty` accepts `Beginner`, `Standard`, or `Proud`. With no song ID, fast
launch selects the first track in the installed, region-filtered catalog.
The result includes the resolved song ID and name.

Use `-UseSaveProfile` for the slower, previously verified menu route. That mode
loads the normal profile and uses its party, items and controller settings. With
no song ID, it prefers an unlocked track and falls back to the first installed
track on an early profile. It allows the game to save normal results.

The script waits for the requested track to reach its actual `Playing` state,
with matching song/difficulty, demo mode off, and music time beyond one second.
`-NoWait` returns immediately after dispatching the Epic URI. If MoM is already
open, close it first or explicitly supply `-RestartGame` to stop the current
session. `-GameDir` selects a particular EGS installation.

Requests use `UserData\MoMSongLaunch-request.json`:

```json
{"RequestId":"98663c24-2f09-42bb-888a-c8c0623a3f43","SongId":0,"Difficulty":0,"FastBoot":true}
```

Use a fresh GUID for each request. `SongId` is optional. Difficulty is 0/1/2 for
Beginner/Standard/Proud. `FastBoot` defaults to false for older JSON callers;
the PowerShell launcher explicitly requests it unless `-UseSaveProfile` is set. The mod
claims the request into `MoMSongLaunch-active-<guid>.json` at startup so it
cannot run again accidentally. Its result is atomically written to
`MoMSongLaunch-result.json`; always match `RequestId` because an old result may
still exist. Status is `pending`, `ready`, or `failed`. A mod timeout stops
automation after 180 seconds. Claimed request files are a local audit trail.
Results also include `fastBoot`, `savesDisabled`, and the verified playback state.
The waiting PowerShell launcher adds `launchElapsedSeconds`, measured from Epic
dispatch; the mod's `elapsedSeconds` starts later, inside Unity.

The mod temporarily allows background execution while processing a request,
restoring the previous setting when it finishes. Both modes prepare a transient
menu selection and guard previews without a rendered row, supporting a return
from the song even on an early profile. The startup action never repeats.

Fast launch replaces `GameSceneFlowManager.GetFirstGameSceneLoadData` with the
normal Loading scene after preparing `MusicStagePlayData`, `BeforePlayData` and
control settings. It creates in-memory backup defaults without opening the normal
profile file. Save entry points and auto-save reservations are suppressed until
process exit; the auto-save poll is also skipped because no profile file system
was opened. These protections remain active if launch fails after creating the
temporary profile. No saved unlock flags are changed.

The `-UseSaveProfile` route enters Track Selection through the existing scene-flow
edge even when its menu button is locked. Track Selection help pages are advanced
through their normal buttons; other tutorials/dialogs can still prevent unattended
startup. The mod does not dismiss account prompts. Ordinary Epic launches without
a request behave normally. KH3 door gating and song-clear reporting are separate
work.

## Song results for the AP client

The mod records attempts independently of the game's save system, including fast
launches with saving disabled. Files live in the MoM install directory under:

```text
UserData/MoMBridge/current-attempt.json
UserData/MoMBridge/latest-result.json
UserData/MoMBridge/results/<eventId>.json
```

`current-attempt.json` records the latest observed attempt, starting with outcome
`playing`. `latest-result.json` is the most recent completed attempt. The `results`
directory retains one terminal event per attempt so a client can catch up after
being closed. Files are replaced atomically; ignore `.tmp` files. An I/O error is
logged and queued writes retry while the game remains open.

Fast-start temporary-profile sessions automatically close without a confirmation
dialog after an eligible clear for the active request. The recorder first commits
the event and both snapshots, then requests the game's normal quit on the next
frame (normally within a second of the native finish callback). A write failure
keeps MoM open for retry. Failed, aborted, assisted, and unrelated attempts stay
open, as do normal saved-profile sessions. The AP client can consume the durable
event after MoM exits; closing does not wait for a server acknowledgement.

Each record has schema version 1, a process `sessionId`, unique attempt `eventId`,
matching launch `requestId` (or null for unrelated play), song ID/name, numeric
difficulty, mode, input device, UTC start/finish times and result statistics.
Outcomes are `cleared`, `failed`, or `aborted`; retrying or leaving through the
pause menu is aborted. Each retry gets a new event ID. The native finish reason
is also included as `nativeExit` (`Clear`, `GameOver`, `Retry`, etc.). A process
crash can leave `current-attempt.json` at `playing`; that is never a completion.

`cleared` reports the game's native clear outcome. `eligibleForChallenge` further
requires the matching requested song/difficulty, Track Selection play in Normal,
One Button, or Performer style,
and no observed demo, autoplay, invulnerability, disabled game-over, or seek start.
A failed/aborted attempt never becomes an eligible clear. Completion comes from
`Game.Scene.MusicStage.Main.OnFinished`, not a score threshold, elapsed time, save
file, or just reaching the result screen. The shipping build inlines
`StartGamePlay`; attempt tracking instead arms at `BeginMainLoop` and observes
actual `Playing` state, including in-place retries.

The client additionally matches the result difficulty and style to those saved in the
launch request. Changing KH3 preferences during a song cannot change its clear
requirements. The standalone reader accepts `--play-style` for the same check;
its default remains Normal for older requests. Item use does not disqualify clears.

Local EGS startup validation on 2026-09-16 reached native `Playing` for One Button
with Potion, Performer with Summoning Star (Mickey enabled and star consumed), and
Normal with Mega-Potion. The launch acknowledgment includes the native style flags,
item IDs/effects, and Mickey state; these are checked before reporting ready.
Version 0.1.3 also verifies the native party roster, including the team's Mickey
when Summoning Star is selected, and exports `UserData/MoMBridge/party-catalog.json`.

The stdlib-only reader at `mom_results.py` provides:

```python
results = read_song_results(
    mom_bridge_dir,
    request_id=pending_request_id,
    song_id=0,
    difficulty=0,
    seen_event_ids=acknowledged_event_ids,
)
clears = [result for result in results if result["eligibleForChallenge"]]
```

The client must persist the pending launch request with its own AP seed/player
and door/location mapping, then match the request, song and difficulty when
reading. Acknowledge event IDs only after applying the result; the reader never
deletes events or advances a cursor itself. Do not use `latest-result.json` alone
to unlock a door, because it may belong to an earlier launch. This supplies the
producer and reusable AP reader. The KH3 client's `MoM Challenges` tab now binds
launches to seed locations and sends verified clears to Archipelago. See
[worlds/kh3/docs/melody-of-memory.md](https://github.com/ashandarei/ArchipelagoMine/blob/2b9818a2d47fc2661ba6a0085e94cc25b1660bde/worlds/kh3/docs/melody-of-memory.md) for YAML settings and the five-song Beginner
example. In-world doors/barriers are not implemented yet.

To inspect a particular launch using the same reader:

```powershell
python read_results.py --bridge-dir "C:\Program Files\Epic Games\KH_MoM\UserData\MoMBridge" --request-id <launch-guid> --song-id 0 --difficulty 0
```

Live validation captured a real Beginner game-over for The 13th Struggle with
score 0, `nativeExit: GameOver`, `outcome: failed`, and both clear/eligibility
flags false. An in-game retry then cleared it with score **6,970,047**,
`nativeExit: Clear`, `outcome: cleared`, and both flags true. The Python AP reader
returned the two distinct events for the same request and accepted only the
second as an eligible clear. The normal save remained disabled in both attempts.
Six reader tests cover matching/deduplication, wrong requests and difficulties,
failure/quit/start handling, excluded play modes, malformed events, and an absent
bridge, alongside the four existing controller-layout tests.

## Diagnostics and verification

Normal play registers the layouts at startup and normalizes Bluetooth events in
the existing input update. To enable diagnostic snapshots,
create an empty `UserData\MoMController-diagnostics.enabled` file before launch.
Remove it and restart to disable diagnostics. Diagnostics write a local
`UserData\MoMController-status.json` and controller transitions to
`MelonLoader\Latest.log`; the 100ms sampling is **only for diagnostics**, not input
delivery. Status files have a UTC timestamp and may be stale after game exit.

```powershell
python -m unittest discover -s tests -v
dotnet run --project tests/CompletionExit.Tests.csproj -c Release
```

The layout tests check the custom state format, SDL button order, independent
D-pad bits, signed sticks and trigger locations. The C# harness checks all four
USB/Bluetooth packet variants, neutral input, isolated buttons, chords, all hat
values, all 256 axis/trigger inputs, digital trigger fallback and malformed
length/ID/transport combinations. At startup, twelve checks exercise the game's
native matcher without injecting inputs.

Version 0.1.4 replaces the previous Unity-source adaptations with an SDL-derived
decoder and a newly authored layout for a mod-defined state. Local input smoke
tests on 2026-10-19 UTC loaded The 13th Struggle on Beginner with this DLL:

- USB: the SDL-derived decoder received 64-byte `0x01` reports. Cross, L1, R1,
  D-pad Up/Down and Options press/release reached the game's controls.
- Bluetooth: the decoder received 78-byte minimal `0x01` reports. L1, R1 and
  Options press/release reached the game's controls.
- Both transports appeared as a gamepad with zero neutral stick readings, and
  the game reported actual playback with gamepad input and saves disabled.

Neither run completed the song. Full-song clears, physical simultaneous chords,
reconnection and analog endpoint tests remain to be checked. The extended
Bluetooth `0x31` path is covered by fixtures, not these live tests. The older
live results below predate the SDL replacement.

Validated local build: Unity **2019.3.5f1**, IL2CPP metadata **24.2**, MelonLoader
**0.7.3**. Initial live validation confirms that the mod loads and the USB
DualSense is exposed as one `MoMDualSenseUSB` gamepad with neutral sticks/buttons.
The user confirmed controller support during the local player test. Reconnect
behavior and latency have not been measured.

Bluetooth validation on 2026-09-12 identified the standard wireless controller
as `MoMDualSenseBluetooth`, receiving minimal `0x01` reports of 78 bytes with
neutral sticks. Cross, L1, R1 and Options press/release were observed during a
real Radiant Garden Beginner launch; the user confirmed wireless controls work.
The extended report conversion is covered by fixtures but has not been observed
on this device in the live test. KH3's temporary MoM background-input block also
activated while preserving the saved ON preference. The player cleared Radiant
Garden with score 6,744,060; MoM published the eligible event and closed. KH3
observed process exit and removed the temporary input block at 19:56:24 UTC.

With the updated KH3 probe, MoM receives window focus on startup and KH3 receives
it after MoM exits. The MoM mod transfers Windows foreground permission to the
single running KH3 process before normal shutdown; KH3's process monitor waits
for actual exit before requesting focus. See [tools/kh3/CONTROLLER_INPUT.md](https://github.com/ashandarei/ArchipelagoMine/blob/2b9818a2d47fc2661ba6a0085e94cc25b1660bde/tools/kh3/CONTROLLER_INPUT.md) for
the bounded retry behavior.

## Implementation notes

MoM ships `Unity.InputSystem.dll` with DS4 layouts but no DualSense layout.
The mod registers `DualSenseGamepad.json`, extending the game's `Gamepad` API
with its own `MOMP` state: two bytes of buttons in SDL gamepad enum order,
four signed-byte stick axes and two unsigned-byte triggers. Sticks are centered
at zero, with Y pointing up. D-pad directions are independent bits.

`DualSenseInput.cs` handles raw HID state events for both narrowly matched
DualSense layouts. `SdlDualSenseReport.cs` decodes each supported packet in place,
stages the eight output bytes before writing, and the adapter changes the event
format to `MOMP`. Unknown HID state packets are discarded. Configuration and
other non-state events retain normal engine handling. No polling service, SDL
runtime DLL, sensor decoding, output reports or Bluetooth CRC validation is added.

The decoder is an altered, reduced C# adaptation of SDL 3.2.28's simple/common
PS5 input handlers at commit `7f3ae3d57459e59943a4ecfefc8f6277ec6bf540`.
See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) for source and zlib terms.
The old Unity-derived layout and Bluetooth converter are removed from current
build/package inputs; the initial extraction metadata describes their history.

The matcher is registered separately with `InputSystem.RegisterLayoutMatcher`:
this game's JSON matcher does not reliably enforce capability restrictions,
and passing a populated nullable matcher through interop was unreliable in
historical tests. Both matchers check the standard DualSense VID/PID and report
size; DS4 and unrelated HID devices keep their normal layouts. Generated game
interop assemblies remain local build references and are not distributed.
