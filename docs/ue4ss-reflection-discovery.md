# UE4SS reflection metadata discovery

## Scope

The HogwartsRP research mod uses the UE4SS 4.0.0 Lua reflection surface to enumerate metadata for the already identified player, controller, and movement objects.

The implementation uses the documented APIs:

- `UObject:GetClass()` to obtain the object's UClass.
- `UStruct:ForEachProperty(callback)` to enumerate reflected property descriptors.
- `UStruct:ForEachFunction(callback)` to enumerate reflected UFunction objects.
- `UStruct:GetSuperStruct()` to walk the class hierarchy.
- `Property:GetFName()`, `Property:GetFullName()`, and `Property:GetClass()` to record property identity and property-class metadata.
- `UFunction:GetFName()`, `UFunction:GetFullName()`, and `UFunction:GetFunctionFlags()` to record function identity and raw flags.

These methods are documented in the UE4SS 4.0.0 Lua API. The implementation does not depend on experimental development-only APIs.

## Discovery timing and object lookup

The research pass no longer assumes that the player exists when UE4SS first loads the Lua mod. It retries the known player/controller class lookups every 2.5 seconds, for up to 24 attempts. The timer callback dispatches object access through the documented `ExecuteInGameThread` API. `FindAllOf` is used as a fallback if `FindFirstOf` does not return a valid object. If the controller is found first, the toolkit checks its `AcknowledgedPawn` and `Pawn` references for the local pawn. Movement discovery first checks the known `CharMoveComp` and `CharacterMovement` properties.

A manual `hogwartsrp_research` console command is registered when UE4SS exposes `RegisterConsoleCommandHandler`. The final report records the number of attempts and the lookup source. A timeout with no player/controller is reported as a discovery failure, not a reflection failure.

## Safety boundary

The reflection pass is metadata-only:

- It does not read arbitrary property values.
- It does not invoke UFunctions or call `ProcessEvent`.
- It does not use setters, write to game objects, install gameplay hooks, or connect to a network.
- It only inspects the player, controller, and movement objects found by the existing class-name probes.
- Every engine-facing call is protected and failures are recorded as unavailable metadata rather than treated as proof that a field does not exist.

The existing allowlisted candidate probes in `discovery.lua` remain separate. Those probes read only the explicitly listed candidate properties and report their observed Lua type/value; they do not invoke candidate functions.

## Reflection provider capability audit

The FunctionReport now distinguishes member exposure from successful metadata enumeration. The provider-capability section obtains each target's class through the same read-only `GetClass` identity lookup used by reflection inspection, then performs protected Lua member lookups for `ForEachProperty`, `ForEachFunction`, and `GetSuperStruct`. It does not invoke those enumeration methods or probe UFunction descriptors. Therefore, `EXPOSED_NOT_INVOKED` is not proof that a method works.

Per-function owned-property enumeration preserves distinct outcomes such as `METHOD_UNAVAILABLE`, `LOOKUP_ERROR`, and `CALL_ERROR`. A `CALL_ERROR` means the wrapper was exposed but the metadata operation failed; it does not mean the Unreal UFunction is absent. The report keeps any other function metadata already obtained and includes a concise error detail.

This audit does not call gameplay functions, invoke `ProcessEvent`, read arbitrary property values, or change game state. The next live report is needed to determine whether the observed nil-call error is caused by a missing binding, an incompatible wrapper, or a callback/descriptor limitation.

Class-name reporting now distinguishes an unavailable `GetClass` method, a failed `GetClass` call, an invalid returned class object, and an unavailable class full name. These statuses avoid presenting an unresolved class name as evidence that the discovered player/controller object itself is invalid.

## Component discovery boundary

The target UE4SS build reports that `GetComponents` is not exposed as a Lua table. The toolkit therefore keeps `ComponentReport.status = UNAVAILABLE` for complete component enumeration. It separately probes only these previously observed player properties: `CharMoveComp`, `CharacterMovement`, `Mesh`, `ConversationComponent`, `ToolSetComponent`, and `AnimationArchitectComponent`.

