# Public Server Directory API

## Current implementation boundary

The standalone Rust directory service provides a persistent SQLite-backed listing API, operator-authorized initial registration, per-server revocable-by-database credentials (credential hashes only), authenticated heartbeats, stale-heartbeat expiry, and public paginated listings. It is an initial service foundation, not yet the full owner-claim or launcher integration described in the public-directory design.

## Run

Configure these server-only environment variables:

- `HOGWARTSRP_DIRECTORY_ADMIN_TOKEN`: random operator credential, at least 32 characters.
- `HOGWARTSRP_DIRECTORY_DATABASE`: SQLite database path (default `data/server-directory.sqlite3`).
- `HOGWARTSRP_DIRECTORY_LISTEN`: bind address (default `127.0.0.1:8790`).

Run `cargo run --manifest-path rust/Cargo.toml -p hogwartsrp-directory-service`. Public deployments must terminate TLS at a trusted reverse proxy. Never put the admin token or server credential in the launcher or a public listing.

## API

### `GET /v1/servers?page=1&page_size=25&game=hogwarts-legacy-pc&search=...`

Returns public listing metadata only. Page size is clamped to 100. A heartbeat is considered online for 90 seconds; stale listings remain discoverable but are returned as offline with zero online players. The listing never includes credentials, credential hashes, player identities, or owner data.

### `POST /v1/servers`

Requires `Authorization: Bearer <operator-token>`. Creates a listing and returns its server ID and one-time server credential. The credential is shown only in this response; the database stores only its SHA-256 digest. Initial registration is operator-controlled; self-service owner claims and credential rotation/revocation endpoints are follow-up work.

JSON fields: `name`, `endpoint`, `game` (currently `hogwarts-legacy-pc`), `maxPlayers`, and optional `description`.

### `PUT /v1/servers/{serverId}/heartbeat`

Requires `Authorization: Bearer <server-credential>`. JSON body: `onlinePlayers`. Counts above the registered capacity are rejected. A successful heartbeat returns 204.

## Security and limitations

- Public endpoint validation rejects private, loopback, link-local, multicast, reserved IPv4/IPv6 ranges and local/test hostnames. The service does not fetch or probe submitted endpoints, so this validation is not an endpoint reachability check and DNS rebinding defenses remain necessary before adding any server-side probe.
- Registration is restricted to an operator bearer token; a complete verified server-owner claim workflow is not implemented.
- Credential revocation currently requires an operator-side database action; a managed rotate/revoke API is not yet implemented.
- No directory server is deployed, no production registration has been created, and no real server heartbeat has been observed.
- The launcher public-server browser and connection handoff are not included in this milestone.
