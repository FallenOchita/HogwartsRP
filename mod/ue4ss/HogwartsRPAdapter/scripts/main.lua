-- HogwartsRP UE4SS player-state adapter
-- Hogwarts Legacy / Steam PC / UE4SS 4.0.0
--
-- Read-only first-stage adapter. It discovers the local player and reads
-- candidate state for diagnostics. It does not write game state, spawn
-- actors, create remote players, or connect to the HogwartsRP server.
--
-- Confirmed in the user's live test:
--   Controller class: BP_Phoenix_Player_Controller_C
--   Pawn class:       BP_Biped_Player_C
--   Position:         Pawn:GetActorLocation()
--   Camera facing:    Controller:GetControlRotation()
--
-- Other movement, jump, sprint, and animation members are exploratory
-- candidates and must be verified against the installed game build.

local MOD_NAME = "HogwartsRPAdapter"
local MOD_VERSION = "0.1.0"
local POLL_INTERVAL_MS = 3000

local Adapter = {}

---------------------------------------------------------------------
-- Safe reflection helpers
---------------------------------------------------------------------

local function isValidObject(obj)
    if obj == nil then
        return false
    end

    local ok, value = pcall(tostring, obj)
    return ok and value ~= "nil"
end

local function safeProperty(obj, property)
    if not isValidObject(obj) then
        return nil
    end

    local ok, result = pcall(function()
        return obj[property]
    end)

    if ok then
        return result
    end

    return nil
end

local function safeMethod(obj, methodName, ...)
    if not isValidObject(obj) then
        return nil
    end

    local ok, result = pcall(function(...)
        local fn = obj[methodName]
        if fn == nil then
            return nil
        end
        return fn(obj, ...)
    end, ...)

    if ok then
        return result
    end

    return nil
end

local function numberOrNil(value)
    if value == nil then
        return nil
    end
    return tonumber(value)
end

local function log(message)
    print("[" .. MOD_NAME .. "] " .. tostring(message) .. "\n")
end

local function formatVector(value)
    if not isValidObject(value) then
        return "unavailable"
    end

    local x = numberOrNil(safeProperty(value, "X"))
    local y = numberOrNil(safeProperty(value, "Y"))
    local z = numberOrNil(safeProperty(value, "Z"))

    if x == nil or y == nil or z == nil then
        return tostring(value)
    end

    return string.format("X=%.2f Y=%.2f Z=%.2f", x, y, z)
end

local function formatRotator(value)
    if not isValidObject(value) then
        return "unavailable"
    end

    local pitch = numberOrNil(safeProperty(value, "Pitch"))
    local yaw = numberOrNil(safeProperty(value, "Yaw"))
    local roll = numberOrNil(safeProperty(value, "Roll"))

    if pitch == nil or yaw == nil or roll == nil then
        return tostring(value)
    end

    return string.format(
        "Pitch=%.2f Yaw=%.2f Roll=%.2f",
        pitch, yaw, roll
    )
end

local function movementModeName(mode)
    local names = {
        [0] = "None",
        [1] = "Walking",
        [2] = "NavWalking",
        [3] = "Falling",
        [4] = "Swimming",
        [5] = "Flying",
        [6] = "Custom"
    }

    local numericMode = numberOrNil(mode)
    if numericMode == nil then
        return "Unknown"
    end

    return names[numericMode] or ("Unknown (" .. tostring(numericMode) .. ")")
end

---------------------------------------------------------------------
-- Local controller and pawn
---------------------------------------------------------------------

function Adapter:getController()
    if type(FindAllOf) ~= "function" then
        return nil
    end

    local ok, controllers = pcall(
        FindAllOf,
        "BP_Phoenix_Player_Controller_C"
    )

    if not ok or type(controllers) ~= "table" then
        return nil
    end

    for _, controller in ipairs(controllers) do
        if isValidObject(controller) then
            local isLocal = safeMethod(
                controller,
                "IsLocalController"
            )

            if isLocal == true then
                return controller
            end
        end
    end

    -- Fail closed: do not accidentally select a remote/unknown controller.
    return nil
end

