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

print("object_reference_audit_spec: PASS")

