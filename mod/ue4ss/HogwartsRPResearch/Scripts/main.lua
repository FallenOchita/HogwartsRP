-- HogwartsRP Research Toolkit
-- UE4SS 4.x / Hogwarts Legacy UE4.27
-- READ-ONLY: no setters, ProcessEvent, gameplay hooks, or network writes.

local MOD = "HogwartsRPResearch"

local function log(message)
    pcall(function()
        print("[" .. MOD .. "] " .. tostring(message) .. "\n")
    end)
end

local function protectedRequire(path)
    local ok, result = pcall(require, path)
    if ok and type(result) == "table" then return result end
    log("Module unavailable: " .. tostring(path) .. " (" .. tostring(result) .. ")")
    return nil
end

local Safe = protectedRequire("safe")
local Reports = protectedRequire("reports")
local Discovery = protectedRequire("discovery")
local States = protectedRequire("states")
local Adapter = protectedRequire("adapter")

log("Loaded. Read-only research mode enabled.")

if not Safe or not Reports or not Discovery then
    log("Startup aborted: required module could not be loaded.")
    return
end

local function runResearch()
    local ok, result = pcall(function()
        local catalog = {
            generated_at = os and os.date and os.date("!%Y-%m-%dT%H:%M:%SZ") or "unknown",
            safety = "READ_ONLY",
            objects = {},
            components = {},
            properties = {},
            functions = {},
            states = {},
            capabilities = {}
        }

        local player = Safe.findFirst("BP_Biped_Player_C")
        local controller = Safe.findFirst("BP_Phoenix_Player_Controller_C")
        local movement = Safe.findFirst("Biped_MovementComponent")
        local gameInstance = Safe.findFirst("BP_PhoenixGameInstance_C")

        catalog.capabilities.find_first_of = Safe.capability("FindFirstOf")
        catalog.capabilities.property_read = Safe.capability("GetPropertyValue")
        catalog.capabilities.full_name = Safe.capability("GetFullName")

        catalog.objects.player = Safe.describeObject(player)
        catalog.objects.controller = Safe.describeObject(controller)
        catalog.objects.movement = Safe.describeObject(movement)
        catalog.objects.game_instance = Safe.describeObject(gameInstance)

        catalog.components = Discovery.components(player)
        catalog.properties = Discovery.properties(player, {
            "CharacterMovement", "CharMoveComp", "Health", "CurrentHealth",
            "Mana", "CurrentMana", "Velocity", "MovementMode"
        })
        catalog.functions = Discovery.functions(player, {
            "GetActorLocation", "GetActorRotation", "GetControlRotation",
            "GetVelocity", "GetMovementComponent"
        })

        if States then
            catalog.states = States.sample(player, movement)
        end

        local reportDir, pathError = Reports.getOutputDirectory()
        if not reportDir then
            log("Cannot resolve report output directory: " .. tostring(pathError))
            return
        end

        log("Report output directory: " .. reportDir)
        local written, failed = Reports.writeAll(reportDir, catalog)

        if Adapter then
            local adapterPath = reportDir .. "/HogwartsLegacyAdapter.lua"
            if Adapter.generate(adapterPath, player, controller, movement) then
                log("Adapter candidate written: " .. adapterPath)
            else
                log("Adapter candidate could not be written.")
            end
        end

        log("Research pass finished. Reports written: " .. tostring(written) ..
            "; failed: " .. tostring(failed))
    end)

    if not ok then
        log("Research pass failed safely: " .. tostring(result))
    end
end

-- Run once. No per-frame polling is enabled in the baseline.
runResearch()
log("Initialization complete.")
