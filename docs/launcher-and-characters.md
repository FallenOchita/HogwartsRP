# Launcher and character creation

The active desktop launcher is the native Rust application at `rust/apps/hogwartsrp-launcher`. The former C# launcher support library, C# WebSocket client, and their test projects have been removed. The .NET server/core remains C#; this migration changes the launcher layer only.

## Authentication and server connection

The Rust launcher obtains a server challenge, discovers the configured OIDC provider, and uses the system browser for an authorization-code flow with PKCE S256. It binds a loopback callback on `127.0.0.1`, validates the returned state, redeems the authorization code, and exchanges the ID token with the selected server for a one-time join ticket.

The selected server validates the ID token signature, issuer, audience, time claims, and server nonce. The launcher does not treat the ID token as a local permission grant or send it over the game WebSocket connection. The server creates a short-lived, single-use ticket; the launcher then authenticates its WebSocket connection to `/connect` with that ticket. The server creates the player session and supplies its session ID and display name.

Configure the same OIDC issuer and public client ID on the server and launcher. The identity provider must support authorization code with PKCE S256, a browser-based native-app flow, a loopback redirect using `127.0.0.1`, and RS256 ID-token signing keys published in OIDC discovery metadata. The server currently validates RS256 only.

Server environment variables are `Authentication__Oidc__Authority` and `Authentication__Oidc__ClientId`. Launcher environment variables are `HOGWARTSRP_OIDC_ISSUER` and `HOGWARTSRP_OIDC_CLIENT_ID`. The authority value must exactly match the provider's `issuer` metadata value. Production authorities and remote server endpoints must use HTTPS/WSS. HTTP/WS is permitted only for loopback development. No provider-specific secrets are embedded in the launcher.

The flow follows [RFC 8252](https://www.rfc-editor.org/rfc/rfc8252.html) for native-app browser authorization, [RFC 7636](https://www.rfc-editor.org/rfc/rfc7636.html) for PKCE, and [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html) plus [OpenID Connect Discovery](https://openid.net/specs/openid-connect-discovery-1_0.html) for ID-token and provider metadata validation.

The server derives account identity from a hash of the configured issuer and signed `sub` claim. Provider claims do not grant permissions or ownership. The challenge expires after five minutes; the resulting join ticket expires after two minutes and is single-use. The launcher does not log the authorization code, ID token, or ticket.

## Character submission

The Rust launcher provides a graphical roleplay character form. It collects a character name, Hogwarts house, optional background and biography, and optional wand details. The Rust `CharacterCreationDraft` validates and normalizes those values before sending a `submit_character` message over the authenticated WebSocket connection.

The server repeats all validation and derives ownership from the authenticated player session. Client-supplied owner IDs or permission claims are not accepted. A successful submission stores roleplay metadata on the server; it does not create or modify a character inside Hogwarts Legacy, nor does it apply appearance, house, wand, or other data to the game.

The client generates a request ID and waits for the matching `character_submission_result`. The Rust WebSocket client bounds message size, event names, argument counts, and primitive argument types. It currently queues server-originated events while awaiting a character-submission response; a separate continuous receive task and reconnect/backoff behavior remain follow-up work.

## Character repository

The server project includes `JsonCharacterRepository`, a file-backed implementation of `ICharacterRepository`. Configure it with a writable data-file path, for example:

```csharp
var characterRepository = new JsonCharacterRepository(
    builder.Configuration["Storage:CharactersPath"] ?? "data/characters.json");
```

The repository loads existing records on first use, serializes writes within the process, writes to a temporary file in the same directory, and replaces the data file after the write completes. It rejects duplicate character IDs and exposes owner-filtered retrieval through `GetByOwnerAsync`. If the JSON file is corrupt, it throws `InvalidDataException` and leaves the existing file untouched.

This adapter is intended for one server process. It does not provide cross-process locking, multi-server coordination, encryption at rest, backups, or a production database. The authenticated WebSocket submission endpoint persists roleplay profile data only.

## Game launch and integration boundary

The Rust launcher detects conventional Steam/Epic installation roots and accepts additional roots through `HOGWARTSRP_GAME_ROOTS`. Steam launch requests use the official app URI for Hogwarts Legacy (990080), allowing Steam to perform its normal ownership and sign-in checks. Epic launch is not implemented yet.

Official Hogwarts Legacy mod support provides a Creator Kit and in-game mod management for content such as quests, dungeons, cosmetics, and skins. Public official documentation does not expose a supported direct character-creation launch API. The launcher therefore starts the game normally and makes no direct-entry attempt by default.

The launcher session is separate from the game process. No real game installation, direct character creation, remote-player synchronization, or playable Hogwarts Legacy multiplayer has been verified by these launcher changes.
