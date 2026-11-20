-- HogwartsRP UE4SS player-state adapter module
-- Hogwarts Legacy / Steam PC / UE4SS 4.0.0 / Lua 5.4
--
-- Read-only telemetry adapter. Discovers the local player and reads
-- candidate state. It does not write game state, spawn actors, create
-- remote players, or connect to the HogwartsRP server.

local Adapter = {}

----------------------------------------------------------------------
-- Safe reflection helpers
----------------------------------------------------------------------

local function isValidObject(obj)
    if obj == nil then return false end
    local ok, value = pcall(tostring, obj)
    return ok and value ~= "nil"
end

local function safeProperty(obj, property)
    if not isValidObject(obj) then return nil end
    local ok, result = pcall(function() return obj[property] end)
    if ok then return result end
    return nil
end

local function safeMethod(obj, methodName, ...)
    if not isValidObject(obj) then return nil end
    local ok, result = pcall(function(...)
        local fn = obj[methodName]
        if type(fn) ~= "function" then return nil end
        return fn(obj, ...)
    end, ...)
    if ok then return result end
    return nil
end

local function numberOrNil(value)
    if value == nil then return nil end
    return tonumber(value)
end

----------------------------------------------------------------------
-- Numeric extraction from UE4SS FVector / FRotator values
----------------------------------------------------------------------

local function extractNumber(value, componentName)
    if value == nil then return nil end

    local candidates = { componentName }
    local lower = componentName:lower()
    if lower ~= componentName then
        table.insert(candidates, lower)
    end

    for _, candidate in ipairs(candidates) do
        local direct = safeProperty(value, candidate)
        if type(direct) == "number" then return direct end
        local converted = numberOrNil(direct)
        if converted ~= nil then return converted end
    end

    local getterOk, getter = pcall(function() return value.GetPropertyValue end)
    if getterOk and type(getter) == "function" then
        for _, candidate in ipairs(candidates) do
            local got, result = pcall(function()
                return getter(value, candidate)
            end)
            if got then
                local converted = numberOrNil(result)
                if converted ~= nil then return converted end
                if type(result) == "number" then return result end
            end
        end
    end

    return numberOrNil(value)
end

local function extractVectorComponents(vector)
    if not isValidObject(vector) then return nil end

    local x = extractNumber(vector, "X")
    local y = extractNumber(vector, "Y")
    local z = extractNumber(vector, "Z")

    if x == nil or y == nil or z == nil then return nil end
    return { x = x, y = y, z = z }
end

local function extractSpeed(velocity)
    if not isValidObject(velocity) then return nil end

    local sizeOk, sizeResult = pcall(function()
        local fn = velocity.Size
        if type(fn) == "function" then return fn(velocity) end
        return nil
    end)
    if sizeOk then
        local converted = numberOrNil(sizeResult)
        if converted ~= nil then return converted end
    end

    local components = extractVectorComponents(velocity)
    if components then
        return math.sqrt(
            components.x * components.x +
            components.y * components.y +
            components.z * components.z
        )
    end
    return nil
end

local function safeMethodAny(obj, methodNames, ...)
    if not isValidObject(obj) then return nil end
    for _, name in ipairs(methodNames) do
        local ok, result = pcall(function(...)
            local fn = obj[name]
            if type(fn) ~= "function" then return nil end
            return fn(obj, ...)
        end, ...)
        if ok and result ~= nil then return result end
    end
    return nil
end

----------------------------------------------------------------------
-- Local controller and player
----------------------------------------------------------------------

function Adapter:getController()
    -- Reuse the last verified local controller while it remains valid.
    local cached = self._controller
    if isValidObject(cached) then
        local localCheck = safeMethod(cached, "IsLocalController")
        if localCheck == true then return cached end
    end
    self._controller = nil

    if type(FindAllOf) ~= "function" then return nil end
    local ok, controllers = pcall(
        FindAllOf, "BP_Phoenix_Player_Controller_C"
    )
    if not ok or type(controllers) ~= "table" then return nil end

    for _, controller in ipairs(controllers) do
        if isValidObject(controller) then
            if safeMethod(controller, "IsLocalController") == true then
                self._controller = controller
                return controller
            end
        end
    end
    return nil
