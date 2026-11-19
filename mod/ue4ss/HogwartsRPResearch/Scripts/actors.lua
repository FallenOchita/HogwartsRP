-- Actor discovery registry. Only explicitly configured class names are queried.
-- No global UObject iteration and no gameplay mutation.
local Safe = require("safe")
local M = {}

M.DEFAULT_CLASSES = {
    player = { "BP_Biped_Player_C" },
    controller = { "BP_Phoenix_Player_Controller_C" },
    movement = { "Biped_MovementComponent" },
    game_instance = { "BP_PhoenixGameInstance_C" },
    npc = {}, student = {}, vendor = {}, companion = {},
    quest_actor = {}, interactive = {}
}

local function parentClassName(object)
    local class = Safe.safeMethod(object, "GetClass")
    if not Safe.isValidObject(class) then return "UNAVAILABLE" end
    local parent = Safe.safeMethod(class, "GetSuperStruct")
    if not Safe.isValidObject(parent) then return "UNAVAILABLE" end
    return Safe.safeFullName(parent)
end

local function record(object, category, sourceClass)
    local details = Safe.describeObject(object)
    details.category = category
    details.source_class = sourceClass
    details.parent_class = parentClassName(object)
    return details
end

function M.scan(registry)
    registry = registry or M.DEFAULT_CLASSES
    local result = { status = "COMPLETED_WITH_LIMITATIONS", categories = {}, totals = { found = 0, unavailable = 0 } }
    for category, classNames in pairs(registry) do
        local categoryResult = { status = "NO_CLASSES_CONFIGURED", classes = {} }
        if type(classNames) == "table" and #classNames > 0 then
            categoryResult.status = "SCANNED"
            for _, className in ipairs(classNames) do
                local objects, err = Safe.findAll(className)
                local classResult = { class_name = className, status = err and "UNAVAILABLE" or "SCANNED", items = {}, error = err }
                if err then
                    result.totals.unavailable = result.totals.unavailable + 1
                else
                    for _, object in ipairs(objects) do
                        classResult.items[#classResult.items + 1] = record(object, category, className)
                        result.totals.found = result.totals.found + 1
                    end
                end
                categoryResult.classes[#categoryResult.classes + 1] = classResult
            end
        end
        result.categories[category] = categoryResult
    end
    return result
end

return M