function Adapter:getPawn()
    local controller = self:getController()
    if not controller then
        return nil
    end

    local pawn = safeProperty(controller, "AcknowledgedPawn")
    if isValidObject(pawn) then
        return pawn
    end

    return nil
end

---------------------------------------------------------------------
-- Confirmed transform reads
---------------------------------------------------------------------

function Adapter:getLocation()
    return safeMethod(self:getPawn(), "GetActorLocation")
end

function Adapter:getActorRotation()
    return safeMethod(self:getPawn(), "GetActorRotation")
end

function Adapter:getControlRotation()
    return safeMethod(self:getController(), "GetControlRotation")
end

-- The user's in-game tests showed control rotation tracks camera yaw,
-- while actor rotation did not. Use this as the current facing candidate.
function Adapter:getFacing()
    return self:getControlRotation()
end

---------------------------------------------------------------------
-- Velocity and movement component
---------------------------------------------------------------------

function Adapter:getVelocity()
    return safeMethod(self:getPawn(), "GetVelocity")
end

function Adapter:getMovementComponent()
    local pawn = self:getPawn()
    if not pawn then
        return nil
    end

    local movement = safeMethod(pawn, "GetCharacterMovement")
    if isValidObject(movement) then
        return movement
    end

    movement = safeMethod(pawn, "GetMovementComponent")
    if isValidObject(movement) then
        return movement
    end

    movement = safeProperty(pawn, "CharacterMovement")
    if isValidObject(movement) then
        return movement
    end

    movement = safeProperty(pawn, "MovementComponent")
    if isValidObject(movement) then
        return movement
    end

    return nil
end

function Adapter:getMovementMode()
    local movement = self:getMovementComponent()
    if not movement then
        return nil
    end

    local mode = safeMethod(movement, "GetMovementMode")
    if mode == nil then
        mode = safeProperty(movement, "MovementMode")
    end

    return mode
end

function Adapter:getMovementModeName()
    return movementModeName(self:getMovementMode())
end

local function getSpeedFromVelocity(velocity, includeZ)
    if not isValidObject(velocity) then
        return nil
    end

    local x = numberOrNil(safeProperty(velocity, "X"))
    local y = numberOrNil(safeProperty(velocity, "Y"))
    local z = numberOrNil(safeProperty(velocity, "Z"))

    if x == nil or y == nil or z == nil then
        return nil
    end

    if includeZ then
        return math.sqrt(x * x + y * y + z * z)
    end

    return math.sqrt(x * x + y * y)
end

function Adapter:getSpeed()
    return getSpeedFromVelocity(self:getVelocity(), true)
end

function Adapter:getHorizontalSpeed()
    return getSpeedFromVelocity(self:getVelocity(), false)
end

---------------------------------------------------------------------
-- Candidate jump and sprint reads (not yet game-verified)
---------------------------------------------------------------------

function Adapter:getJumpState()
    local pawn = self:getPawn()
    local movement = self:getMovementComponent()

    if not pawn then
        return nil
    end

    return {
        canJump = safeMethod(pawn, "CanJump"),
        isJumpProvidingForce = safeMethod(
            movement,
            "IsJumpProvidingForce"
        ),
        currentJumpCount = safeProperty(
            movement,
            "JumpCurrentCount"
        ),
        isFalling = safeMethod(movement, "IsFalling"),
        movementMode = self:getMovementMode()
    }
end

function Adapter:getSprintState()
    local pawn = self:getPawn()
    local movement = self:getMovementComponent()

    if not pawn then
        return nil
    end

    return {
        isSprinting = safeMethod(pawn, "IsSprinting"),
        isRunning = safeMethod(pawn, "IsRunning"),
        wantsToSprint = safeProperty(movement, "bWantsToSprint"),
        wantsToRun = safeProperty(movement, "bWantsToRun"),
        maxWalkSpeed = safeProperty(movement, "MaxWalkSpeed")
    }
end

---------------------------------------------------------------------
-- Candidate animation reads (not yet game-verified)
---------------------------------------------------------------------

