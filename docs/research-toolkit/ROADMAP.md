# Research Toolkit Roadmap

## Phase 1 — Runtime foundation
- [x] Read-only startup and protected module loading.
- [x] Safe object validation and method/property access wrappers.
- [x] Candidate lookup for verified player, controller, movement, and GameInstance classes.
- [x] Initial report and adapter-candidate writers.
- [ ] Verify UE4SS module loading and require() behavior on the target installation.
- [ ] Verify report filesystem path and write permissions.

## Phase 2 — Verified telemetry baseline
- [ ] Re-run player/controller/movement discovery in a clean game session.
- [ ] Record location, actor rotation, control rotation, velocity, and movement mode samples.
- [ ] Add serializers for FVector, FRotator, enums, UObject references, and null values.
- [ ] Add bounded sampling with a conservative interval and hard stop.

## Phase 3 — Reflection providers
- [ ] Inspect the exact installed UE4SS v4.x headers/API surface.
- [ ] Implement UObject property metadata enumeration only through a verified safe provider.
- [ ] Implement UFunction metadata enumeration without invoking functions.
- [ ] Add struct, array, enum, and object-reference serializers.
- [ ] Include metadata source and confidence in every report row.

## Phase 4 — Actor and component catalog
- [ ] Add bounded GUObjectArray-backed discovery only through a supported UE4SS API.
- [ ] Classify actors by verified parent class and observed component signatures.
- [ ] Record full name, class, outer, world, owner, and parent class.
- [ ] Add NPC/student/vendor/companion/quest/interaction candidate categories.
- [ ] Add scan caps, deduplication, and cancellation.

## Phase 5 — Gameplay-state evidence
- [ ] Collect baseline locomotion samples and calibrate thresholds.
- [ ] Discover traversal, mount, combat, and social state sources.
- [ ] Require repeatable observations before promoting a detector from UNKNOWN.
- [ ] Record evidence, confidence, and source for every classification.

## Phase 6 — Adapter generation and validation
- [ ] Generate adapters only from confirmed source mappings.
- [ ] Include source confidence and API availability checks.
- [ ] Add adapter smoke tests against known telemetry.
- [ ] Keep generated adapter separate from the production multiplayer runtime until reviewed.

## Acceptance criteria
- No gameplay writes or mutating function calls.
- Missing properties, methods, objects, or filesystem capabilities cannot crash the mod.
- Every unavailable reflection capability is explicitly reported.
- Reports distinguish observed, inferred, and unknown values.
- Repeated scans are bounded and never run every frame by default.
