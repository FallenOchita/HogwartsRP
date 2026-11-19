- Fixed a missing Lua string-concatenation operator in the component discovery status log, which could abort the research pass after discovery completed.

# Changelog

All notable HogwartsRP changes are recorded here.

## [Unreleased]

### Fixed — MovementMode enum object path

- Passes `/Script/Engine.EMovementMode` (the UObject lookup path) to `StaticFindObject` while retaining `Enum /Script/Engine.EMovementMode` as the exact reflected full-name identity check.
- Reports both values separately and preserves the UEnum class validation plus the targeted value-3 lookup.
- Adds a regression assertion for the lookup argument. No broad enum enumeration or gameplay access is introduced.

### Added — Targeted MovementMode enum-object lookup

- Added one exact StaticFindObject lookup for Enum /Script/Engine.EMovementMode when the named MovementMode descriptor is a ByteProperty.
- Validates the returned object's full name and UEnum class identity before making a targeted UEnum:GetNameByValue(3) metadata lookup.
- Reports the enum as a corroborating candidate, not a direct ByteProperty-to-UEnum association, because UE4SS 4.0.0 does not expose that descriptor link.
- Adds regression coverage for a successful exact lookup and an unavailable enum object.
- Does not enumerate arbitrary enums, read property values, inspect offsets, invoke gameplay functions, write properties, add hooks, or perform network operations.

### Fixed — Named-property lookup status separation

- Reports `NOT_FOUND` when documented direct named-property lookup completes across the inspected class chain without finding the requested descriptor.
- Reports `LOOKUP_INCOMPLETE` when one or more direct lookup attempts cannot be completed.
- Keeps first-property sample limitations and enumeration errors in separate diagnostic fields so partial descriptor enumeration cannot mislabel a completed direct lookup.
- Adds regression coverage for completed not-found lookups and unavailable lookup paths.
- Remains read-only reflection metadata inspection; no property values, gameplay functions, writes, hooks, or network operations are used.
### Changed — Safe first-property sampling for UE4SS component catalogs

- Changed component-class property descriptor collection to stop after the first callback, avoiding the live UE4SS 4.0.0 continuation path that repeatedly produced `attempt to call a nil value` after one callback.
- Explicitly labels the output as a first-property sample per class, sets `property_catalog_complete = false`, and reports `PARTIAL` even when the class hierarchy itself is complete.
- Preserves the count-only continuation and early-stop probes for diagnostics; the continuation probe is evidence only and does not control the descriptor sample.
- Ensures a missing property in the sample is never reported as proof that the property is absent; direct named-property lookup remains separate and fallback enumeration is marked incomplete.
- Adds regression assertions for one-property sampling, early termination, partial status, and preserved continuation-error diagnostics.
- Remains read-only: only property descriptor metadata is recorded; no property values, gameplay functions, writes, hooks, or network operations are used.

### Added — ForEachProperty early-stop comparison

- Added a second count-only probe that returns `true` on its first callback, exercising the documented early-stop path without inspecting the property descriptor.
- Reports the early-stop result separately from the existing `false`-return continuation probe and the detailed descriptor pass, making it possible to distinguish continuation failures from failures that occur even when iteration is stopped immediately.
- Added regression coverage showing that a synthetic continuation failure is avoided by the early-stop probe while the `false`-return probe still reproduces it.
- The probes remain read-only reflection metadata operations; no property values, gameplay functions, or game state are accessed or changed.


### Added — ForEachProperty callback diagnostics

- Added a count-only `ForEachProperty` probe that returns `false` and performs no property descriptor inspection, allowing the UE4SS iterator/callback bridge to be compared with the normal metadata callback.
- Preserved the full method-call error for component property enumeration, bounded to 2,048 characters with the original error length and an explicit truncation flag.
- Added regression coverage for successful probes, callback-continuation failures, and preservation of errors longer than the previous 180-character diagnostic limit.
- The probe enumerates reflection metadata only; it does not read property values, invoke gameplay functions, or modify game objects.

### Added — Read-only component property descriptor catalog

