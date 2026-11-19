-- Safe UE4SS access helpers. All engine-facing operations are protected.
local M = {}

local function callMethod(object, methodName, ...)
    if object == nil or type(methodName) ~= "string" then return false, "invalid input" end
    local ok, method = pcall(function() return object[methodName] end)
    if not ok or type(method) ~= "function" then return false, "method unavailable" end
    local args = {...}
    local unpackArgs = table.unpack or unpack
    local result = { pcall(function() return method(object, unpackArgs(args)) end) }
    if not result[1] then return false, tostring(result[2]) end
    table.remove(result, 1)
    return true, unpackArgs(result)
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
    local ok, value = callMethod(object, methodName, ...)
    if not ok then return nil, value end
    return value
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
    if not M.isValidObject(object) then return "INVALID" end
    local classObject = M.safeProperty(object, "Class")
    if classObject == nil then classObject = M.safeMethod(object, "GetClass") end
    if classObject == nil then return "UNAVAILABLE" end
    return M.safeFullName(classObject)
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
