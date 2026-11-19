# Asset Research Toolkit Roadmap

## Phase 1 — Read-only baseline

- Installation file inventory and optional SHA-256.
- Pak/IoStore container file inventory without extraction.
- Loose Unreal asset path classification and string heuristics.
- JSON, Markdown, HTML reports and search.
- User-supplied Steam or Epic installation path.

## Phase 2 — UE4.27 parser adapter

- Define a versioned parser interface and capability manifest.
- Add fixtures for supported cooked package layouts.
- Parse package summary, names, imports/exports, class, outer, and dependencies where supported.
- Attach provenance and confidence to every field.
- Keep encrypted container handling out of scope.

## Phase 3 — Blueprint and gameplay catalogs

- Verified parent class, interfaces, component templates, variables, functions, and properties.
- Target BP_Biped_Player_C and BP_Phoenix_Player_Controller_C.
- Character, NPC, creature, animation, widget, material, and world classification from verified class metadata.
- Separate generated Blueprint classes from source asset packages.

## Phase 4 — Specialized datasets

- Supported localization metadata and locres decoding.
- Audio bank/stream metadata for documented formats.
- DataTable, Enum, Struct, world, persistent-level, and sublevel relationships.

## Phase 5 — Search and analysis

- SQLite full-text index, filters, incremental rescans, and report diffs.
- Verified import/export dependency graph.
- Optional export of verified findings to the separate UE4SS research toolkit.

## Promotion criteria

A field becomes verified only when backed by a documented parser field, validated fixture, or reproducible editor/runtime report. The raw file inventory remains useful when deep metadata is unavailable.
