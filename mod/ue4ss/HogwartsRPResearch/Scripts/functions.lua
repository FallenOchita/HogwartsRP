-- UFunction metadata inventory. Never invokes discovered functions.
local Reflection = require("reflection")
local M = {}

function M.inspect(object, options)
    local metadata = Reflection.inspect(object, options)
    local items = {}
    for _, fn in ipairs(metadata.functions or {}) do
        local owned = fn.owned_property_metadata or {}
        items[#items + 1] = {
            name = fn.name, full_name = fn.full_name,
            parameter_count = "UNAVAILABLE", return_type = "UNAVAILABLE",
            owned_field_count = #owned, function_flags = fn.function_flags,
            note = "UE4SS field enumeration does not yet establish parameter direction or return-property semantics."
        }
    end
    return { status = metadata.status, object = metadata.object, items = items, invocation_performed = false }
end

return M
