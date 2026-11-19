# HogwartsRP Research Toolkit Roadmap

## Phase 1 — Safe foundation
- [x] Protected property and method access
- [x] Object validation and safe naming
- [x] Player/controller/movement discovery
- [x] Eight report outputs
- [x] Read-only adapter generation

## Phase 2 — Reflection inventory
- [x] UClass superclass traversal
- [x] UStruct::ForEachProperty metadata enumeration
- [x] UStruct::ForEachFunction metadata enumeration
- [x] Bounded enumeration and failure reporting
- [ ] Determine UE4SS-exposed FProperty type/value APIs from live probes
- [ ] Decode UFunction parameter direction, parameter count, and return property using verified metadata flags

## Phase 3 — Actor and component catalog
- [x] Configurable class registry and FindAllOf-based scan
- [x] Full name, class, outer, world, owner, parent-class records
- [ ] Discover validated NPC/student/vendor/companion/quest/interactive classes
- [ ] Find a verified component enumeration mechanism
- [ ] Catalog major actor component sets

## Phase 4 — Property and function research
- [x] Readable property sampling from enumerated names
- [x] Non-invoking function inventory
- [ ] Verify object/struct/array/enum value decoding
- [ ] Validate parameter count and return type

## Phase 5 — Gameplay state baselines
- [ ] Collect stationary, walking, running, sprinting, and airborne samples
- [ ] Determine traversal indicators
- [ ] Determine broom/creature mount indicators
- [ ] Determine combat, casting, dodge, Protego, and target-lock indicators
- [ ] Determine conversation, shop, and classroom indicators

## Phase 6 — Telemetry and adapter
- [x] Confirmed position, rotation, camera rotation, velocity, movement mode registry
- [x] Separate unverified health/mana/equipment/spell candidates
- [ ] Validate health and mana source
- [ ] Validate equipment and spell sources
- [ ] Generate adapter only from confirmed source definitions

## Phase 7 — Quality
- [ ] Add offline report fixtures and parser tests
- [ ] Add bounded scan/time budgets
- [ ] Verify all eight reports in live UE4SS
- [ ] Verify generated adapter against live telemetry
- [ ] Document UE4SS version-specific capabilities
