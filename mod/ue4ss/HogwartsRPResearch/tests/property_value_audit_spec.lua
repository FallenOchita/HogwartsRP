-- Standalone Lua regression checks for safe.inspectPropertyValue.
-- Run from mod/ue4ss/HogwartsRPResearch with: lua tests/property_value_audit_spec.lua
package.path = "Scripts/?.lua;" .. package.path
local Safe = require("safe")
local Discovery = require("discovery")

local function equal(actual, expected, label)
    assert(actual == expected, (label or "value") .. ": expected " .. tostring(expected) .. ", got " .. tostring(actual))
end

local primitive = { Speed = 123.5, Enabled = true, Label = "walk" }
local number = Safe.inspectPropertyValue(primitive, "Speed")
equal(number.status, "PRIMITIVE_VALUE_READ", "number status")
equal(number.summary, "123.5", "number summary")
equal(number.lua_type, "number", "number type")

local boolean = Safe.inspectPropertyValue(primitive, "Enabled")
equal(boolean.status, "PRIMITIVE_VALUE_READ", "boolean status")
equal(boolean.summary, "true", "boolean summary")

local missing = Safe.inspectPropertyValue(primitive, "Missing")
equal(missing.status, "PROPERTY_UNAVAILABLE", "missing status")

local tableValue = Safe.inspectPropertyValue({ Value = { X = 1 } }, "Value")
equal(tableValue.status, "TABLE_VALUE_NOT_EXPANDED", "table status")
equal(tableValue.summary, "TABLE_NOT_EXPANDED", "table summary")

local opaque = io.tmpfile()
assert(opaque, "could not create userdata fixture")
local opaqueResult = Safe.inspectPropertyValue({ Value = opaque }, "Value")
equal(opaqueResult.status, "OPAQUE_USERDATA", "opaque userdata status")
equal(opaqueResult.summary, "OPAQUE_USERDATA_NOT_DEREFERENCED", "opaque userdata summary")
opaque:close()

local getterError = { GetPropertyValue = function() error("synthetic getter failure") end }
local failed = Safe.inspectPropertyValue(getterError, "Velocity")
equal(failed.status, "PROPERTY_READ_ERROR", "getter error status")
equal(failed.access, "GetPropertyValue", "getter error access")


local referenceUserdata = io.tmpfile()
assert(referenceUserdata, "could not create reference userdata fixture")
local referenceAudit = Discovery.objectReferenceAudit(
    {
        controller = {
            GetFullName = function() return "BP_Phoenix_Player_Controller_C /Game/Test.Root.Controller" end,
            GetPropertyValue = function(_, name)
                if name == "AcknowledgedPawn" then return referenceUserdata end
                if name == "PlayerState" then return nil end
                if name == "Player" then return 42 end
            end
        },
        movement = {
            GetFullName = function() return "Biped_MovementComponent /Game/Test.Root.Player.CharMoveComp" end,
            GetPropertyValue = function(_, name)
                if name == "CharacterOwner" then return { X = 1 } end
                if name == "PawnOwner" then error("synthetic movement getter failure") end
            end
        }
    },
    { controller = { "AcknowledgedPawn", "Player", "PlayerState" }, movement = { "CharacterOwner", "PawnOwner" } }
)
equal(referenceAudit.status, "READ_ONLY_OBJECT_REFERENCE_AUDIT", "reference audit status")
equal(referenceAudit.requested_count, 5, "reference audit requested count")
equal(referenceAudit.items[1].status, "OPAQUE_USERDATA", "reference opaque status")
equal(referenceAudit.items[1].summary, "OPAQUE_USERDATA_NOT_DEREFERENCED", "reference opaque summary")
equal(referenceAudit.items[2].status, "PRIMITIVE_VALUE_READ", "primitive reference status")
equal(referenceAudit.items[2].summary, "NON_OBJECT_VALUE_NOT_EXPANDED", "primitive reference suppressed")
equal(referenceAudit.items[3].status, "PROPERTY_UNAVAILABLE", "missing reference status")
equal(referenceAudit.items[4].status, "TABLE_VALUE_NOT_EXPANDED", "table reference status")
equal(referenceAudit.items[4].summary, "NON_OBJECT_VALUE_NOT_EXPANDED", "table reference suppressed")
equal(referenceAudit.items[5].status, "PROPERTY_READ_ERROR", "reference getter error status")
equal(referenceAudit.object_reference_count, 0, "object reference count")
equal(referenceAudit.opaque_userdata_count, 1, "opaque reference count")
equal(referenceAudit.error_count, 1, "reference error count")
referenceUserdata:close()


