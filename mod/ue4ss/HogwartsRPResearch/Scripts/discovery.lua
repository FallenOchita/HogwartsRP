-- Best-effort discovery. Unsupported reflection surfaces are reported, not guessed.
local Safe = require("safe")
local M = {}

function M.components(actor)
    if not Safe.isValidObject(actor) then
        return { status = "PLAYER_NOT_FOUND", items = {} }
    end

    -- GetComponents is not exposed as a Lua table in the user's UE4SS build.
    -- Probe only previously observed, explicitly allowlisted object properties.
    -- This is a candidate-reference catalog, not a complete component enumeration.
    local candidates = {
        "CharMoveComp",
        "CharacterMovement",
        "Mesh",
        "ConversationComponent",
        "ToolSetComponent",
        "AnimationArchitectComponent"
    }
    local items = {}
    for _, propertyName in ipairs(candidates) do
        local value, readError = Safe.safeProperty(actor, propertyName)
        local item = {
            property = propertyName,
            status = "UNRESOLVED",
            full_path = "UNAVAILABLE",
            class = "UNAVAILABLE"
        }
        if Safe.isValidObject(value) then
            local fullName = Safe.safeFullName(value)
            if fullName ~= "INVALID" and not fullName:match("^UNAVAILABLE") then
                item.status = "REFERENCE_FOUND"
                item.full_path = fullName
                item.class = Safe.safeClassName(value)
            else
                item.status = "VALUE_NOT_IDENTIFIED_AS_OBJECT"
            end
        elseif value == nil then
            item.status = "PROPERTY_UNAVAILABLE"
            item.detail = readError
        else
            item.status = "INVALID_REFERENCE"
        end
        items[#items + 1] = item
    end

    return {
        status = "UNAVAILABLE",
        discovery_mode = "ALLOWLISTED_PROPERTY_REFERENCE_PROBES",
        reason = "GetComponents is not exposed as a Lua table by this UE4SS build; listed entries are individual property candidates, not a complete component list.",
        items = items,
        truncated = false
    }
end

function M.properties(object, names)
    local result = {}
    if not Safe.isValidObject(object) then return { status = "OBJECT_NOT_FOUND", items = result } end
    for _, name in ipairs(names or {}) do
        local value, err = Safe.safeProperty(object, name)
        result[#result + 1] = {
            name = name,
            property_type = type(value),
            value = value == nil and "UNAVAILABLE" or tostring(value),
            detail = err
        }
    end
    return { status = "ALLOWLIST_ONLY", items = result }
end

function M.functions(object, candidates)
    local result = {}
    if not Safe.isValidObject(object) then return { status = "OBJECT_NOT_FOUND", items = result } end
    for _, name in ipairs(candidates or {}) do
        local value = Safe.safeProperty(object, name)
        result[#result + 1] = {
            name = name,
            exposed_as_callable = type(value) == "function",
            parameter_count = "UNAVAILABLE",
            return_type = "UNAVAILABLE",
            note = "Presence probe only; no function invoked"
        }
    end
    return {
        status = "CALLABLE_PROBE_ONLY",
        note = "Generic UFunction parameter metadata requires a verified UE4SS reflection provider.",
        items = result
    }
end

return M
