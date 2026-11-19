-- Evidence-based state placeholders. Unknown remains unknown; no name-based guesses.
local Safe = require("safe")
local M = {}

local function readMovementMode(movement)
    if not Safe.isValidObject(movement) then return "UNKNOWN" end
    local value = Safe.safeProperty(movement, "MovementMode")
    if value == nil then return "UNKNOWN" end
    return tostring(value)
end

function M.sample(player, movement)
    local velocity = Safe.safeMethod(player, "GetVelocity")
    local speed = nil
    if velocity ~= nil then
        local ok, magnitude = pcall(function()
            if type(velocity.Size) == "function" then return velocity:Size() end
            return nil
        end)
        if ok then speed = magnitude end
    end

    return {
        locomotion = {
            movement_mode = readMovementMode(movement),
            speed = speed or "UNAVAILABLE",
            classification = "UNCLASSIFIED",
            note = "Thresholds require in-game baseline samples before Idle/Walking/Running/Sprinting labels are assigned."
        },
        traversal = "UNKNOWN",
        mounts = "UNKNOWN",
        combat = "UNKNOWN",
        social = "UNKNOWN"
    }
end

return M
