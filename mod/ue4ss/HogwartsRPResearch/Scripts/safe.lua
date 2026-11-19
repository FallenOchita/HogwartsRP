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
