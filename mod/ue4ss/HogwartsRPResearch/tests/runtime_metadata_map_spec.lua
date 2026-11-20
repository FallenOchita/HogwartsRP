-- Regression checks for the normalized player runtime metadata map.
-- Run from mod/ue4ss/HogwartsRPResearch with:
-- lua5.4 tests/runtime_metadata_map_spec.lua
package.path = "Scripts/?.lua;" .. package.path
local Discovery = require("discovery")
local function equal(actual, expected, label)
    assert(actual == expected, (label or "value") .. ": expected " ..
        tostring(expected) .. ", got " .. tostring(actual))
end

local catalog = {
    objects = {
        player = { status = "FOUND", full_name = "BP_Biped_Player_C /Game/Player", class_name = "Class /Script/Phoenix.Biped_Player" },
        controller = { status = "FOUND", full_name = "BP_Phoenix_Player_Controller_C /Game/Controller", class_name = "Class /Script/Phoenix.Biped_Controller" },
        movement = { status = "FOUND", full_name = "Biped_MovementComponent /Game/Player.CharMoveComp", class_name = "Class /Script/Phoenix.Biped_MovementComponent" }
    },
    properties = {
        value_audit = { items = {
            { target = "player", property = "CharacterMovement", status = "OBJECT_REFERENCE_FOUND", summary = "Biped_MovementComponent /Game/Player.CharMoveComp" }
        } },
        reference_value_audit = { items = {
            { target = "controller", property = "Pawn", status = "OBJECT_REFERENCE_FOUND", summary = "BP_Biped_Player_C /Game/Player" },
            { target = "movement", property = "UpdatedComponent", status = "PROPERTY_UNAVAILABLE" }
        } },
        related_reference_audit = { routes = {
            {
                root_property = "Player", target = "local_player",
                root_status = "OBJECT_REFERENCE_FOUND", root_reference = "LocalPlayer /Engine/Transient.LocalPlayer",
                items = {
                    { property = "PlayerController", status = "OBJECT_REFERENCE_FOUND", summary = "BP_Phoenix_Player_Controller_C /Game/Controller" }
                },
                metadata_audit = { items = {
                    { property = "PlayerController", status = "FOUND", lookup_scope_class = "Class /Script/Engine.Player",
                      descriptor = { full_name = "ObjectProperty /Script/Engine.Player:PlayerController" } },
                    { property = "Origin", status = "NOT_FOUND" }
                } }
            }
        } },
        targeted_property_metadata = {
            player = { items = {
                { property = "CharacterMovement", status = "FOUND", lookup_scope_class = "Class /Script/Phoenix.Biped_Player",
                  descriptor = { full_name = "ObjectProperty /Script/Engine.Character:CharacterMovement" } },
                { property = "CharMoveComp", status = "NOT_FOUND" }
            } },
            controller = { items = {} }, movement = { items = {} }
        }
    }
}
local map = Discovery.buildRuntimeMetadataMap(catalog)
equal(map.status, "ASSEMBLED_FROM_EXISTING_AUDITS", "map status")
equal(map.object_count, 4, "discovered and reference-identified object count")
equal(map.confirmed_reference_count, 4, "confirmed reference count")
equal(map.descriptor_count, 2, "confirmed descriptor count")
equal(map.unresolved_count, 2, "unresolved evidence count")
equal(map.references[1].target, "Biped_MovementComponent /Game/Player.CharMoveComp",
    "CharacterMovement observed target")
equal(map.descriptors[1].declaring_class, "/Script/Engine.Character",
    "descriptor declaring class parsed from full name")
equal(map.unresolved[1].property, "UpdatedComponent", "unavailable reference retained")
equal(map.unresolved[2].property, "CharMoveComp", "not-found descriptor retained")
equal(map.safety, "REPORT_ASSEMBLY_ONLY_REUSES_EXISTING_AUDIT_TABLES_NO_UOBJECT_ACCESS_NO_PROPERTY_READS_NO_UFUNCTION_INVOCATION",
    "map assembly safety boundary")
print("runtime_metadata_map_spec: PASS")