- Added a separate `ComponentPropertyCatalog.txt` for classes represented by confirmed player-Outer-matched components.
- Captures descriptor name, full name, property class, and declaring class without reading property values.
- Caches one bounded metadata pass per reflected class and preserves partial captures with per-class enumeration diagnostics.
- Adds explicit complete, partial, failed, and not-attempted outcomes; no failed enumeration is treated as proof that uncaptured properties are absent.
- Raises report serialization depth to ten levels so grouped class hierarchies are fully visible, with a 50,000-line output ceiling and explicit truncation marker.
- Adds standalone Lua regression coverage for successful catalogs, callback-continuation failures, and duplicate-class caching; the Linux CI job runs both the new catalog spec and the existing property-value audit spec.


### Added — Component class hierarchy inventory

- Added read-only `GetClass()` and `GetSuperStruct()` metadata collection for confirmed player-Outer-matched component instances.
- Added a per-class hierarchy cache so repeated component instances share one class-chain record rather than repeating reflection work.
- Added explicit `COMPLETE`, `PARTIAL`, and `DEPTH_LIMIT_REACHED` outcomes, plus aggregate complete/partial counts.
- Preserved the existing Outer ownership boundary, duplicate class groups, unusual class metadata review flags, and unresolved Outer counts.
- Deliberately deferred broad component property enumeration: the live UE4SS 4.0.0 `ForEachProperty` path has shown callback-continuation errors, so this change does not imply a complete property catalog.
- No component property values, gameplay UFunctions, setters, hooks, or network operations are accessed.
## [Unreleased]

### Added — ByteProperty enum-association capability audit

- Added a metadata-only capability record for the MovementMode descriptor, including its property-class identity and exposure of documented generic Property methods.
- Explicitly records that UE4SS 4.0.0 documents UEnum lookup methods but no ByteProperty-to-UEnum accessor; enum association remains unverified and enum entries are not enumerated.
- Does not probe speculative accessor names, inspect memory offsets, invoke enum accessors or the listed Property capability methods, read property values, or call gameplay functions.

### Added — Direct named-property metadata lookup

- Added a targeted UObject:Reflection() / UObjectReflection:GetProperty() lookup for the requested MovementMode descriptor across the existing class hierarchy.
- Recorded per-class lookup outcomes separately from ForEachProperty enumeration diagnostics, including lookup scope and descriptor identity.
- Kept enum association unverified; direct property metadata lookup does not expose enum entries or establish a property-to-UEnum association.
- Preserved strict read-only behavior: no property values, gameplay functions, writes, hooks, or network operations.


### Added — UE4SS property-enumeration diagnostics

- Added per-class diagnostics to the existing ForEachProperty pass: invocation status, callback invocation count, partial property count, last observed property name, and concise errors.
- Included the same diagnostic records in the named-property audit so incomplete reflection passes are distinguishable from a completed pass that did not find a descriptor.
- Protected the callback body and report callback failures separately from method lookup and invocation failures.
- Kept enumeration single-pass and metadata-only; no retries, property-value reads, gameplay function calls, writes, hooks, or network operations were added.


### Fixed — Windows Rust worker integration-test cleanup

- Ensure the ResourceManager stops its Rust worker in the test cleanup path, including when an assertion fails.
- Retry temporary-directory removal for a bounded period to tolerate transient Windows executable-file locks after process exit.


### Added — Rust worker restart policy primitive

- Added an opt-in bounded retry policy with capped exponential backoff and strict configuration limits.
- Added tests for delay progression, maximum-delay capping, retry-budget bounds, and invalid configurations.
- This commit defines and validates the recovery policy; automatic process replacement remains the next integration step.


### Improved — Rust resource health supervision

- Propagate unexpected Rust worker exits into the owning resource's lifecycle status and mark the resource Failed with its exit code when available.
- Expose a ResourceManager worker-exit notification for host diagnostics while isolating observer exceptions.
- Preserve a failure that occurs during startup instead of accidentally overwriting it with a later Started transition.
- Added a ResourceManager integration test that starts the compiled Rust fixture, triggers its controlled exit, and verifies the resource status transition.


### Fixed — Rust worker exit supervision race

- Wait for the worker process to fully exit after stdout reaches EOF before reading its exit code or notifying observers.
- Prevented a timing race that could report a null exit code on Unix even when the worker exited with a known nonzero code.

