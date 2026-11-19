-- Standalone regression checks for read-only component property metadata.
-- Run from mod/ue4ss/HogwartsRPResearch with:
-- lua tests/component_property_catalog_spec.lua
package.path = "Scripts/?.lua;" .. package.path

local Reflection = require("reflection")
local Discovery = require("discovery")

local function equal(actual, expected, label)
    assert(actual == expected, (label or "value") .. ": expected " ..
        tostring(expected) .. ", got " .. tostring(actual))
end

local function nameObject(value)
    return { ToString = function() return value end }
end

local bytePropertyClass = {
    GetFullName = function() return "Class /Script/CoreUObject.ByteProperty" end,
    GetFName = function() return nameObject("ByteProperty") end
}

local function property(name, fullName)
    return {
        GetFullName = function() return fullName end,
        GetFName = function() return nameObject(name) end,
        GetClass = function() return bytePropertyClass end
    }
end

local function class(fullName, properties, parent, failAfterFirst)
    local shortName = fullName:match("%.([^%.]+)$") or fullName
    return {
        GetFullName = function() return fullName end,
        GetFName = function() return nameObject(shortName) end,
        GetSuperStruct = function() return parent end,
        ForEachProperty = function(_, callback)
            if failAfterFirst then
                if properties[1] then callback(properties[1]) end
                error("synthetic continuation failure")
            end
            for _, item in ipairs(properties) do
                if callback(item) then break end
            end
            return false
        end
    }
end

local base = class("Class /Script/Engine.ActorComponent", {
    property("BaseFlag", "BoolProperty /Script/Engine.ActorComponent:BaseFlag")
})
local derived = class("Class /Script/Test.TestComponent", {
    property("MovementMode", "ByteProperty /Script/Test.TestComponent:MovementMode")
}, base)
local component = {
    GetFullName = function() return "TestComponent /Game/Test.Actor.Component" end,
    GetClass = function() return derived end
}

local complete = Reflection.catalogComponentProperties(component)
equal(complete.status, "COMPLETE", "complete catalog status")
equal(complete.class_hierarchy_status, "COMPLETE", "complete hierarchy status")
equal(complete.class_count, 2, "class count")
equal(complete.property_count, 2, "property count")
equal(complete.properties[1].declaring_class, "Class /Script/Test.TestComponent", "derived declaring class")
equal(complete.properties[2].declaring_class, "Class /Script/Engine.ActorComponent", "base declaring class")
equal(complete.class_diagnostics[1].iteration_probe.status, "OK", "successful minimal probe status")
equal(complete.class_diagnostics[1].iteration_probe.callback_invocations, 1, "successful minimal probe callback count")

local failingClass = class("Class /Script/Test.FailingComponent", {
    property("CapturedBeforeFailure", "BoolProperty /Script/Test.FailingComponent:CapturedBeforeFailure")
}, nil, true)
local failingComponent = {
    GetFullName = function() return "FailingComponent /Game/Test.Actor.FailingComponent" end,
    GetClass = function() return failingClass end
}
local partial = Reflection.catalogComponentProperties(failingComponent)
equal(partial.status, "PARTIAL", "partial catalog status")
equal(partial.property_count, 1, "partial captured property count")
equal(partial.class_diagnostics[1].status, "CALL_ERROR_AFTER_CALLBACK", "partial diagnostic status")
local failingDiagnostic = partial.class_diagnostics[1]
equal(failingDiagnostic.iteration_probe.status, "CALL_ERROR_AFTER_CALLBACK", "minimal probe failure status")
equal(failingDiagnostic.iteration_probe.callback_invocations, 1, "minimal probe callback count")
assert(failingDiagnostic.error:find("synthetic continuation failure", 1, true),
    "full enumeration error should retain the original error text")
assert(failingDiagnostic.error_length > 180, "full enumeration error length should exceed the old 180-character cap")
equal(failingDiagnostic.error_truncated, false, "full enumeration error truncation flag")

local actor = {
    GetFullName = function() return "BP_Biped_Player_C /Game/Test.Root.Player" end
}
local function ownedComponent(path)
    return {
        GetFullName = function() return path end,
        GetClass = function() return derived end,
        GetOuter = function() return actor end
    }
end

local originalFindObjects = _G.FindObjects
_G.FindObjects = function()
    return {
        ownedComponent("TestComponent /Game/Test.Root.Player.ComponentA"),
        ownedComponent("TestComponent /Game/Test.Root.Player.ComponentB")
    }
end
local discovered = Discovery.components(actor, Reflection.catalogComponentProperties)
_G.FindObjects = originalFindObjects

equal(discovered.player_owned_matches, 2, "confirmed component count")
equal(discovered.component_property_catalog.unique_class_count, 1, "cached class catalog count")
equal(discovered.component_property_catalog.classes[1].component_count, 2, "catalog instance count")
equal(discovered.class_inventory.classes[1].count, 2, "duplicate class inventory count")

print("component_property_catalog_spec: PASS")