end

function Adapter:getPlayer()
    -- Ensure controller discovery has happened even when this getter is
    -- called independently of getController().
    local controller = self._controller
    if not isValidObject(controller) or
       safeMethod(controller, "IsLocalController") ~= true then
        controller = self:getController()
    end
    if not isValidObject(controller) then return nil end

    local pawn = safeProperty(controller, "AcknowledgedPawn")
    if isValidObject(pawn) then return pawn end

    pawn = safeProperty(controller, "Pawn")
    if isValidObject(pawn) then return pawn end
    return nil
end

----------------------------------------------------------------------
-- Transform, facing, and movement telemetry
----------------------------------------------------------------------

function Adapter:getLocation()
    local player = self:getPlayer()
    if not player then return nil end
    local location = safeMethodAny(
        player, { "K2_GetActorLocation", "GetActorLocation" }
    )
    return extractVectorComponents(location)
end

function Adapter:getRotation()
    local player = self:getPlayer()
    if not player then return nil end
    local rotation = safeMethodAny(
        player, { "K2_GetActorRotation", "GetActorRotation" }
    )
    if not isValidObject(rotation) then return nil end

    local pitch = extractNumber(rotation, "Pitch")
    local yaw = extractNumber(rotation, "Yaw")
    local roll = extractNumber(rotation, "Roll")
    if pitch == nil or yaw == nil or roll == nil then return nil end
    return { pitch = pitch, yaw = yaw, roll = roll }
end

function Adapter:getControlRotation()
    local controller = self:getController()
    if not controller then return nil end
    local rotation = safeMethod(controller, "GetControlRotation")
    if not isValidObject(rotation) then return nil end

    local pitch = numberOrNil(safeProperty(rotation, "Pitch"))
    local yaw = numberOrNil(safeProperty(rotation, "Yaw"))
    local roll = numberOrNil(safeProperty(rotation, "Roll"))
    if pitch == nil or yaw == nil or roll == nil then return nil end
    return { pitch = pitch, yaw = yaw, roll = roll }
end

function Adapter:getVelocity()
    local player = self:getPlayer()
    if not player then return nil end
    return extractVectorComponents(safeMethod(player, "GetVelocity"))
end

function Adapter:getMovementComponent()
    local player = self:getPlayer()
    if not player then return nil end

    local movement = safeMethod(player, "GetCharacterMovement")
    if isValidObject(movement) then return movement end

    movement = safeMethod(player, "GetMovementComponent")
    if isValidObject(movement) then return movement end

    movement = safeProperty(player, "CharacterMovement")
    if isValidObject(movement) then return movement end

    movement = safeProperty(player, "MovementComponent")
    if isValidObject(movement) then return movement end

    return nil
end

function Adapter:getMovementMode()
    local movement = self:getMovementComponent()
    if not movement then return nil end

    local mode = safeMethod(movement, "GetMovementMode")
    if mode == nil then
        mode = safeProperty(movement, "MovementMode")
    end
    return mode
end

function Adapter:getSpeed()
    local velocity = self:getVelocity()
    if not velocity then return nil end
    return extractSpeed(velocity)
end

function Adapter:getMovementState()
    -- Deliberately unclassified until the game's enum and movement
    -- transitions have been validated against live evidence.
    return "UNKNOWN"
end

----------------------------------------------------------------------
-- Consolidated diagnostic snapshot
----------------------------------------------------------------------

function Adapter:getSnapshot()
    local controller = self:getController()
    local player = self:getPlayer()

    return {
        controllerFound = isValidObject(controller),
        playerFound = isValidObject(player),
        location = self:getLocation(),
        rotation = self:getRotation(),
        controlRotation = self:getControlRotation(),
        velocity = self:getVelocity(),
        speed = self:getSpeed(),
        movementMode = self:getMovementMode(),
        movementState = self:getMovementState(),
    }
end

return Adapter
