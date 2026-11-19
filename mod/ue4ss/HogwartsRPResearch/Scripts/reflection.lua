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

local function conciseDiagnostic(value)
    local message = tostring(value or "unknown error")
    message = message:match("([^\\r\\n]+)") or message
    if #message > 180 then message = message:sub(1, 177) .. "..." end
    return message
end

local function enumerateProperties(struct, output, limit, namedPropertyAudit, declaringClass)
    local diagnostic = {
        class = declaringClass,
        method = "ForEachProperty",
        status = "NOT_STARTED",
        properties_before = #output,
        properties_after = #output,
        properties_captured = 0,
        callback_invocations = 0,
        last_property_name = "UNAVAILABLE"
    }

    local callbackError = nil
    local result, callError, callStatus = Safe.safeMethod(struct, "ForEachProperty", function(property)
        diagnostic.callback_invocations = diagnostic.callback_invocations + 1
        local callbackOk, shouldStop = pcall(function()
            if #output >= limit then
                diagnostic.stop_reason = "PROPERTY_LIMIT_REACHED"
                return true
            end
            if Safe.isValidObject(property) then
                local record = propertyRecord(property)
                output[#output + 1] = record
                diagnostic.last_property_name = record.name
                diagnostic.properties_captured = #output - diagnostic.properties_before
                if namedPropertyAudit and record.name == namedPropertyAudit.property then
                    namedPropertyAudit.status = "FOUND"
                    namedPropertyAudit.descriptor = record
                    namedPropertyAudit.declaring_class = declaringClass
                    if record.property_class == "EnumProperty" then
                        namedPropertyAudit.enum_association_status = "ENUM_PROPERTY_TYPE_CONFIRMED_ENUM_REFERENCE_UNAVAILABLE"
                    elseif record.property_class == "ByteProperty" then
                        namedPropertyAudit.enum_association_status = "BYTE_PROPERTY_ENUM_ASSOCIATION_UNVERIFIED"
                    else
                        namedPropertyAudit.enum_association_status = "NOT_AN_ENUM_PROPERTY_CLASS"
                    end
                end
            end
            return false
        end)
        if not callbackOk then
            callbackError = conciseDiagnostic(shouldStop)
            diagnostic.callback_error = callbackError
            diagnostic.stop_reason = "CALLBACK_ERROR"
            return true
        end
        return shouldStop
    end)

    diagnostic.properties_after = #output
    diagnostic.properties_captured = #output - diagnostic.properties_before
    diagnostic.invocation_status = callStatus or (callError and "CALL_ERROR" or "OK")
    if callbackError then
        diagnostic.status = "CALLBACK_ERROR"
    elseif callStatus == "CALL_ERROR" and diagnostic.callback_invocations > 0 then
        diagnostic.status = "CALL_ERROR_AFTER_CALLBACK"
    elseif callStatus == "CALL_ERROR" then
        diagnostic.status = "CALL_ERROR_BEFORE_CALLBACK"
    elseif callStatus == "LOOKUP_ERROR" or callStatus == "METHOD_UNAVAILABLE" then
        diagnostic.status = callStatus
    elseif callStatus == "OK" then
        diagnostic.status = diagnostic.stop_reason or "OK"
    else
        diagnostic.status = callStatus or "UNKNOWN"
    end
    if callError ~= nil then diagnostic.error = conciseDiagnostic(callError) end

    if callError and namedPropertyAudit and namedPropertyAudit.status ~= "FOUND" then
        namedPropertyAudit.status = "ENUMERATION_INCOMPLETE"
        namedPropertyAudit.enumeration_error = diagnostic.error
    end
    return callError and tostring(callError) or nil, diagnostic
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
    local report = {
        status = "INSPECTED",
        safety = "CLASS_LOOKUP_AND_MEMBER_EXPOSURE_ONLY_NO_ENUMERATION",
        targets = {}
    }
    local methods = { "ForEachProperty", "ForEachFunction", "GetSuperStruct" }
    for targetName, object in pairs(objects or {}) do
        local target = { status = Safe.isValidObject(object) and "FOUND" or "NOT_FOUND", methods = {} }
        if Safe.isValidObject(object) then
            -- GetClass is the same read-only identity lookup used by inspect().
            -- No ForEach* or UFunction descriptor method is invoked here.
            local class = Safe.safeMethod(object, "GetClass")
            target.class = Safe.safeFullName(class)
            if Safe.isValidObject(class) then
                for _, methodName in ipairs(methods) do
                    target.methods[methodName] = Safe.methodCapability(class, methodName)
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
    local namedPropertyAudit = nil
    if type(options.named_property) == "string" then
        namedPropertyAudit = {
            status = "PROPERTY_NOT_FOUND",
            property = options.named_property,
            object = Safe.safeFullName(object),
            enum_association_status = "NOT_ESTABLISHED",
            enum_entries = {},
            safety = "REFLECTION_PROPERTY_ENUMERATION_METADATA_ONLY_NO_VALUE_READS_NO_ENUMERATION_OF_ENUM_VALUES"
        }
    end
    local seenClasses = {}
    local current = class
    local propertyErrors, functionErrors, propertyDiagnostics = {}, {}, {}

    for _ = 1, limits.max_super_depth do
        if not Safe.isValidObject(current) then break end

        local classFullName = Safe.safeFullName(current)
        if seenClasses[classFullName] then break end
        seenClasses[classFullName] = true
        classChain[#classChain + 1] = classFullName

        if #properties < limits.max_properties then
            local err, diagnostic = enumerateProperties(current, properties, limits.max_properties, namedPropertyAudit, classFullName)
            propertyDiagnostics[#propertyDiagnostics + 1] = diagnostic
            if err then propertyErrors[#propertyErrors + 1] = {
                class = classFullName, status = diagnostic.status, detail = err,
                callback_invocations = diagnostic.callback_invocations,
                properties_captured = diagnostic.properties_captured,
                last_property_name = diagnostic.last_property_name
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

    if namedPropertyAudit then
        namedPropertyAudit.enumeration_diagnostics = propertyDiagnostics
        if namedPropertyAudit.status == "PROPERTY_NOT_FOUND"
            and #properties >= limits.max_properties then
            namedPropertyAudit.status = "ENUMERATION_LIMIT_REACHED"
        elseif namedPropertyAudit.status == "PROPERTY_NOT_FOUND" then
            for _, diagnostic in ipairs(propertyDiagnostics) do
                if diagnostic.status ~= "OK" and diagnostic.status ~= "PROPERTY_LIMIT_REACHED" then
                    namedPropertyAudit.status = "ENUMERATION_INCOMPLETE"
                    namedPropertyAudit.enumeration_error = diagnostic.error or diagnostic.callback_error or diagnostic.status
                    break
                end
            end
        end
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
        named_property_audit = namedPropertyAudit,
        property_enumeration_diagnostics = propertyDiagnostics,
        safety = "METADATA_ONLY_NO_PROPERTY_VALUE_READS_NO_UFUNCTION_INVOCATION"
    }
end


return M
