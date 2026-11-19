-- Safe UE4SS access helpers. All engine-facing operations are protected.
local M = {}

local function conciseError(value)
    local message = tostring(value or "unknown error")
    message = message:match("([^\r\n]+)") or message
    if #message > 180 then message = message:sub(1, 177) .. "..." end
    return message
end

local function callMethod(object, methodName, ...)
    if object == nil or type(methodName) ~= "string" then
        return false, "invalid input", "INVALID_INPUT"
    end
    local ok, method = pcall(function() return object[methodName] end)
    if not ok then return false, conciseError(method), "LOOKUP_ERROR" end
    if type(method) ~= "function" then
        return false, "method unavailable", "METHOD_UNAVAILABLE"
    end
    local args = {...}
    local unpackArgs = table.unpack or unpack
    local result = { pcall(function() return method(object, unpackArgs(args)) end) }
    if not result[1] then
        return false, conciseError(result[2]), "CALL_ERROR"
    end
    table.remove(result, 1)
    return true, unpackArgs(result), "OK"
end

function M.isValidObject(object)
    if object == nil then return false end
    local ok, value = pcall(function()
        if type(object.IsValid) == "function" then return object:IsValid() end
        return true
    end)
    return ok and value ~= false
end

function M.safeMethod(object, methodName, ...)
    if not M.isValidObject(object) then return nil, "invalid object" end
    local ok, value, status = callMethod(object, methodName, ...)
    if not ok then return nil, value, status end
    return value, nil, status
end

function M.safeProperty(object, propertyName)
    if not M.isValidObject(object) or type(propertyName) ~= "string" then
        return nil, "invalid object or property name"
    end

    local ok, getter = pcall(function() return object.GetPropertyValue end)
    if ok and type(getter) == "function" then
        local got, value = pcall(function() return getter(object, propertyName) end)
        if got then return value end
        return nil, tostring(value)
    end

    local got, value = pcall(function() return object[propertyName] end)
    if got then return value end
    return nil, tostring(value)
end

function M.inspectPropertyValue(object, propertyName)
    local record = { property = propertyName, status = "UNAVAILABLE", lua_type = "nil",
        access = "NONE", summary = "UNAVAILABLE" }
    if not M.isValidObject(object) or type(propertyName) ~= "string" then
        record.status = "INVALID_INPUT"
        return record
    end

    local ok, getter = pcall(function() return object.GetPropertyValue end)
    local value
    if ok and type(getter) == "function" then
        local got, result = pcall(function() return getter(object, propertyName) end)
        if not got then
            record.status = "PROPERTY_READ_ERROR"
            record.access = "GetPropertyValue"
            record.detail = conciseError(result)
            return record
        end
        value = result
        record.access = "GetPropertyValue"
    else
        local got, result = pcall(function() return object[propertyName] end)
        if not got then
            record.status = "PROPERTY_READ_ERROR"
            record.access = "DirectIndex"
            record.detail = conciseError(result)
            return record
        end
        value = result
        record.access = "DirectIndex"
    end

    record.lua_type = type(value)
    if value == nil then
        record.status = "PROPERTY_UNAVAILABLE"
    elseif record.lua_type == "string" or record.lua_type == "number" or record.lua_type == "boolean" then
        record.status = "PRIMITIVE_VALUE_READ"
        record.summary = tostring(value)
    elseif record.lua_type == "userdata" then
        local fullName = M.safeFullName(value)
        if fullName ~= "INVALID" and not fullName:match("^UNAVAILABLE") then
            record.status = "OBJECT_REFERENCE_FOUND"
            record.summary = fullName
        else
            record.status = "OPAQUE_USERDATA"
            record.summary = "OPAQUE_USERDATA_NOT_DEREFERENCED"
        end
    elseif record.lua_type == "table" then
        record.status = "TABLE_VALUE_NOT_EXPANDED"
        record.summary = "TABLE_NOT_EXPANDED"
    else
        record.status = "VALUE_TYPE_UNSUPPORTED"
        record.summary = "UNSUPPORTED_VALUE_TYPE"
    end
    return record
end

function M.safeFullName(object)
    if not M.isValidObject(object) then return "INVALID" end
    local value, err = M.safeMethod(object, "GetFullName")
    if value ~= nil then return tostring(value) end
    return "UNAVAILABLE: " .. tostring(err)
end

function M.safeClassName(object)
    if not M.isValidObject(object) then return "INVALID_OBJECT" end
    local classObject, classError, classStatus = M.safeMethod(object, "GetClass")
    if classObject == nil then
        return classStatus == "CALL_ERROR" and "GETCLASS_CALL_ERROR: " .. conciseError(classError)
            or "GETCLASS_UNAVAILABLE"
    end
    if not M.isValidObject(classObject) then return "CLASS_OBJECT_INVALID" end
    local fullName = M.safeFullName(classObject)
    if fullName == "INVALID" or fullName:match("^UNAVAILABLE") then
        return "CLASS_FULL_NAME_UNAVAILABLE"
    end
    return fullName
end

function M.capability(name)
    local ok, value = pcall(function() return _G[name] end)
    return {
        available = ok and type(value) == "function",
        detail = ok and type(value) == "function" and "present" or "not exposed"
    }
end

-- Inspect method exposure without invoking the method. This separates a
-- missing Lua binding from a binding that exists but fails when called.
function M.methodCapability(object, methodName)
    local record = { method = methodName, exposed = false, status = "OBJECT_UNAVAILABLE" }
    if not M.isValidObject(object) or type(methodName) ~= "string" then
        record.status = "INVALID_INPUT"
        return record
    end
    local ok, value = pcall(function() return object[methodName] end)
    if not ok then
        record.status = "LOOKUP_ERROR"
        record.detail = conciseError(value)
        return record
    end
    record.exposed = type(value) == "function"
    record.status = record.exposed and "EXPOSED_NOT_INVOKED" or "METHOD_UNAVAILABLE"
    record.lua_type = type(value)
    return record
end

function M.findAll(className)
    if type(className) ~= "string" then return {}, "invalid class name" end
    local ok, fn = pcall(function() return _G.FindAllOf end)
    if not ok or type(fn) ~= "function" then return {}, "FindAllOf unavailable" end

    local found, objects = pcall(function() return fn(className) end)
    if not found then return {}, tostring(objects) end
    if type(objects) ~= "table" then return {}, "no instances returned" end

    local valid = {}
    for _, object in pairs(objects) do
        if M.isValidObject(object) then valid[#valid + 1] = object end
    end
    return valid, nil
end

function M.findFirst(className)
    if type(className) ~= "string" then return nil, "invalid class name" end

    local ok, fn = pcall(function() return _G.FindFirstOf end)
    if ok and type(fn) == "function" then
        local found, object = pcall(function() return fn(className) end)
        if found and M.isValidObject(object) then return object, "FindFirstOf" end
    end

    local objects, allError = M.findAll(className)
    if #objects > 0 then return objects[1], "FindAllOf fallback" end
    return nil, allError or "no non-default instance found"
end

function M.describeObject(object)
    if not M.isValidObject(object) then return { status = "NOT_FOUND" } end
    local outer = M.safeMethod(object, "GetOuter")
    local world = M.safeMethod(object, "GetWorld")
    local owner = M.safeMethod(object, "GetOwner")
    return {
        status = "FOUND",
        full_name = M.safeFullName(object),
        class_name = M.safeClassName(object),
        outer = outer and M.safeFullName(outer) or "UNAVAILABLE",
        world = world and M.safeFullName(world) or "UNAVAILABLE",
        owner = owner and M.safeFullName(owner) or "UNAVAILABLE"
    }
end

return M
