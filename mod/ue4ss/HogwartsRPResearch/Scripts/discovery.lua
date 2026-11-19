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

-- Resolve the reflected class chain for a confirmed component using only
-- documented UE4SS class metadata. No component property values are inspected.
local function componentClassHierarchy(component, maxDepth)
    local result = {
        status = "CLASS_UNAVAILABLE",
        class_full_name = "UNAVAILABLE",
        class_chain = {},
        max_depth = maxDepth
    }
    local class, classError, classStatus = Safe.safeMethod(component, "GetClass")
    if not Safe.isValidObject(class) then
        result.lookup_status = classStatus or "LOOKUP_FAILED"
        if classError then result.error = tostring(classError) end
        return result
    end

    result.class_full_name = objectIdentity(class) or Safe.safeFullName(class)
    local current = class
    local seen = {}
    for _ = 1, maxDepth do
        if not Safe.isValidObject(current) then
            result.status = "PARTIAL"
            result.termination = "INVALID_SUPERCLASS_REFERENCE"
            return result
        end
        local name = objectIdentity(current) or Safe.safeFullName(current)
        if not name or name == "UNAVAILABLE" then
            result.status = "PARTIAL"
            result.termination = "CLASS_IDENTITY_UNAVAILABLE"
            return result
        end
        if seen[name] then
            result.status = "PARTIAL"
            result.termination = "CLASS_CYCLE_DETECTED"
            return result
        end
        seen[name] = true
        result.class_chain[#result.class_chain + 1] = name

        local parent, parentError, parentStatus = Safe.safeMethod(current, "GetSuperStruct")
        if parentError ~= nil or (parentStatus ~= nil and parentStatus ~= "OK") then
            result.status = "PARTIAL"
            result.termination = parentStatus or "SUPERCLASS_LOOKUP_FAILED"
            result.error = parentError and tostring(parentError) or nil
            return result
        end
        if not Safe.isValidObject(parent) then
            result.status = "COMPLETE"
            result.termination = "NO_SUPERSTRUCT_RETURNED"
            return result
        end
        current = parent
    end

    result.status = "DEPTH_LIMIT_REACHED"
    result.termination = "MAX_SUPER_DEPTH"
    return result
end

local function buildComponentInventory(items)
    local groups = {}
    local reviewItems = {}
    for _, item in ipairs(items) do
        local className = item.class or "CLASS_UNAVAILABLE"
        local group = groups[className]
        if not group then
            group = { class = className, count = 0, full_paths = {}, class_hierarchy = item.class_hierarchy or { status = "NOT_ATTEMPTED", class_chain = {} } }
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

function M.components(actor, propertyCataloger)
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
    local hierarchyCache = {}
    local propertyCatalogCache = {}
    local hierarchyDepth = 16
    for _, component in pairs(objects) do
        if Safe.isValidObject(component) then
            returnedCount = returnedCount + 1
            local matches, relationshipStatus, outerChain, outerError, depth =
                belongsToActor(component, actorName)

            if matches then
                matchedCount = matchedCount + 1
                if #items < MAX_COMPONENT_REPORTS then
                    local componentPath = objectIdentity(component) or "UNAVAILABLE"
                    local componentClass = Safe.safeClassName(component)
                    local hierarchyKey = componentClass or "CLASS_UNAVAILABLE"
                    if hierarchyCache[hierarchyKey] == nil then
                        hierarchyCache[hierarchyKey] = componentClassHierarchy(component, hierarchyDepth)
                    end
                    if propertyCatalogCache == nil then propertyCatalogCache = {} end
                    if propertyCatalogCache[hierarchyKey] == nil then
                        if type(propertyCataloger) == "function" then
                            local catalogOk, catalogResult = pcall(propertyCataloger, component)
                            if catalogOk and type(catalogResult) == "table" then
                                propertyCatalogCache[hierarchyKey] = catalogResult
                            else
                                propertyCatalogCache[hierarchyKey] = {
                                    status = "CATALOG_CALL_FAILED",
                                    class_full_name = componentClass or "UNAVAILABLE",
                                    error = tostring(catalogResult)
                                }
                            end
                        else
                            propertyCatalogCache[hierarchyKey] = {
                                status = "NOT_ATTEMPTED",
                                class_full_name = componentClass or "UNAVAILABLE",
                                properties = {},
                                class_diagnostics = {}
                            }
                        end
                    end
                    local propertyEntry = propertyCatalogCache[hierarchyKey]
                    propertyEntry.component_count = (propertyEntry.component_count or 0) + 1
                    propertyEntry.sample_component_path = propertyEntry.sample_component_path or componentPath
                    items[#items + 1] = {
                        status = "PLAYER_OUTER_MATCH",
                        full_path = componentPath,
                        class = componentClass,
                        class_hierarchy = hierarchyCache[hierarchyKey],
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
    local propertyCatalogClasses = {}
    local propertyCatalogComplete, propertyCatalogPartial, propertyCatalogFailed = 0, 0, 0
    for _, entry in pairs(propertyCatalogCache) do
        propertyCatalogClasses[#propertyCatalogClasses + 1] = entry
        if entry.status == "COMPLETE" then
            propertyCatalogComplete = propertyCatalogComplete + 1
        elseif entry.status == "PARTIAL" then
            propertyCatalogPartial = propertyCatalogPartial + 1
        elseif entry.status == "NOT_ATTEMPTED" then
            -- Kept distinct from a failed attempt.
        else
            propertyCatalogFailed = propertyCatalogFailed + 1
        end
    end
    table.sort(propertyCatalogClasses, function(a, b)
        return tostring(a.class_full_name or "") < tostring(b.class_full_name or "")
    end)
    local propertyCatalogStatus = "NOT_ATTEMPTED"
    if type(propertyCataloger) == "function" then
        if propertyCatalogPartial > 0 or propertyCatalogFailed > 0 then
            propertyCatalogStatus = "PARTIAL"
        elseif propertyCatalogComplete == #propertyCatalogClasses and #propertyCatalogClasses > 0 then
            propertyCatalogStatus = "COMPLETE"
        elseif #propertyCatalogClasses > 0 then
            propertyCatalogStatus = "FAILED"
        end
    end
    local componentPropertyCatalog = {
        status = propertyCatalogStatus,
        unique_class_count = #propertyCatalogClasses,
        complete_class_count = propertyCatalogComplete,
        partial_class_count = propertyCatalogPartial,
        failed_class_count = propertyCatalogFailed,
        classes = propertyCatalogClasses,
        safety = "READ_ONLY_PROPERTY_DESCRIPTOR_METADATA_ONLY_NO_PROPERTY_VALUE_READS_NO_GAMEPLAY_FUNCTIONS",
        note = "Catalog covers only classes from confirmed player Outer matches. A partial ForEachProperty pass is retained as partial metadata and is not evidence that uncaptured properties are absent."
    }
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
        class_hierarchy = {
            status = "METADATA_ONLY",
            unique_class_count = inventory.unique_class_count,
            complete_class_count = (function()
                local count = 0
                for _, group in ipairs(inventory.classes) do
                    if group.class_hierarchy and group.class_hierarchy.status == "COMPLETE" then count = count + 1 end
                end
                return count
            end)(),
            partial_class_count = (function()
                local count = 0
                for _, group in ipairs(inventory.classes) do
                    if group.class_hierarchy and group.class_hierarchy.status ~= "COMPLETE" then count = count + 1 end
                end
                return count
            end)(),
            max_super_depth = hierarchyDepth,
            note = "Class hierarchy is resolved through GetClass and GetSuperStruct only. It does not establish component ownership beyond the separately reported Outer match, and does not enumerate property values."
        },
        property_catalog_status = propertyCatalogStatus,
        component_property_catalog = componentPropertyCatalog,
        note = "Enumeration success means the UE4SS query returned a table. PLAYER_OUTER_MATCH confirms the Outer relationship only. Class metadata is grouped separately; unusual class names remain flagged for review even when a hierarchy is available."
    }
end

function M.propertyValueAudit(player, movement, names)
    local result = { status = "READ_ONLY_PROPERTY_VALUE_AUDIT", items = {} }
    local targets = { { name = "player", object = player }, { name = "movement", object = movement } }
    for _, target in ipairs(targets) do
        if Safe.isValidObject(target.object) then
            for _, name in ipairs(names or {}) do
                local item = Safe.inspectPropertyValue(target.object, name)
                item.target = target.name
                item.target_full_name = Safe.safeFullName(target.object)
                result.items[#result.items + 1] = item
            end
        else
            result.items[#result.items + 1] = {
                target = target.name, target_full_name = "UNAVAILABLE", property = "*",
                status = "TARGET_NOT_FOUND", lua_type = "nil", access = "NONE",
                summary = "UNAVAILABLE"
            }
        end
    end
    result.note = "Property values are summarized only. Opaque userdata and tables are not dereferenced or expanded; no gameplay UFunction is invoked."
    return result
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
