-- HogwartsRP UE4SS diagnostic smoke test
-- This script intentionally does not inspect or modify game objects.
-- Its only purpose is to prove that UE4SS loaded this mod and expose its version.

local MOD_NAME = "HogwartsRPDiagnostics"

local function log(message)
    print("[" .. MOD_NAME .. "] " .. tostring(message) .. "\n")
end

log("Loaded successfully.")
log("Diagnostic mode only; no gameplay objects were accessed or changed.")

if UE4SS ~= nil and type(UE4SS.GetVersion) == "function" then
    local ok, major, minor, hotfix = pcall(UE4SS.GetVersion)

    if ok then
        log(string.format(
            "UE4SS version: %s.%s.%s",
            tostring(major),
            tostring(minor),
            tostring(hotfix)
        ))
    else
        log("UE4SS.GetVersion() failed: " .. tostring(major))
    end
else
    log("UE4SS.GetVersion() is unavailable in this loader build.")
end

log("Smoke test complete. Check UE4SS.log for this message.")
