# Launcher and character creation

The preview launcher selects a server, obtains a join credential through `IJoinCredentialProvider`, connects through the replaceable network-client adapter, then detects the game and asks a platform adapter to launch. When OIDC is configured, the launcher uses the system browser and authorization-code flow with PKCE, a loopback callback, and a server-issued nonce. The selected server validates the signed ID token against its configured issuer and client ID, then creates a short-lived, single-use join ticket. The launcher does not send the ID token to the WebSocket game session or accept identity claims from the client. Without OIDC configuration, the console app falls back to the development ticket prompt. The server's `issue-ticket` command is available only when its listener binds to loopback.

Configure the same OIDC issuer and public-client ID on the server and launcher. The identity provider must support authorization code with PKCE S256, a browser-based native-app flow, a loopback redirect using `127.0.0.1`, and RS256 ID-token signing keys published in OIDC discovery metadata. The server currently validates RS256 only. Server environment variables are `Authentication__Oidc__Authority` and `Authentication__Oidc__ClientId`; launcher environment variables are `HogwartsRP__Oidc__Authority` and `HogwartsRP__Oidc__ClientId`. The authority value must exactly match the provider's `issuer` metadata value. Production authorities and remote server endpoints must use HTTPS. HTTP is permitted only for loopback development. No provider-specific secrets are embedded in the launcher.

The flow follows [RFC 8252](https://www.rfc-editor.org/rfc/rfc8252.html) for native-app browser authorization, [RFC 7636](https://www.rfc-editor.org/rfc/rfc7636.html) for PKCE, and [OpenID Connect Core](https://openid.net/specs/openid-connect-core-1_0.html) plus [OpenID Connect Discovery](https://openid.net/specs/openid-connect-discovery-1_0.html) for ID-token and provider metadata validation.

The server derives account identity from a hash of the configured issuer and signed `sub` claim. It uses `name`, then `preferred_username`, for the display name, with a safe fallback. Provider claims do not grant permissions or ownership. The challenge expires after five minutes; the resulting join ticket expires after two minutes and is single-use. The launcher does not log the authorization code, ID token, or ticket.

Installation detection is limited to conventional Steam/Epic Windows install paths and explicitly configured roots. Steam uses the platform app URI. The launcher keeps its own protocol session open, but the game is not connected to it. A compatible Hogwarts Legacy multiplayer adapter and Epic launcher adapter are not included.

Official Hogwarts Legacy mod support provides a Creator Kit and in-game mod management for content such as quests, dungeons, cosmetics, and skins. Public official documentation does not expose a supported direct character-creation launch API. Community character editor mods demonstrate in-game editing via a mod loader and hotkeys, not a stable launcher API. Consequently the launcher makes no direct-entry attempt by default and reports normal-launch fallback. `ICharacterCreationBridge` and `ICharacterCreationIntegration` allow a future verified integration to be added without changing launcher logic.

## Character submission model

Server submission data includes name, optional appearance JSON, house, wand, background, and roleplay profile. Validation limits input sizes and restricts house values. The server stamps ownership from its authenticated player session and does not accept client-supplied permission or owner fields.

The launcher library contains `CharacterCreationDraft`, a UI-independent form model. It normalizes and checks the character name, validates the selected house, enforces background/biography length limits and optional wand-length bounds, and builds the existing `CharacterSubmission` contract. The model deliberately leaves appearance JSON unset until a verified appearance representation is available. Client validation is for usability only—the server repeats authoritative validation.

## Interactive console character creation

After connecting to the selected server and authenticating, the console launcher asks whether to register a roleplay character. If accepted, it prompts for a name and one of the four Hogwarts houses, plus optional background, biography, and wand details. The draft is validated locally before submission. A valid draft is sent over the already-authenticated WebSocket connection using the existing request-correlation protocol. The launcher displays the server's acceptance or rejection response. Declining the prompt skips registration and continues to the existing game-launch flow.

The console form is a functional launcher interaction, not a graphical UI. A successful response means the server accepted and stored roleplay metadata. It does not create a Hogwarts Legacy character or synchronize appearance, house, wand, position, or other game state.

## Character repository

The server project includes `JsonCharacterRepository`, a file-backed implementation of `ICharacterRepository`. Configure it with a writable data-file path, for example:

```csharp
var characterRepository = new JsonCharacterRepository(
    builder.Configuration["Storage:CharactersPath"] ?? "data/characters.json");
```

The repository loads existing records on first use, serializes writes within the process, writes to a temporary file in the same directory, and replaces the data file after the write completes. It rejects duplicate character IDs and exposes owner-filtered retrieval through `GetByOwnerAsync`. If the JSON file is corrupt, it throws `InvalidDataException` and leaves the existing file untouched.

This adapter is intended for one server process. It does not provide cross-process locking, multi-server coordination, encryption at rest, backups, or a production database. The server exposes an authenticated WebSocket submission endpoint. The launcher network client has a matching submission method that awaits the correlated server response.

No real game installation or integration has been tested in this environment. There is no claim that direct character creation or in-game multiplayer works.

## Character submission protocol

After authenticating the WebSocket connection, a client may submit a character using a message shaped like:

```json
{
  "type": "submit_character",
  "requestId": "client-generated-correlation-id",
  "submission": {
    "name": "Ada",
    "appearanceJson": null,
    "house": "Ravenclaw",
    "wand": { "wood": "Oak", "core": "Phoenix feather", "length": 11, "flexibility": "Supple" },
    "background": "Scholar",
    "roleplayProfile": { "background": "Student", "biography": "A curious student." }
  }
}
```

The server responds with `type: "character_submission_result"`, echoing the request ID and including a `submissionResult` object. The authenticated account ID is taken from the server-created session, never from the client payload. Configure the server's `Storage:CharactersPath` setting to choose the JSON repository file. The endpoint stores roleplay profile data on the server; it does not apply appearance, house, wand, or character data to the game.

## Launcher character submission client

The authenticated launcher connection exposes `SubmitCharacterAsync(CharacterSubmission)`. It sends a `submit_character` message with a generated request ID and waits for the matching `character_submission_result`. The network receive loop routes results by request ID, so an unrelated or late response cannot complete a different submission. If the connection closes before a result arrives, pending submissions fail rather than hanging. Cancellation is supported while sending or waiting. The server remains authoritative for validation and ownership; the launcher does not send an account/owner identifier. This flow persists roleplay character data only and does not apply the data to Hogwarts Legacy.

## Character submission client regression tests

The solution includes `HogwartsRP.Networking.Client.Tests`. Its in-process WebSocket server tests the launcher protocol without requiring a real game or remote server. Coverage includes concurrent submissions whose responses arrive out of order and the connection closing before a submission result is returned. These are transport/protocol tests only.

## Launcher character draft and console flow tests

The solution includes `HogwartsRP.Launcher.Tests`, covering draft-to-submission mapping, field normalization, invalid names and houses, wand-length bounds, optional fields, and the console flow's successful submission, validation rejection, server rejection, and opt-out behavior. These tests do not replace server validation or establish in-game character creation.