function Adapter:getMesh()
    local pawn = self:getPawn()
    if not pawn then
        return nil
    end

    local mesh = safeMethod(pawn, "GetMesh")
    if isValidObject(mesh) then
        return mesh
    end

    mesh = safeProperty(pawn, "Mesh")
    if isValidObject(mesh) then
        return mesh
    end

    return nil
end

function Adapter:getAnimInstance()
    return safeMethod(self:getMesh(), "GetAnimInstance")
end

function Adapter:getAnimationState()
    local anim = self:getAnimInstance()
    if not isValidObject(anim) then
        return nil
    end

    return {
        isMoving = safeProperty(anim, "bIsMoving"),
        isInAir = safeProperty(anim, "bIsInAir"),
        isJumping = safeProperty(anim, "bIsJumping"),
        isFalling = safeProperty(anim, "bIsFalling"),
        isSprinting = safeProperty(anim, "bIsSprinting"),
        speed = safeProperty(anim, "Speed"),
        groundSpeed = safeProperty(anim, "GroundSpeed"),
        direction = safeProperty(anim, "Direction"),
        movementSpeed = safeProperty(anim, "MovementSpeed"),
        acceleration = safeProperty(anim, "Acceleration"),
        isCrouching = safeProperty(anim, "bIsCrouching")
    }
end

---------------------------------------------------------------------
-- Conservative movement classification
---------------------------------------------------------------------

function Adapter:getMovementState()
    local mode = self:getMovementMode()
    local speed = self:getHorizontalSpeed()

    if numberOrNil(mode) == 3 then
        return "Airborne"
    end

    if speed == nil then
        return "Unknown"
    end

    if speed < 5 then
        return "Idle"
    end

    -- Speed alone cannot distinguish walking from running reliably.
    return "Moving"
end

---------------------------------------------------------------------
-- Snapshot
---------------------------------------------------------------------

function Adapter:getSnapshot()
    local velocity = self:getVelocity()
    local movementMode = self:getMovementMode()

    return {
        location = self:getLocation(),
        actorRotation = self:getActorRotation(),
        facing = self:getFacing(),
        controlRotation = self:getControlRotation(),
        velocity = velocity,
        speed = getSpeedFromVelocity(velocity, true),
        horizontalSpeed = getSpeedFromVelocity(velocity, false),
        movementMode = movementMode,
        movementModeName = movementModeName(movementMode),
        movementState = self:getMovementState(),
        jump = self:getJumpState(),
        sprint = self:getSprintState(),
        animation = self:getAnimationState()
    }
end

function Adapter:dump()
    local snapshot = self:getSnapshot()

    log("Movement state: " .. tostring(snapshot.movementState))
    log("Position: " .. formatVector(snapshot.location))
    log("Facing/control rotation: " .. formatRotator(snapshot.facing))
    log("Actor rotation (diagnostic): " .. formatRotator(snapshot.actorRotation))
    log("Velocity: " .. formatVector(snapshot.velocity))
    log("Speed: " .. tostring(snapshot.speed))
    log("Horizontal speed: " .. tostring(snapshot.horizontalSpeed))
    log("Movement mode: " .. tostring(snapshot.movementModeName))
end

---------------------------------------------------------------------
-- UE4SS entry point
---------------------------------------------------------------------

_G.HogwartsLegacyAdapter = Adapter

log("Loaded. Adapter version: " .. MOD_VERSION)
log("Read-only mode; no game state is modified.")
log("Confirmed reads: local pawn location and controller control rotation.")
log("Movement component, jump, sprint, and animation fields remain exploratory.")

if type(LoopAsync) == "function" and type(ExecuteInGameThread) == "function" then
    LoopAsync(POLL_INTERVAL_MS, function()
        ExecuteInGameThread(function()
            local ok, err = pcall(function()
                local controller = Adapter:getController()
                local pawn = Adapter:getPawn()

                if controller and pawn then
                    Adapter:dump()
                else
                    log("Waiting for a confirmed local controller and acknowledged pawn.")
                end
            end)

            if not ok then
                log("Diagnostic pass failed safely: " .. tostring(err))
            end
        end)
    end)
else
    log("Polling APIs unavailable; adapter loaded without automatic diagnostics.")
end
