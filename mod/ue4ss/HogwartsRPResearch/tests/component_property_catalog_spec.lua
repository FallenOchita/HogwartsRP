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
                local shouldStop = properties[1] and callback(properties[1]) or false
                if not shouldStop then
                    error(type(failAfterFirst) == "string" and failAfterFirst or "synthetic continuation failure")
                end
                return false
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
equal(complete.status, "PARTIAL", "sampled catalog status")
equal(complete.class_hierarchy_status, "COMPLETE", "complete hierarchy status")
equal(complete.class_count, 2, "class count")
equal(complete.property_count, 2, "one sampled property per class")
equal(complete.property_catalog_mode, "FIRST_PROPERTY_SAMPLE_PER_CLASS", "catalog sampling mode")
equal(complete.property_sample_limit_per_class, 1, "per-class sample limit")
equal(complete.property_catalog_complete, false, "sampled catalog must not claim completeness")
equal(complete.properties[1].declaring_class, "Class /Script/Test.TestComponent", "derived declaring class")
equal(complete.properties[2].declaring_class, "Class /Script/Engine.ActorComponent", "base declaring class")
equal(complete.class_diagnostics[1].iteration_probe.status, "OK", "successful minimal probe status")
equal(complete.class_diagnostics[1].iteration_probe.callback_invocations, 1, "successful minimal probe callback count")
equal(complete.class_diagnostics[1].iteration_probe.callback_invocations,
    complete.class_diagnostics[1].callback_invocations, "successful probe/detail callback count parity")
equal(complete.class_diagnostics[1].iteration_stop_probe.status, "OK", "successful early-stop probe status")
equal(complete.class_diagnostics[1].iteration_stop_probe.callback_mode,
    "COUNT_ONLY_STOP_AFTER_FIRST_RETURN_TRUE", "early-stop callback mode")
equal(complete.class_diagnostics[1].iteration_stop_probe.callback_invocations, 1,
    "successful early-stop callback count")
equal(complete.class_diagnostics[1].enumeration_mode, "FIRST_PROPERTY_SAMPLE_EARLY_STOP",
    "descriptor pass sampling mode")
equal(complete.class_diagnostics[1].property_sample_limit, 1, "descriptor pass sample limit")
equal(complete.class_diagnostics[1].status, "FIRST_PROPERTY_SAMPLE_CAPTURED",
    "descriptor pass reports first-property sample")

local longSyntheticError = "synthetic continuation failure " .. string.rep("diagnostic-detail-", 24)
local failingClass = class("Class /Script/Test.FailingComponent", {
    property("CapturedBeforeFailure", "BoolProperty /Script/Test.FailingComponent:CapturedBeforeFailure")
}, nil, longSyntheticError)
local failingComponent = {
    GetFullName = function() return "FailingComponent /Game/Test.Actor.FailingComponent" end,
    GetClass = function() return failingClass end
}
local partial = Reflection.catalogComponentProperties(failingComponent)
equal(partial.status, "PARTIAL", "partial catalog status")
equal(partial.property_count, 1, "partial captured property count")
equal(partial.class_diagnostics[1].status, "FIRST_PROPERTY_SAMPLE_CAPTURED", "sampled descriptor status")
local failingDiagnostic = partial.class_diagnostics[1]
equal(failingDiagnostic.iteration_probe.status, "CALL_ERROR_AFTER_CALLBACK", "minimal probe failure status")
equal(failingDiagnostic.iteration_probe.callback_invocations, 1,
    "continuation probe callback count")
equal(failingDiagnostic.callback_invocations, 1, "early-stop descriptor callback count")
equal(failingDiagnostic.properties_captured, 1, "sampled descriptor count")
equal(failingDiagnostic.iteration_stop_probe.status, "OK", "early-stop probe should avoid continuation failure")
equal(failingDiagnostic.iteration_stop_probe.callback_invocations, 1, "early-stop probe callback count")
assert(failingDiagnostic.iteration_probe.error:find("synthetic continuation failure", 1, true),
    "minimal probe should preserve the original error text")
assert(failingDiagnostic.iteration_probe.error_length > 180,
    "minimal probe error length should exceed the old 180-character cap")
equal(failingDiagnostic.iteration_probe.error_truncated, false, "minimal probe error truncation flag")
equal(failingDiagnostic.error, nil, "early-stop descriptor pass should not retain continuation error")

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
