# HogwartsRP Research Toolkit Architecture

## Runtime contract

Target: Hogwarts Legacy (Unreal Engine 4.27), UE4SS 4.x, Windows/Steam.

The UE4SS mod is read-only. It may enumerate reflection metadata, read allowlisted properties, call verified observational getters, and write diagnostic files under its own reports directory. It must not call gameplay setters, invoke arbitrary UFunctions, install gameplay hooks, change actor state, or send network writes.

## Layers

1. Safe access — object validity, protected property/method access, safe names, capability probes, bounded class lookup.
2. Reflection — UClass/UStruct superclass chains, property metadata, UFunction metadata. Metadata enumeration is separate from reading live property values.
3. Discovery — explicitly configured class scans and actor relationships. No assumed global actor iterator.
4. State analysis — evidence-based state classification. Unknown is a valid result.
5. Telemetry — confirmed source registry and separate unverified candidates.
6. Reporting and adapters — deterministic text reports and generated read-only Lua adapter candidates.

## Evidence levels

- CONFIRMED: directly exercised successfully in the target game build.
- ENUMERATED: metadata was returned by a supported UE4SS reflection API.
- CANDIDATE: a property/class name is queued for testing but not validated.
- UNAVAILABLE: the API or value could not be read.
- UNKNOWN: available evidence is insufficient to classify a gameplay state.

Never convert UNAVAILABLE or UNKNOWN into false, zero, empty, or a guessed class.

## Verified telemetry

- Player: BP_Biped_Player_C
- Controller: BP_Phoenix_Player_Controller_C
- Movement: Biped_MovementComponent / CharMoveComp
- Position: GetActorLocation()
- Actor rotation: GetActorRotation()
- Camera/control rotation: GetControlRotation()
- Velocity: GetVelocity()
- Movement mode: MovementMode

## Component discovery limitation

The tested UE4SS build does not expose GetComponents as a Lua table. Component discovery must preserve the UNAVAILABLE result until a verified enumeration path is available. Do not substitute a guessed API.

## Runtime boundaries

The runtime module owns UE object access and writes reports beneath Scripts/reports. Offline tools may parse these reports and generate adapter candidates but must not inject code into the game process.