A candidate is recorded as `REFERENCE_FOUND` only when its value can be identified by a valid full object name. Other results remain unresolved or unavailable. This allowlisted reference catalog is not a complete component list and does not claim that every candidate is a component. No arbitrary property scan or `GetComponents` workaround is attempted.

The relationship report also checks the named `Player`, `CharacterOwner`, `PawnOwner`, and `UpdatedComponent` references alongside the existing controller pawn and movement outer checks. These are read-only property-reference checks; owner resolution remains unprobed.

## Output

The existing reports are extended:

- `PropertyReport.txt`: candidate value probes plus reflected property metadata for player, controller, and movement classes.
- `FunctionReport.txt`: candidate callable-presence probes plus reflected function metadata for those classes.
- Each reflection result records the class hierarchy, property/function counts by enumeration, safety boundary, and truncation state.

For each UFunction, the toolkit records its full name, name, raw function flags, and UFunction-owned property descriptors. The current documented Lua API does not expose enough property-flag detail here to safely label parameter direction or reconstruct a complete callable signature. Those fields are deliberately not guessed.

## Limits

Enumeration is bounded to 16 superclass levels, 512 properties, 512 functions, and 96 UFunction-owned property descriptors per function. A report reaching a limit is marked as truncated. Limits can be adjusted in the call to `Reflection.inspect` after observing the cost and output size in the target game.

The first in-game run is the compatibility test. A successful Lua load does not guarantee that every reflected class supports every metadata method in the live Hogwarts Legacy build. Review the reflection status and any enumeration errors in the generated reports before promoting a field into the multiplayer adapter contract.


## Movement-mode enum metadata audit

The PropertyReport includes `movement_mode_enum_audit` for the reflected `MovementMode` property on the discovered movement component. The audit walks the reflected class hierarchy and records only the property descriptor's name, full name, property-class name, and class identity. It uses the documented `GetClass`, `GetFName`, `GetFullName`, `ForEachProperty`, and `GetSuperStruct` metadata APIs; it does not read the property value or invoke gameplay functions.

UE4SS 4.0.0's documented Property API does not expose a property-to-`UEnum` accessor. Consequently, an `EnumProperty` descriptor can be identified as enum-backed, but its associated enum object and enumerator names/values cannot be claimed from this API surface. A `ByteProperty` descriptor is especially inconclusive because it may or may not carry enum metadata. The report therefore leaves enum association and enum entries unverified rather than assuming that raw value `3` means `MOVE_Falling`. The live movement value remains raw and locomotion remains `UNCLASSIFIED` until the association is independently established.


## Movement-mode audit enumeration correction

The movement-mode descriptor audit now runs inside the existing successful class-hierarchy property enumeration performed by `Reflection.inspect`. It does not make a second `ForEachProperty` call or attempt to terminate a callback early after finding the target. The matching property record is captured from the same descriptor metadata already used to populate the movement reflection report, with its declaring class recorded. If the shared enumeration itself fails or reaches its configured cap before the property is found, the audit reports that limitation rather than presenting an unverified enum result.


## Property-enumeration failure diagnostics

The research report now records a per-class diagnostic for each existing ForEachProperty pass. Each record includes the declaring class, method invocation status, callback invocation count, number of properties captured during that pass, the last property name observed, and any concise invocation or callback error. The named-property audit includes the same diagnostic records so a missing descriptor can be distinguished from a class whose enumeration did not complete.

Status interpretation:

- METHOD_UNAVAILABLE and LOOKUP_ERROR describe Lua binding lookup outcomes.
- CALL_ERROR_BEFORE_CALLBACK means the exposed method call failed before the callback was observed.
- CALL_ERROR_AFTER_CALLBACK means at least one callback ran before the method call failed; the last observed property and partial count are retained.
- CALLBACK_ERROR means the protected Lua callback body itself failed; the error is recorded and enumeration is asked to stop.
- OK means the method call returned without a recorded error. A limit stop is separately labeled PROPERTY_LIMIT_REACHED.

These diagnostics do not retry enumeration, invoke a second ForEachProperty pass, inspect property values, dereference opaque userdata, invoke gameplay functions, or infer that a property is absent from a failed/incomplete pass. Callback counts and partial counts are diagnostic evidence only; they do not establish why the UE4SS binding failed internally.
