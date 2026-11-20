-- HogwartsRP UE4SS player-state adapter entry point
-- Hogwarts Legacy / Steam PC / UE4SS 4.0.0 / Lua 5.4
--
-- Read-only diagnostics. This entry point does not write game state,
-- spawn actors, create remote players, or connect to the server.

local MOD_NAME = "HogwartsRPAdapter"
local MOD_VERSION = "0.4.0"
local POLL_INTERVAL_MS = 3000

local Adapter = require("adapter")

_G.HogwartsLegacyAdapter = Adapter

----------------------------------------------------------------------
-- Logging and formatting
----------------------------------------------------------------------

local function log(message)
    print("[" .. MOD_NAME .. "] " .. tostring(message) .. "\n")
end

local function formatLocation(loc)
    if type(loc) ~= "table" or
       type(loc.x) ~= "number" or
       type(loc.y) ~= "number" or
       type(loc.z) ~= "number" then
        return "unavailable"
    end
    return string.format("X=%.2f Y=%.2f Z=%.2f", loc.x, loc.y, loc.z)
end

local function formatRotator(rot)
    if type(rot) ~= "table" or
       type(rot.pitch) ~= "number" or
       type(rot.yaw) ~= "number" or
       type(rot.roll) ~= "number" then
        return "unavailable"
    end
    return string.format(
        "Pitch=%.2f Yaw=%.2f Roll=%.2f",
        rot.pitch, rot.yaw, rot.roll
    )
end

local function formatSpeed(speed)
    if type(speed) ~= "number" then
        return "unavailable"
    end
    return string.format("%.2f", speed)
end

----------------------------------------------------------------------
-- Stable callback references and non-overlapping polling
----------------------------------------------------------------------

local CallbackRefs = {}
local pollQueued = false

local function telemetryPass()
    -- Keep the queue guard set for the entire diagnostic pass. Reset it
    -- after the protected work completes, including when the pass errors.
    local ok, err = pcall(function()
        local snapshot = Adapter:getSnapshot()

        if type(snapshot) ~= "table" then
            log("Snapshot unavailable.")
            return
        end

        if not snapshot.controllerFound or not snapshot.playerFound then
            log(
                "Waiting for local controller/pawn. Controller=" ..
                tostring(snapshot.controllerFound) ..
                " Pawn=" .. tostring(snapshot.playerFound)
            )
            return
        end

        log("Movement state: " .. tostring(snapshot.movementState))
        log("Location: " .. formatLocation(snapshot.location))
        log("Rotation: " .. formatRotator(snapshot.rotation))
        log("Control rotation: " .. formatRotator(snapshot.controlRotation))
        log("Velocity: " .. formatLocation(snapshot.velocity))
        log("Speed: " .. formatSpeed(snapshot.speed))
        log("Movement mode: " .. tostring(snapshot.movementMode))
    end)

    pollQueued = false

    if not ok then
        log("Diagnostic pass failed safely: " .. tostring(err))
    end
end

CallbackRefs.telemetryPass = telemetryPass

local function gameThreadPoll()
    if pollQueued then
        return
    end

    if type(ExecuteInGameThread) ~= "function" then
        log("ExecuteInGameThread is unavailable.")
        return
    end

    pollQueued = true
    local ok, err = pcall(ExecuteInGameThread, CallbackRefs.telemetryPass)
    if not ok then
        pollQueued = false
        log("Could not queue game-thread snapshot: " .. tostring(err))
    end
end

CallbackRefs.gameThreadPoll = gameThreadPoll

----------------------------------------------------------------------
-- UE4SS entry point
----------------------------------------------------------------------

log("Loaded. Adapter version: " .. MOD_VERSION)
log("Read-only mode; no game state is modified.")

if type(LoopAsync) == "function" then
    -- Retain both callback functions for the lifetime of this module.
    LoopAsync(POLL_INTERVAL_MS, CallbackRefs.gameThreadPoll)
    log("Snapshot polling scheduled every " .. POLL_INTERVAL_MS .. " ms.")
else
    log("LoopAsync unavailable; adapter loaded without automatic diagnostics.")
end