### Added — Rust resource runtime integration

- Added precompiled Rust worker declarations through repeated `rust_script` manifest entries, with resource-relative path validation.
- Added a .NET process supervisor that waits for worker readiness, bounds JSON-lines frames and request duration, routes command/event requests, captures stderr diagnostics, and performs graceful shutdown with process-tree termination fallback.
- Integrated Rust worker startup and shutdown into ResourceManager alongside Lua resource scripts, and exposed Rust command/event registration plus explicit command and event dispatch methods.
- Aligned the Rust SDK JSON field casing with the .NET web JSON protocol and added a regression test for `eventName` serialization.
- Added manifest parsing coverage and updated Rust support and architecture documentation.

### Integration limitations

- Rust resources must be compiled and deployed for the server platform; automatic Cargo builds and artifact discovery are not included.
- Rust command declarations and network-event declarations are surfaced by ResourceManager but are not yet automatically connected to the framework-wide command registry or WebSocket authorization/allowlist pipeline.
- Rust workers currently run with the server account's OS permissions; this slice does not yet apply the Lua worker's OS memory/CPU limits. Treat Rust resources as trusted code.


### Fixed — Movement-mode property audit enumeration

- Reused the movement class hierarchy's existing reflected-property enumeration to capture `MovementMode`, avoiding a second `ForEachProperty` invocation and early callback termination.
- Recorded the matched descriptor and declaring class from the same metadata pass used by the reflection report.
- Preserved explicit incomplete/not-found outcomes when enumeration fails or reaches its configured cap.


### Improved — Movement-mode enum metadata audit

- Added a read-only reflected-property descriptor audit for the movement component's `MovementMode` field, including its property class and declaring class hierarchy.
- Kept enum association explicitly unverified when the documented UE4SS 4.0.0 property API does not expose the associated `UEnum`; no enum names or values are guessed.
- Preserved the raw numeric movement-mode value and `UNCLASSIFIED` locomotion state pending verified enum association.
- No property values are newly read by the metadata audit, and no gameplay UFunctions, writes, hooks, or network operations are introduced.


### Fixed — Rust launcher Windows build

- Fixed the Windows-only Rust launcher compile error in atomic server-profile persistence by mapping the successful temporary-file persist result to unit. The underlying atomic replacement behavior is retained.

### Changed — Native Rust desktop launcher

- Added `rust/apps/hogwartsrp-launcher`, an eframe/egui desktop application built and launched entirely from Rust.
- Ported saved server profiles, server status checks, configured browser account-portal opening, and Steam launch requests to the Rust application.
- Removed the WinForms launcher executable project and its C# UI source files from the repository and .NET solution.
- Added a Windows CI release build for the Rust launcher; Rust workspace library tests continue on Ubuntu and Windows.
- Updated the README and launcher documentation to identify the Rust binary as the only active desktop launcher.

### Migration boundary

- The Rust launcher does not call the legacy C# launcher library. That library remains temporarily in the .NET solution for its existing tests/contracts, but is not used to start or operate the Rust UI.
- OIDC + PKCE join authentication, authenticated WebSocket sessions, character creation/submission, broader Steam library discovery, Epic launch, and auto-update still need Rust implementations. These features are not claimed as ported.

### Added — Rust resource SDK and native-core foundation

- Added a Rust workspace with a process-oriented resource SDK, an example Rust resource, and a native-core crate for bounded validation primitives.
- Added resource/event name validation, argument-count limits, and protocol-frame size limits with unit tests.
- Added a JSON-lines worker SDK with command/event registration and an explicit server-authoritative trust boundary.
- Added Ubuntu and Windows CI steps to format and test the Rust workspace.
- Documented the Rust resource and native-component architecture, security boundaries, and staged integration requirements.

### Scope and integration status

- This is a foundation milestone, not end-to-end Rust resource hosting. The current C# ResourceManager still launches Lua workers only; Rust worker discovery, build/deployment, process supervision, command/event routing, and shutdown integration remain future work.
- The Rust native-core crate is not yet loaded by .NET through FFI and is not yet on the framework hot path. No performance improvement is claimed.

