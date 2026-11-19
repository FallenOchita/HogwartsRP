# HogwartsRP UE4SS Diagnostics (Steam)

This is the first, deliberately small Hogwarts Legacy client-side integration test for HogwartsRP.

## What it does

- Prints a clear startup message to the UE4SS console/log.
- Reports the UE4SS version when the documented `UE4SS.GetVersion()` API is available.
- Does not search for player objects.
- Does not read or change gameplay state.
- Does not connect to a network or authenticate.
- Does not implement multiplayer.

The only success condition for this stage is seeing:

```text
[HogwartsRPDiagnostics] Loaded successfully.
[HogwartsRPDiagnostics] Diagnostic mode only; no gameplay objects were accessed or changed.
[HogwartsRPDiagnostics] UE4SS version: ...
[HogwartsRPDiagnostics] Smoke test complete. Check UE4SS.log for this message.
```

The version line may instead say that `UE4SS.GetVersion()` is unavailable on an older loader build.

## Steam game folder

The usual Steam installation folder is:

```text
<SteamLibrary>\\steamapps\\common\\Hogwarts Legacy
```

The game executable directory is:

```text
<Hogwarts Legacy>\\Phoenix\\Binaries\\Win64
```

Install this mod into the UE4SS `Mods` directory used by your compatible UE4SS installation:

```text
<Hogwarts Legacy>\\Phoenix\\Binaries\\Win64\\Mods\\HogwartsRPDiagnostics\\scripts\\main.lua
```

If your UE4SS installation uses a different documented Mods directory, use that directory instead. Do not install a second UE4SS copy over an existing one.

Add this line to UE4SS `Mods\\mods.txt`:

```text
HogwartsRPDiagnostics : 1
```

Keep any existing entries in `mods.txt`; add this as a new line.

## Verify

1. Back up your game folder's existing UE4SS files and configuration before changing anything.
2. Confirm that the UE4SS build you have is intended for your installed Hogwarts Legacy game build. A loader build tested against an older game build is not proof of current compatibility.
3. Launch Hogwarts Legacy through Steam normally.
4. Check the UE4SS console, if enabled, or the `UE4SS.log` file beside the UE4SS DLL. The Hogwarts Legacy UE4SS guide documents the log under `Phoenix\\Binaries\\Win64`.
5. Search the log for `[HogwartsRPDiagnostics]`.

If the message appears, the diagnostic Lua mod loaded. That does not prove player discovery, transform access, networking, or multiplayer.

If the game fails to start, remove only the diagnostic mod folder and its `mods.txt` entry, restore the previous configuration, and verify the game files through Steam before trying other changes.

## Compatibility warning

Do not combine this diagnostic UE4SS setup with HogWarp. HogWarp's published setup guidance says UE4SS is incompatible with its loader setup. HogwartsRP is pursuing its own independent adapter, not modifying or depending on HogWarp.

## References

- [UE4SS — Creating a Lua Mod](https://docs.ue4ss.com/guides/creating-a-lua-mod.html)
- [UE4SS — Lua API](https://docs.ue4ss.com/dev/lua-api)
- [UE4SS — Installation and log verification](https://docs.ue4ss.com/installation-guide.html)
- [Hogwarts Legacy RE-UE4SS compatibility notes](https://www.nexusmods.com/hogwartslegacy/mods/942)
- [HogWarp setup guidance (compatibility warning)](https://steamcommunity.com/sharedfiles/filedetails/?id=3112157370)

## Status

**Diagnostic script authored; not yet verified in the user's live Steam installation.**

The next gate is a real UE4SS log showing that this script loaded with the installed game build. Do not add player-object access, hooks, networking, or transform synchronization until this gate passes.

## Create a sanitized diagnostic report

After launching the game once with the diagnostic mod enabled, run the report helper from PowerShell:

```powershell
.\\tools\\diagnostics\\Get-HogwartsRPDiagnosticReport.ps1 -LogPath "C:\\Path\\To\\Hogwarts Legacy\\Phoenix\\Binaries\\Win64\\UE4SS.log"
```

The helper reads only the last 5,000 log lines and emits normalized versions of recognized diagnostic markers. It never echoes arbitrary tagged log lines or unrelated log content. Version values are restricted to a short version-token format, and the log path is omitted.

To save the same sanitized report to a text file, add `-OutputPath ".\\HogwartsRP-diagnostic-report.txt"`. If your log or executable is in a different location, provide the appropriate paths with `-LogPath` and `-GameExecutablePath`.

Run the regression tests from the repository root:

```powershell
pwsh -NoProfile -File .\\tools\\diagnostics\\Test-HogwartsRPDiagnosticReport.ps1
```

The tests cover PASS, PARTIAL, NOT VERIFIED, missing-log handling, optional report output, and exclusion of arbitrary log payloads. A `PASS` result confirms only that the diagnostic Lua mod loaded and completed its smoke test. It does not prove game object access or multiplayer.

---

# HogwartsRP Player-State Adapter (experimental)

This is a separate UE4SS mod from the harmless `HogwartsRPDiagnostics` smoke-test mod.

## Included

- Local controller discovery using `BP_Phoenix_Player_Controller_C`.
- Local pawn discovery through `AcknowledgedPawn`.
- Position read through `GetActorLocation()`.
- Facing candidate through `GetControlRotation()`.
- Read-only velocity and movement-component discovery.
- Exploratory movement-mode, jump, sprint, and animation property reads.
- A snapshot API exposed as `_G.HogwartsLegacyAdapter` while the mod is loaded.
- Three-second diagnostic polling on the UE4SS game thread.

## Install

1. Close Hogwarts Legacy.
2. Back up the existing UE4SS folder/configuration.
3. Copy the repository folder `mod/ue4ss/HogwartsRPAdapter` into the **same UE4SS `Mods` directory that contains your existing `HogwartsRPDiagnostics` folder**. This avoids guessing whether your installation uses `Phoenix/Binaries/Win64/Mods` or a loader-specific `ue4ss/Mods` path.
4. Confirm the resulting file exists at:
   `<your UE4SS Mods directory>/HogwartsRPAdapter/scripts/main.lua`.
5. Open `Mods/mods.txt` and add:
   `HogwartsRPAdapter : 1`
6. Preserve all existing `mods.txt` entries. Do not add a second UE4SS installation.
7. Launch Hogwarts Legacy through Steam and inspect `UE4SS.log` beside the UE4SS DLL.

The folder name and `mods.txt` entry must match exactly. UE4SS installations commonly use a capitalized `Scripts` directory; follow the layout used by your installed UE4SS build.

## Expected log markers

Search for `[HogwartsRPAdapter]`. On load, the mod reports its version and read-only scope. Once a local controller and pawn are found, it prints a state snapshot approximately every three seconds.

## Important compatibility and safety notes

- The controller, pawn, position, and control-rotation findings were reported from a live local test, but still depend on the installed game build.
- Movement component access and the jump, sprint, and animation members are exploratory. An unavailable value is not evidence that the game lacks that capability.
- Movement classification intentionally reports `Moving`, not guessed walking/running/sprinting states.
- This adapter does not connect to the HogwartsRP server, transmit player data, spawn remote players, or implement multiplayer.
- Do not run this alongside HogWarp; the existing compatibility warning in this document still applies.
- If the game fails to start, remove only the `HogwartsRPAdapter` folder and its `mods.txt` line, then restore your backup.

## Current status

**Adapter authored; live installation and full movement-field verification pending.** A successful load only verifies that UE4SS executed the script. It does not establish multiplayer or network synchronization.
