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

-- Audit the documented metadata surface exposed by a property descriptor.
-- Capability methods are looked up only; no enum accessor is invoked or guessed.
local function enumAssociationCapabilityAudit(property, descriptor)
    local methodCapabilities = {}
    for _, methodName in ipairs({ "GetFullName", "GetFName", "GetClass", "IsA" }) do
        methodCapabilities[methodName] = Safe.methodCapability(property, methodName)
    end

    local associationStatus = "NOT_AN_ENUM_PROPERTY_CLASS"
    if descriptor.property_class == "ByteProperty" then
        associationStatus = "BYTE_PROPERTY_ASSOCIATION_UNVERIFIED"
    elseif descriptor.property_class == "EnumProperty" then
        associationStatus = "ENUM_PROPERTY_TYPE_CONFIRMED_REFERENCE_UNAVAILABLE"
    end

    return {
        status = associationStatus,
        descriptor_property_class = descriptor.property_class,
        documented_property_method_capabilities = methodCapabilities,
        enum_reference_status = "NO_DOCUMENTED_BYTEPROPERTY_ENUM_ACCESSOR",
        enum_api_status = "UENUM_METHODS_REQUIRE_AN_EXISTING_UENUM_REFERENCE",
        enum_entries_status = "NOT_ENUMERATED_NO_VERIFIED_UENUM_REFERENCE",
        documentation_scope = "UE4SS_4.0.0_LUA_API",
        note = "The documented Property API exposes generic descriptor identity methods but no ByteProperty-to-UEnum accessor. UEnum lookup methods cannot identify the associated enum without an independently obtained UEnum reference.",
        safety = "PROPERTY_DESCRIPTOR_CAPABILITY_LOOKUPS_ONLY_NO_ENUM_ACCESSOR_OR_ENUMERATION_CALLS"
    }
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
    local _, callError, callStatus = Safe.safeMethod(struct, "ForEachProperty", function(property)
        diagnostic.callback_invocations = diagnostic.callback_invocations + 1
        local callbackOk, shouldStop = pcall(function()
            if #output >= limit then
                diagnostic.stop_reason = "PROPERTY_LIMIT_REACHED"
                return true
            end
            if Safe.isValidObject(property) then
                local record = propertyRecord(property)
                record.declaring_class = declaringClass
                output[#output + 1] = record
                diagnostic.last_property_name = record.name
                diagnostic.properties_captured = #output - diagnostic.properties_before
                if namedPropertyAudit and record.name == namedPropertyAudit.property then
                    namedPropertyAudit.status = "FOUND"
                    namedPropertyAudit.descriptor = record
                    namedPropertyAudit.declaring_class = declaringClass
                    namedPropertyAudit.enum_association_capability_audit = enumAssociationCapabilityAudit(property, record)
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

-- Resolve one named descriptor through UE4SS's documented UObjectReflection API.
-- This avoids depending on ForEachProperty callback iteration for the named-property audit.
local function lookupNamedProperty(struct, propertyName, lookupClass)
    local diagnostic = {
        lookup_class = lookupClass,
        method = "Reflection:GetProperty",
        property = propertyName,
        status = "NOT_STARTED"
    }

    local reflection, reflectionError, reflectionStatus = Safe.safeMethod(struct, "Reflection")
    if not Safe.isValidObject(reflection) then
        diagnostic.status = reflectionStatus or "REFLECTION_UNAVAILABLE"
        diagnostic.error = reflectionError and conciseDiagnostic(reflectionError) or nil
        return nil, diagnostic
    end

    local property, propertyError, propertyStatus = Safe.safeMethod(reflection, "GetProperty", propertyName)
    if Safe.isValidObject(property) then
        diagnostic.status = "FOUND"
        local record = propertyRecord(property)
        diagnostic.descriptor = record
        diagnostic.enum_association_capability_audit = enumAssociationCapabilityAudit(property, record)
        return record, diagnostic
    end

    if propertyError == nil and propertyStatus == "OK" then
        diagnostic.status = "NOT_FOUND_ON_LOOKUP_CLASS"
    else
        diagnostic.status = propertyStatus or "LOOKUP_ERROR"
        diagnostic.error = propertyError and conciseDiagnostic(propertyError) or nil
    end
    return nil, diagnostic
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


-- Build a bounded property-descriptor catalog for one confirmed component.
-- The caller caches this result by class identity so duplicate instances do
-- not trigger repeated reflection passes. Each class receives one
-- ForEachProperty attempt; partial results and callback errors are preserved.
function M.catalogComponentProperties(component, options)
    options = options or {}
    local maxDepth = options.max_super_depth or DEFAULTS.max_super_depth
    local maxProperties = options.max_properties or 256
    local result = {
        status = "NOT_STARTED",
        component_full_name = Safe.safeFullName(component),
        class_full_name = "UNAVAILABLE",
        class_chain = {},
        class_hierarchy_status = "NOT_STARTED",
        properties = {},
        class_diagnostics = {},
        max_super_depth = maxDepth,
        max_properties = maxProperties,
        properties_truncated = false,
        safety = "CLASS_PROPERTY_DESCRIPTOR_METADATA_ONLY_NO_PROPERTY_VALUE_READS_NO_GAMEPLAY_UFUNCTIONS"
    }

    if not Safe.isValidObject(component) then
        result.status = "COMPONENT_NOT_FOUND"
        result.class_hierarchy_status = "NOT_ATTEMPTED"
        return result
    end

    local class, classError, classStatus = Safe.safeMethod(component, "GetClass")
    if not Safe.isValidObject(class) then
        result.status = "CLASS_UNAVAILABLE"
        result.class_hierarchy_status = classStatus or "CLASS_LOOKUP_FAILED"
        result.error = classError and conciseDiagnostic(classError) or nil
        return result
    end

    result.class_full_name = Safe.safeFullName(class)
    local current = class
    local seen = {}
    local hierarchyComplete = false
    local partial = false

    for _ = 1, maxDepth do
        if not Safe.isValidObject(current) then
            result.class_hierarchy_status = "PARTIAL"
            result.hierarchy_termination = "INVALID_SUPERCLASS_REFERENCE"
            partial = true
            break
        end

        local classFullName = Safe.safeFullName(current)
        if not classFullName or classFullName == "INVALID" or classFullName:match("^UNAVAILABLE") then
            result.class_hierarchy_status = "PARTIAL"
            result.hierarchy_termination = "CLASS_IDENTITY_UNAVAILABLE"
            partial = true
            break
        end
        if seen[classFullName] then
            result.class_hierarchy_status = "PARTIAL"
            result.hierarchy_termination = "CLASS_CYCLE_DETECTED"
            partial = true
            break
        end
        seen[classFullName] = true
        result.class_chain[#result.class_chain + 1] = classFullName

        if #result.properties < maxProperties then
            local enumerationError, diagnostic = enumerateProperties(
                current, result.properties, maxProperties, nil, classFullName
            )
            result.class_diagnostics[#result.class_diagnostics + 1] = diagnostic
            if enumerationError then
                partial = true
                diagnostic.error = conciseDiagnostic(enumerationError)
            end
        else
            result.properties_truncated = true
            partial = true
            result.class_diagnostics[#result.class_diagnostics + 1] = {
                class = classFullName,
                method = "ForEachProperty",
                status = "NOT_RUN_PROPERTY_LIMIT_REACHED",
                properties_before = #result.properties,
                properties_after = #result.properties,
                properties_captured = 0,
                callback_invocations = 0,
                last_property_name = "UNAVAILABLE"
            }
        end

        if #result.properties >= maxProperties then
            result.properties_truncated = true
            partial = true
            result.class_hierarchy_status = "NOT_CONTINUED_PROPERTY_LIMIT_REACHED"
            result.hierarchy_termination = "PROPERTY_LIMIT_REACHED"
            break
        end

        local parent, parentError, parentStatus = Safe.safeMethod(current, "GetSuperStruct")
        if parentError ~= nil or (parentStatus ~= nil and parentStatus ~= "OK") then
            result.class_hierarchy_status = "PARTIAL"
            result.hierarchy_termination = parentStatus or "SUPERCLASS_LOOKUP_FAILED"
            result.hierarchy_error = parentError and conciseDiagnostic(parentError) or nil
            partial = true
            break
        end
        if not Safe.isValidObject(parent) then
            result.class_hierarchy_status = "COMPLETE"
            result.hierarchy_termination = "NO_SUPERSTRUCT_RETURNED"
            hierarchyComplete = true
            break
        end
        current = parent
    end

    if not hierarchyComplete and result.hierarchy_status == nil and
        result.class_hierarchy_status == "NOT_STARTED" then
        result.class_hierarchy_status = "DEPTH_LIMIT_REACHED"
        result.hierarchy_termination = "MAX_SUPER_DEPTH"
        partial = true
    elseif not hierarchyComplete and result.class_hierarchy_status == "NOT_STARTED" then
        result.class_hierarchy_status = "DEPTH_LIMIT_REACHED"
        result.hierarchy_termination = "MAX_SUPER_DEPTH"
        partial = true
    end

    result.property_count = #result.properties
    result.class_count = #result.class_chain
    if partial then
        result.status = result.property_count > 0 and "PARTIAL" or "FAILED"
    else
        result.status = "COMPLETE"
    end
    result.note = "Only property descriptor names, full names, property classes, and declaring classes are recorded. ForEachProperty failures retain captured metadata and are never treated as proof that uncaptured properties are absent."
    return result
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
            enum_association_capability_audit = { status = "NOT_ATTEMPTED" },
            enum_entries = {},
            direct_lookup_diagnostics = {},
            direct_lookup_status = "NOT_ATTEMPTED",
            safety = "DIRECT_REFLECTION_METADATA_LOOKUP_AND_PROPERTY_ENUMERATION_ONLY_NO_VALUE_READS_NO_ENUMERATION_OF_ENUM_VALUES"
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

        if namedPropertyAudit and namedPropertyAudit.status ~= "FOUND" then
            local descriptor, lookupDiagnostic = lookupNamedProperty(current, namedPropertyAudit.property, classFullName)
            namedPropertyAudit.direct_lookup_diagnostics[#namedPropertyAudit.direct_lookup_diagnostics + 1] = lookupDiagnostic
            if descriptor then
                namedPropertyAudit.status = "FOUND"
                namedPropertyAudit.descriptor = descriptor
                namedPropertyAudit.lookup_scope_class = classFullName
                namedPropertyAudit.enum_association_capability_audit = lookupDiagnostic.enum_association_capability_audit
                namedPropertyAudit.enum_association_status = descriptor.property_class == "EnumProperty"
                    and "ENUM_PROPERTY_TYPE_CONFIRMED_ENUM_REFERENCE_UNAVAILABLE"
                    or (descriptor.property_class == "ByteProperty" and "BYTE_PROPERTY_ENUM_ASSOCIATION_UNVERIFIED"
                    or "NOT_AN_ENUM_PROPERTY_CLASS")
            end
        end

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
        local directLookupIncomplete = false
        local directLookupFound = false
        for _, diagnostic in ipairs(namedPropertyAudit.direct_lookup_diagnostics) do
            if diagnostic.status == "FOUND" then
                directLookupFound = true
                break
            elseif diagnostic.status ~= "NOT_FOUND_ON_LOOKUP_CLASS" then
                directLookupIncomplete = true
            end
        end
        namedPropertyAudit.direct_lookup_status = directLookupFound and "FOUND"
            or (directLookupIncomplete and "LOOKUP_INCOMPLETE" or "NOT_FOUND")
        if namedPropertyAudit.status == "PROPERTY_NOT_FOUND" then
            namedPropertyAudit.status = directLookupIncomplete and "LOOKUP_INCOMPLETE" or "PROPERTY_NOT_FOUND"
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