### Improved — UE4SS reflection-provider capability audit

- Added read-only member-exposure checks for reflected class enumeration methods, without invoking those methods.
- Added provider capability details to the generated reflection report so method exposure is separated from successful metadata enumeration.
- Preserved distinct per-function outcomes for unavailable methods, lookup failures, and call failures; a call failure is not reported as proof that the underlying Unreal function is absent.
- Kept the research boundary unchanged: no gameplay UFunction invocation, ProcessEvent calls, property writes, hooks, or network operations.
- Live UE4SS validation is still required to determine why selected UFunction descriptor wrappers fail during owned-property enumeration.

### Improved — Read-only movement property value audit

- Added a structured property-value audit for selected player and movement-component candidates, recording access method, Lua value type, and conservative read status.
- Primitive values are reported as raw evidence; object references are identified by full name where available.
- Opaque userdata and Lua tables are not dereferenced or expanded, preventing `TrivialObject` values from being mistaken for decoded numbers or structs.
- Updated gameplay-state reporting to consume audit records instead of invoking `GetVelocity` or vector methods; movement mode and speed remain unavailable unless a primitive value is safely established.
- Added a standalone Lua regression harness for primitive, missing, table, opaque-userdata, and getter-error cases.
- Preserved the read-only boundary: no gameplay UFunctions, setters, hooks, property writes, or network operations are used by the new audit.

### Improved — Read-only component inventory validation

- Added a class-grouped inventory for confirmed player-Outer matches, including instance counts and full paths for repeated classes.
- Added a separate review list for unusual class-name metadata instead of assuming that an Outer match alone proves conventional ActorComponent inheritance.
- Added exact-full-path cross-checks between valid allowlisted property references and enumerated matches.
- Preserved invalid or unavailable property references as unresolved; the toolkit does not infer that `CharMoveComp` and `CharacterMovement` are aliases.
- Kept the existing 256-entry report cap, ownership counts, and read-only discovery behavior.
- Added documentation for the observed `BP_PlayerPerceptionPoint_C` class-metadata anomaly and the need for a future verified class-hierarchy pass.


### Added — Read-only Hogwarts Legacy asset research toolkit

- Added a standalone Python inventory engine and PowerShell launcher under tools/asset-research.
- Added recursive installation inventory for regular files, with relative paths, sizes, extensions, UTC timestamps, and optional streaming SHA-256.
- Added loose .uasset/.umap candidate classification, bounded printable-string references, asset catalogs, and an explicitly unverified dependency graph.
- Added JSON, Markdown, and HTML reports for file inventory and candidate Blueprint, character, animation, audio, world/map/level, localization, DataTable, Enum, and Struct catalogs.
- Added catalog search, configuration example, automated tests, architecture documentation, and roadmap.
- Kept the installation read-only; .pak/.utoc/.ucas containers are inventoried but not extracted or decrypted. Cooked metadata remains heuristic until validated by a compatible UE4.27 parser.

### Improved — Player-owned component object enumeration

- Added a UE4SS 4.0.0 `FindObjects` query for `ActorComponent` and derived component classes, using the documented six-argument API.
- Filtered returned objects by walking their read-only `GetOuter()` chain and matching it to the discovered player pawn identity.
- Added explicit counts for returned component objects, player-owned matches, unresolved outer chains, and report truncation.
- Preserved the existing allowlisted property-reference probes as a separate cross-check instead of treating them as a complete component list.
- Kept enumeration read-only: no component creation, gameplay UFunction invocation, property writes, hooks, or network activity. Live UE4SS validation is required before claiming the query works in Hogwarts Legacy.

### Fixed — Research toolkit Lua startup syntax

- Removed a stray, incomplete duplicate `log()` declaration that caused `main.lua` to fail parsing at end-of-file before the research toolkit could initialize.
- Kept the existing valid logger and all discovery/report behavior unchanged. This is a syntax-only correction; live UE4SS validation is still required.

### Improved — Read-only UE4SS reflection and component diagnostics

