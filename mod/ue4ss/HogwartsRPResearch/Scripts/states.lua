-- Evidence-based state placeholders. Unknown remains unknown; no name-based guesses.
-- This module consumes property-audit records only; it does not invoke gameplay functions.
local M = {}

local function findAudit(audit, target, property)
    for _, item in ipairs((audit and audit.items) or {}) do
        if item.target == target and item.property == property then return item end
    end
    return nil
end

function M.sample(player, movement, audit)
    local movementMode = findAudit(audit, "movement", "MovementMode")
        or findAudit(audit, "player", "MovementMode")
    local velocity = findAudit(audit, "movement", "Velocity")
        or findAudit(audit, "player", "Velocity")

    local modeValue = "UNAVAILABLE"
    if movementMode and movementMode.status == "PRIMITIVE_VALUE_READ" then
        modeValue = movementMode.summary
    end

    local speedValue = "UNAVAILABLE"
    if velocity and velocity.status == "PRIMITIVE_VALUE_READ" then
        speedValue = "RAW_VALUE:" .. velocity.summary
    end

    return {
        locomotion = {
            movement_mode = modeValue,
            movement_mode_read_status = movementMode and movementMode.status or "NOT_PROBED",
            velocity_read_status = velocity and velocity.status or "NOT_PROBED",
            speed = speedValue,
            classification = "UNCLASSIFIED",
            note = "No speed or locomotion label is inferred from opaque values. Baseline samples and verified decoding are still required."
        },
        traversal = "UNKNOWN",
        mounts = "UNKNOWN",
        combat = "UNKNOWN",
        social = "UNKNOWN"
    }
end

return M
