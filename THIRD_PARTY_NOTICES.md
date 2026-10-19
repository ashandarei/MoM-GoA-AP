# Third-party notices

The mod source is distributed under the inherited [GPL version 3](LICENSE).
The SDL-derived decoder also retains its upstream zlib notice. External
libraries retain their own terms. Their license texts and upstream notices
are collected in [THIRD_PARTY_LICENSES.txt](THIRD_PARTY_LICENSES.txt).

## SDL-derived controller decoder

`SdlDualSenseReport.cs` is a reduced C# adaptation of SDL 3.2.28's
[PS5 input handlers](https://github.com/libsdl-org/SDL/blob/7f3ae3d57459e59943a4ecfefc8f6277ec6bf540/src/joystick/hidapi/SDL_hidapi_ps5.c),
Copyright (C) 1997-2025 Sam Lantinga, under the permissive zlib license.
The source retains the complete notice and identifies the changes.
The decoder emits the mod-defined `MOMP` state consumed by
`DualSenseGamepad.json`. No SDL runtime DLL is required or bundled.

## External dependencies

These are build/runtime references supplied by the local MelonLoader installation;
the package helper does not bundle their DLLs or merge them into this mod.
The collected texts distinguish these library terms from the mod source license.

| Component | Version | License and corresponding source |
| --- | --- | --- |
| MelonLoader | 0.7.3 | [Apache-2.0](https://github.com/LavaGang/MelonLoader/tree/982ed9981ac5b0619648a2e79bcc02fd8861db33) |
| HarmonyX (`0Harmony.dll`) | 2.10.2 | [MIT](https://github.com/BepInEx/HarmonyX/tree/97b2a2e9cecd6d0c757da4dbb696abaff9163faa) |
| Il2CppInterop.Runtime | 1.5.1-ci.845 | [LGPL-3.0-only](https://github.com/BepInEx/Il2CppInterop/tree/f03c8f4ae507d47ea814f3d11d1ec6b0391c1576) |

Il2CppInterop and its use are covered by LGPL-3.0-only. It is loaded as a separate
library. Users may modify or replace it and rebuild against a compatible version;
this project imposes no restriction on reverse engineering to debug those modifications.
The package includes the mod's actual build sources, and the linked source above
identifies the interop library version used for the documented local build.

## Origin and game content

Extracted from [ArchipelagoMine](https://github.com/ashandarei/ArchipelagoMine);
original paths and revision are recorded in [extraction metadata](docs/extraction.json).
The extraction metadata records the original snapshot, before the SDL controller
replacement and standalone build/documentation changes. Current source and
packages omit the former Unity-derived controller implementation; this does not
relicense earlier versions.
No game, Unity engine, generated game assemblies, Sony SDK, music or artwork is
included in the package. These notices grant no rights in those components.

This is an unofficial mod, with no claimed endorsement by Square Enix, Disney,
Sony, Unity Technologies, Lava Gang or BepInEx. Product names identify compatibility.
