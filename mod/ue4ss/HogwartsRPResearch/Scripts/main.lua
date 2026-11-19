-- HogwartsRP Research Toolkit
-- UE4SS 4.x / Hogwarts Legacy UE4.27
-- READ-ONLY: no setters, gameplay UFunction invocation, hooks, or network writes.

local MOD = "HogwartsRPResearch"

local MAX_ATTEMPTS = 24
local RETRY_DELAY_MS = 2500
local attemptCount = 0
local completed = false
local commandRegistered = false

local function log(message)
    pcall(function() print("[" .. MOD .. "] " .. tostring(message) .. "\n") end)
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
local Reflection = protectedRequire("reflection")
local Relationships = protectedRequire("relationships")

log("Loaded. Read-only research mode enabled.")

if not Safe or not Reports or not Discovery then
    log("Startup aborted: required module could not be loaded.")
    return
end

local function firstValid(...)
    for i = 1, select("#", ...) do
        local candidate = select(i, ...)
        if Safe.isValidObject(candidate) then return candidate end
    end
    return nil
end

local function findTargets()
    local player, playerSource = Safe.findFirst("BP_Biped_Player_C")
    local controller, controllerSource = Safe.findFirst("BP_Phoenix_Player_Controller_C")

    -- The local controller can expose its possessed pawn before a class-wide
    -- lookup returns the pawn. This is a read-only property lookup.
    if not Safe.isValidObject(player) and Safe.isValidObject(controller) then
        local acknowledgedPawn = Safe.safeProperty(controller, "AcknowledgedPawn")
        local pawn = Safe.safeProperty(controller, "Pawn")
        player = firstValid(acknowledgedPawn, pawn)
        if Safe.isValidObject(player) then playerSource = "controller pawn property" end
    end

    local movement = nil
    if Safe.isValidObject(player) then
        local charMoveComp = Safe.safeProperty(player, "CharMoveComp")
        local characterMovement = Safe.safeProperty(player, "CharacterMovement")
        movement = firstValid(charMoveComp, characterMovement)
    end
    if not Safe.isValidObject(movement) then
        movement = Safe.findFirst("Biped_MovementComponent")
    end

    return {
        player = player,
        controller = controller,
        movement = movement,
        player_source = playerSource or "not found",
        controller_source = controllerSource or "not found"
    }
end

local function inspectMetadata(object, options)
    if not Reflection then
        return { status = "REFLECTION_MODULE_UNAVAILABLE", properties = {}, functions = {}, class_chain = {} }
    end
    local ok, result = pcall(function() return Reflection.inspect(object, options) end)
    if ok and type(result) == "table" then return result end
    return { status = "REFLECTION_FAILED", error = tostring(result), properties = {}, functions = {}, class_chain = {} }
end

