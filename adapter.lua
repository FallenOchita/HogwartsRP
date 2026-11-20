-- HogwartsRP UE4SS player-state adapter module
-- Hogwarts Legacy / Steam PC / UE4SS 4.0.0 / Lua 5.4
--
-- Read-only telemetry adapter. Discovers the local player and reads
-- candidate state. It does not write game state, spawn actors, create
-- remote players, or connect to the HogwartsRP server.
--
-- UE4SS telemetry binding behavior (Hogwarts Legacy, UE4SS 4.0.0) — VERIFIED:
--
--   GetVelocity() returns a plain Lua table {x=..., y=..., z=...} with
--   lowercase numeric keys — no wrapping at all. Components are direct numbers.
--
--   GetActorLocation() and GetActorRotation() return nil when called directly.
--   Use K2_GetActorLocation() and K2_GetActorRotation() (BlueprintCallable
--   versions exposed by UE4SS) instead.
--
--   GetControlRotation() returns numeric Pitch/Yaw/Roll directly (already
--   working, no wrapping).
--
--   extractNumber() tries both uppercase ("X","Y","Z") and lowercase
--   ("x","y","z") component keys to handle both plain tables and wrapped
--   userdata values.
--
--   For speed, FVector:Size() is tried first, then manual calculation from
--   components (sqrt(x^2 + y^2 + z^2)).

local Adapter = {}

----------------------------------------------------------------------
-- Safe reflection helpers
----------------------------------------------------------------------

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

----------------------------------------------------------------------
-- Numeric extraction from UE4SS-wrapped FVector / FRotator values
----------------------------------------------------------------------

-- Attempt to extract a single numeric component from a wrapped vector/rotator
-- value. Tries multiple strategies because UE4SS 4.0.0 on Hogwarts Legacy
-- wraps FVector components in additional userdata layers.
--
-- Hogwarts Legacy UE4SS 4.0.0 actual behavior (verified live):
--   GetVelocity() returns a plain Lua table {x=..., y=..., z=...} with
--   lowercase numeric keys — no wrapping at all.
--   GetActorLocation() / GetActorRotation() return nil via direct call;
--   K2_GetActorLocation() / K2_GetActorRotation() must be used instead.
local function extractNumber(value, componentName)
    if value == nil then
        return nil
    end

    -- Strategy 1: direct property access. Try both uppercase ("X", "Y", "Z")
    -- and lowercase ("x", "y", "z") because GetVelocity() returns a plain
    -- Lua table with lowercase keys.
    local candidates = { componentName }
    local lower = componentName:lower()
    if lower ~= componentName then
        table.insert(candidates, lower)
    end

    for _, cand in ipairs(candidates) do
        local direct = safeProperty(value, cand)
        if type(direct) == "number" then
            return direct
        end
        local asNumber = numberOrNil(direct)
        if asNumber ~= nil then
            return asNumber
        end
    end

    -- Strategy 2: GetPropertyValue method (available on UObject wrappers)
    local getterOk, getter = pcall(function()
        return value.GetPropertyValue
    end)
    if getterOk and type(getter) == "function" then
        for _, cand in ipairs(candidates) do
            local got, result = pcall(function()
                return getter(value, cand)
            end)
            if got then
                local asNumber = numberOrNil(result)
                if asNumber ~= nil then
                    return asNumber
                end
                if type(result) == "number" then
                    return result
                end
            end
        end
    end

    -- Strategy 3: try tonumber() on the raw value itself
    local asNumber = numberOrNil(value)
    if asNumber ~= nil then
        return asNumber
    end

    return nil
end

-- Extract X, Y, Z numeric components from a UE4SS-wrapped FVector value.
-- Returns a table {x, y, z} with numeric values, or nil if extraction fails.
local function extractVectorComponents(vector)
    if not isValidObject(vector) then
        return nil
    end

    local x = extractNumber(vector, "X")
    local y = extractNumber(vector, "Y")
    local z = extractNumber(vector, "Z")

    if x == nil or y == nil or z == nil then
        return nil
    end

    return { x = x, y = y, z = z }
end

