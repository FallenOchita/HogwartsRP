# Lua scripting

Put server Lua files in a resource directory and list them with `server_script` in `fxmanifest.lua`. Each file receives its own restricted MoonSharp state in this prototype. Available API registration includes `RegisterCommand`, `RegisterNetEvent`, `AddEventHandler`, `TriggerEvent`, `TriggerClientEvent`, `TriggerServerEvent`, and `GetPlayerName`. The network calls explicitly fail because no network adapter is installed. Client execution, Wait scheduling, character/house APIs, and persistence are not implemented yet.

MoonSharp runs a Lua 5.2 dialect, not Lua 5.4. Avoid presenting this runtime as compatible with FiveM Lua 5.4 resources. There is no unrestricted OS, filesystem, process, or network library exposed; in-process execution quotas are not yet enforced.
