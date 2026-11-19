-- Standalone Lua regression checks for safe.inspectPropertyValue.
-- Run from mod/ue4ss/HogwartsRPResearch with: lua tests/property_value_audit_spec.lua
package.path = "Scripts/?.lua;" .. package.path
local Safe = require("safe")

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

print("property_value_audit_spec: PASS")
