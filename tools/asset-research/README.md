# Hogwarts Legacy Asset Research Toolkit

A separate Windows/Python read-only utility for inventorying a locally installed Hogwarts Legacy copy. It inventories files, computes optional streaming SHA-256 hashes, classifies loose Unreal assets, records bounded printable-string references, and produces JSON, Markdown, and HTML reports.

## Safety and accuracy

- Opens source files read-only; does not write inside the game directory.
- Report output must be outside the game installation.
- Does not patch executables, bypass DRM/authentication/anti-cheat, crack encryption, or modify assets.
- .pak, .utoc, and .ucas containers are inventoried and hashed as files; this tool does not extract or decrypt their contents.
- Loose .uasset/.umap metadata is heuristic. It does not claim authoritative Blueprint reflection, import/export dependency data, or complete asset discovery.
- .locres files are inventoried; their localized string payloads are not decoded in this baseline.

## Requirements

Windows 10/11, PowerShell 5.1+ or 7+, Python 3.10+. No third-party packages are required.

## Quick start

~~~powershell
cd tools/asset-research
.\run.ps1 -Action scan -GameRoot "D:\SteamLibrary\steamapps\common\Hogwarts Legacy" -Output ".\reports"
~~~

Skip hashing for a faster inventory:

~~~powershell
.\run.ps1 -Action scan -NoHash
~~~

Search the generated catalog:

~~~powershell
.\run.ps1 -Action search -Query "Student"
.\run.ps1 -Action search -Query "Broom"
~~~

## Generated reports

- FileInventory.json, FileInventory.md, FileInventory.html
- AssetCatalog.json, AssetCatalog.html
- BlueprintInventory.md and BlueprintInventory.json
- CharacterCatalog.json
- AnimationCatalog.json
- AudioCatalog.json
- WorldReport.json, MapInventory.json, LevelInventory.json
- LocalizationCatalog.json
- DataTableCatalog.json, EnumCatalog.json, StructCatalog.json
- DependencyGraph.json
- RunSummary.json

Domain catalogs may be empty. Filename/path classification is a candidate, not proof of an asset's actual Unreal class.

## Full Blueprint metadata

Parent class, interfaces, components, variables, functions, and verified dependencies require a compatible UE4.27 cooked-package parser or a legitimate editor-side export. This baseline intentionally avoids undocumented package parsing and encrypted container extraction. The next parser integration must be version-pinned, fixture-tested, and expose provenance/confidence for every extracted field.

## Roadmap

1. Add automated tests and representative synthetic fixtures.
2. Add a versioned UE4.27 parser adapter for already-accessible loose assets.
3. Extract validated package imports/exports and Blueprint metadata.
4. Add supported localization and audio metadata readers.
5. Add SQLite full-text search, incremental scans, and version-to-version report diffs.
6. Export verified adapter datasets for the separate UE4SS research toolkit.
