# Resource manifests

`fxmanifest.lua` is parsed as data, never executed as Lua. One declaration per line is accepted: `fx_version 'cerulean'`, `game 'hogwartslegacy'`, scalar `name`, `author`, `description`, `version`, and repeated single-quoted `server_script`, `client_script`, `shared_script`, `dependency`, or `dependencies`. Client scripts are discovered but not executed by this server host. Inline Lua expressions, lists, variables, and function calls are rejected.

Dependencies are validated before start and started in deterministic name order subject to dependency order. Cycles and missing dependencies fail startup.