local function runResearch(targets, finalAttempt)
    local ok, result = pcall(function()
        local player, controller, movement = targets.player, targets.controller, targets.movement
        local catalog = {
            generated_at = os and os.date and os.date("!%Y-%m-%dT%H:%M:%SZ") or "unknown",
            safety = "READ_ONLY",
            discovery = {
                attempts = attemptCount,
                final_attempt = finalAttempt == true,
                player_lookup = targets.player_source,
                controller_lookup = targets.controller_source,
                retry_policy = "Game-thread lookup every 2500ms; maximum 24 attempts"
            },
            objects = {},
            components = {},
            properties = {},
            functions = {},
            reflection = {},
            relationships = {},
            states = {},
            capabilities = {}
        }

        catalog.capabilities.find_first_of = Safe.capability("FindFirstOf")
        catalog.capabilities.find_all_of = Safe.capability("FindAllOf")
        catalog.capabilities.execute_with_delay = Safe.capability("ExecuteWithDelay")
        catalog.capabilities.execute_in_game_thread = Safe.capability("ExecuteInGameThread")
        catalog.capabilities.console_command_handler = Safe.capability("RegisterConsoleCommandHandler")
        catalog.capabilities.for_each_property = {
            documented = true, api = "UStruct:ForEachProperty(callback)", source = "UE4SS 4.0.0 Lua API"
        }
        catalog.capabilities.for_each_function = {
            documented = true, api = "UStruct:ForEachFunction(callback)", source = "UE4SS 4.0.0 Lua API"
        }

        catalog.objects.player = Safe.describeObject(player)
        catalog.objects.controller = Safe.describeObject(controller)
        catalog.objects.movement = Safe.describeObject(movement)
        catalog.components = Discovery.components(player)

        catalog.reflection.player = inspectMetadata(player)
        catalog.reflection.controller = inspectMetadata(controller)
        catalog.reflection.movement = inspectMetadata(movement, { named_property = "MovementMode" })
        if Reflection and Reflection.capabilityReport then
            catalog.reflection.provider_capabilities = Reflection.capabilityReport({
                player = player, controller = controller, movement = movement
            })
        else
            catalog.reflection.provider_capabilities = {
                status = "CAPABILITY_AUDIT_UNAVAILABLE",
                safety = "MEMBER_LOOKUP_ONLY_NO_METHOD_INVOCATION"
            }
        end

        if Relationships then
            local relationshipOk, relationshipResult = pcall(function()
                return Relationships.inspect(player, controller, movement)
            end)
            catalog.relationships = relationshipOk and relationshipResult or {
                status = "RELATIONSHIP_INSPECTION_FAILED", error = tostring(relationshipResult)
            }
        else
            catalog.relationships = { status = "RELATIONSHIP_MODULE_UNAVAILABLE" }
        end

        local propertyCandidates = {
            "CharacterMovement", "CharMoveComp", "Health", "CurrentHealth",
            "Mana", "CurrentMana", "Velocity", "MovementMode"
        }
        catalog.properties = {
            candidate_value_probes = Discovery.properties(player, propertyCandidates),
            value_audit = Discovery.propertyValueAudit(player, movement, {
                "CharacterMovement", "CharMoveComp", "Velocity", "MovementMode"
            }),
            player_metadata = catalog.reflection.player.properties,
            controller_metadata = catalog.reflection.controller.properties,
            movement_metadata = catalog.reflection.movement.properties
        }
        catalog.properties.movement_mode_enum_audit =
            catalog.reflection.movement.named_property_audit or {
                status = "AUDIT_UNAVAILABLE",
                property = "MovementMode",
                enum_association_status = "NOT_ESTABLISHED",
                enum_entries = {},
                safety = "NO_PROPERTY_VALUE_READS_NO_GAMEPLAY_FUNCTION_INVOCATION"
            }

        catalog.functions = {
            candidate_presence_probes = Discovery.functions(player, {
                "GetActorLocation", "GetActorRotation", "GetControlRotation",
                "GetVelocity", "GetMovementComponent"
            }),
            player_metadata = catalog.reflection.player.functions,
            controller_metadata = catalog.reflection.controller.functions,
            movement_metadata = catalog.reflection.movement.functions
        }

        if States then catalog.states = States.sample(player, movement, catalog.properties.value_audit) end

        local reportDir, pathError = Reports.getOutputDirectory()
        if not reportDir then
            log("Cannot resolve report output directory: " .. tostring(pathError))
            return
        end

        log("Report output directory: " .. reportDir)
        log("Discovery result — player: " .. tostring(catalog.objects.player.status) ..
            ", controller: " .. tostring(catalog.objects.controller.status) ..
            ", movement: " .. tostring(catalog.objects.movement.status))
        log("Reflection status — player: " .. tostring(catalog.reflection.player.status) ..
            ", controller: " .. tostring(catalog.reflection.controller.status) ..
            ", movement: " .. tostring(catalog.reflection.movement.status))
        log("Relationship validation status: " .. tostring(catalog.relationships.status))
        log("Component discovery status: " .. tostring(catalog.components.status) ..
            "; query results: " .. tostring(catalog.components.component_objects_returned or "UNAVAILABLE") ..
            "; player-owned matches: " .. tostring(catalog.components.player_owned_matches or 0) ..
            "; unresolved outer chains: " .. tostring(catalog.components.unresolved_outer_chains or 0))

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

    if not ok then log("Research pass failed safely: " .. tostring(result)) end
end

local function attemptResearch()
    if completed then return end
    attemptCount = attemptCount + 1

    local targets = findTargets()
    local hasPlayer = Safe.isValidObject(targets.player)
    local hasController = Safe.isValidObject(targets.controller)

    if hasPlayer or hasController then
        completed = true
        log("Player/controller objects discovered after " .. tostring(attemptCount) .. " attempt(s).")
        runResearch(targets, false)
        return
    end

    if attemptCount >= MAX_ATTEMPTS then
        completed = true
        log("Player/controller not found after " .. tostring(attemptCount) ..
            " attempts. Writing diagnostic reports; check object names and game state.")
        runResearch(targets, true)
        return
    end

    if attemptCount == 1 or attemptCount % 4 == 0 then
        log("Waiting for the game world/player objects (attempt " ..
            tostring(attemptCount) .. "/" .. tostring(MAX_ATTEMPTS) .. ").")
    end
end

local function queueNextAttempt()
    local delayFn = _G.ExecuteWithDelay
    local gameThreadFn = _G.ExecuteInGameThread
    if type(delayFn) ~= "function" or type(gameThreadFn) ~= "function" then
        log("Automatic delayed discovery unavailable. Use the hogwartsrp_research console command after entering the game.")
        return
    end

    local ok, err = pcall(function()
        delayFn(RETRY_DELAY_MS, function()
            local queued, queueError = pcall(function()
                gameThreadFn(function()
                    attemptResearch()
                    if not completed then queueNextAttempt() end
                end)
            end)
            if not queued then log("Could not queue game-thread discovery: " .. tostring(queueError)) end
        end)
    end)
    if not ok then log("Could not schedule delayed discovery: " .. tostring(err)) end
end

local registerCommand = _G.RegisterConsoleCommandHandler
if type(registerCommand) == "function" then
    local ok, err = pcall(function()
        registerCommand("hogwartsrp_research", function()
            if completed then
                log("Research pass already completed. Restart the mod to run it again.")
            else
                attemptResearch()
            end
            return true
        end)
    end)
    commandRegistered = ok
    if not ok then log("Console command registration failed: " .. tostring(err)) end
end

if commandRegistered then
    log("Manual fallback registered: hogwartsrp_research")
end

-- Defer UObject access until the game thread has had time to create the world.
queueNextAttempt()
log("Initialization complete; waiting for game-world discovery.")
