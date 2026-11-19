# Asset Research Toolkit Architecture

## Separation

This is an offline desktop utility, separate from the HogwartsRP server, launcher, and UE4SS runtime mod. The installation is treated as a read-only source. Reports and logs live outside the game root.

## Pipeline

1. Discovery: recursively enumerate regular files without following symlinks.
2. Inventory: record file name, size, extension, relative path, UTC modified time, and optional streaming SHA-256.
3. Classification: classify loose Unreal assets from path/name patterns; results are candidates.
4. Inspection: read ordinary text files and bounded printable strings from loose .uasset/.umap files.
5. Indexing: create asset records and candidate string-reference edges.
6. Reporting: emit JSON, Markdown, and HTML catalogs.
7. Search: query path, object-path guess, class placeholder, and candidate references.

## Layout

~~~text
tools/asset-research/
  asset_research.py
  run.ps1
  config.example.json
  README.md
reports/                 generated locally
logs/                    generated locally
docs/asset-research-toolkit/
  ARCHITECTURE.md
  ROADMAP.md
~~~

## Data model

File records include name, bytes, extension, relative path, modification time, hash, and container/asset flags. Asset records include relative path, guessed object path, class placeholder, package stem, metadata status, and reference candidates. Dependency graph edges are marked unverified until supported by package metadata.

## Safety

No container extraction/decryption, key recovery, access-control bypass, executable patching, asset modification, game launch, or game-code invocation. The source scan uses read-only file operations and does not follow symlinks.

## Known limitations

A path or printable string does not prove a Blueprint class, dependency, component, variable, function, or gameplay role. Cooked assets may omit editor-only metadata. Full catalogs require a supported UE4.27 parser or a user-generated editor export.
