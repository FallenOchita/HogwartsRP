## 2026-11-18 — UE4SS reflection reliability and component-reference diagnostics

### Improved

- Normalized method lookup and invocation failures into concise statuses, retaining the first diagnostic line rather than repeating full Lua stack traces for every unsupported UFunction descriptor.
- Preserved UFunction name, full name, and raw flags when owned-property enumeration fails; each descriptor now reports `OK`, `METHOD_UNAVAILABLE`, or `CALL_ERROR` metadata status.
- Improved class-name diagnostics so unresolved class metadata is distinguished from an invalid player/controller/movement object.
- Kept `ComponentReport.status = UNAVAILABLE` for full component enumeration because `GetComponents` is not exposed as a Lua table by this UE4SS build.
- Added bounded, allowlisted probes for the previously observed player properties `CharMoveComp`, `CharacterMovement`, `Mesh`, `ConversationComponent`, `ToolSetComponent`, and `AnimationArchitectComponent`. Only candidates with a valid full object name are reported as `REFERENCE_FOUND`.
- Expanded read-only relationship validation to include controller `Player` and movement `CharacterOwner`, `PawnOwner`, and `UpdatedComponent` references.
- Updated the reflection guide, runtime log, and `CHANGELOG.md`.

### Validation boundary

- This is a source update on the existing research-toolkit pull request. It has not yet been installed or tested in the live Hogwarts Legacy/UE4SS process.
- Component property candidates are not a complete component list. `GetComponents` remains unavailable and the report says so explicitly.
- No gameplay UFunction is invoked; no property is written; no ProcessEvent, hook, network, or multiplayer behavior is added.

### Next validation

Install the PR branch, run one research pass, and share the updated `ComponentReport.txt`, `RelationshipReport.txt`, `FunctionReport.txt`, and the three object reports. Confirm whether class metadata now resolves and which allowlisted references produce `REFERENCE_FOUND`.

