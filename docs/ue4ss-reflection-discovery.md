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

The toolkit uses the documented UE4SS 4.0.0 `FindObjects(0, "ActorComponent", nil, 0, 0, false)` query to collect ActorComponent-derived objects, then confirms player association only when the object's Outer chain resolves to the discovered player pawn. The report keeps returned-object count, confirmed matches, unresolved Outer chains, non-matches, depth-limit outcomes, and truncation state separate.

For each reported player-Outer-matched component, the toolkit records its class identity and a bounded superclass chain using `GetClass()` and `GetSuperStruct()`. Hierarchy results are cached by reported class name. `COMPLETE`, `PARTIAL`, and `DEPTH_LIMIT_REACHED` describe the class metadata traversal only; they do not strengthen the Outer ownership result. Unusual class metadata, including Blueprint-generated class forms, remains flagged for review rather than silently normalized.

The existing allowlisted player-property reference checks remain separate and are cross-checked against the confirmed component paths. The broad component property catalog is intentionally deferred: live UE4SS 4.0.0 reports show that `ForEachProperty` may fail after a callback has returned, so this pass does not treat a partial enumeration as a complete property inventory or retry it. No opaque userdata is dereferenced and no arbitrary component property values are read.
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


## Direct named-property metadata lookup

The MovementMode audit now supplements the class-by-class ForEachProperty inventory with the documented UE4SS UObjectReflection path: UObject:Reflection() followed by UObjectReflection:GetProperty(propertyName). This performs a targeted metadata lookup for the requested property on each class in the already established superclass chain. It records the lookup scope class and the returned descriptor identity without reading the property's value.

The direct lookup has its own per-class diagnostics and an independent direct_lookup_status. Outcomes distinguish a descriptor found, a property not found on a queried class, and an incomplete lookup caused by an unavailable or failing reflection method. A descriptor's full name is retained as the evidence for its declaring type; lookup_scope_class is not automatically treated as the declaring class because the API documentation does not specify whether GetProperty includes inherited descriptors.

This API returns property metadata only. It does not provide a documented property-to-UEnum accessor or enumerator list, so enum association remains unverified unless separately established. The existing ForEachProperty inventory and its diagnostics remain intact; a failed enumeration does not invalidate a successful direct lookup.

## ByteProperty enum-association capability audit

When the direct MovementMode lookup returns a ByteProperty, the report now records a capability audit of the descriptor's documented generic methods (GetFullName, GetFName, GetClass, and IsA). These are protected member lookups only; the methods are not invoked by this audit. It records the descriptor's property-class identity and explicitly marks the enum reference as unavailable through the documented UE4SS 4.0.0 Property API.

The report exposes this as enum_association_capability_audit, including documented_property_method_capabilities, descriptor_property_class, enum_reference_status, enum_api_status, and enum_entries_status. Capability method results use the same EXPOSED_NOT_INVOKED / METHOD_UNAVAILABLE / LOOKUP_ERROR distinction as the existing provider audit. The shared object-validity guard may perform its normal validity check; the listed capability methods themselves are only looked up.

The UE4SS 4.0.0 API documents UEnum lookup/enumeration methods, but those methods require an existing UEnum object. It does not document a ByteProperty accessor that supplies the associated enum reference. Therefore, the toolkit does not guess accessor names, inspect offsets, invoke undocumented methods, or enumerate arbitrary enums. The report retains BYTE_PROPERTY_ASSOCIATION_UNVERIFIED, NO_DOCUMENTED_BYTEPROPERTY_ENUM_ACCESSOR, and NOT_ENUMERATED_NO_VERIFIED_UENUM_REFERENCE until a supported reference path is established.

This capability audit is metadata-only. It does not read the live property value, inspect opaque userdata, call gameplay UFunctions, write properties, or enumerate enum entries.


## Confirmed-component property descriptor catalog

The ComponentReport pass now produces a separate `ComponentPropertyCatalog.txt` for classes represented by confirmed player-Outer-matched components. The catalog is grouped by reflected class identity, and duplicate component instances reuse one class-level metadata pass. Each property record contains only its descriptor name, full name, property-class identity, and declaring class.

The catalog walks the component's reflected class hierarchy using `GetClass()` and `GetSuperStruct()`. It performs at most one `ForEachProperty(callback)` attempt per class in that walk, with a 256-property cap per component class catalog and a 16-level superclass cap. It does not retry a failed enumeration. Captured descriptors and per-class diagnostics are retained when UE4SS reports `CALL_ERROR_AFTER_CALLBACK`, `CALL_ERROR_BEFORE_CALLBACK`, `CALLBACK_ERROR`, `METHOD_UNAVAILABLE`, or `LOOKUP_ERROR`. Such a catalog is marked `PARTIAL` or `FAILED`; uncaptured properties are not treated as absent.

The catalog is limited to classes discovered through the existing `FindObjects(0, "ActorComponent", nil, 0, 0, false)` query and confirmed by an Outer chain that resolves to the player pawn. It does not associate the 3,811 unresolved Outer-chain objects with the player. No component property values are read, opaque userdata is not dereferenced, and no gameplay UFunctions, setters, hooks, or network operations are invoked.

