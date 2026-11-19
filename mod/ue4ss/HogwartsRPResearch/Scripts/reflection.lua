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
    local _, enumerationError, enumerationStatus = Safe.safeMethod(fn, "ForEachProperty", function(property)
        if #fields >= maxFields then return true end
        if Safe.isValidObject(property) then
            fields[#fields + 1] = propertyRecord(property)
        end
        return false
    end)
    local metadataStatus = enumerationStatus or (enumerationError and "CALL_ERROR" or "OK")
    if enumerationStatus == "METHOD_UNAVAILABLE" then
        metadataStatus = "METHOD_UNAVAILABLE"
    elseif enumerationStatus == "LOOKUP_ERROR" then
        metadataStatus = "LOOKUP_ERROR"
    elseif enumerationStatus == "CALL_ERROR" then
        metadataStatus = "CALL_ERROR"
    end

    return {
        name = fieldName(fn),
        full_name = Safe.safeFullName(fn),
        function_flags = Safe.safeMethod(fn, "GetFunctionFlags") or "UNAVAILABLE",
        owned_property_metadata = fields,
        owned_property_metadata_truncated = #fields >= maxFields,
        owned_property_metadata_status = metadataStatus,
        owned_property_metadata_note = "Metadata-only attempt. UE4SS may not expose UFunction property iteration for every descriptor; no function is invoked.",
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

-- Capability report only reads Lua member exposure; it does not invoke any
-- reflected method. A method marked EXPOSED_NOT_INVOKED may still fail at call time.
function M.capabilityReport(objects)
    local report = { status = "INSPECTED", safety = "MEMBER_LOOKUP_ONLY_NO_METHOD_INVOCATION", targets = {} }
    local methods = { "ForEachProperty", "ForEachFunction", "GetSuperStruct" }
    for targetName, object in pairs(objects or {}) do
        local target = { status = Safe.isValidObject(object) and "FOUND" or "NOT_FOUND", methods = {} }
        if Safe.isValidObject(object) then
            local class = Safe.safeMethod(object, "GetClass")
            target.class = Safe.safeFullName(class)
            if Safe.isValidObject(class) then
                for _, methodName in ipairs(methods) do
                    target.methods[methodName] = Safe.methodCapability(class, methodName)
                end
                local firstFunction = nil
                local _, _, enumStatus = Safe.safeMethod(class, "ForEachFunction", function(fn)
                    if Safe.isValidObject(fn) then firstFunction = fn; return true end
                    return false
                end)
                target.function_discovery_status = enumStatus or "UNKNOWN"
                if Safe.isValidObject(firstFunction) then
                    target.function_descriptor_methods = {
                        ForEachProperty = Safe.methodCapability(firstFunction, "ForEachProperty"),
                        GetFunctionFlags = Safe.methodCapability(firstFunction, "GetFunctionFlags"),
                        GetFName = Safe.methodCapability(firstFunction, "GetFName")
                    }
                else
                    target.function_descriptor_methods = { status = "NO_FUNCTION_DESCRIPTOR_AVAILABLE" }
                end
            else
                target.status = "CLASS_UNAVAILABLE"
            end
        end
        report.targets[targetName] = target
    end
    return report
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
    local propertyErrors, functionErrors = {}, {}

    for _ = 1, limits.max_super_depth do
        if not Safe.isValidObject(current) then break end

        local classFullName = Safe.safeFullName(current)
        if seenClasses[classFullName] then break end
        seenClasses[classFullName] = true
        classChain[#classChain + 1] = classFullName

        if #properties < limits.max_properties then
            local err = enumerateProperties(current, properties, limits.max_properties)
            if err then propertyErrors[#propertyErrors + 1] = {
                class = classFullName, status = "PARTIAL_OR_FAILED", detail = err
            } end
        end
        if #functions < limits.max_functions then
            local err = enumerateFunctions(current, functions, limits.max_functions, limits.max_function_fields)
            if err then functionErrors[#functionErrors + 1] = {
                class = classFullName, status = "PARTIAL_OR_FAILED", detail = err
            } end
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
        property_enumeration_errors = propertyErrors,
        function_enumeration_errors = functionErrors,
        safety = "METADATA_ONLY_NO_PROPERTY_VALUE_READS_NO_UFUNCTION_INVOCATION"
    }
end

return M
