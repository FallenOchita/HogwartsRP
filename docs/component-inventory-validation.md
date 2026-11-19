# Read-only component inventory validation

## Purpose

This change improves the interpretation of the existing UE4SS 4.0.0 component query. It does not change the query or ownership rule. It adds a grouped inventory, class-metadata review markers, and exact-path comparisons with the allowlisted property-reference probes.

## What each result means

- `PLAYER_OUTER_MATCH` means the object's read-only Outer chain resolved to the discovered player pawn full name.
- `class_inventory.classes` groups the reported matches by the class full-name string returned by the existing safe class-name helper.
- `duplicate_class_groups` lists classes with multiple distinct object paths. Instances are retained individually.
- `class_metadata_review` flags class names that do not use the expected `Class /Script/...` form. This is a review signal, not a definitive type verdict.
- `property_reference_crosscheck` compares only candidates with a valid full path against enumerated match paths. It does not infer property aliases.

## Interpretation of the supplied live report

The supplied report contained 34 player-Outer matches. Thirty-three entries reported class names in the `Class /Script/...` form. The `PlayerPerceptionPoint` entry reported `BlueprintGeneratedClass /Game/Pawn/NPC/Enemy/Perception/BP_PlayerPerceptionPoint.BP_PlayerPerceptionPoint_C` and is therefore retained but flagged for class-hierarchy review.

The `CharacterMovement` property reference resolves to the same full path as the enumerated `CharMoveComp` object. The `CharMoveComp` property probe itself reported `INVALID_REFERENCE`; the new cross-check records that as lacking a valid path to compare. It deliberately does not claim the two property names are aliases.

The 3,811 unresolved Outer chains remain unresolved. No assumption is made that these objects belong to the player or that their unresolved status is caused by a specific UE4SS limitation.

## Safety and limits

- The query remains `FindObjects(0, "ActorComponent", nil, 0, 0, false)`.
- The Outer walk remains read-only and bounded to eight levels.
- The existing report cap remains 256 entries.
- Class names are grouped as metadata strings; this change does not invoke class methods beyond the existing `GetClass` / full-name read path.
- No gameplay UFunctions, `ProcessEvent`, setters, property writes, hooks, object creation/destruction, or network operations are added.
- A class-name prefix is not a substitute for verified inheritance metadata. A future hierarchy-validation pass must use a UE4SS API verified against the target 4.0.0 runtime before classifying unusual entries as ActorComponents.

## Runtime validation still required

Install the branch in the target Hogwarts Legacy UE4SS 4.0.0 environment and inspect the new `ComponentReport.txt`. Confirm:

1. The class groups sum to the number of reported items (not necessarily the total match count if the 256-entry cap is reached).
2. Duplicate groups preserve every individual full path.
3. The PlayerPerceptionPoint entry appears in the class-metadata review list.
4. `CharacterMovement` produces an exact path match when it resolves, while `CharMoveComp` remains explicitly unresolved if its property probe is still invalid.
5. The total query, matched, unresolved, non-matching, and truncation counts remain consistent with the live run.

Source changes and successful GitHub checks do not replace this in-game validation.