- Normalized UE4SS method failures into concise statuses (`METHOD_UNAVAILABLE`, `CALL_ERROR`, and related states) so unsupported UFunction descriptor wrappers no longer flood reports with repeated tracebacks.
- Preserved per-function identity and flags when UFunction-owned property enumeration is unavailable; recorded the metadata-enumeration status without invoking any gameplay function.
- Made class-name diagnostics distinguish invalid source objects, unavailable/failed `GetClass`, invalid class wrappers, and unavailable class full names.
- Kept complete component enumeration explicitly `UNAVAILABLE` because this UE4SS build does not expose `GetComponents` as a Lua table.
- Added a bounded allowlist of previously observed player component-reference properties and expanded read-only relationship checks for `Player`, `CharacterOwner`, `PawnOwner`, and `UpdatedComponent`.
- Updated the reflection research guide and runtime diagnostics. No property writes, gameplay UFunction calls, hooks, or network activity were added.

### Fixed — HogwartsRP research report output paths

- Resolve the report directory from the research mod's installed script location instead of the game's current working directory.
- Added a documented UE4SS `IterateGameDirectories()` fallback for installations where the Lua chunk source is not an absolute path.
- Added the packaged `Scripts/reports/` directory marker required by the report writer.
- Report generation now returns successful/failed file counts and logs the resolved absolute output directory.
- Adapter generation now uses the same resolved output directory as the text reports.
- Kept report output read-only with respect to game state; only local report files are written.


### Added — Browser account session foundation

- Added a process-local browser account session registry with cryptographically random bearer and CSRF tokens.
- Stored only token digests, bound each session to a server-supplied central account ID, and exposed session resolution without returning credentials.
- Added fixed session expiry, bounded capacity with concurrency-safe issuance, expired-session pruning, constant-time CSRF verification, and per-session revocation.
- Added regression tests for account binding, malformed tokens, CSRF pairing, expiry, capacity, and revocation.
- Documented the required future cookie, HTTPS, CSRF-header, origin-check, and distributed-session integration rules.

### Security and scope

- Session issuance remains a trusted server-side operation. These reusable session inspection/logout handlers do not expose a browser login route, provider callback, account-link endpoint, or portal UI.
- Sessions are process-local and are invalidated on service restart; a production multi-instance portal requires a shared session backend.

### Planned — Public directory and player account linking foundation

- Added `docs/public-directory-and-account-linking.md` defining the architecture and security requirements for a central public server directory and browser-based player account connections for Discord, Steam, and Epic Games.
- Specified independent optional provider links, server-verified identity, single-use state, PKCE where supported, stable provider identifiers, duplicate-link prevention, and unlink protections.
- Defined directory registration, revocable server credentials, authenticated heartbeats, stale-server expiry, public-data privacy, endpoint validation, and launcher browser requirements.
- This is a design/security foundation only. No provider OAuth integration, central account portal, public directory API, or launcher UI is implemented by this change.


### Added — Central account identity storage foundation

- Added `JsonAccountIdentityStore` with server-generated central account IDs and JSON persistence for player accounts.
- Added provider identity link records for Discord, Steam, and Epic Games without storing provider tokens, authorization codes, or secrets.
- Enforced one linked identity per provider per HogwartsRP account and global uniqueness of each provider subject.
- Added account-scoped link listing and unlink operations, idempotent repeat linking, input validation, atomic file replacement, and corrupt-store preservation.
- Added regression tests for persistence, ownership isolation, duplicate-link rejection, unlinking, and invalid data.
- Documented the trust boundary: provider identities must be verified by a future server-side adapter before calling the store. No browser authentication, OAuth/OpenID callback, or public API is exposed yet.

### Added

