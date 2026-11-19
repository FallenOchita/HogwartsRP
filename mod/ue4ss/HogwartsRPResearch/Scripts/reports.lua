-- Text report writer. Output is resolved relative to this module's actual
-- installed location, not the game's current working directory.
local M = {}

local function safeString(value)
    if value == nil then return "UNAVAILABLE" end
    local ok, result = pcall(tostring, value)
    return ok and result or "UNPRINTABLE"
end

local function scriptDirectory()
    -- In UE4SS 4.x the module chunk source is normally its absolute script path.
    local ok, info = pcall(function()
        if debug and type(debug.getinfo) == "function" then
            return debug.getinfo(1, "S")
        end
        return nil
    end)

    if ok and type(info) == "table" and type(info.source) == "string" then
        local source = info.source
        if source:sub(1, 1) == "@" then source = source:sub(2) end
        source = source:gsub("\\", "/")
        local directory = source:match("^(.*)/[^/]+$")
        local absolute = source:match("^%a:/") or source:match("^//") or source:match("^/")
        if directory and directory ~= "" and absolute then return directory end
    end

    -- Documented UE4SS fallback: Win64 absolute directory.
    local fallbackOk, fallback = pcall(function()
        if type(IterateGameDirectories) ~= "function" then return nil end
        local dirs = IterateGameDirectories()
        local game = dirs and dirs.Game
        local binaries = game and game.Binaries
        local win64 = binaries and binaries.Win64
        return win64 and win64.__absolute_path or nil
    end)

    if fallbackOk and type(fallback) == "string" and fallback ~= "" then
        fallback = fallback:gsub("\\", "/")
        return fallback .. "/ue4ss/Mods/HogwartsRPResearch/Scripts"
    end

    return nil
end

function M.getOutputDirectory()
    local directory = scriptDirectory()
    if not directory then
        return nil, "Could not resolve this mod's Scripts directory"
    end

    local output = directory .. "/reports"
    -- The reports directory is shipped in the mod package (with .gitkeep).
    -- Probe its marker so a missing installation folder is reported clearly.
    local ok, marker = pcall(io.open, output .. "/.gitkeep", "r")
    if not ok or not marker then
        return nil, "Expected reports folder is missing: " .. output ..
            " (install the complete HogwartsRPResearch/Scripts/reports folder)"
    end
    pcall(function() marker:close() end)
    return output
end

local function linesFor(title, data)
    local lines = { "HogwartsRP Research Toolkit", title, "Mode: READ_ONLY", "" }
    local function walk(prefix, value, depth)
        if depth > 5 then
            lines[#lines + 1] = prefix .. " = [DEPTH_LIMIT]"
            return
        end
        if type(value) == "table" then
            local ok, err = pcall(function()
                for key, child in pairs(value) do
                    walk(prefix .. "." .. tostring(key), child, depth + 1)
                end
            end)
            if not ok then
                lines[#lines + 1] = prefix .. " = [ENUMERATION_FAILED: " .. safeString(err) .. "]"
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
        for _, line in ipairs(linesFor(title, data)) do
            assert(file:write(line, "\n"))
        end
        assert(file:close())
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
        GameplayStateReport = catalog.states,
        RelationshipReport = catalog.relationships
    }

    local written, failed = 0, 0
    for name, data in pairs(reports) do
        local ok, err = write(directory .. "/" .. name .. ".txt", name, data)
        if ok then
            written = written + 1
        else
            failed = failed + 1
            pcall(print, "[HogwartsRPResearch] Report write failed for " ..
                name .. ": " .. safeString(err) .. "\n")
        end
    end
    return written, failed
end

return M
