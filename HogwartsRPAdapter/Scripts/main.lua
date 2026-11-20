-- HogwartsRP UE4SS player-state adapter entry point
-- Hogwarts Legacy / Steam PC / UE4SS 4.0.0 / Lua 5.4
--
-- Read-only first-stage adapter. It discovers the local player and reads
-- candidate telemetry state. It does not write game state, spawn actors,
-- create remote players, or connect to the HogwartsRP server.
--
-- Confirmed in live testing:
--   Controller class: BP_Phoenix_Player_Controller_C
--   Pawn class:       BP_Biped_Player_C
--   Control rotation: Controller:GetControlRotation() (numeric Pitch/Yaw/Roll)
--
-- Location, rotation, and velocity are extracted from UE4SS-wrapped FVector
-- values using the numeric extraction strategies defined in adapter.lua.

local MOD_NAME = "HogwartsRPAdapter"
local MOD_VERSION = "0.2.0"
local POLL_INTERVAL_MS = 3000

local Adapter = require("adapter")

----------------------------------------------------------------------
-- Logging
----------------------------------------------------------------------

local function log(message)
    print("[" .. MOD_NAME .. "] " .. tostring(message) .. "\n")
end

local function formatLocation(loc)
    if not loc then
        return "unavailable"
    end
    return string.format("X=%.2f Y=%.2f Z=%.2f", loc.x, loc.y, loc.z)
end

local function formatRotator(rot)
    if not rot then
        return "unavailable"
    end
    return string.format("Pitch=%.2f Yaw=%.2f Roll=%.2f", rot.pitch, rot.yaw, rot.roll)
end

local function formatSpeed(speed)
    if speed == nil then
        return "unavailable"
    end
    return string.format("%.2f", speed)
end

----------------------------------------------------------------------
-- UE4SS entry point
----------------------------------------------------------------------

_G.HogwartsLegacyAdapter = Adapter

log("Loaded. Adapter version: " .. MOD_VERSION)
log("Read-only mode; no game state is modified.")

----------------------------------------------------------------------
-- Polling callbacks (named references stored in a persistent table
-- to prevent Lua GC and UE4SS C++ callback collector from freeing
-- closures that UE4SS's callback system references.)
----------------------------------------------------------------------

local CallbackRefs = {}

local function telemetryPass()
    local ok, err = pcall(function()
        local controller = Adapter:getController()
        local player = Adapter:getPlayer()

        if controller and player then
            local location = Adapter:getLocation()
            local rotation = Adapter:getRotation()
            local controlRotation = Adapter:getControlRotation()
            local velocity = Adapter:getVelocity()
            local speed = Adapter:getSpeed()
            local movementMode = Adapter:getMovementMode()
            local movementState = Adapter:getMovementState()

            log("Movement state: " .. tostring(movementState))
            log("Location: " .. formatLocation(location))
            log("Rotation: " .. formatRotator(rotation))
            log("Control rotation: " .. formatRotator(controlRotation))
            log("Velocity: " .. formatLocation(velocity))
            log("Speed: " .. formatSpeed(speed))
            log("Movement mode: " .. tostring(movementMode))
        else
            log("Waiting for a confirmed local controller and acknowledged pawn.")
        end
    end)

    if not ok then
        log("Diagnostic pass failed safely: " .. tostring(err))
    end
end

CallbackRefs.telemetryPass = telemetryPass

local function gameThreadPoll()
    ExecuteInGameThread(CallbackRefs.telemetryPass)
end
CallbackRefs.gameThreadPoll = gameThreadPoll

if type(LoopAsync) == "function" then
    LoopAsync(POLL_INTERVAL_MS, CallbackRefs.gameThreadPoll)
else
    log("Polling APIs unavailable; adapter loaded without automatic diagnostics.")
end