# HogwartsRP Game Adapter Protocol v1

This document defines the first server-side protocol slice for a game-owned client connection. It does not claim that the UE4SS mod can currently open a WebSocket or authenticate by itself.

## Connection lifecycle

1. The game-side transport obtains a short-lived, single-use join credential through the configured account sign-in flow. It must not embed a server secret or reuse a launcher process credential.
2. It opens a WebSocket to the selected server's `/connect` endpoint.
3. The first frame is the existing authentication message:

```json
{ "type": "authenticate", "credential": "<single-use-join-ticket>" }
```

4. The server validates and consumes the credential, creates the authoritative player session, and sends the existing `connected` message.
5. The game adapter sends one handshake frame:

```json
{
  "type": "game_adapter_hello",
  "protocolVersion": 1,
  "adapterVersion": "0.1.0",
  "gameBuild": "optional-build-token",
  "ue4ssVersion": "optional-version-token",
  "capabilities": ["local_position_read", "camera_rotation_read"]
}
```

6. The server validates bounded identifiers and replies:

```json
{
  "type": "game_adapter_ready",
  "protocolVersion": 1,
  "playerId": "<server-owned-session-id>",
  "displayName": "<server-owned-display-name>",
  "serverTimeUtc": "..."
}
```

The player ID and display name are server-owned values. The client must not send an account ID, player ID, permission, house, or character owner in its handshake.

## Heartbeat

After the handshake, the game client sends a monotonically increasing nonnegative sequence:

```json
{ "type": "game_adapter_heartbeat", "sequence": 1 }
```

The server responds with `game_adapter_heartbeat_ack`, echoing the sequence and including its UTC timestamp. A heartbeat before a valid handshake, a repeated sequence, or a negative sequence closes the connection with a policy rejection.

## Validation and security

- Protocol version is currently exactly `1`; unsupported versions are rejected.
- Adapter version is required and limited to 32 ASCII token characters.
- Game build and UE4SS version are optional, bounded tokens.
- At most 16 capability names are accepted; names must be unique lowercase identifier tokens of at most 48 characters.
- Capability names are informational client claims only. They do not enable server permissions or prove that a game API works.
- Frames remain subject to the existing 32 KiB WebSocket limit.
- Existing one-use credential validation, server-owned session identity, HTTPS requirement for remote listeners, and connection cleanup remain in force.

## Deliberately not included

Protocol v1 does not transmit movement, spawn remote actors, apply transforms, replicate spells, or synchronize world state. Those messages must wait until the game-side transport is proven and the server has authoritative validation, rate limits, sequence/timestamp handling, and tests for movement data.

## Implementation status

- Server-side handshake validation and response: implemented in the current development branch.
- Server-side monotonic heartbeat acknowledgement: implemented in the current development branch.
- UE4SS-to-native transport bridge: not implemented.
- Game-side credential acquisition: not implemented.
- End-to-end game-owned authenticated session: not yet verified.
- Two-client multiplayer: not implemented.
