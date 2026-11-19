# Changelog

All notable HogwartsRP changes are recorded here.

## [Unreleased]

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