-- Exercise the bounded second-hop lookup with userdata-backed UObject fixtures.
local fakeHandles = {}
local function fakeUObject(fullName, fields)
    local handle = assert(io.tmpfile(), "could not create fake UObject userdata")
    local original = debug.getmetatable(handle)
    fakeHandles[#fakeHandles + 1] = { handle = handle, metatable = original }
    local originalIndex = original and original.__index
    debug.setmetatable(handle, {
        __index = function(self, key)
            if key == "IsValid" then return function() return true end end
            if key == "GetFullName" then return function() return fullName end end
            if key == "GetPropertyValue" then
                return function(_, propertyName)
                    local value = fields[propertyName]
                    if value == "__RAISE__" then error("synthetic second-hop getter failure") end
                    return value
                end
            end
            if type(originalIndex) == "table" then return originalIndex[key] end
            if type(originalIndex) == "function" then return originalIndex(self, key) end
            return nil
        end
    })
    return handle
end

local localPlayerController = fakeUObject("BP_Phoenix_Player_Controller_C /Game/Test.Root.Controller")
local localPlayer = fakeUObject("LocalPlayer /Engine/Transient.GameEngine.LocalPlayer", {
    PlayerController = localPlayerController
})
local playerPawn = fakeUObject("BP_Biped_Player_C /Game/Test.Root.Player", {})
local playerState = fakeUObject("PlayerState /Game/Test.Root.PlayerState", {
    PawnPrivate = playerPawn
})
local rootController = {
    GetPropertyValue = function(_, name)
        if name == "Player" then return localPlayer end
        if name == "PlayerState" then return playerState end
    end
}
local related = Discovery.relatedObjectReferenceAudit(rootController, {
    { root_property = "Player", target = "local_player", properties = { "PlayerController" } },
    { root_property = "PlayerState", target = "player_state", properties = { "PawnPrivate" } }
})
equal(related.status, "READ_ONLY_SECOND_HOP_REFERENCE_AUDIT", "second-hop audit status")
equal(related.root_reference_count, 2, "second-hop root references")
equal(related.object_reference_count, 2, "second-hop child references")
equal(related.routes[1].items[1].summary, "BP_Phoenix_Player_Controller_C /Game/Test.Root.Controller", "LocalPlayer controller reference")
equal(related.routes[2].items[1].summary, "BP_Biped_Player_C /Game/Test.Root.Player", "PlayerState pawn reference")

-- An opaque root is never dereferenced to attempt its child allowlist.
local opaqueRoot = assert(io.tmpfile(), "could not create opaque root fixture")
local opaqueController = { GetPropertyValue = function(_, name) if name == "Player" then return opaqueRoot end end }
local opaqueRelated = Discovery.relatedObjectReferenceAudit(opaqueController, {
    { root_property = "Player", target = "local_player", properties = { "PlayerController" } }
})
equal(opaqueRelated.routes[1].root_status, "OPAQUE_USERDATA", "opaque root status")
equal(#opaqueRelated.routes[1].items, 0, "opaque root is not expanded")

-- Restore each original file metatable before closing its FILE* userdata.
for _, entry in ipairs(fakeHandles) do
    debug.setmetatable(entry.handle, entry.metatable)
    entry.handle:close()
end
opaqueRoot:close()

print("object_reference_audit_spec: PASS")

