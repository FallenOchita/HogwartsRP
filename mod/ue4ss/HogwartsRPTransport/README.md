# HogwartsRP native WebSocket transport (experimental)

This is a Windows native Lua C module for the UE4SS Lua adapter. It uses the Windows WinHTTP WebSocket client and the existing HogwartsRP `/connect` protocol. It is separate from the game-object adapter.

## Scope in this first slice

- Opens a WebSocket to the selected server's `/connect` endpoint.
- Allows HTTPS for remote servers; plain HTTP is restricted to loopback.
- Sends the existing one-use `authenticate` frame.
- Sends protocol-v1 `game_adapter_hello` after the server's `connected` response.
- Maintains a 15-second monotonically sequenced heartbeat.
- Keeps networking on a worker thread; it does not read or write Unreal objects.
- Does not send movement, spawn actors, or change game state.
- Does not log or return the join credential.

The module accepts a join ticket from its caller. **It does not acquire a ticket, implement launcher IPC, or provide OIDC sign-in.** The active Rust launcher still needs its OIDC/ticket flow ported before it can securely hand a ticket to the game process. Do not put a ticket in a command line, URL, log, config file, or source file.

## Build

Build on Windows with Visual Studio 2022 (C++20), CMake 3.22+, and the exact Lua headers/import library matching the UE4SS build being used.

Set these environment variables before configuring:

- `UE4SS_LUA_INCLUDE_DIR`: directory containing the matching `lua.h` and `lauxlib.h`.
- `UE4SS_LUA_LIBRARY_DIR`: directory containing the matching Lua import library (`lua54.lib` or equivalent).

Then:

```powershell
cmake -S mod/ue4ss/HogwartsRPTransport -B build/HogwartsRPTransport -A x64
cmake --build build/HogwartsRPTransport --config Release
```

The output is `hogwartsrp_transport.dll`. Place it beside the adapter's Lua scripts so UE4SS can load it as a native Lua C library. The exact Lua ABI and module-loading behavior must be confirmed against the installed UE4SS build before distributing a binary.

## Lua API

```lua
local transport = require("hogwartsrp_transport")

-- The ticket must come from a future authenticated launcher-to-game handoff.
transport.connect(
    "https://your-server.example",
    oneUseJoinTicket,
    "0.1.0",
    gameBuildToken,
    ue4ssVersionToken
)

local state = transport.status()
print(state.state, state.detail)

-- On explicit disconnect / mod shutdown:
transport.disconnect()
```

`connect` returns after starting the worker. `status` returns `{ state, detail, running }`. States include `idle`, `starting`, `authenticating`, `connected`, `disconnected`, and `error`.

## Security and limitations

- Join credentials are accepted only as an in-memory argument, used for the first authentication frame, then the native worker's credential buffer is overwritten.
- Remote endpoints require TLS. WinHTTP performs normal certificate validation; the module does not disable certificate checks.
- Message size is bounded to 32 KiB.
- The current response check recognizes expected protocol message types; it is not a general JSON client.
- A transport error ends the worker; reconnect policy is a later layer.
- The Lua C ABI must match UE4SS's embedded Lua build. A DLL built against an arbitrary system Lua is not assumed compatible.
- This module has not yet been compiled against the user's installed UE4SS SDK, loaded in Hogwarts Legacy, or tested against a live HogwartsRP server.

## Next integration gate

Port OIDC join-ticket acquisition to the active Rust launcher, then add a one-time local IPC handoff that exposes the ticket only to the launched game process. Until that exists, do not call `transport.connect` from the game adapter with a manually stored or hard-coded ticket.
