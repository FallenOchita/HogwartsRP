-- Best-effort discovery. Unsupported reflection surfaces are reported, not guessed.
local Safe = require("safe")
local M = {}

function M.components(actor)
    if not Safe.isValidObject(actor) then return { status = "PLAYER_NOT_FOUND", items = {} } end
    local components = Safe.safeMethod(actor, "GetComponents")
    if type(components) ~= "table" then
        return { status = "UNAVAILABLE", reason = "GetComponents is not exposed as a Lua table by this UE4SS build", items = {} }
    end

    local items = {}
    local limit = math.min(#components, 256)
    for i = 1, limit do
        local component = components[i]
        if Safe.isValidObject(component) then
            items[#items + 1] = {
                name = Safe.safeProperty(component, "Name") or "UNAVAILABLE",
                class = Safe.safeClassName(component),
                full_path = Safe.safeFullName(component)
            }
        end
    end
    return { status = "OK", items = items, truncated = #components > limit }
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
