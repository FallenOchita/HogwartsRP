-- UE4SS 4.0 reflection metadata discovery.
-- READ-ONLY: enumerates UClass/UStruct metadata; never reads property values,
-- invokes gameplay UFunctions, or writes to Unreal objects.
local Safe = require("safe")
local M = {}

local DEFAULTS = {
    max_super_depth = 16,
    max_properties = 512,
    max_functions = 512,
    max_function_fields = 96
}

local function fieldName(field)
    local fname = Safe.safeMethod(field, "GetFName")
    if fname ~= nil then
        local value = Safe.safeMethod(fname, "ToString")
        if value ~= nil then return tostring(value) end
    end
    return "UNAVAILABLE"
end

local function fieldClassName(field)
    local fieldClass = Safe.safeMethod(field, "GetClass")
    if fieldClass == nil then return "UNAVAILABLE" end

    local name = fieldName(fieldClass)
    if name ~= "UNAVAILABLE" then return name end

    return Safe.safeFullName(fieldClass)
end

local function propertyRecord(property)
    return {
        name = fieldName(property),
        full_name = Safe.safeFullName(property),
        property_class = fieldClassName(property)
    }
end

local function functionRecord(fn, maxFields)
    local fields = {}
    local enumerationError = nil
    local ok, err = pcall(function()
        local result, callError = Safe.safeMethod(fn, "ForEachProperty", function(property)
            if #fields >= maxFields then return true end
            if Safe.isValidObject(property) then
                fields[#fields + 1] = propertyRecord(property)
            end
            return false
        end)
        if result == nil and callError ~= nil then
            enumerationError = tostring(callError)
        end
    end)
    if not ok then enumerationError = tostring(err) end

    return {
        name = fieldName(fn),
        full_name = Safe.safeFullName(fn),
        function_flags = Safe.safeMethod(fn, "GetFunctionFlags") or "UNAVAILABLE",
        owned_property_metadata = fields,
        owned_property_metadata_truncated = #fields >= maxFields,
        owned_property_metadata_note = "UFunction-owned property records are listed without decoding parameter direction or invoking the function.",
        enumeration_error = enumerationError
    }
end

local function enumerateProperties(struct, output, limit)
    local errorMessage = nil
    local ok, err = pcall(function()
        local result, callError = Safe.safeMethod(struct, "ForEachProperty", function(property)
            if #output >= limit then return true end
            if Safe.isValidObject(property) then
                output[#output + 1] = propertyRecord(property)
            end
            return false
        end)
        if result == nil and callError ~= nil then errorMessage = tostring(callError) end
    end)
    if not ok then errorMessage = tostring(err) end
    return errorMessage
end

local function enumerateFunctions(struct, output, limit, maxFields)
    local errorMessage = nil
    local ok, err = pcall(function()
        local result, callError = Safe.safeMethod(struct, "ForEachFunction", function(fn)
            if #output >= limit then return true end
            if Safe.isValidObject(fn) then
                output[#output + 1] = functionRecord(fn, maxFields)
            end
            return false
        end)
        if result == nil and callError ~= nil then errorMessage = tostring(callError) end
    end)
    if not ok then errorMessage = tostring(err) end
    return errorMessage
end

function M.inspect(object, options)
    options = options or {}
    local limits = {
        max_super_depth = options.max_super_depth or DEFAULTS.max_super_depth,
        max_properties = options.max_properties or DEFAULTS.max_properties,
        max_functions = options.max_functions or DEFAULTS.max_functions,
        max_function_fields = options.max_function_fields or DEFAULTS.max_function_fields
    }

    if not Safe.isValidObject(object) then
        return { status = "OBJECT_NOT_FOUND", properties = {}, functions = {}, class_chain = {} }
    end

    local class = Safe.safeMethod(object, "GetClass")
    if not Safe.isValidObject(class) then
        return {
            status = "CLASS_UNAVAILABLE",
            object = Safe.safeFullName(object),
            properties = {},
            functions = {},
            class_chain = {}
        }
    end

    local properties, functions, classChain = {}, {}, {}
    local seenClasses = {}
    local current = class
    local propertyError, functionError = nil, nil

    for _ = 1, limits.max_super_depth do
        if not Safe.isValidObject(current) then break end

        local classFullName = Safe.safeFullName(current)
        if seenClasses[classFullName] then break end
        seenClasses[classFullName] = true
        classChain[#classChain + 1] = classFullName

        if #properties < limits.max_properties then
            propertyError = enumerateProperties(current, properties, limits.max_properties)
        end
        if #functions < limits.max_functions then
            functionError = enumerateFunctions(current, functions, limits.max_functions, limits.max_function_fields)
        end

        if #properties >= limits.max_properties and #functions >= limits.max_functions then
            break
        end

        current = Safe.safeMethod(current, "GetSuperStruct")
    end

    return {
        status = (#properties > 0 or #functions > 0) and "ENUMERATED" or "NO_METADATA_ENUMERATED",
        object = Safe.safeFullName(object),
        class = Safe.safeFullName(class),
        class_chain = classChain,
        properties = properties,
        functions = functions,
        limits = limits,
        properties_truncated = #properties >= limits.max_properties,
        functions_truncated = #functions >= limits.max_functions,
        property_enumeration_error = propertyError,
        function_enumeration_error = functionError,
        safety = "METADATA_ONLY_NO_PROPERTY_VALUE_READS_NO_UFUNCTION_INVOCATION"
    }
end

return M
