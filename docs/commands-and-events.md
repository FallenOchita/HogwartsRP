# Commands and events

The console accepts `status`, `quit`, `issue-ticket`, and commands registered from Lua. `issue-ticket <account-id> <display name>` prints a single-use credential with a two-minute lifetime for protocol development. It is an operator-only bootstrap command and does not replace a trusted account identity provider.

The server listens at `http://127.0.0.1:7777` by default. The health endpoint is `/health`; WebSocket clients connect to `/connect`. Remote listeners must use HTTPS/WSS. A client sends `{"type":"authenticate","credential":"..."}` first and receives a `connected` message containing the server-created player ID. It may then send `{"type":"event","name":"resource:event","arguments":[...]}`. The server sends client events with the same event envelope and the assigned `playerId`.

Lua resources must call `RegisterNetEvent(name)` before a client can trigger that event. Matching handlers registered with `AddEventHandler` receive the server-issued session ID and authenticated display name as their first two arguments, followed by client-supplied values. The server routes the event only to resources that registered it. Client events are untrusted input: validate gameplay rules and permissions in the resource handler. Network values are limited to 32 primitive JSON arguments, strings up to 8192 characters, and 32 KiB per WebSocket message.
