# HogwartsRP Research & Extraction Toolkit

A read-only UE4SS research subsystem for Hogwarts Legacy (UE4.27, UE4SS 4.x, Windows/Steam).

## Safety contract

- No setters, gameplay commands, ProcessEvent calls, hooks that alter gameplay, or network transmission.
- Reflection is best-effort and capability-probed. Unsupported UE4SS APIs are reported as unavailable, never guessed.
- Discovery is bounded by configured class candidates and scan limits.
- Reports contain metadata and explicitly allowlisted readable values only.
- Adapter output is generated as a candidate and is not automatically loaded into the multiplayer runtime.

## Current verified baseline

- Player controller: `BP_Phoenix_Player_Controller_C`
- Player pawn: `BP_Biped_Player_C`
- Movement component: `CharMoveComp` / `Biped_MovementComponent`
- Confirmed telemetry calls: `GetActorLocation()`, `GetActorRotation()`, `GetControlRotation()`, `GetVelocity()`, `CharacterMovement.MovementMode`
- Previously observed GameInstance class: `BP_PhoenixGameInstance_C`

These are evidence-backed starting points, not assumptions about every game build.

## Runtime layout

`mod/ue4ss/HogwartsRPResearch/` contains the UE4SS mod. Its script modules separate safe calls, discovery, reflection, reporting, and adapter generation. `tools/diagnostics/` contains host-side report collection and validation tools.

## Capability model

UE4SS Lua bindings differ between builds and object wrappers. The toolkit calls APIs only after checking their presence and protects every call with `pcall`. If a binding cannot enumerate a property or UFunction on the current build, the report records `UNAVAILABLE`; it does not attempt unsafe native memory traversal.

## Reports

The runtime creates the following files under its configured reports directory:

- PlayerReport.txt
- ControllerReport.txt
- MovementReport.txt
- NPCReport.txt
- ComponentReport.txt
- PropertyReport.txt
- FunctionReport.txt
- GameplayStateReport.txt

A machine-readable catalog can be added after the text report schema stabilizes.

## Install

Copy `mod/ue4ss/HogwartsRPResearch` into the UE4SS Mods directory, enable the mod using the normal UE4SS mod configuration, launch Hogwarts Legacy, and inspect `UE4SS.log` plus the generated reports directory.

Do not run multiple discovery passes every frame. The initial implementation runs once after load and uses bounded, opt-in refreshes.

## Important limitations

- Full-world actor enumeration depends on a stable object-array API exposed by the installed UE4SS build. The Lua mod does not walk native memory itself.
- Generic UProperty/UFunction enumeration is not guaranteed by the public Lua wrapper. The framework provides safe capability probes and report slots; add a provider only after verifying its exact API against the installed UE4SS headers/build.
- Health, mana, equipment, spell, traversal, mount, combat, and social states remain unknown until a source is discovered and validated.
- State labels are not inferred from names alone. Reports distinguish observed values from hypotheses.

## Development order

1. Confirm loader and safe helper behavior.
2. Revalidate player/controller/movement telemetry.
3. Add component and property allowlists.
4. Add verified reflection providers.
5. Expand actor candidates and bounded world discovery.
6. Add evidence-backed state detectors.
7. Generate adapter candidates and regression tests.
