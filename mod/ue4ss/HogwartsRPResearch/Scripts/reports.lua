-- Text report writer. Filesystem access is optional and guarded.
local M = {}

local function safeString(value)
    if value == nil then return "UNAVAILABLE" end
    if type(value) == "table" then
        local ok, encoded = pcall(function() return tostring(value) end)
        return ok and encoded or "UNPRINTABLE"
    end
    return tostring(value)
end

local function linesFor(title, data)
    local lines = { "HogwartsRP Research Toolkit", title, "Mode: READ_ONLY", "" }
    local function walk(prefix, value, depth)
        if depth > 5 then
            lines[#lines + 1] = prefix .. " = [DEPTH_LIMIT]"
            return
        end
        if type(value) == "table" then
            for key, child in pairs(value) do
                walk(prefix .. "." .. tostring(key), child, depth + 1)
            end
        else
            lines[#lines + 1] = prefix .. " = " .. safeString(value)
        end
    end
    walk("data", data, 0)
    return lines
end

local function write(path, title, data)
    local ok, err = pcall(function()
        local file = assert(io.open(path, "w"))
        for _, line in ipairs(linesFor(title, data)) do file:write(line, "\n") end
        file:close()
    end)
    return ok, err
end

function M.writeAll(directory, catalog)
    local reports = {
        PlayerReport = catalog.objects and catalog.objects.player,
        ControllerReport = catalog.objects and catalog.objects.controller,
        MovementReport = catalog.objects and catalog.objects.movement,
        NPCReport = { status = "NOT_YET_DISCOVERED", note = "NPC class catalog is not yet validated." },
        ComponentReport = catalog.components,
        PropertyReport = catalog.properties,
        FunctionReport = catalog.functions,
        GameplayStateReport = catalog.states
    }

    for name, data in pairs(reports) do
        local ok, err = write(directory .. "/" .. name .. ".txt", name, data)
        if not ok then
            print("[HogwartsRPResearch] Report write failed for " .. name .. ": " .. tostring(err) .. "\n")
        end
    end
end

return M