The report writer now permits ten nested table levels so class hierarchy chains in the grouped class inventory are visible. It also enforces a 50,000-line output ceiling and records an explicit truncation marker if that ceiling is reached. The separate property catalog status and per-class diagnostics remain the authority for whether descriptor enumeration completed.


## Targeted MovementMode enum-object lookup

The MovementMode audit now performs one additional, narrowly scoped lookup when the direct property descriptor is a ByteProperty named MovementMode. It calls the documented UE4SS 4.0.0 StaticFindObject API with the UObject path "/Script/Engine.EMovementMode" (without the reflected type prefix). The returned object must still have the exact reflected full name "Enum /Script/Engine.EMovementMode" and UEnum class identity before the documented UEnum:GetNameByValue(3) method is called. The report records the lookup path and expected full name separately, along with only that requested name/value pair.

This does not extract a UEnum pointer from the ByteProperty. The report therefore uses ENGINE_ENUM_CANDIDATE_FOUND_ASSOCIATION_NOT_DIRECTLY_EXPOSED, not a direct-association claim. The lookup is corroborative: Epic's UE4.27 MovementMode reference documents the engine movement-mode enum and identifies value 3 as MOVE_FALLING. The live report must still confirm that the exact UEnum object is present and resolves the value as expected.

The lookup does not enumerate arbitrary UEnums, read the MovementMode property value, inspect offsets, dereference opaque userdata, invoke gameplay UFunctions, or modify game state. If the exact object cannot be resolved or validated, enum association remains unverified.


## Targeted player and movement property descriptors

The PropertyReport now includes `targeted_property_metadata`, a bounded set of direct `Reflection:GetProperty(propertyName)` lookups for explicitly allowlisted descriptors on the already discovered player, controller, and movement objects. The player allowlist covers CharacterMovement, CharMoveComp, Mesh, ConversationComponent, ToolSetComponent, and AnimationArchitectComponent. The controller allowlist covers AcknowledgedPawn, Pawn, Player, and PlayerState. The movement allowlist covers CharacterOwner, PawnOwner, UpdatedComponent, and Velocity. MovementMode remains in its separate enum audit to avoid duplicating that lookup.

Each result records the descriptor's name, full name, property class, lookup scope, and per-class lookup outcomes. `FOUND` means a descriptor was returned; `NOT_FOUND` is used only when direct lookup completed across the traversed class chain; `LOOKUP_INCOMPLETE` means a lookup or class-hierarchy step failed or could not be completed. A descriptor's full name, rather than the lookup-scope class, remains the evidence for its declaring class.

This pass does not use `ForEachProperty`, read property values, dereference opaque userdata, invoke gameplay UFunctions, write properties, install hooks, or perform network operations. The fixed allowlist is intentionally narrow; unlisted properties are not asserted to be absent.


## Controller and movement object-reference value audit

The PropertyReport now includes a separate reference_value_audit for a fixed allowlist of controller and movement reference properties: controller AcknowledgedPawn, Pawn, Player, and PlayerState; movement CharacterOwner, PawnOwner, and UpdatedComponent. Each allowlisted value is read through the existing guarded property-value helper. When a value is a valid UObject userdata with an available full name, only that full name is recorded as its identity. Opaque userdata remains opaque. Lua tables and primitive values are classified without exposing or expanding their contents. Missing properties, unavailable targets, and property-read errors remain distinct outcomes.

This audit does not call GetOwner, invoke gameplay UFunctions, inspect nested component properties, dereference opaque userdata, write properties, install hooks, or perform network operations. It is separate from the descriptor-only targeted_property_metadata section and the existing player/movement value audit.


## LocalPlayer and PlayerState second-hop reference audit

The PropertyReport now includes a narrowly scoped `related_reference_audit` that follows only two references already observed on the controller: `Player` to `LocalPlayer.PlayerController`, and `PlayerState` to `PlayerState.PawnPrivate`. The child property is read only after the parent value is validated as a UObject userdata reference with a readable full name. The report records only the child object's full-name identity, or a bounded status such as unavailable, opaque, or read error.

This is a fixed two-route, one-level follow-up, not recursive traversal. It does not enumerate arbitrary LocalPlayer or PlayerState properties, inspect child objects beyond those single properties, dereference opaque userdata, invoke gameplay UFunctions, write properties, install hooks, or perform network operations. A live report is required to confirm whether these candidate properties are present and populated in the Hogwarts Legacy runtime.


## Targeted LocalPlayer and PlayerState metadata

The related-reference audit can optionally pass a validated second-hop UObject to the existing bounded `Reflection:GetProperty` allowlist lookup. This is used for two fixed targets only:

- `LocalPlayer`: `PlayerController`, `AspectRatioAxisConstraint`, `Origin`, and `Size`.
- `PlayerState`: `PawnPrivate`, `PlayerId`, `PlayerNamePrivate`, `bIsSpectator`, `bIsABot`, `bIsInactive`, `Score`, and `Ping`.

The metadata audit is performed only when the corresponding controller reference has first been validated as a UObject. It records descriptor identity and class-chain lookup status. It does not read the values of these properties, enumerate arbitrary fields, or invoke gameplay functions. Candidate names that are absent or cannot be looked up are reported with the existing explicit `NOT_FOUND` or `LOOKUP_INCOMPLETE` semantics; their presence is not assumed.
