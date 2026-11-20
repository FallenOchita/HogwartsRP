-- HogwartsRP UE4SS player-state adapter entry point
-- Hogwarts Legacy / Steam PC / UE4SS 4.0.0 / Lua 5.4
--
-- Read-only diagnostics. This entry point does not write game state,
-- spawn actors, create remote players, or connect to the server.

local MOD_NAME = "HogwartsRPAdapter"
local MOD_VERSION = "0.3.0"
local POLL_INTERVAL_MS = 3000

local Adapter = require("adapter")

_G.HogwartsLegacyAdapter = Adapter

local function log(message)
    print("[" .. MOD_NAME .. "] " .. tostring(message) .. "\n")
end

local function formatVector(value)
    if type(value) ~= "table" then return "unavailable" end
    if type(value.x) ~= "number" or
       type(value.y) ~= "number" or
       type(value.z) ~= "number" then
        return "unavailable"
    end
    return string.format(
        "X=%.2f Y=%.2f Z=%.2f",
        value.x, value.y, value.z
    )
end

local function formatRotator(value)
    if type(value) ~= "table" then return "unavailable" end
    if type(value.pitch) ~= "number" or
       type(value.yaw) ~= "number" or
       type(value.roll) ~= "number" then
        return "unavailable"
    end
    return string.format(
        "Pitch=%.2f Yaw=%.2f Roll=%.2f",
        value.pitch, value.yaw, value.roll
    )
end

local function formatNumber(value)
    if type(value) ~= "number" then return "unavailable" end
    return string.format("%.2f", value)
end

local pollQueued = false

-- Keep a stable callback reference instead of constructing a new game-thread
-- closure on every asynchronous LoopAsync tick. The guard also prevents this
-- mod from queuing another poll while its previous poll is still pending.
local function pollOnGameThread()
    pollQueued = false

    local ok, snapshotOrError = pcall(function()
        return Adapter:getSnapshot()
    end)

    if not ok then
        log("Snapshot failed safely: " .. tostring(snapshotOrError))
        return
    end

    local snapshot = snapshotOrError
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
    log("Location: " .. formatVector(snapshot.location))
    log("Rotation: " .. formatRotator(snapshot.rotation))
    log("Control rotation: " .. formatRotator(snapshot.controlRotation))
    log("Velocity: " .. formatVector(snapshot.velocity))
    log("Speed: " .. formatNumber(snapshot.speed))
    log("Movement mode: " .. tostring(snapshot.movementMode))
end

local function asyncPollCallback()
    if pollQueued then return end
    if type(ExecuteInGameThread) ~= "function" then
        log("ExecuteInGameThread is unavailable.")
        return
    end

    pollQueued = true
    local ok, err = pcall(ExecuteInGameThread, pollOnGameThread)
    if not ok then
        pollQueued = false
        log("Could not queue game-thread snapshot: " .. tostring(err))
    end
end

log("Loaded. Adapter version: " .. MOD_VERSION)
log("Read-only telemetry mode enabled.")

if type(LoopAsync) == "function" and
   type(ExecuteInGameThread) == "function" then
    -- asyncPollCallback and pollOnGameThread remain strongly referenced
    -- for the lifetime of this Lua module.
    LoopAsync(POLL_INTERVAL_MS, asyncPollCallback)
    log("Snapshot polling scheduled every " .. POLL_INTERVAL_MS .. " ms.")
else
    log("Polling APIs unavailable; adapter loaded without automatic diagnostics.")
end
