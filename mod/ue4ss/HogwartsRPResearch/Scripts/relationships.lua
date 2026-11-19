-- Player/controller/pawn/component relationship validation.
-- READ-ONLY: property references and object metadata only; no gameplay UFunctions.
local Safe = require("safe")
local M = {}

local function identity(object)
    if not Safe.isValidObject(object) then return nil end
    local name = Safe.safeFullName(object)
    if name == "INVALID" or name:match("^UNAVAILABLE") then return nil end
    return name
end

local function classInfo(object)
    if not Safe.isValidObject(object) then return { status = "OBJECT_NOT_FOUND" } end
    local classObject, err, callStatus = Safe.safeMethod(object, "GetClass")
    if not Safe.isValidObject(classObject) then
        local status = callStatus == "CALL_ERROR" and "GETCLASS_CALL_ERROR"
            or (classObject == nil and "GETCLASS_UNAVAILABLE" or "CLASS_OBJECT_INVALID")
        return { status = status, class_full_name = "UNAVAILABLE", detail = err }
    end
    local name = identity(classObject)
    return name and { status = "RESOLVED", class_full_name = name }
        or { status = "CLASS_FULL_NAME_UNAVAILABLE", class_full_name = "UNAVAILABLE" }
end

local function propertyReference(source, propertyName, expected)
    if not Safe.isValidObject(source) then return { status = "SOURCE_OBJECT_NOT_FOUND" } end
    local value, err = Safe.safeProperty(source, propertyName)
    if not Safe.isValidObject(value) then
        return { status = value == nil and "PROPERTY_UNAVAILABLE" or "INVALID_REFERENCE",
            reference = "UNAVAILABLE", detail = err }
    end
    local actualName, expectedName = identity(value), identity(expected)
    local status
    if not actualName then
        status = "IDENTITY_UNRESOLVED"
    elseif expected == nil then
        status = "REFERENCE_FOUND"
    elseif expectedName then
        status = actualName == expectedName and "MATCH" or "MISMATCH"
    else
        status = "EXPECTED_IDENTITY_UNAVAILABLE"
    end
    return { status = status, reference = actualName or "UNAVAILABLE",
        expected = expectedName or (expected == nil and "NOT_APPLICABLE" or "UNAVAILABLE") }
end

local function outerReference(source, expected)
    if not Safe.isValidObject(source) then return { status = "SOURCE_OBJECT_NOT_FOUND" } end
    local outer, err = Safe.safeMethod(source, "GetOuter")
    if not Safe.isValidObject(outer) then
        return { status = "OUTER_UNAVAILABLE", outer = "UNAVAILABLE", detail = err }
    end
    local actualName, expectedName = identity(outer), identity(expected)
    local status = actualName and expectedName and
        (actualName == expectedName and "MATCH" or "MISMATCH") or "IDENTITY_UNRESOLVED"
    return { status = status, outer = actualName or "UNAVAILABLE",
        expected = expectedName or "UNAVAILABLE" }
end

function M.inspect(player, controller, movement)
    local ack = propertyReference(controller, "AcknowledgedPawn", player)
    local pawn = propertyReference(controller, "Pawn", player)
    if not Safe.isValidObject(controller) then
        ack.status, pawn.status = "CONTROLLER_NOT_FOUND", "CONTROLLER_NOT_FOUND"
    end
    return {
        status = "INSPECTED",
        safety = "READ_ONLY_PROPERTY_REFERENCES_AND_OBJECT_METADATA_ONLY",
        objects = {
            player = { full_name = identity(player) or "UNAVAILABLE", class = classInfo(player),
                outer = outerReference(player, nil) },
            controller = { full_name = identity(controller) or "UNAVAILABLE", class = classInfo(controller),
                outer = outerReference(controller, nil) },
            movement = { full_name = identity(movement) or "UNAVAILABLE", class = classInfo(movement),
                outer = outerReference(movement, player) }
        },
        relationships = {
            controller_acknowledged_pawn = ack,
            controller_pawn = pawn,
            controller_player_property = propertyReference(controller, "Player", nil),
            movement_character_owner = propertyReference(movement, "CharacterOwner", player),
            movement_pawn_owner = propertyReference(movement, "PawnOwner", player),
            movement_updated_component = propertyReference(movement, "UpdatedComponent", nil),
            movement_outer_is_player = outerReference(movement, player)
        },
        owner_resolution = {
            status = "NOT_PROBED",
            note = "GetOwner was not invoked. Outer and explicit read-only reference properties are reported separately."
        }
    }
end

return M
