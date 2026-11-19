-- Evidence-based telemetry source registry.
-- Only the APIs listed as confirmed are used by the generated adapter.
local Safe = require("safe")
local M = {}

M.CONFIRMED = {
    position = { object = "BP_Biped_Player_C", method = "GetActorLocation" },
    rotation = { object = "BP_Biped_Player_C", method = "GetActorRotation" },
    camera_rotation = { object = "BP_Phoenix_Player_Controller_C", method = "GetControlRotation" },
    velocity = { object = "BP_Biped_Player_C", method = "GetVelocity" },
    movement_mode = { object = "Biped_MovementComponent", property = "MovementMode" }
}
M.UNVERIFIED = {
    health = { candidates = { "Health", "CurrentHealth" }, status = "NOT_VERIFIED" },
    mana = { candidates = { "Mana", "CurrentMana" }, status = "NOT_VERIFIED" },
    equipment = { candidates = {}, status = "NOT_DISCOVERED" },
    spells = { candidates = {}, status = "NOT_DISCOVERED" }
}

local function methodAvailable(object, methodName)
    if not Safe.isValidObject(object) then return false end
    local ok, value = pcall(function() return object[methodName] end)
    return ok and type(value) == "function"
end

function M.audit(player, controller, movement)
    local objects = {
        BP_Biped_Player_C = player,
        BP_Phoenix_Player_Controller_C = controller,
        Biped_MovementComponent = movement
    }
    local result = { confirmed = {}, unverified = M.UNVERIFIED }
    for key, source in pairs(M.CONFIRMED) do
        local object = objects[source.object]
        local available = false
        if source.method then available = methodAvailable(object, source.method)
        elseif source.property then available = Safe.safeProperty(object, source.property) ~= nil end
        result.confirmed[key] = {
            source = source.object, member = source.method or source.property,
            status = available and "AVAILABLE" or "UNAVAILABLE"
        }
    end
    return result
end

return M