- Added the first Windows desktop launcher shell using WinForms, with server connection/sign-in, roleplay character registration, status feedback, and Steam game launch actions.
- Added Windows-targeted desktop build configuration while retaining the existing launcher, authentication, networking, and character-validation services.
- Added startup-splash and Windows application/window-icon support for the selected HogwartsRP artwork. The image files are supplied in the artwork bundle and must be copied into `src/HogwartsRP.Launcher.App/Assets` to display the final art; text fallbacks remain available when assets are absent.
- Added saved server profiles to the desktop launcher, including local JSON persistence, selecting saved entries, updating an existing profile by address, and removing saved entries.
- Added a public read-only `/status` endpoint exposing server name, live authenticated-player count, capacity, and check timestamp without exposing player identities.
- Added launcher status checks for saved server addresses, including online player counts and bounded request timeouts, with response validation and regression tests.
- Added regression tests for server-profile persistence, sorting, updates, deletion, invalid input, and corrupt-file preservation.
- Added an interactive console character-creation flow to the launcher, connected to the authenticated character-submission endpoint.
- Added launcher-flow tests for successful submission, client-side validation rejection, server rejection, and skipping character creation.
- Added an experimental read-only Hogwarts Legacy UE4SS player-state adapter with local controller/pawn discovery, confirmed position/control-rotation reads, velocity and movement-component probes, and exploratory jump/sprint/animation diagnostics. Added separate installation and verification instructions. Live adapter installation and multiplayer synchronization remain unverified.

### Added

- Added a launcher-side character creation draft model that trims and validates names, Hogwarts houses, roleplay text, and optional wand details before building a typed submission.
- Added dedicated launcher tests for valid draft mapping, invalid names/houses, wand-length bounds, and omitted optional fields.
- Added dedicated WebSocket client protocol tests covering concurrent character submissions with out-of-order responses and connection loss before a response.
- Added authenticated WebSocket character submissions backed by the JSON character repository, with server-derived account ownership and correlated results.
- Wired the character repository into server startup with a configurable `Storage:CharactersPath`.
- A harmless UE4SS Lua diagnostic mod for the Steam PC version, with installation and log-verification instructions.
- A local PowerShell helper that produces a sanitized UE4SS diagnostic report without uploading or exposing unrelated log content.
- Regression tests for sanitized UE4SS reports, including status detection, missing logs, optional report output, and exclusion of arbitrary log payloads.
- A JSON-backed character repository with atomic file replacement, duplicate-ID protection, and owner-filtered retrieval.
- Regression tests for character persistence across repository instances, owner isolation, duplicate IDs, and corrupt-store preservation.
- Core-owned player sessions with server-generated IDs, single-use join tickets, and per-resource client-event validation.
- A WebSocket server transport with bounded JSON messages and authenticated server-to-client delivery.
- A replaceable client network adapter and launcher credential-provider interface.
- Optional OIDC browser sign-in with PKCE, server-issued replay-resistant challenges, server-side ID-token validation, and short-lived join tickets.
- Mock-provider tests for OIDC signature and claim validation, challenge expiry, and single-use ticket issuance.
- A loopback-only operator development-ticket command for local protocol work.
- A preview launcher flow that authenticates to the selected server, requests Hogwarts Legacy launch, and keeps the protocol session open until the launcher exits.
- Lua resource APIs for registering client-to-server events and sending events to authenticated players.

### Changed

- The console launcher now offers character registration after server authentication and before launching Hogwarts Legacy.
- Character registration reports server acceptance or rejection; declining the prompt leaves the existing launch flow unchanged.
- Added the missing `HogwartsRP.GameIntegration.Abstractions` project reference to `HogwartsRP.Server`, resolving the character repository contract types during compilation.
- The diagnostic report helper now emits only normalized known markers and constrained version tokens instead of echoing arbitrary log lines.
- CI now runs the PowerShell diagnostic-report regression tests on Windows and Ubuntu.

### Limitations

- The desktop launcher now provides a graphical Windows shell; server discovery/browser, final artwork files must be copied from the supplied bundle, and in-game character creation remains future work.
- Character submissions persist roleplay metadata on the server; they do not apply house, wand, appearance, or character data to the game.
- The JSON character repository is a single-server persistence adapter; it is not a multi-instance database or backup system.
- Character submission is available through the authenticated launcher/server WebSocket protocol and JSON repository, but it does not create or modify a character inside Hogwarts Legacy.
- When OIDC is not configured, the console launcher uses a development-ticket prompt for loopback servers only.
- OIDC sign-in requires issuer and public-client configuration on both launcher and server; RS256 is the only currently accepted ID-token signing algorithm.
- The launcher owns the current network session. Hogwarts Legacy itself has no compatible multiplayer adapter yet.
- Direct character-creation entry, game-client event transport, and in-game multiplayer remain unavailable.