-- Extract numeric speed from a UE4SS-wrapped FVector velocity value.
-- Tries FVector:Size() first (method call on vector userdata), then falls
-- back to manual calculation from components.
local function extractSpeed(velocity)
    if not isValidObject(velocity) then
        return nil
    end

    -- Strategy 1: FVector:Size() method call
    local sizeOk, sizeResult = pcall(function()
        local fn = velocity.Size
        if type(fn) == "function" then
            return fn(velocity)
        end
        return nil
    end)
    if sizeOk then
        local asNumber = numberOrNil(sizeResult)
        if asNumber ~= nil then
            return asNumber
        end
    end

    -- Strategy 2: manual calculation from components
    local comps = extractVectorComponents(velocity)
    if comps then
        return math.sqrt(comps.x * comps.x + comps.y * comps.y + comps.z * comps.z)
    end

    return nil
end

----------------------------------------------------------------------
-- Adapter API
----------------------------------------------------------------------

function Adapter:getPlayer()
    local pawn = safeProperty(self._controller, "AcknowledgedPawn")
    if isValidObject(pawn) then
        return pawn
    end
    pawn = safeProperty(self._controller, "Pawn")
    if isValidObject(pawn) then
        return pawn
    end
    return nil
end

function Adapter:getController()
    if type(FindAllOf) ~= "function" then
        return nil
    end

    local ok, controllers = pcall(FindAllOf, "BP_Phoenix_Player_Controller_C")
    if not ok or type(controllers) ~= "table" then
        return nil
    end

    for _, controller in ipairs(controllers) do
        if isValidObject(controller) then
            local isLocal = safeMethod(controller, "IsLocalController")
            if isLocal == true then
                self._controller = controller
                return controller
            end
        end
    end

    return nil
end

function Adapter:getMovementComponent()
    local pawn = self:getPlayer()
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

-- Try a list of method names on an object, returning the first non-nil result.
local function safeMethodAny(obj, methodNames, ...)
    if not isValidObject(obj) then
        return nil
    end
    for _, name in ipairs(methodNames) do
        local ok, result = pcall(function(...)
            local fn = obj[name]
            if fn == nil then
                return nil
            end
            return fn(obj, ...)
        end, ...)
        if ok and result ~= nil then
            return result
        end
    end
    return nil
end

function Adapter:getLocation()
    local pawn = self:getPlayer()
    if not pawn then
        return nil
    end

    -- K2_GetActorLocation is the BlueprintCallable version exposed by UE4SS.
    -- Direct GetActorLocation returns nil on this build.
    local location = safeMethodAny(pawn, { "K2_GetActorLocation", "GetActorLocation" })
    if not isValidObject(location) then
        return nil
    end

    local comps = extractVectorComponents(location)
    if not comps then
        return nil
    end

    return comps
end

function Adapter:getRotation()
    local pawn = self:getPlayer()
    if not pawn then
        return nil
    end

    -- K2_GetActorRotation is the BlueprintCallable version exposed by UE4SS.
    -- Direct GetActorRotation returns nil on this build.
    local rotation = safeMethodAny(pawn, { "K2_GetActorRotation", "GetActorRotation" })
    if not isValidObject(rotation) then
        return nil
    end

    local pitch = extractNumber(rotation, "Pitch")
    local yaw = extractNumber(rotation, "Yaw")
    local roll = extractNumber(rotation, "Roll")

    if pitch == nil or yaw == nil or roll == nil then
        return nil
    end

    return { pitch = pitch, yaw = yaw, roll = roll }
end

function Adapter:getControlRotation()
    local controller = self:getController()
    if not controller then
        return nil
    end

    local rotation = safeMethod(controller, "GetControlRotation")
    if not isValidObject(rotation) then
        return nil
    end

    local pitch = numberOrNil(safeProperty(rotation, "Pitch"))
    local yaw = numberOrNil(safeProperty(rotation, "Yaw"))
    local roll = numberOrNil(safeProperty(rotation, "Roll"))

    if pitch == nil or yaw == nil or roll == nil then
        return nil
    end

    return { pitch = pitch, yaw = yaw, roll = roll }
end

function Adapter:getVelocity()
    local pawn = self:getPlayer()
    if not pawn then
        return nil
    end

    local velocity = safeMethod(pawn, "GetVelocity")
    if not isValidObject(velocity) then
        return nil
    end

    local comps = extractVectorComponents(velocity)
    if not comps then
        return nil
    end

    return comps
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

function Adapter:getSpeed()
    local velocity = self:getVelocity()
    if not velocity then
        return nil
    end

    return extractSpeed(velocity)
end

function Adapter:getMovementState()
    return "UNKNOWN"
end

----------------------------------------------------------------------
-- Snapshot (diagnostic helper)
----------------------------------------------------------------------

function Adapter:getSnapshot()
    return {
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