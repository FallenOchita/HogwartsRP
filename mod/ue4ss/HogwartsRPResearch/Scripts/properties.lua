-- Property value sampling built on reflection metadata.
-- Values are read only when UE4SS exposes the property through safeProperty.
local Safe = require("safe")
local Reflection = require("reflection")
local M = {}

local function valueText(value)
    if value == nil then return "UNAVAILABLE" end
    local ok, result = pcall(tostring, value)
    return ok and result or "UNPRINTABLE"
end

function M.inspect(object, options)
    local metadata = Reflection.inspect(object, options)
    local result = {
        status = metadata.status, object = metadata.object, items = {},
        metadata_only = true,
        note = "Property values are best-effort reads; unsupported values remain UNAVAILABLE."
    }
    if not Safe.isValidObject(object) then return result end
    for _, property in ipairs(metadata.properties or {}) do
        local value, err = Safe.safeProperty(object, property.name)
        result.items[#result.items + 1] = {
            name = property.name,
            property_type = property.property_class or "UNAVAILABLE",
            value = valueText(value),
            readable = value ~= nil,
            read_error = err,
            full_name = property.full_name
        }
    end
    return result
end

return M
