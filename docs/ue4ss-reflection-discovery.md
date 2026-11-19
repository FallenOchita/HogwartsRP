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

## Safety boundary

The reflection pass is metadata-only:

- It does not read arbitrary property values.
- It does not invoke UFunctions or call `ProcessEvent`.
- It does not use setters, write to game objects, install gameplay hooks, or connect to a network.
- It only inspects the player, controller, and movement objects found by the existing class-name probes.
- Every engine-facing call is protected and failures are recorded as unavailable metadata rather than treated as proof that a field does not exist.

The existing allowlisted candidate probes in `discovery.lua` remain separate. Those probes read only the explicitly listed candidate properties and report their observed Lua type/value; they do not invoke candidate functions.

## Output

The existing reports are extended:

- `PropertyReport.txt`: candidate value probes plus reflected property metadata for player, controller, and movement classes.
- `FunctionReport.txt`: candidate callable-presence probes plus reflected function metadata for those classes.
- Each reflection result records the class hierarchy, property/function counts by enumeration, safety boundary, and truncation state.

For each UFunction, the toolkit records its full name, name, raw function flags, and UFunction-owned property descriptors. The current documented Lua API does not expose enough property-flag detail here to safely label parameter direction or reconstruct a complete callable signature. Those fields are deliberately not guessed.

## Limits

Enumeration is bounded to 16 superclass levels, 512 properties, 512 functions, and 96 UFunction-owned property descriptors per function. A report reaching a limit is marked as truncated. Limits can be adjusted in the call to `Reflection.inspect` after observing the cost and output size in the target game.

The first in-game run is the compatibility test. A successful Lua load does not guarantee that every reflected class supports every metadata method in the live Hogwarts Legacy build. Review the reflection status and any enumeration errors in the generated reports before promoting a field into the multiplayer adapter contract.
