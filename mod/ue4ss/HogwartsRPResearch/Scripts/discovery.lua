-- Best-effort read-only discovery for Hogwarts Legacy / UE4SS 4.0.0.
-- Component enumeration uses FindObjects for ActorComponent-derived objects,
-- then validates ownership by walking UObject Outer relationships to the player.
local Safe = require("safe")
local M = {}

local MAX_COMPONENT_REPORTS = 256
local MAX_OUTER_DEPTH = 8

local PROPERTY_CANDIDATES = {
    "CharMoveComp",
    "CharacterMovement",
    "Mesh",
    "ConversationComponent",
    "ToolSetComponent",
    "AnimationArchitectComponent"
}

local function objectIdentity(object)
    if not Safe.isValidObject(object) then return nil end
    local name = Safe.safeFullName(object)
    if name == "INVALID" or name:match("^UNAVAILABLE") then return nil end
    return name
end

local function propertyReferences(actor)
    local items = {}
    for _, propertyName in ipairs(PROPERTY_CANDIDATES) do
        local value, readError = Safe.safeProperty(actor, propertyName)
        local item = {
            property = propertyName,
            status = "UNRESOLVED",
            full_path = "UNAVAILABLE",
            class = "UNAVAILABLE"
        }
        if Safe.isValidObject(value) then
            local fullName = objectIdentity(value)
            if fullName then
                item.status = "REFERENCE_FOUND"
                item.full_path = fullName
                item.class = Safe.safeClassName(value)
            else
                item.status = "VALUE_NOT_IDENTIFIED_AS_OBJECT"
            end
        elseif value == nil then
            item.status = "PROPERTY_UNAVAILABLE"
            item.detail = readError
        else
            item.status = "INVALID_REFERENCE"
        end
        items[#items + 1] = item
    end
    return items
end

local function belongsToActor(object, actorName)
    local current = object
    local chain = {}
    for depth = 1, MAX_OUTER_DEPTH do
        local outer, outerError = Safe.safeMethod(current, "GetOuter")
        if not Safe.isValidObject(outer) then
            return false, "OUTER_CHAIN_UNAVAILABLE", chain, outerError
        end
        local outerName = objectIdentity(outer)
        if not outerName then
            return false, "OUTER_IDENTITY_UNAVAILABLE", chain
        end
        chain[#chain + 1] = outerName
        if outerName == actorName then
            return true, "MATCH", chain, nil, depth
        end
        current = outer
    end
    return false, "OUTER_DEPTH_LIMIT", chain
end

local function buildComponentInventory(items)
    local groups = {}
    local reviewItems = {}
    for _, item in ipairs(items) do
        local className = item.class or "CLASS_UNAVAILABLE"
        local group = groups[className]
        if not group then
            group = { class = className, count = 0, full_paths = {} }
            groups[className] = group
        end
        group.count = group.count + 1
        group.full_paths[#group.full_paths + 1] = item.full_path

        -- A normal reflected UClass full name has the "Class /Script/..." form.
        -- Preserve unusual metadata for review; do not silently discard it or
        -- claim inheritance from a name string alone.
        if not className:match("^Class /Script/") then
            reviewItems[#reviewItems + 1] = {
                full_path = item.full_path,
                class = className,
                status = "CLASS_METADATA_REVIEW_REQUIRED",
                note = "The returned object's class name is not in the expected reflected UClass form; verify its class hierarchy before treating it as a conventional component."
            }
        end
    end

    local classGroups, duplicateGroups = {}, {}
    for _, group in pairs(groups) do
        table.sort(group.full_paths)
        classGroups[#classGroups + 1] = group
        if group.count > 1 then
            duplicateGroups[#duplicateGroups + 1] = {
                class = group.class,
                count = group.count,
                full_paths = group.full_paths
            }
        end
    end
    table.sort(classGroups, function(a, b) return a.class < b.class end)
    table.sort(duplicateGroups, function(a, b) return a.class < b.class end)

    return {
        unique_class_count = #classGroups,
        classes = classGroups,
        duplicate_class_groups = duplicateGroups,
        class_metadata_review_count = #reviewItems,
        class_metadata_review = reviewItems
    }
end

local function crossCheckPropertyReferences(propertyCandidates, items)
    local checks = {}
    for _, candidate in ipairs(propertyCandidates or {}) do
        local check = {
            property = candidate.property,
            property_status = candidate.status,
            reference_full_path = candidate.full_path,
            status = "NO_VALID_REFERENCE_TO_COMPARE"
        }
        if candidate.status == "REFERENCE_FOUND" and candidate.full_path ~= "UNAVAILABLE" then
            check.status = "REFERENCE_NOT_IN_ENUMERATED_MATCHES"
            for _, item in ipairs(items) do
                if item.full_path == candidate.full_path then
                    check.status = "EXACT_PATH_MATCH"
                    check.enumerated_class = item.class
                    break
                end
            end
        end
        checks[#checks + 1] = check
    end
    return checks
end

function M.components(actor)
    if not Safe.isValidObject(actor) then
        return {
            status = "PLAYER_NOT_FOUND",
            discovery_mode = "FIND_OBJECTS_ACTOR_COMPONENT",
            items = {},
            property_reference_candidates = {}
        }
    end

    local actorName = objectIdentity(actor)
    local propertyCandidates = propertyReferences(actor)
    if not actorName then
        return {
            status = "PLAYER_IDENTITY_UNAVAILABLE",
            discovery_mode = "FIND_OBJECTS_ACTOR_COMPONENT",
            reason = "The player pawn full name could not be resolved, so component ownership cannot be validated.",
            items = {},
            property_reference_candidates = propertyCandidates
        }
    end

    local findObjects = _G.FindObjects
    if type(findObjects) ~= "function" then
        return {
            status = "ENUMERATION_API_UNAVAILABLE",
            discovery_mode = "ALLOWLISTED_PROPERTY_REFERENCE_PROBES",
            reason = "FindObjects is not exposed by this UE4SS runtime. Property references are candidates only, not a complete component list.",
            items = {},
            property_reference_candidates = propertyCandidates
        }
    end

    -- UE4SS 4.0.0 signature:
    -- FindObjects(NumObjectsToFind, ClassName, ObjectShortName,
    --             RequiredFlags, BannedFlags, bExactClass)
    -- A count of zero requests all matching instances; false includes subclasses.
    local ok, objects = pcall(function()
        return findObjects(0, "ActorComponent", nil, 0, 0, false)
    end)
    if not ok then
        return {
            status = "ENUMERATION_CALL_FAILED",
            discovery_mode = "FIND_OBJECTS_ACTOR_COMPONENT",
            reason = tostring(objects),
            items = {},
            property_reference_candidates = propertyCandidates
        }
    end
    if type(objects) ~= "table" then
        return {
            status = "ENUMERATION_RESULT_UNAVAILABLE",
            discovery_mode = "FIND_OBJECTS_ACTOR_COMPONENT",
            reason = "FindObjects did not return a Lua table.",
            items = {},
            property_reference_candidates = propertyCandidates
        }
    end

    local returnedCount, matchedCount = 0, 0
    local unresolvedCount, nonMatchingCount, depthLimitCount = 0, 0, 0
    local items = {}
    for _, component in pairs(objects) do
        if Safe.isValidObject(component) then
            returnedCount = returnedCount + 1
            local matches, relationshipStatus, outerChain, outerError, depth =
                belongsToActor(component, actorName)

            if matches then
                matchedCount = matchedCount + 1
                if #items < MAX_COMPONENT_REPORTS then
                    items[#items + 1] = {
                        status = "PLAYER_OUTER_MATCH",
                        full_path = objectIdentity(component) or "UNAVAILABLE",
                        class = Safe.safeClassName(component),
                        outer_depth = depth,
                        outer_chain = outerChain
                    }
                end
            elseif relationshipStatus == "OUTER_CHAIN_UNAVAILABLE" or
                relationshipStatus == "OUTER_IDENTITY_UNAVAILABLE" then
                unresolvedCount = unresolvedCount + 1
            elseif relationshipStatus == "OUTER_DEPTH_LIMIT" then
                depthLimitCount = depthLimitCount + 1
            else
                nonMatchingCount = nonMatchingCount + 1
            end
        end
    end

    local inventory = buildComponentInventory(items)
    return {
        status = "ENUMERATED",
        discovery_mode = "FIND_OBJECTS_ACTOR_COMPONENT",
        api = "FindObjects(0, ActorComponent, nil, 0, 0, false)",
        ownership_rule = "The component Outer chain must resolve to the discovered player pawn full name.",
        player_full_name = actorName,
        component_objects_returned = returnedCount,
        player_owned_matches = matchedCount,
        unresolved_outer_chains = unresolvedCount,
        non_matching_outer_chains = nonMatchingCount,
        outer_depth_limit_count = depthLimitCount,
        items = items,
        max_reported = MAX_COMPONENT_REPORTS,
        truncated = matchedCount > MAX_COMPONENT_REPORTS,
        property_reference_candidates = propertyCandidates,
        property_reference_crosscheck = crossCheckPropertyReferences(propertyCandidates, items),
        class_inventory = inventory,
        note = "Enumeration success means the UE4SS query returned a table. PLAYER_OUTER_MATCH confirms the Outer relationship only. Class metadata is grouped separately; unusual class names require hierarchy validation before being treated as conventional components."
    }
end

function M.properties(object, names)
    local result = {}
    if not Safe.isValidObject(object) then return { status = "OBJECT_NOT_FOUND", items = result } end
    for _, name in ipairs(names or {}) do
        local value, err = Safe.safeProperty(object, name)
        result[#result + 1] = {
            name = name,
            property_type = type(value),
            value = value == nil and "UNAVAILABLE" or tostring(value),
            detail = err
        }
    end
    return { status = "ALLOWLIST_ONLY", items = result }
end

function M.functions(object, candidates)
    local result = {}
    if not Safe.isValidObject(object) then return { status = "OBJECT_NOT_FOUND", items = result } end
    for _, name in ipairs(candidates or {}) do
        local value = Safe.safeProperty(object, name)
        result[#result + 1] = {
            name = name,
            exposed_as_callable = type(value) == "function",
            parameter_count = "UNAVAILABLE",
            return_type = "UNAVAILABLE",
            note = "Presence probe only; no function invoked"
        }
    end
    return {
        status = "CALLABLE_PROBE_ONLY",
        note = "Generic UFunction parameter metadata requires a verified UE4SS reflection provider.",
        items = result
    }
end

return M
