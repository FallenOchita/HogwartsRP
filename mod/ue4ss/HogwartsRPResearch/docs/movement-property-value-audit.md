# Movement property value audit

This milestone adds a conservative, read-only value audit for allowlisted player and movement-component properties.

## Report fields

`PropertyReport.data.value_audit` contains one record per target/property pair:

- `target` and `target_full_name`: which discovered object was inspected.
- `property`: the allowlisted property name.
- `access`: `GetPropertyValue`, `DirectIndex`, or `NONE`.
- `lua_type`: Lua's reported type, not an Unreal property-class claim.
- `status`: `PRIMITIVE_VALUE_READ`, `OBJECT_REFERENCE_FOUND`, `OPAQUE_USERDATA`, `TABLE_VALUE_NOT_EXPANDED`, `PROPERTY_UNAVAILABLE`, `PROPERTY_READ_ERROR`, or another explicit failure state.
- `summary`: a primitive's raw text, an object's full name, or a fixed opaque/unavailable marker.

Opaque userdata and tables are deliberately not expanded. In particular, a `TrivialObject` is not assumed to be a vector, integer, or readable Unreal struct.

## GameplayStateReport behavior

The state sampler consumes the audit records only. It does not call `GetVelocity`, `Size`, or any gameplay UFunction. It reports a movement-mode value only when the property audit returns a primitive. It reports velocity as raw evidence only when the audited property itself is primitive; it does not convert that value into speed.

Locomotion remains `UNCLASSIFIED` until the source and interpretation of movement telemetry are validated against in-game baselines.

## Regression test

From this directory, run:

```bash
lua tests/property_value_audit_spec.lua
```

The test covers primitive values, missing properties, table non-expansion, opaque userdata, and property getter errors. It uses synthetic Lua objects and does not validate UE4SS or Hogwarts Legacy behavior.

## Safety boundary

No gameplay functions, setters, hooks, property writes, component mutations, or network operations are part of this audit. The only writes are local diagnostic report files.
